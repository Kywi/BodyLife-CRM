using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class CorrectionRecordNavigationSmokeTests
    : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public CorrectionRecordNavigationSmokeTests(ReceptionAppFixture app)
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
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task CorrectionPanelsLinkToClientHistoryAndExactAudit(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureClientHistoryScenarioAsync();
        var occurredDate = scenario.OccurredDate.ToString("yyyy-MM-dd");
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize
            {
                Width = width,
                Height = height,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            await LoginAsync(
                page,
                useOwner ? _app.LoginName : _app.AdminLoginName,
                useOwner ? _app.Password : _app.AdminPassword,
                $"{viewportName} correction record navigation smoke");
            await OpenClientProfileAsync(
                page,
                scenario.CardNumber,
                scenario.ClientDisplayName);
            var profileUrl = page.Url;

            var profile = ClientProfile(page);
            var activeVisit = profile
                .Locator(".recent-visit-row[data-visit-status='active']")
                .First;
            var visitId = Guid.Parse(
                Assert.IsType<string>(
                    await activeVisit.GetAttributeAsync("data-visit-id")));
            var visitPanel = activeVisit.Locator("[data-cancel-visit-panel]");
            await OpenPanelAsync(visitPanel, viewportName, "Visit cancellation panel");
            var visitRecords = visitPanel.GetByRole(
                AriaRole.Navigation,
                new() { Name = "Visit correction records", Exact = true });
            await AssertCorrectionRoutesAsync(
                visitRecords,
                scenario.ClientId,
                "Visit",
                visitId,
                occurredDate,
                viewportName);
            await AssertFitsViewportAsync(page, viewportName, "Visit correction records");
            await CaptureVisualAsync(page, viewportName, "visit-correction-record-links");

            await visitRecords.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Client history", Exact = true })
                .ClickAsync();
            await AssertHistoryPageAsync(
                page,
                scenario.ClientId,
                "Visit",
                occurredDate,
                viewportName);

            profile = await ReloadProfileAsync(
                page,
                profileUrl,
                scenario.ClientDisplayName);
            activeVisit = profile.Locator($"[data-visit-id='{visitId}']");
            visitPanel = activeVisit.Locator("[data-cancel-visit-panel]");
            await OpenPanelAsync(visitPanel, viewportName, "Visit cancellation panel");
            await visitPanel.GetByRole(
                    AriaRole.Navigation,
                    new() { Name = "Visit correction records", Exact = true })
                .GetByRole(
                    AriaRole.Link,
                    new() { Name = "Audit timeline", Exact = true })
                .ClickAsync();
            await AssertAuditPageAsync(
                page,
                scenario.ClientId,
                "Visit",
                visitId,
                viewportName);

            profile = await ReloadProfileAsync(
                page,
                profileUrl,
                scenario.ClientDisplayName);
            var activePayment = profile
                .Locator(".recent-payment-row[data-payment-status='active']")
                .First;
            var paymentId = Guid.Parse(
                Assert.IsType<string>(
                    await activePayment.GetAttributeAsync("data-payment-id")));
            var originalPaymentId = Guid.Parse(
                Assert.IsType<string>(
                    await activePayment
                        .Locator("[data-original-payment-id]")
                        .GetAttributeAsync("data-original-payment-id")));
            var paymentPanel = activePayment.Locator("[data-correct-payment-panel]");
            await OpenPanelAsync(paymentPanel, viewportName, "Payment correction panel");
            var paymentRecords = paymentPanel.GetByRole(
                AriaRole.Navigation,
                new() { Name = "Payment correction records", Exact = true });
            await AssertCorrectionRoutesAsync(
                paymentRecords,
                scenario.ClientId,
                "Payment",
                originalPaymentId,
                occurredDate,
                viewportName);
            Assert.Equal(
                paymentId.ToString(),
                await paymentPanel.Locator(
                        "input[name='form.OriginalPaymentId']")
                    .InputValueAsync());
            await AssertFitsViewportAsync(page, viewportName, "Payment correction records");
            await CaptureVisualAsync(page, viewportName, "payment-correction-record-links");

            await paymentRecords.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Client history", Exact = true })
                .ClickAsync();
            await AssertHistoryPageAsync(
                page,
                scenario.ClientId,
                "Payment",
                occurredDate,
                viewportName);

            profile = await ReloadProfileAsync(
                page,
                profileUrl,
                scenario.ClientDisplayName);
            activePayment = profile.Locator($"[data-payment-id='{paymentId}']");
            paymentPanel = activePayment.Locator("[data-correct-payment-panel]");
            await OpenPanelAsync(paymentPanel, viewportName, "Payment correction panel");
            await paymentPanel.GetByRole(
                    AriaRole.Navigation,
                    new() { Name = "Payment correction records", Exact = true })
                .GetByRole(
                    AriaRole.Link,
                    new() { Name = "Audit timeline", Exact = true })
                .ClickAsync();
            await AssertAuditPageAsync(
                page,
                scenario.ClientId,
                "Payment",
                originalPaymentId,
                viewportName);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task LoginAsync(
        IPage page,
        string loginName,
        string password,
        string deviceLabel)
    {
        await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" })
            .FillAsync(loginName);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
    }

    private static async Task OpenClientProfileAsync(
        IPage page,
        string cardNumber,
        string displayName)
    {
        await page.GetByRole(
                AriaRole.Searchbox,
                new() { Name = "Client search" })
            .FillAsync(cardNumber);
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("handler=Search", StringComparison.OrdinalIgnoreCase));
        await page.GetByRole(
                AriaRole.Button,
                new() { Name = "Search", Exact = true })
            .ClickAsync();
        var response = await responseTask;
        Assert.True(response.Ok, $"Profile search returned HTTP {response.Status}.");
        Assert.Equal("true", response.Request.Headers["hx-request"]);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.htmx-request') === null");
        await ClientProfile(page)
            .GetByRole(
                AriaRole.Heading,
                new() { Name = displayName, Exact = true })
            .WaitForAsync();
    }

    private static async Task<ILocator> ReloadProfileAsync(
        IPage page,
        string profileUrl,
        string displayName)
    {
        await page.GotoAsync(
            profileUrl,
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        var profile = ClientProfile(page);
        await profile.GetByRole(
                AriaRole.Heading,
                new() { Name = displayName, Exact = true })
            .WaitForAsync();
        return profile;
    }

    private static ILocator ClientProfile(IPage page)
    {
        return page.GetByRole(
            AriaRole.Region,
            new() { Name = "Client profile", Exact = true });
    }

    private static async Task OpenPanelAsync(
        ILocator panel,
        string viewportName,
        string label)
    {
        await ExpectVisibleAsync(panel.Locator("summary"), viewportName, label);
        if (await panel.GetAttributeAsync("open") is null)
        {
            await panel.Locator("summary").ClickAsync();
        }

        await ExpectVisibleAsync(
            panel.Locator("[data-correction-record-links]"),
            viewportName,
            $"{label} record links");
    }

    private static async Task AssertCorrectionRoutesAsync(
        ILocator records,
        Guid clientId,
        string entity,
        Guid auditEntityId,
        string occurredDate,
        string viewportName)
    {
        var history = records.GetByRole(
            AriaRole.Link,
            new() { Name = "Client history", Exact = true });
        var audit = records.GetByRole(
            AriaRole.Link,
            new() { Name = "Audit timeline", Exact = true });
        await AssertMinimumTouchTargetAsync(history, viewportName, "Client history link");
        await AssertMinimumTouchTargetAsync(audit, viewportName, "Audit timeline link");

        var historyHref = Assert.IsType<string>(await history.GetAttributeAsync("href"));
        Assert.Contains($"clientId={clientId}", historyHref, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"entity={entity}", historyHref, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"from={occurredDate}", historyHref, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"to={occurredDate}", historyHref, StringComparison.OrdinalIgnoreCase);

        var auditHref = Assert.IsType<string>(await audit.GetAttributeAsync("href"));
        Assert.Contains($"clientId={clientId}", auditHref, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"entity={entity}", auditHref, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            $"entityId={auditEntityId}",
            auditHref,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertHistoryPageAsync(
        IPage page,
        Guid clientId,
        string entity,
        string occurredDate,
        string viewportName)
    {
        await page.WaitForURLAsync("**/Audit/ClientHistory?**");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await ExpectVisibleAsync(
            page.GetByRole(
                AriaRole.Heading,
                new() { Name = "Client history", Exact = true }),
            viewportName,
            $"{entity} Client history");
        Assert.Equal(
            clientId.ToString(),
            await page.GetByLabel("Client ID", new() { Exact = true }).InputValueAsync());
        Assert.Equal(
            entity,
            await page.GetByLabel("Source type", new() { Exact = true }).InputValueAsync());
        Assert.Equal(
            occurredDate,
            await page.GetByLabel("Occurred from (UTC)", new() { Exact = true })
                .InputValueAsync());
        Assert.Equal(
            occurredDate,
            await page.GetByLabel("Occurred through (UTC)", new() { Exact = true })
                .InputValueAsync());
        Assert.True(
            await page.Locator("[data-client-history-list] > [data-client-history-row]")
                .CountAsync() > 0,
            $"{entity} history should retain canonical source rows.");
        await AssertFitsViewportAsync(page, viewportName, $"{entity} Client history");
    }

    private static async Task AssertAuditPageAsync(
        IPage page,
        Guid clientId,
        string entity,
        Guid entityId,
        string viewportName)
    {
        await page.WaitForURLAsync("**/Audit/Timeline?**");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await ExpectVisibleAsync(
            page.GetByRole(
                AriaRole.Heading,
                new() { Name = "Audit timeline", Exact = true }),
            viewportName,
            $"{entity} Audit timeline");
        Assert.Equal(
            clientId.ToString(),
            await page.GetByLabel("Client ID", new() { Exact = true }).InputValueAsync());
        Assert.Equal(
            entity,
            await page.GetByLabel("Entity type", new() { Exact = true }).InputValueAsync());
        Assert.Equal(
            entityId.ToString(),
            await page.GetByLabel("Entity ID", new() { Exact = true }).InputValueAsync());
        Assert.True(
            await page.Locator("[data-audit-entry-list] > [data-audit-entry-id]")
                .CountAsync() > 0,
            $"Exact {entity} Audit timeline should retain canonical entries.");
        await AssertFitsViewportAsync(page, viewportName, $"{entity} Audit timeline");
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

    private static async Task CaptureVisualAsync(
        IPage page,
        string viewportName,
        string state)
    {
        var screenshotDirectory = Environment.GetEnvironmentVariable(
            "BODYLIFE_UI_SCREENSHOT_DIR");
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
