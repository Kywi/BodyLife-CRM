using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class NegativeClientsReportSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public NegativeClientsReportSmokeTests(ReceptionAppFixture app)
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
    public async Task NegativeClientsReportPagesCanonicalDebtAndVisitProvenance(
        string viewportName,
        int width,
        int height)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureNegativeClientsReportScenarioAsync();
        Assert.InRange(
            scenario.TotalMemberships,
            scenario.PageSize + 2,
            scenario.PageSize * 2);
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
                $"{viewportName} negative-clients report smoke");
            await page.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Daily report", Exact = true })
                .ClickAsync();
            await page.WaitForURLAsync("**/Reports/Daily**");
            var negativeClientsLink = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Negative clients", Exact = true });
            await AssertMinimumTouchTargetAsync(
                negativeClientsLink,
                viewportName,
                "negative-clients report navigation");
            await negativeClientsLink.ClickAsync();
            await page.WaitForURLAsync("**/Reports/NegativeClients**");

            Assert.Equal("Clients with negative visits - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Clients with negative visits", Exact = true }),
                viewportName,
                "negative-clients report heading");

            var asOfDate = page.GetByLabel("As-of date", new() { Exact = true });
            var loadReport = page.GetByRole(
                AriaRole.Button,
                new() { Name = "Load report", Exact = true });
            await AssertMinimumTouchTargetAsync(
                asOfDate,
                viewportName,
                "negative-clients as-of date input");
            await AssertMinimumTouchTargetAsync(
                loadReport,
                viewportName,
                "load negative-clients report button");

            var selectedDate = scenario.AsOfDate.ToString("yyyy-MM-dd");
            await asOfDate.FillAsync(selectedDate);
            await loadReport.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains($"asOf={selectedDate}", page.Url, StringComparison.Ordinal);
            var firstPageRows = page.Locator(
                "[data-negative-client-rows] > .negative-client-row");
            Assert.Equal(scenario.PageSize, await firstPageRows.CountAsync());
            var negativeBalances = await firstPageRows.EvaluateAllAsync<int[]>(
                "rows => rows.map(row => Number(row.dataset.negativeBalance))");
            Assert.All(negativeBalances, balance => Assert.True(balance > 0));
            var remainingVisits = await firstPageRows.EvaluateAllAsync<int[]>(
                "rows => rows.map(row => Number(row.dataset.remainingVisits))");
            Assert.All(remainingVisits, remaining => Assert.True(remaining < 0));

            var featuredRow = firstPageRows.Filter(new LocatorFilterOptions
            {
                HasText = scenario.FeaturedClientDisplayName,
            });
            Assert.Equal(
                scenario.FeaturedClientId.ToString(),
                await featuredRow.GetAttributeAsync("data-client-id"));
            Assert.Equal("3", await featuredRow.GetAttributeAsync("data-negative-balance"));
            Assert.Equal("-3", await featuredRow.GetAttributeAsync("data-remaining-visits"));
            Assert.Equal(
                scenario.FeaturedFirstNegativeVisitId.ToString(),
                await featuredRow.GetAttributeAsync("data-first-negative-visit-id"));
            Assert.Equal(
                scenario.FeaturedFirstNegativeVisitDate.ToString("yyyy-MM-dd"),
                await featuredRow.GetAttributeAsync("data-first-negative-visit-date"));
            Assert.Equal(
                scenario.FeaturedEffectiveEndDate.ToString("yyyy-MM-dd"),
                await featuredRow.GetAttributeAsync("data-effective-end"));
            await ExpectVisibleAsync(
                featuredRow.GetByText("+380 67 840 00 01", new() { Exact = true }),
                viewportName,
                "negative Client phone");
            await ExpectVisibleAsync(
                featuredRow.GetByText("Negative plan 01", new() { Exact = true }),
                viewportName,
                "negative Membership type snapshot");
            await ExpectVisibleAsync(
                featuredRow.GetByText("3 visits owed", new() { Exact = true }),
                viewportName,
                "canonical negative balance label");
            await ExpectVisibleAsync(
                featuredRow.GetByText(
                    scenario.FeaturedFirstNegativeVisitDate.ToString("yyyy-MM-dd"),
                    new() { Exact = true }),
                viewportName,
                "first-negative Visit date");
            await ExpectVisibleAsync(
                featuredRow.GetByText(
                    $"{scenario.FeaturedLastCountedVisitAt:yyyy-MM-dd HH:mm} UTC",
                    new() { Exact = true }),
                viewportName,
                "last counted Visit");
            await ExpectVisibleAsync(
                featuredRow.GetByText(
                    "Membership has a negative visit balance.",
                    new() { Exact = true }),
                viewportName,
                "negative-balance warning");
            await ExpectVisibleAsync(
                featuredRow.GetByRole(
                    AriaRole.Link,
                    new() { Name = "View visits", Exact = true }),
                viewportName,
                "first-negative Visit navigation");

            var rowLinks = firstPageRows.Locator(".report-row-actions .secondary-link");
            await AssertMinimumTouchTargetsAsync(
                rowLinks,
                viewportName,
                "negative-client row link");
            await AssertFitsViewportAsync(page, viewportName, "negative-clients first page");
            await CaptureVisualAsync(page, viewportName, "negative-clients-report");

            var next = page.GetByRole(AriaRole.Link, new() { Name = "Next", Exact = true });
            await next.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains($"offset={scenario.PageSize}", page.Url, StringComparison.Ordinal);
            var secondPageRows = page.Locator(
                "[data-negative-client-rows] > .negative-client-row");
            Assert.Equal(
                scenario.TotalMemberships - scenario.PageSize,
                await secondPageRows.CountAsync());
            var openingRow = secondPageRows.Filter(new LocatorFilterOptions
            {
                HasText = scenario.OpeningClientDisplayName,
            });
            Assert.Equal(
                scenario.OpeningClientId.ToString(),
                await openingRow.GetAttributeAsync("data-client-id"));
            Assert.Equal("1", await openingRow.GetAttributeAsync("data-negative-balance"));
            Assert.Equal("-1", await openingRow.GetAttributeAsync("data-remaining-visits"));
            Assert.True(string.IsNullOrEmpty(
                await openingRow.GetAttributeAsync("data-first-negative-visit-id")));
            Assert.True(string.IsNullOrEmpty(
                await openingRow.GetAttributeAsync("data-first-negative-visit-date")));
            await ExpectVisibleAsync(
                openingRow.GetByText("Not recorded", new() { Exact = true }),
                viewportName,
                "honest missing first-negative Visit provenance");
            await ExpectVisibleAsync(
                openingRow.GetByText("No counted visits", new() { Exact = true }),
                viewportName,
                "opening-state last Visit label");
            Assert.Equal(
                0,
                await openingRow.GetByRole(
                        AriaRole.Link,
                        new() { Name = "View visits", Exact = true })
                    .CountAsync());
            await AssertMinimumTouchTargetsAsync(
                openingRow.Locator(".report-row-actions .secondary-link"),
                viewportName,
                "opening-state row link");
            await AssertFitsViewportAsync(page, viewportName, "negative-clients second page");

            var previous = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Previous", Exact = true });
            await AssertMinimumTouchTargetAsync(
                previous,
                viewportName,
                "previous negative-clients page");
            await previous.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            featuredRow = page.Locator(
                    "[data-negative-client-rows] > .negative-client-row")
                .Filter(new LocatorFilterOptions
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
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = scenario.FeaturedClientDisplayName, Exact = true }),
                viewportName,
                "negative Client profile");
            await ExpectVisibleAsync(
                page.Locator(
                    $"[data-visit-id='{scenario.FeaturedFirstNegativeVisitId}']"),
                viewportName,
                "first-negative Visit source row");
            await AssertFitsViewportAsync(page, viewportName, "negative Client profile");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task InvalidOffsetKeepsDateFilterAndNeverShowsNegativeRows()
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
                "invalid negative-clients offset smoke");
            await page.GotoAsync(
                new Uri(
                    _app.BaseAddress,
                    "/Reports/NegativeClients?asOf=2052-05-20&offset=-1").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await ExpectVisibleAsync(
                page.GetByLabel("As-of date", new() { Exact = true }),
                "tablet",
                "negative-clients retry date filter");
            var error = page.GetByRole(AriaRole.Alert);
            await ExpectVisibleAsync(error, "tablet", "negative-clients offset error");
            Assert.Contains(
                "Offset must be between 0 and 10000.",
                await error.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.Equal(0, await page.Locator("[data-negative-client-rows]").CountAsync());
            await AssertFitsViewportAsync(page, "tablet", "invalid negative-clients offset");
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
