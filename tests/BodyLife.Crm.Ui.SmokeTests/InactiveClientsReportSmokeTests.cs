using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class InactiveClientsReportSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public InactiveClientsReportSmokeTests(ReceptionAppFixture app)
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
    [InlineData("tablet", 1024, 768)]
    [InlineData("phone", 390, 844)]
    public async Task InactiveClientsReportPreservesThresholdsAndCanonicalSourceState(
        string viewportName,
        int width,
        int height)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureInactiveClientsReportScenarioAsync();
        Assert.Equal(scenario.PageSize + 1, scenario.KnownInactiveClientCount);
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
                _app.LoginName,
                _app.Password,
                $"{viewportName} inactive-clients report smoke");
            await page.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Daily report", Exact = true })
                .ClickAsync();
            await page.WaitForURLAsync("**/Reports/Daily**");
            var inactiveClientsLink = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Inactive clients", Exact = true });
            await AssertMinimumTouchTargetAsync(
                inactiveClientsLink,
                viewportName,
                "inactive-clients report navigation");
            await inactiveClientsLink.ClickAsync();
            await page.WaitForURLAsync("**/Reports/InactiveClients**");

            Assert.Equal("Inactive clients - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Inactive clients", Exact = true }),
                viewportName,
                "inactive-clients report heading");
            Assert.Equal(0, await page.Locator("[data-inactive-client-rows]").CountAsync());

            var asOfDate = page.GetByLabel("As-of date", new() { Exact = true });
            var threshold = page.GetByLabel(
                "Inactive for at least",
                new() { Exact = true });
            var includeNeverVisited = page.GetByLabel(
                "Include never visited",
                new() { Exact = true });
            var includeNeverVisitedControl = page.Locator(
                "label[for='inactive-clients-include-no-visits']");
            var loadReport = page.GetByRole(
                AriaRole.Button,
                new() { Name = "Load report", Exact = true });
            await AssertMinimumTouchTargetAsync(
                asOfDate,
                viewportName,
                "inactive-clients as-of date input");
            await AssertMinimumTouchTargetAsync(
                threshold,
                viewportName,
                "inactive-clients threshold selector");
            await AssertMinimumTouchTargetAsync(
                includeNeverVisitedControl,
                viewportName,
                "include never-visited control");
            await AssertMinimumTouchTargetAsync(
                loadReport,
                viewportName,
                "load inactive-clients report button");

            var selectedDate = scenario.AsOfDate.ToString("yyyy-MM-dd");
            await asOfDate.FillAsync(selectedDate);
            await threshold.SelectOptionAsync("14");
            await loadReport.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains($"asOf={selectedDate}", page.Url, StringComparison.Ordinal);
            Assert.Contains("threshold=14", page.Url, StringComparison.Ordinal);
            var firstPageRows = page.Locator(
                "[data-inactive-client-rows] > .inactive-client-row");
            Assert.Equal(scenario.PageSize, await firstPageRows.CountAsync());
            var daysInactive = await firstPageRows.EvaluateAllAsync<int[]>(
                "rows => rows.map(row => Number(row.dataset.daysInactive))");
            Assert.Equal([60, 45, 30, 29, 25, 21, 20, 18, 17, 15], daysInactive);

            var featuredRow = firstPageRows.Filter(new LocatorFilterOptions
            {
                HasText = scenario.FeaturedClientDisplayName,
            });
            Assert.Equal(
                scenario.FeaturedClientId.ToString(),
                await featuredRow.GetAttributeAsync("data-client-id"));
            Assert.Equal(
                scenario.FeaturedLastVisitId.ToString(),
                await featuredRow.GetAttributeAsync("data-last-visit-id"));
            Assert.Equal("60", await featuredRow.GetAttributeAsync("data-days-inactive"));
            Assert.Equal("Trial", await featuredRow.GetAttributeAsync("data-last-visit-kind"));
            Assert.Equal("Inactive", await featuredRow.GetAttributeAsync("data-operational-status"));
            Assert.Equal(
                scenario.FeaturedMembershipId.ToString(),
                await featuredRow.GetAttributeAsync("data-membership-id"));
            Assert.Equal(
                "Current",
                await featuredRow.GetAttributeAsync("data-membership-summary-kind"));
            await ExpectVisibleAsync(
                featuredRow.GetByText("+380 67 850 00 01", new() { Exact = true }),
                viewportName,
                "inactive Client phone");
            await ExpectVisibleAsync(
                featuredRow.GetByText("BL-INACTIVE-01", new() { Exact = true }),
                viewportName,
                "inactive Client card");
            await ExpectVisibleAsync(
                featuredRow.GetByText("60 days inactive", new() { Exact = true }),
                viewportName,
                "canonical days-inactive value");
            await ExpectVisibleAsync(
                featuredRow.GetByText("Client inactive", new() { Exact = true }),
                viewportName,
                "separate operational status");
            await ExpectVisibleAsync(
                featuredRow.GetByText(
                    $"{scenario.FeaturedLastVisitAt:yyyy-MM-dd HH:mm} UTC",
                    new() { Exact = true }),
                viewportName,
                "last active Visit timestamp");
            await ExpectVisibleAsync(
                featuredRow.GetByText(
                    "Current: Inactive current plan",
                    new() { Exact = true }),
                viewportName,
                "current Membership summary");
            await ExpectVisibleAsync(
                featuredRow.GetByText(
                    scenario.FeaturedEffectiveEndDate.ToString("yyyy-MM-dd"),
                    new() { Exact = true }),
                viewportName,
                "canonical Membership effective end");
            await AssertMinimumTouchTargetsAsync(
                firstPageRows.Locator(".report-row-actions .secondary-link"),
                viewportName,
                "inactive-client row link");
            await AssertFitsViewportAsync(page, viewportName, "inactive-clients first page");

            await page.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Next", Exact = true })
                .ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains($"offset={scenario.PageSize}", page.Url, StringComparison.Ordinal);
            Assert.Contains("threshold=14", page.Url, StringComparison.Ordinal);
            var secondPageRows = page.Locator(
                "[data-inactive-client-rows] > .inactive-client-row");
            Assert.Equal(1, await secondPageRows.CountAsync());
            var boundaryRow = secondPageRows.Filter(new LocatorFilterOptions
            {
                HasText = scenario.BoundaryClientDisplayName,
            });
            Assert.Equal(
                scenario.BoundaryClientId.ToString(),
                await boundaryRow.GetAttributeAsync("data-client-id"));
            Assert.Equal("14", await boundaryRow.GetAttributeAsync("data-days-inactive"));

            await page.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Previous", Exact = true })
                .ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            threshold = page.GetByLabel("Inactive for at least", new() { Exact = true });
            await threshold.SelectOptionAsync("30");
            await page.GetByRole(
                    AriaRole.Button,
                    new() { Name = "Load report", Exact = true })
                .ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var thresholdThirtyRows = page.Locator(
                "[data-inactive-client-rows] > .inactive-client-row");
            Assert.Equal(
                scenario.ThresholdThirtyClientCount,
                await thresholdThirtyRows.CountAsync());
            var lastMembershipRow = thresholdThirtyRows.Filter(new LocatorFilterOptions
            {
                HasText = scenario.LastMembershipClientDisplayName,
            });
            Assert.Equal(
                scenario.LastMembershipClientId.ToString(),
                await lastMembershipRow.GetAttributeAsync("data-client-id"));
            Assert.Equal(
                scenario.LastMembershipId.ToString(),
                await lastMembershipRow.GetAttributeAsync("data-membership-id"));
            Assert.Equal(
                "Last",
                await lastMembershipRow.GetAttributeAsync("data-membership-summary-kind"));
            await ExpectVisibleAsync(
                lastMembershipRow.GetByText(
                    "Last: Inactive last plan",
                    new() { Exact = true }),
                viewportName,
                "last Membership summary");

            threshold = page.GetByLabel("Inactive for at least", new() { Exact = true });
            includeNeverVisited = page.GetByLabel(
                "Include never visited",
                new() { Exact = true });
            await threshold.SelectOptionAsync("60");
            await includeNeverVisited.CheckAsync();
            await page.GetByRole(
                    AriaRole.Button,
                    new() { Name = "Load report", Exact = true })
                .ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains("threshold=60", page.Url, StringComparison.Ordinal);
            Assert.Contains("includeNoVisits=true", page.Url, StringComparison.OrdinalIgnoreCase);
            var thresholdSixtyRows = page.Locator(
                "[data-inactive-client-rows] > .inactive-client-row");
            Assert.Equal(scenario.PageSize, await thresholdSixtyRows.CountAsync());
            Assert.Equal(
                scenario.ThresholdSixtyClientCount,
                (await thresholdSixtyRows.EvaluateAllAsync<int[]>(
                    "rows => rows.map(row => row.dataset.daysInactive ? 1 : 0)"))
                    .Sum());
            var neverVisitedRow = thresholdSixtyRows.Filter(new LocatorFilterOptions
            {
                HasText = scenario.NeverVisitedClientDisplayName,
            });
            Assert.Equal(
                scenario.NeverVisitedClientId.ToString(),
                await neverVisitedRow.GetAttributeAsync("data-client-id"));
            Assert.True(string.IsNullOrEmpty(
                await neverVisitedRow.GetAttributeAsync("data-last-visit-id")));
            Assert.True(string.IsNullOrEmpty(
                await neverVisitedRow.GetAttributeAsync("data-last-visit-date")));
            Assert.True(string.IsNullOrEmpty(
                await neverVisitedRow.GetAttributeAsync("data-days-inactive")));
            Assert.True(string.IsNullOrEmpty(
                await neverVisitedRow.GetAttributeAsync("data-membership-id")));
            await ExpectVisibleAsync(
                neverVisitedRow.GetByText("Never visited", new() { Exact = true }).First,
                viewportName,
                "never-visited label without an invented date");
            await ExpectVisibleAsync(
                neverVisitedRow.GetByText("No Membership", new() { Exact = true }),
                viewportName,
                "honest missing Membership summary");
            Assert.Equal(
                0,
                await neverVisitedRow.GetByRole(
                        AriaRole.Link,
                        new() { Name = "View visits", Exact = true })
                    .CountAsync());
            var nextPageLink = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Next", Exact = true });
            Assert.Contains(
                "includeNoVisits=True",
                await nextPageLink.GetAttributeAsync("href") ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            await AssertFitsViewportAsync(page, viewportName, "inactive-clients never-visited page");
            await CaptureVisualAsync(page, viewportName, "inactive-clients-report");

            featuredRow = thresholdSixtyRows.Filter(new LocatorFilterOptions
            {
                HasText = scenario.FeaturedClientDisplayName,
            });
            await featuredRow.GetByRole(
                    AriaRole.Link,
                    new() { Name = "View visits", Exact = true })
                .ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains(
                $"clientId={scenario.FeaturedClientId}",
                page.Url,
                StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("#recent-visits-title", page.Url, StringComparison.Ordinal);
            await ExpectVisibleAsync(
                page.Locator($"[data-visit-id='{scenario.FeaturedLastVisitId}']"),
                viewportName,
                "last active Visit source row");
            await AssertFitsViewportAsync(page, viewportName, "inactive Client profile");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task InvalidOffsetKeepsInactiveFiltersAndNeverShowsPartialRows()
    {
        Assert.NotNull(_browser);
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
                "invalid inactive-clients offset smoke");
            await page.GotoAsync(
                new Uri(
                    _app.BaseAddress,
                    "/Reports/InactiveClients?asOf=2025-05-20&threshold=14&includeNoVisits=true&offset=-1")
                    .ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            Assert.Equal(
                "2025-05-20",
                await page.GetByLabel("As-of date", new() { Exact = true }).InputValueAsync());
            Assert.Equal(
                "14",
                await page.GetByLabel(
                        "Inactive for at least",
                        new() { Exact = true })
                    .InputValueAsync());
            Assert.True(await page.GetByLabel(
                    "Include never visited",
                    new() { Exact = true })
                .IsCheckedAsync());
            var error = page.GetByRole(AriaRole.Alert);
            await ExpectVisibleAsync(error, "tablet", "inactive-clients offset error");
            Assert.Contains(
                "Offset must be between 0 and 10000.",
                await error.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.Equal(0, await page.Locator("[data-inactive-client-rows]").CountAsync());
            await AssertFitsViewportAsync(page, "tablet", "invalid inactive-clients offset");
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

    private static async Task AssertMinimumTouchTargetsAsync(
        ILocator locators,
        string viewportName,
        string label)
    {
        var count = await locators.CountAsync();
        Assert.True(count > 0, $"At least one {label} should exist on {viewportName} viewport.");

        for (var index = 0; index < count; index++)
        {
            await AssertMinimumTouchTargetAsync(
                locators.Nth(index),
                viewportName,
                $"{label} {index + 1}");
        }
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
