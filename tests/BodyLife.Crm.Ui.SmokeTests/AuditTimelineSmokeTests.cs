using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class AuditTimelineSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public AuditTimelineSmokeTests(ReceptionAppFixture app)
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
    public async Task OwnerAndAdminCanInspectFilteredAppendOnlyTimeline(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        Assert.Equal(scenario.PageSize + 2, scenario.TotalEntries);
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
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
                $"{viewportName} audit timeline smoke");

            var auditNavigation = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Audit timeline", Exact = true });
            await AssertMinimumTouchTargetAsync(
                auditNavigation,
                viewportName,
                "audit timeline navigation");
            await auditNavigation.ClickAsync();
            await page.WaitForURLAsync("**/Audit/Timeline**");

            Assert.Equal("Audit timeline - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Audit timeline", Exact = true }),
                viewportName,
                "audit timeline heading");
            await ExpectVisibleAsync(
                page.GetByText(
                    useOwner ? "BodyLife Owner" : "Smoke Named Admin",
                    new() { Exact = true }),
                viewportName,
                "current audit viewer account");

            var clientId = page.GetByLabel("Client ID", new() { Exact = true });
            var entityType = page.GetByLabel("Entity type", new() { Exact = true });
            var recordedFrom = page.GetByLabel(
                "Recorded from (UTC)",
                new() { Exact = true });
            var recordedThrough = page.GetByLabel(
                "Recorded through (UTC)",
                new() { Exact = true });
            var action = page.GetByLabel("Business action", new() { Exact = true });
            var applyFilters = page.GetByRole(
                AriaRole.Button,
                new() { Name = "Apply filters", Exact = true });
            await AssertMinimumTouchTargetAsync(clientId, viewportName, "Client filter");
            await AssertMinimumTouchTargetAsync(entityType, viewportName, "entity filter");
            await AssertMinimumTouchTargetAsync(
                recordedFrom,
                viewportName,
                "recorded-from filter");
            await AssertMinimumTouchTargetAsync(
                recordedThrough,
                viewportName,
                "recorded-through filter");
            await AssertMinimumTouchTargetAsync(action, viewportName, "action filter");
            await AssertMinimumTouchTargetAsync(
                applyFilters,
                viewportName,
                "apply audit filters");

            var recordedDate = scenario.RecordedDate.ToString("yyyy-MM-dd");
            await clientId.FillAsync(scenario.ClientId.ToString());
            await entityType.SelectOptionAsync("Visit");
            await recordedFrom.FillAsync(recordedDate);
            await recordedThrough.FillAsync(recordedDate);
            await action.SelectOptionAsync("visit.marked");
            await applyFilters.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains(
                $"clientId={scenario.ClientId}",
                page.Url,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("entity=Visit", page.Url, StringComparison.Ordinal);
            Assert.Contains("action=visit.marked", page.Url, StringComparison.Ordinal);
            var firstPageRows = page.Locator("[data-audit-entry-list] > .audit-entry");
            Assert.Equal(scenario.PageSize, await firstPageRows.CountAsync());
            var actions = await firstPageRows.EvaluateAllAsync<string[]>(
                "rows => rows.map(row => row.dataset.actionType)");
            Assert.All(actions, value => Assert.Equal("visit.marked", value));
            var entityTypes = await firstPageRows.EvaluateAllAsync<string[]>(
                "rows => rows.map(row => row.dataset.entityType)");
            Assert.All(entityTypes, value => Assert.Equal("Visit", value));

            var featured = page.Locator(
                $"[data-audit-entry-id='{scenario.FeaturedAuditEntryId}']");
            Assert.Equal("PaperFallback", await featured.GetAttributeAsync("data-entry-origin"));
            Assert.Equal(
                "SharedReceptionAdmin",
                await featured.GetAttributeAsync("data-account-kind"));
            Assert.Equal("true", await featured.GetAttributeAsync("data-changed-after-close"));
            await ExpectVisibleAsync(
                featured.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Visit marked", Exact = true }),
                viewportName,
                "featured audit action");
            await ExpectVisibleAsync(
                featured.GetByText("Paper fallback", new() { Exact = true }),
                viewportName,
                "paper fallback label");
            await ExpectVisibleAsync(
                featured.GetByText("Changed after close", new() { Exact = true }),
                viewportName,
                "changed-after-close label");
            await ExpectVisibleAsync(
                featured.GetByText("Shared Reception/Admin", new() { Exact = true }),
                viewportName,
                "shared account label");
            await ExpectVisibleAsync(
                featured.GetByText(scenario.SharedDeviceLabel, new() { Exact = true }),
                viewportName,
                "shared device label");
            await ExpectVisibleAsync(
                featured.GetByText(
                    scenario.SharedSessionId.ToString("N")[..8],
                    new() { Exact = true }),
                viewportName,
                "shared session label");
            await ExpectVisibleAsync(
                featured.GetByText(
                    $"{scenario.FeaturedOccurredAt:yyyy-MM-dd HH:mm:ss} UTC",
                    new() { Exact = true }),
                viewportName,
                "fallback occurred time");
            await ExpectVisibleAsync(
                featured.GetByText(
                    $"{scenario.FeaturedRecordedAt:yyyy-MM-dd HH:mm:ss} UTC",
                    new() { Exact = true }),
                viewportName,
                "fallback recorded time");
            await ExpectVisibleAsync(
                featured.GetByText("Recovered from paper register", new() { Exact = true }),
                viewportName,
                "fallback reason");
            await ExpectVisibleAsync(
                featured.GetByText(
                    "Entered after reception connectivity returned",
                    new() { Exact = true }),
                viewportName,
                "fallback comment");

            var envelope = featured.Locator(".audit-envelope-details > summary");
            await AssertMinimumTouchTargetAsync(envelope, viewportName, "audit envelope toggle");
            await envelope.ClickAsync();
            await ExpectVisibleAsync(
                featured.GetByText(scenario.FeaturedCorrelationId, new() { Exact = true }),
                viewportName,
                "request correlation id");
            var envelopeText = await featured.Locator(".audit-json-grid").InnerTextAsync();
            Assert.Contains(scenario.ClientId.ToString(), envelopeText, StringComparison.Ordinal);
            Assert.Contains("remainingVisits", envelopeText, StringComparison.Ordinal);
            await AssertFitsViewportAsync(page, viewportName, "expanded audit envelope");
            await CaptureVisualAsync(page, viewportName, "audit-timeline");

            var next = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Next", Exact = true });
            await AssertMinimumTouchTargetAsync(next, viewportName, "next audit page");
            await next.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains($"offset={scenario.PageSize}", page.Url, StringComparison.Ordinal);
            Assert.Equal(
                scenario.TotalEntries - scenario.PageSize,
                await page.Locator("[data-audit-entry-list] > .audit-entry").CountAsync());
            Assert.Equal("visit.marked", await action.InputValueAsync());
            Assert.Equal(recordedDate, await recordedFrom.InputValueAsync());
            Assert.Equal(recordedDate, await recordedThrough.InputValueAsync());
            var previous = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Previous", Exact = true });
            await AssertMinimumTouchTargetAsync(previous, viewportName, "previous audit page");
            await AssertFitsViewportAsync(page, viewportName, "audit second page");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task InvalidOffsetKeepsAuditFiltersAndReturnsNoPartialTimeline()
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = 1024,
                Height = 768,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            await LoginAsync(
                page,
                _app.LoginName,
                _app.Password,
                "invalid audit offset smoke");
            var recordedDate = scenario.RecordedDate.ToString("yyyy-MM-dd");
            await page.GotoAsync(
                new Uri(
                    _app.BaseAddress,
                    $"/Audit/Timeline?clientId={scenario.ClientId}&entity=Visit&from={recordedDate}&to={recordedDate}&action=visit.marked&offset=-1")
                    .ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            var error = page.GetByRole(AriaRole.Alert);
            await ExpectVisibleAsync(error, "tablet", "invalid audit offset error");
            Assert.Contains(
                "Offset must be between 0 and 10000.",
                await error.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.Equal(
                recordedDate,
                await page.GetByLabel("Recorded from (UTC)", new() { Exact = true })
                    .InputValueAsync());
            Assert.Equal(
                "visit.marked",
                await page.GetByLabel("Business action", new() { Exact = true })
                    .InputValueAsync());
            Assert.Equal(0, await page.Locator("[data-audit-entry-list]").CountAsync());
            await AssertFitsViewportAsync(page, "tablet", "invalid audit filter");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task AuditTimelineRequiresAuthentication()
    {
        Assert.NotNull(_browser);
        var context = await _browser.NewContextAsync();

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(
                new Uri(_app.BaseAddress, "/Audit/Timeline").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            Assert.Contains("/Login", page.Url, StringComparison.Ordinal);
            Assert.Contains("ReturnUrl=%2FAudit%2FTimeline", page.Url, StringComparison.Ordinal);
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
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" }).FillAsync(loginName);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
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
            bounds.Width >= 44,
            $"{label} should be at least 44px wide on {viewportName}, but was {bounds.Width:F1}px.");
        Assert.True(
            bounds.Height >= 44,
            $"{label} should be at least 44px high on {viewportName}, but was {bounds.Height:F1}px.");
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
