using System.Globalization;
using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class IssueMembershipSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IssueMembershipSmokeTests(ReceptionAppFixture app)
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
    [InlineData("tablet", 1024, 768, "BL-ISSUE-TABLET", "Issue Tablet", true, false)]
    [InlineData("phone", 390, 844, "BL-ISSUE-PHONE", "Issue Phone", false, true)]
    public async Task OwnerIssuesOneCanonicalMembershipOnTargetViewport(
        string viewportName,
        int width,
        int height,
        string cardNumber,
        string clientDisplayName,
        bool includePayment,
        bool hasExistingNegative)
    {
        var clientId = viewportName == "tablet"
            ? _app.IssueTabletClientId
            : _app.IssuePhoneClientId;
        var existingMembershipId = hasExistingNegative
            ? _app.IssuePhoneExistingMembershipId
            : (Guid?)null;
        var existingState = existingMembershipId is { } existingId
            ? await _app.ReadMembershipStateAsync(existingId)
            : null;
        var membershipCountBefore = await _app.CountIssuedMembershipsAsync(clientId);
        var context = await CreateContextAsync(width, height);

        try
        {
            var page = await OpenReceptionAsync(context, $"{viewportName} issue membership");
            await SubmitHtmxSearchAsync(page, cardNumber);

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await ExpectVisibleAsync(
                profile.GetByRole(AriaRole.Heading, new() { Name = clientDisplayName }),
                viewportName,
                "issue client profile");
            var panel = profile.Locator("#issue-membership-action-panel");
            await OpenIssueMembershipPanelAsync(panel, viewportName);
            var form = panel.Locator("form");
            Assert.Equal(1, await form.Locator(
                "input[name='__RequestVerificationToken']").CountAsync());
            Assert.Equal("this:drop", await form.GetAttributeAsync("hx-sync"));
            Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));

            var typeSelect = panel.GetByLabel("Membership type", new() { Exact = true });
            Assert.Equal(1, await typeSelect.Locator("option").CountAsync());
            Assert.Equal(
                _app.IssueMembershipTypeId.ToString(),
                await typeSelect.InputValueAsync());
            await ExpectVisibleAsync(
                panel.Locator(".issue-membership-snapshot").GetByText(
                    "Eight visits / 30 days",
                    new() { Exact = true }),
                viewportName,
                "active membership type");
            Assert.Equal(
                0,
                await panel.GetByText("Legacy 12 visits / 45 days", new() { Exact = true })
                    .CountAsync());
            await ExpectVisibleAsync(
                panel.GetByText("950.00 UAH", new() { Exact = true }),
                viewportName,
                "server snapshot price");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var probeDate = today.AddDays(1);
            await RefreshPreviewDateAsync(page, probeDate);
            panel = profile.Locator("#issue-membership-action-panel");
            await ExpectVisibleAsync(
                panel.Locator("[data-issue-preview-base-end]").Filter(
                    new LocatorFilterOptions
                    {
                        HasText = probeDate.AddDays(29).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    }),
                viewportName,
                "refreshed base end date");
            await RefreshPreviewDateAsync(page, today);
            panel = profile.Locator("#issue-membership-action-panel");
            form = panel.Locator("form");

            var idempotencyKey = await form.Locator(
                "input[name='form.IdempotencyKey']").InputValueAsync();
            Assert.False(string.IsNullOrWhiteSpace(idempotencyKey));

            if (hasExistingNegative)
            {
                await AssertNegativeDecisionAsync(
                    page,
                    profile,
                    panel,
                    viewportName,
                    clientId,
                    membershipCountBefore,
                    idempotencyKey);
                panel = profile.Locator("#issue-membership-action-panel");
                form = panel.Locator("form");
            }
            else
            {
                Assert.Equal(0, await panel.Locator(".issue-negative-decision").CountAsync());
            }

            if (includePayment)
            {
                var paymentToggle = panel.GetByLabel(
                    "Add cash membership-sale payment",
                    new() { Exact = true });
                await paymentToggle.CheckAsync();
                var paymentAmount = panel.GetByLabel("Amount (UAH)", new() { Exact = true });
                Assert.False(await paymentAmount.IsDisabledAsync());
                Assert.Equal("950.00", await paymentAmount.InputValueAsync());
                await paymentAmount.FillAsync("0");
                await SubmitHtmxIssueMembershipAsync(page, bypassValidation: true);

                panel = profile.Locator("#issue-membership-action-panel");
                await ExpectVisibleAsync(
                    panel.GetByText(
                        "Payment amount must be greater than zero.",
                        new() { Exact = true }),
                    viewportName,
                    "positive issue payment requirement");
                Assert.Equal(
                    idempotencyKey,
                    await panel.Locator("input[name='form.IdempotencyKey']").InputValueAsync());
                await AssertNoIssueMutationAsync(clientId, membershipCountBefore);
                await panel.GetByLabel("Amount (UAH)", new() { Exact = true }).FillAsync("950");
            }
            else
            {
                Assert.True(await panel.GetByLabel(
                    "Amount (UAH)",
                    new() { Exact = true }).IsDisabledAsync());
            }

            var comment = $"{viewportName} reception membership issue.";
            await panel.GetByLabel("Comment (optional)", new() { Exact = true })
                .FillAsync(comment);
            await DelayIssueMembershipRequestsAsync(page);
            await AssertMinimumTouchTargetAsync(
                panel.GetByRole(AriaRole.Button, new() { Name = "Issue membership" }),
                viewportName,
                "Issue Membership button");
            await AssertFitsViewportAsync(page, viewportName, "Issue Membership form");
            await CaptureVisualAsync(page, viewportName, "issue-membership-form");

            await SubmitHtmxIssueMembershipAsync(page, repeatTapWhileBusy: true);

            var successText = includePayment
                ? "Membership issued with cash payment."
                : "Membership issued.";
            await ExpectVisibleAsync(
                profile.GetByText(successText),
                viewportName,
                "Issue Membership success message");
            Assert.Equal(
                membershipCountBefore + 1,
                await _app.CountIssuedMembershipsAsync(clientId));
            Assert.Equal(1L, await _app.CountIssueMembershipAuditEntriesAsync(clientId));
            Assert.Equal(1L, await _app.CountIssueMembershipIdempotencyKeysAsync(clientId));

            var membership = await _app.ReadLatestIssuedMembershipAsync(clientId);
            Assert.Equal(_app.IssueMembershipTypeId, membership.MembershipTypeId);
            Assert.Equal("Eight visits / 30 days", membership.TypeNameSnapshot);
            Assert.Equal(30, membership.DurationDaysSnapshot);
            Assert.Equal(8, membership.VisitsLimitSnapshot);
            Assert.Equal(950m, membership.PriceAmountSnapshot);
            Assert.Equal("UAH", membership.PriceCurrencySnapshot);
            Assert.Equal(today, membership.StartDate);
            Assert.Equal(today.AddDays(29), membership.BaseEndDate);
            Assert.Equal(comment, membership.Comment);
            Assert.Equal("active", membership.Status);
            var membershipState = await _app.ReadMembershipStateAsync(membership.MembershipId);
            Assert.Equal(0, membershipState.CountedVisits);
            Assert.Equal(8, membershipState.RemainingVisits);
            Assert.Equal(0, membershipState.NegativeBalance);
            Assert.Equal(today.AddDays(29), membershipState.EffectiveEndDate);

            Assert.Equal(includePayment ? 1L : 0L, await _app.CountActivePaymentsAsync(clientId));
            Assert.Equal(
                includePayment ? 1L : 0L,
                await _app.CountCreatePaymentAuditEntriesAsync(clientId));
            Assert.Equal(0L, await _app.CountCreatePaymentIdempotencyKeysAsync(clientId));
            if (includePayment)
            {
                var payment = await _app.ReadLatestActivePaymentAsync(clientId);
                Assert.Equal(950m, payment.Amount);
                Assert.Equal("UAH", payment.Currency);
                Assert.Equal("membership_sale", payment.PaymentContext);
                Assert.Equal(membership.MembershipId, payment.MembershipId);
                Assert.Equal(comment, payment.Comment);
                Assert.Equal("active", payment.Status);
            }

            if (existingMembershipId is { } unchangedMembershipId)
            {
                Assert.Equal(existingState, await _app.ReadMembershipStateAsync(unchangedMembershipId));
                await ExpectVisibleAsync(
                    profile.GetByText("Multiple active memberships require explicit selection."),
                    viewportName,
                    "canonical ambiguous membership warning");
            }
            else
            {
                await ExpectVisibleAsync(
                    profile.Locator(".membership-summary-grid").GetByText("8", new() { Exact = true }),
                    viewportName,
                    "canonical remaining visits");
            }

            await AssertFitsViewportAsync(page, viewportName, "canonical Membership profile");
            await CaptureVisualAsync(page, viewportName, "issue-membership-success");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task AssertNegativeDecisionAsync(
        IPage page,
        ILocator profile,
        ILocator panel,
        string viewportName,
        Guid clientId,
        long membershipCountBefore,
        string idempotencyKey)
    {
        await ExpectVisibleAsync(
            panel.GetByText("Balance: -1", new() { Exact = false }),
            viewportName,
            "existing negative balance");
        var options = panel.Locator("input[name='form.NegativeHandlingDecision']");
        Assert.Equal(3, await options.CountAsync());
        Assert.Equal(
            1,
            await panel.Locator(
                "input[name='form.NegativeHandlingDecision']:not([disabled])").CountAsync());
        Assert.True(await panel.GetByRole(
            AriaRole.Button,
            new() { Name = "Issue membership" }).IsDisabledAsync());

        await SubmitHtmxIssueMembershipAsync(page, bypassValidation: true);
        panel = profile.Locator("#issue-membership-action-panel");
        await ExpectVisibleAsync(
            panel.GetByText(
                "Choose how the existing negative visits remain handled before issuing.",
                new() { Exact = true }),
            viewportName,
            "negative decision requirement");
        Assert.Equal(
            idempotencyKey,
            await panel.Locator("input[name='form.IdempotencyKey']").InputValueAsync());
        await AssertNoIssueMutationAsync(clientId, membershipCountBefore);

        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains(
                "handler=IssueMembershipPreview",
                StringComparison.OrdinalIgnoreCase));
        await panel.GetByLabel(
            "Leave negative balance visible",
            new() { Exact = true }).CheckAsync();
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
        panel = profile.Locator("#issue-membership-action-panel");
        Assert.False(await panel.GetByRole(
            AriaRole.Button,
            new() { Name = "Issue membership" }).IsDisabledAsync());
    }

    private async Task AssertNoIssueMutationAsync(Guid clientId, long membershipCountBefore)
    {
        Assert.Equal(membershipCountBefore, await _app.CountIssuedMembershipsAsync(clientId));
        Assert.Equal(0L, await _app.CountIssueMembershipAuditEntriesAsync(clientId));
        Assert.Equal(0L, await _app.CountIssueMembershipIdempotencyKeysAsync(clientId));
        Assert.Equal(0L, await _app.CountActivePaymentsAsync(clientId));
        Assert.Equal(0L, await _app.CountCreatePaymentAuditEntriesAsync(clientId));
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

    private static async Task OpenIssueMembershipPanelAsync(
        ILocator panel,
        string viewportName)
    {
        await ExpectVisibleAsync(
            panel.Locator("summary"),
            viewportName,
            "Issue Membership action");
        if (await panel.GetAttributeAsync("open") is null)
        {
            await panel.Locator("summary").ClickAsync();
        }

        await ExpectVisibleAsync(panel.Locator("form"), viewportName, "Issue Membership form");
    }

    private static async Task RefreshPreviewDateAsync(IPage page, DateOnly startDate)
    {
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains(
                "handler=IssueMembershipPreview",
                StringComparison.OrdinalIgnoreCase));
        await page.GetByLabel("Start date", new() { Exact = true })
            .FillAsync(startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static async Task SubmitHtmxIssueMembershipAsync(
        IPage page,
        bool repeatTapWhileBusy = false,
        bool bypassValidation = false)
    {
        var panel = page.Locator("#issue-membership-action-panel");
        var form = panel.Locator("form");
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("handler=IssueMembership", StringComparison.OrdinalIgnoreCase));
        var disabledTask = page.WaitForFunctionAsync(
            "() => document.querySelector('#issue-membership-action-panel button[type=\"submit\"]')?.disabled === true");
        var submitButton = panel.Locator("[data-issue-membership-submit]");

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

    private static Task DelayIssueMembershipRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=IssueMembership*",
            async route =>
            {
                if (route.Request.Method == "POST")
                {
                    await Task.Delay(500);
                }

                await route.ContinueAsync();
            });
    }

    private static void AssertHtmxResponse(IResponse response)
    {
        Assert.True(response.Ok, $"htmx request returned HTTP {response.Status}.");
        Assert.True(response.Request.Headers.TryGetValue("hx-request", out var htmxRequest));
        Assert.Equal("true", htmxRequest);
        if (response.Request.Method == "GET")
        {
            Assert.DoesNotContain(
                "__RequestVerificationToken",
                response.Url,
                StringComparison.Ordinal);
        }
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
