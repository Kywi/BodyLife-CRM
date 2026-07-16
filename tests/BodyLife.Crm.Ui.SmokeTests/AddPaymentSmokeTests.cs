using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class AddPaymentSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public AddPaymentSmokeTests(ReceptionAppFixture app)
    {
        _app = app;
    }

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }

    [Theory]
    [InlineData(
        "tablet",
        1024,
        768,
        "BL-PAYMENT-TABLET",
        "Payment Tablet",
        "MembershipSale",
        "Membership sale",
        850)]
    [InlineData(
        "phone",
        390,
        844,
        "BL-PAYMENT-PHONE",
        "Payment Phone",
        "OneOff",
        "One-off payment",
        125)]
    public async Task OwnerAddsOneCanonicalCashPaymentOnTargetViewport(
        string viewportName,
        int width,
        int height,
        string cardNumber,
        string clientDisplayName,
        string contextValue,
        string historyContextLabel,
        int amount)
    {
        var clientId = viewportName == "tablet"
            ? _app.PaymentTabletClientId
            : _app.PaymentPhoneClientId;
        var membershipId = viewportName == "tablet"
            ? _app.PaymentTabletMembershipId
            : (Guid?)null;
        var membershipStateBefore = membershipId is { } canonicalMembershipId
            ? await _app.ReadMembershipStateAsync(canonicalMembershipId)
            : null;
        var context = await CreateContextAsync(width, height);

        try
        {
            var page = await OpenReceptionAsync(context, $"{viewportName} add payment");
            await SubmitHtmxSearchAsync(page, cardNumber);

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await ExpectVisibleAsync(
                profile.GetByRole(AriaRole.Heading, new() { Name = clientDisplayName }),
                viewportName,
                "Payment client profile");
            var panel = profile.Locator("#add-payment-action-panel");
            await OpenAddPaymentPanelAsync(panel, viewportName);
            var form = panel.Locator("form");
            Assert.Equal(1, await form.Locator(
                "input[name='__RequestVerificationToken']").CountAsync());
            Assert.Equal("this:drop", await form.GetAttributeAsync("hx-sync"));
            Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));
            await ExpectVisibleAsync(
                panel.GetByText(
                    "This standalone payment does not close negative visits or recalculate membership state.",
                    new() { Exact = true }),
                viewportName,
                "standalone Payment warning");

            var fixedContext = panel.Locator(".payment-fixed-context");
            await ExpectVisibleAsync(
                fixedContext.GetByText("Cash", new() { Exact = true }),
                viewportName,
                "cash-only method");
            await ExpectVisibleAsync(
                fixedContext.GetByText("UAH", new() { Exact = true }),
                viewportName,
                "fixed currency");
            var paymentContext = panel.GetByLabel("Payment context", new() { Exact = true });
            Assert.Equal(0, await paymentContext.Locator("option[value='NegativeClosure']").CountAsync());
            await paymentContext.SelectOptionAsync(contextValue);

            var membership = panel.GetByLabel("Membership (optional)", new() { Exact = true });
            if (membershipId is { } selectedMembershipId)
            {
                var membershipOption = membership.Locator("option").Filter(
                    new LocatorFilterOptions { HasText = "Payment tablet snapshot" });
                Assert.Equal(1, await membershipOption.CountAsync());
                Assert.Contains(
                    "Payment tablet snapshot",
                    await membershipOption.InnerTextAsync(),
                    StringComparison.Ordinal);
                await membership.SelectOptionAsync(selectedMembershipId.ToString());
            }
            else
            {
                Assert.Equal(1, await membership.Locator("option").CountAsync());
                Assert.Equal(string.Empty, await membership.InputValueAsync());
            }

            var idempotencyKey = await form.Locator(
                "input[name='form.IdempotencyKey']").InputValueAsync();
            Assert.False(string.IsNullOrWhiteSpace(idempotencyKey));
            Assert.False(string.IsNullOrWhiteSpace(await panel.GetByLabel(
                "Occurred (UTC)",
                new() { Exact = true }).InputValueAsync()));

            await panel.GetByLabel("Amount (UAH)", new() { Exact = true }).FillAsync("0");
            await SubmitHtmxAddPaymentAsync(page, bypassValidation: true);

            panel = profile.Locator("#add-payment-action-panel");
            await ExpectVisibleAsync(
                panel.GetByRole(AriaRole.Alert),
                viewportName,
                "Payment validation error");
            await ExpectVisibleAsync(
                panel.GetByText(
                    "Payment amount must be greater than zero.",
                    new() { Exact = true }),
                viewportName,
                "positive amount requirement");
            Assert.NotNull(await panel.GetAttributeAsync("open"));
            Assert.Equal(
                idempotencyKey,
                await panel.Locator("input[name='form.IdempotencyKey']").InputValueAsync());
            Assert.Equal(0L, await _app.CountActivePaymentsAsync(clientId));
            Assert.Equal(0L, await _app.CountCreatePaymentAuditEntriesAsync(clientId));
            Assert.Equal(0L, await _app.CountCreatePaymentIdempotencyKeysAsync(clientId));

            await panel.GetByLabel("Amount (UAH)", new() { Exact = true })
                .FillAsync(amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var comment = $"{viewportName} reception cash payment.";
            await panel.GetByLabel("Comment (optional)", new() { Exact = true })
                .FillAsync(comment);
            await DelayCreatePaymentRequestsAsync(page);
            await AssertMinimumTouchTargetAsync(
                panel.GetByRole(AriaRole.Button, new() { Name = "Add payment" }),
                viewportName,
                "Add Payment button");
            await AssertFitsViewportAsync(page, viewportName, "Add Payment form");
            await CaptureVisualAsync(page, viewportName, "add-payment-form");

            await SubmitHtmxAddPaymentAsync(page, repeatTapWhileBusy: true);

            await ExpectVisibleAsync(
                profile.GetByText("Payment added."),
                viewportName,
                "Payment success message");
            var paymentRow = profile.Locator("[data-payment-status='active']").First;
            await ExpectVisibleAsync(paymentRow, viewportName, "canonical Payment row");
            await ExpectVisibleAsync(
                paymentRow.GetByText($"{amount} UAH", new() { Exact = true }),
                viewportName,
                "canonical Payment amount");
            await ExpectVisibleAsync(
                paymentRow.GetByText(historyContextLabel, new() { Exact = true }),
                viewportName,
                "canonical Payment context");
            await ExpectVisibleAsync(
                paymentRow.GetByText(comment, new() { Exact = true }),
                viewportName,
                "canonical Payment comment");

            if (membershipId is not null)
            {
                await ExpectVisibleAsync(
                    paymentRow.GetByText("Payment tablet snapshot", new() { Exact = true }),
                    viewportName,
                    "linked Membership snapshot");
            }

            Assert.Equal(1L, await _app.CountActivePaymentsAsync(clientId));
            Assert.Equal(1L, await _app.CountCreatePaymentAuditEntriesAsync(clientId));
            Assert.Equal(1L, await _app.CountCreatePaymentIdempotencyKeysAsync(clientId));
            var payment = await _app.ReadLatestActivePaymentAsync(clientId);
            Assert.Equal(amount, payment.Amount);
            Assert.Equal("UAH", payment.Currency);
            Assert.Equal(
                contextValue == "MembershipSale" ? "membership_sale" : "one_off",
                payment.PaymentContext);
            Assert.Equal(membershipId, payment.MembershipId);
            Assert.Equal(comment, payment.Comment);
            Assert.Equal("active", payment.Status);

            if (membershipId is { } unchangedMembershipId)
            {
                Assert.Equal(
                    membershipStateBefore,
                    await _app.ReadMembershipStateAsync(unchangedMembershipId));
            }

            await AssertFitsViewportAsync(page, viewportName, "canonical Payment profile");
            await CaptureVisualAsync(page, viewportName, "add-payment-success");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, int height)
    {
        Assert.NotNull(_browser);

        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = width,
                Height = height,
            },
        });
    }

    private async Task<IPage> OpenReceptionAsync(
        IBrowserContext context,
        string deviceLabel)
    {
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Reception request returned HTTP {response.Status}.");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" })
            .FillAsync(_app.LoginName);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(_app.Password);
        await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
        await page.GetByRole(AriaRole.Heading, new() { Name = "Reception" }).WaitForAsync();
        return page;
    }

    private static async Task SubmitHtmxSearchAsync(IPage page, string query)
    {
        await page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" })
            .FillAsync(query);
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("handler=Search", StringComparison.OrdinalIgnoreCase));
        await page.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static async Task OpenAddPaymentPanelAsync(
        ILocator panel,
        string viewportName)
    {
        await ExpectVisibleAsync(
            panel.Locator("summary"),
            viewportName,
            "Add Payment action");
        if (await panel.GetAttributeAsync("open") is null)
        {
            await panel.Locator("summary").ClickAsync();
        }

        await ExpectVisibleAsync(panel.Locator("form"), viewportName, "Add Payment form");
    }

    private static async Task SubmitHtmxAddPaymentAsync(
        IPage page,
        bool repeatTapWhileBusy = false,
        bool bypassValidation = false)
    {
        var panel = page.Locator("#add-payment-action-panel");
        var form = panel.Locator("form");
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("handler=CreatePayment", StringComparison.OrdinalIgnoreCase));
        var disabledTask = page.WaitForFunctionAsync(
            "() => document.querySelector('#add-payment-action-panel button[type=\"submit\"]')?.disabled === true");
        var submitButton = panel.Locator("[data-add-payment-submit]");

        if (bypassValidation)
        {
            await form.EvaluateAsync(
                "form => { form.noValidate = true; form.requestSubmit(); }");
        }
        else
        {
            await submitButton.ClickAsync();
        }

        await disabledTask;
        if (repeatTapWhileBusy)
        {
            Assert.True(await submitButton.IsDisabledAsync());
            await submitButton.EvaluateAsync("button => button.click()");
        }

        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static Task DelayCreatePaymentRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=CreatePayment*",
            async route =>
            {
                await Task.Delay(500);
                await route.ContinueAsync();
            });
    }

    private static void AssertHtmxResponse(IResponse response)
    {
        Assert.True(response.Ok, $"htmx request returned HTTP {response.Status}.");
        Assert.True(response.Request.Headers.TryGetValue("hx-request", out var htmxRequest));
        Assert.Equal("true", htmxRequest);
    }

    private static async Task WaitForHtmxSettleAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.htmx-request') === null");
    }

    private static async Task ExpectVisibleAsync(
        ILocator locator,
        string viewportName,
        string label)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5_000,
        });
        Assert.True(
            await locator.IsVisibleAsync(),
            $"{label} should be visible on {viewportName} viewport.");
    }

    private static async Task AssertFitsViewportAsync(
        IPage page,
        string viewportName,
        string state)
    {
        var fitsViewport = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= window.innerWidth + 1");
        Assert.True(
            fitsViewport,
            $"{viewportName} {state} should not require horizontal scrolling.");
    }

    private static async Task AssertMinimumTouchTargetAsync(
        ILocator locator,
        string viewportName,
        string label)
    {
        var bounds = await locator.BoundingBoxAsync();
        Assert.NotNull(bounds);
        Assert.True(
            bounds.Width >= 44 && bounds.Height >= 44,
            $"{label} should be at least 44px in both dimensions on {viewportName}.");
    }

    private static async Task CaptureVisualAsync(
        IPage page,
        string viewportName,
        string state)
    {
        var screenshotDirectory = Environment.GetEnvironmentVariable("BODYLIFE_UI_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            return;
        }

        Directory.CreateDirectory(screenshotDirectory);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = true,
            Path = Path.Combine(screenshotDirectory, $"{viewportName}-{state}.png"),
        });
    }
}
