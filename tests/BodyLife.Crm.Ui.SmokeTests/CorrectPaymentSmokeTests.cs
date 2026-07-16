using System.Globalization;
using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class CorrectPaymentSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public CorrectPaymentSmokeTests(ReceptionAppFixture app)
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
    [InlineData("tablet", 1024, 768, 100, "replace")]
    [InlineData("phone", 390, 844, 900, "cancel")]
    public async Task OwnerCorrectsCanonicalPaymentWithValidationAndDuplicateTapProtection(
        string viewportName,
        int width,
        int height,
        int originalAmount,
        string correctionMode)
    {
        var context = await CreateContextAsync(width, height);

        try
        {
            var page = await OpenReceptionAsync(context, $"{viewportName} payment correction");
            await SubmitHtmxSearchAsync(page, "BL-PAYMENT-HISTORY");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await ExpectVisibleAsync(
                profile.GetByRole(AriaRole.Heading, new() { Name = "Payment History" }),
                viewportName,
                "Payment correction client profile");
            var sourceRow = profile.Locator(".recent-payment-row").Filter(
                new LocatorFilterOptions { HasText = $"{originalAmount} UAH" });
            Assert.Equal(1, await sourceRow.CountAsync());
            var sourcePaymentIdValue = await sourceRow.GetAttributeAsync("data-payment-id");
            Assert.True(Guid.TryParse(sourcePaymentIdValue, out var sourcePaymentId));
            var panelSelector = $"#correct-payment-panel-{sourcePaymentId:N}";
            var panel = page.Locator(panelSelector);
            await OpenCorrectionPanelAsync(panel, viewportName);

            var form = panel.Locator("form");
            Assert.Equal(1, await form.Locator(
                "input[name='__RequestVerificationToken']").CountAsync());
            Assert.Equal("this:drop", await form.GetAttributeAsync("hx-sync"));
            Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));
            await ExpectVisibleAsync(
                panel.GetByText(
                    "The original Payment remains visible. Canonical cash totals use its cancellation or replacement record.",
                    new() { Exact = true }),
                viewportName,
                "correction history warning");
            await ExpectVisibleAsync(
                panel.GetByText($"{originalAmount} UAH", new() { Exact = true }),
                viewportName,
                "original Payment amount context");

            var idempotencyKey = await form.Locator(
                "input[name='form.IdempotencyKey']").InputValueAsync();
            Assert.False(string.IsNullOrWhiteSpace(idempotencyKey));
            var activeBefore = await _app.CountActivePaymentsAsync(_app.PaymentHistoryClientId);
            var auditBefore = await _app.CountCorrectPaymentAuditEntriesAsync(
                _app.PaymentHistoryClientId);
            var idempotencyBefore = await _app.CountCorrectPaymentIdempotencyKeysAsync(
                _app.PaymentHistoryClientId);

            if (correctionMode == "cancel")
            {
                await panel.GetByRole(AriaRole.Radio, new() { Name = "Cancel payment" })
                    .CheckAsync();
                await AssertReplacementFieldsDisabledAsync(panel);
            }
            else
            {
                Assert.True(await panel.GetByRole(
                    AriaRole.Radio,
                    new() { Name = "Replace payment" }).IsCheckedAsync());
                await ExpectVisibleAsync(
                    panel.Locator("[data-payment-replacement-fields]"),
                    viewportName,
                    "replacement fields");
                Assert.Equal(
                    originalAmount.ToString(CultureInfo.InvariantCulture),
                    await panel.GetByLabel(
                        "Replacement amount (UAH)",
                        new() { Exact = true }).InputValueAsync());
            }

            await SubmitHtmxCorrectionAsync(
                page,
                sourcePaymentId,
                bypassValidation: true);

            panel = page.Locator(panelSelector);
            await ExpectVisibleAsync(
                panel.GetByRole(AriaRole.Alert),
                viewportName,
                "Payment correction validation error");
            await ExpectVisibleAsync(
                panel.GetByText(
                    "Enter why this Payment should be corrected or canceled.",
                    new() { Exact = true }),
                viewportName,
                "Payment correction reason requirement");
            Assert.NotNull(await panel.GetAttributeAsync("open"));
            Assert.Equal(
                idempotencyKey,
                await panel.Locator("input[name='form.IdempotencyKey']").InputValueAsync());
            Assert.Equal(0L, await _app.CountPaymentCorrectionsAsync(sourcePaymentId));
            Assert.Equal(0L, await _app.CountPaymentCancellationsAsync(sourcePaymentId));
            Assert.Equal(auditBefore, await _app.CountCorrectPaymentAuditEntriesAsync(
                _app.PaymentHistoryClientId));
            Assert.Equal(idempotencyBefore, await _app.CountCorrectPaymentIdempotencyKeysAsync(
                _app.PaymentHistoryClientId));

            var reason = correctionMode == "replace"
                ? "Trial amount was entered incorrectly"
                : "Membership payment was duplicated";
            await panel.GetByLabel("Reason", new() { Exact = true }).FillAsync(reason);
            await panel.GetByLabel("Correction comment (optional)", new() { Exact = true })
                .FillAsync($"{viewportName} correction review");
            await panel.GetByRole(
                AriaRole.Checkbox,
                new()
                {
                    Name = "I confirm this Payment should be corrected and its original history preserved.",
                })
                .CheckAsync();

            const int replacementAmount = 140;
            const string replacementComment = "Corrected trial cash amount";
            if (correctionMode == "replace")
            {
                await panel.GetByLabel(
                    "Replacement amount (UAH)",
                    new() { Exact = true }).FillAsync(
                        replacementAmount.ToString(CultureInfo.InvariantCulture));
                await panel.GetByLabel("Replacement context", new() { Exact = true })
                    .SelectOptionAsync("Other");
                await panel.GetByLabel(
                    "Replacement comment (optional)",
                    new() { Exact = true }).FillAsync(replacementComment);
            }

            await DelayCorrectPaymentRequestsAsync(page);
            await AssertMinimumTouchTargetAsync(
                panel.GetByRole(AriaRole.Button, new() { Name = "Apply correction" }),
                viewportName,
                "Correct Payment button");
            await AssertFitsViewportAsync(page, viewportName, "Correct Payment form");
            await CaptureVisualAsync(page, viewportName, "correct-payment-form");

            await SubmitHtmxCorrectionAsync(
                page,
                sourcePaymentId,
                repeatTapWhileBusy: true);

            var expectedOutcome = correctionMode == "replace"
                ? "Payment corrected."
                : "Payment canceled.";
            await ExpectVisibleAsync(
                profile.GetByText(expectedOutcome),
                viewportName,
                "Payment correction success message");
            var canonicalSourceRow = profile.Locator(
                $"[data-payment-id='{sourcePaymentId}']");
            await ExpectVisibleAsync(
                canonicalSourceRow,
                viewportName,
                "canonical original Payment row");
            Assert.Equal(
                0,
                await canonicalSourceRow.Locator("[data-correct-payment-panel]").CountAsync());

            if (correctionMode == "replace")
            {
                Assert.Equal("replaced", await canonicalSourceRow.GetAttributeAsync(
                    "data-payment-status"));
                await ExpectVisibleAsync(
                    canonicalSourceRow.GetByText("Replaced payment", new() { Exact = true }),
                    viewportName,
                    "original replacement direction");
                var replacementRow = profile.Locator("[data-payment-status='active']").Filter(
                    new LocatorFilterOptions { HasText = $"{replacementAmount} UAH" });
                await ExpectVisibleAsync(
                    replacementRow,
                    viewportName,
                    "canonical replacement Payment row");
                await ExpectVisibleAsync(
                    replacementRow.GetByText(
                        "Corrected replacement",
                        new() { Exact = true }),
                    viewportName,
                    "replacement correction direction");
                await ExpectVisibleAsync(
                    replacementRow.Locator(".recent-payment-comment").GetByText(
                        replacementComment,
                        new() { Exact = true }),
                    viewportName,
                    "replacement Payment comment");
                Assert.Equal(1L, await _app.CountPaymentCorrectionsAsync(sourcePaymentId));
                Assert.Equal(0L, await _app.CountPaymentCancellationsAsync(sourcePaymentId));
                Assert.Equal(activeBefore, await _app.CountActivePaymentsAsync(
                    _app.PaymentHistoryClientId));
                var replacement = await _app.ReadLatestActivePaymentAsync(
                    _app.PaymentHistoryClientId);
                Assert.Equal(replacementAmount, replacement.Amount);
                Assert.Equal("other", replacement.PaymentContext);
                Assert.Equal(replacementComment, replacement.Comment);
            }
            else
            {
                Assert.Equal("canceled", await canonicalSourceRow.GetAttributeAsync(
                    "data-payment-status"));
                await ExpectVisibleAsync(
                    canonicalSourceRow.GetByText("Cancellation", new() { Exact = true }),
                    viewportName,
                    "canonical Payment cancellation");
                await ExpectVisibleAsync(
                    canonicalSourceRow.GetByText(reason, new() { Exact = true }),
                    viewportName,
                    "canonical Payment cancellation reason");
                Assert.Equal(0L, await _app.CountPaymentCorrectionsAsync(sourcePaymentId));
                Assert.Equal(1L, await _app.CountPaymentCancellationsAsync(sourcePaymentId));
                Assert.Equal(activeBefore - 1, await _app.CountActivePaymentsAsync(
                    _app.PaymentHistoryClientId));
            }

            Assert.Equal(auditBefore + 1, await _app.CountCorrectPaymentAuditEntriesAsync(
                _app.PaymentHistoryClientId));
            Assert.Equal(idempotencyBefore + 1, await _app.CountCorrectPaymentIdempotencyKeysAsync(
                _app.PaymentHistoryClientId));
            await AssertFitsViewportAsync(page, viewportName, "canonical corrected Payment profile");
            await CaptureVisualAsync(page, viewportName, "correct-payment-success");
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

    private static async Task OpenCorrectionPanelAsync(
        ILocator panel,
        string viewportName)
    {
        await ExpectVisibleAsync(
            panel.Locator("summary"),
            viewportName,
            "Correct Payment action");
        if (await panel.GetAttributeAsync("open") is null)
        {
            await panel.Locator("summary").ClickAsync();
        }

        await ExpectVisibleAsync(panel.Locator("form"), viewportName, "Correct Payment form");
    }

    private static async Task AssertReplacementFieldsDisabledAsync(ILocator panel)
    {
        var replacementFields = panel.Locator("[data-payment-replacement-fields]");
        Assert.False(await replacementFields.IsVisibleAsync());
        var inputs = panel.Locator("[data-payment-replacement-input]");
        for (var index = 0; index < await inputs.CountAsync(); index++)
        {
            Assert.True(await inputs.Nth(index).IsDisabledAsync());
        }
    }

    private static async Task SubmitHtmxCorrectionAsync(
        IPage page,
        Guid paymentId,
        bool repeatTapWhileBusy = false,
        bool bypassValidation = false)
    {
        var panelSelector = $"#correct-payment-panel-{paymentId:N}";
        var panel = page.Locator(panelSelector);
        var form = panel.Locator("form");
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("handler=CorrectPayment", StringComparison.OrdinalIgnoreCase));
        var disabledTask = page.WaitForFunctionAsync(
            $"() => document.querySelector('{panelSelector} button[type=\"submit\"]')?.disabled === true");
        var submitButton = panel.Locator("[data-correct-payment-submit]");

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

    private static Task DelayCorrectPaymentRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=CorrectPayment*",
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
