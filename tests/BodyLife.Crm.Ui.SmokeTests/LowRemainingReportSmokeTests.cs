using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class LowRemainingReportSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public LowRemainingReportSmokeTests(ReceptionAppFixture app)
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
    public async Task LowRemainingReportFiltersAndPagesCanonicalMembershipState(
        string viewportName,
        int width,
        int height)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureLowRemainingReportScenarioAsync();
        Assert.InRange(
            scenario.TotalMemberships,
            scenario.PageSize + 1,
            scenario.PageSize * 2);
        Assert.InRange(
            scenario.ThresholdOneMemberships,
            1,
            scenario.PageSize);
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
                _app.LoginName,
                _app.Password,
                $"{viewportName} low-remaining report smoke");
            await page.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Daily report", Exact = true })
                .ClickAsync();
            await page.WaitForURLAsync("**/Reports/Daily**");
            var lowRemainingLink = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Low remaining", Exact = true });
            await AssertMinimumTouchTargetAsync(
                lowRemainingLink,
                viewportName,
                "low-remaining report navigation");
            await lowRemainingLink.ClickAsync();
            await page.WaitForURLAsync("**/Reports/LowRemaining**");

            Assert.Equal("Memberships low on visits - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Memberships low on visits", Exact = true }),
                viewportName,
                "low-remaining report heading");

            var asOfDate = page.GetByLabel("As-of date", new() { Exact = true });
            var threshold = page.GetByLabel(
                "Remaining visits at most",
                new() { Exact = true });
            var loadReport = page.GetByRole(
                AriaRole.Button,
                new() { Name = "Load report", Exact = true });
            Assert.Equal("2", await threshold.InputValueAsync());
            await AssertMinimumTouchTargetAsync(
                asOfDate,
                viewportName,
                "low-remaining as-of date input");
            await AssertMinimumTouchTargetAsync(
                threshold,
                viewportName,
                "remaining-visits threshold input");
            await AssertMinimumTouchTargetAsync(
                loadReport,
                viewportName,
                "load low-remaining report button");

            var selectedDate = scenario.AsOfDate.ToString("yyyy-MM-dd");
            await asOfDate.FillAsync(selectedDate);
            await threshold.FillAsync("1");
            await loadReport.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains($"asOf={selectedDate}", page.Url, StringComparison.Ordinal);
            Assert.Contains("remaining=1", page.Url, StringComparison.Ordinal);
            var filteredRows = page.Locator(
                "[data-low-remaining-rows] > .low-remaining-row");
            Assert.Equal(scenario.ThresholdOneMemberships, await filteredRows.CountAsync());
            Assert.Equal(
                0,
                await page.GetByRole(AriaRole.Link, new() { Name = "Next", Exact = true })
                    .CountAsync());
            var filteredRemainingVisits = await filteredRows.EvaluateAllAsync<int[]>(
                "rows => rows.map(row => Number(row.dataset.remainingVisits))");
            Assert.All(filteredRemainingVisits, remaining => Assert.True(remaining <= 1));

            await threshold.FillAsync("2");
            await loadReport.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains("remaining=2", page.Url, StringComparison.Ordinal);
            var firstPageRows = page.Locator(
                "[data-low-remaining-rows] > .low-remaining-row");
            Assert.Equal(scenario.PageSize, await firstPageRows.CountAsync());

            var negativeRow = firstPageRows.Filter(new LocatorFilterOptions
            {
                HasText = scenario.NegativeClientDisplayName,
            });
            Assert.Equal(
                scenario.NegativeClientId.ToString(),
                await negativeRow.GetAttributeAsync("data-client-id"));
            Assert.Equal("3", await negativeRow.GetAttributeAsync("data-visits-limit"));
            Assert.Equal("5", await negativeRow.GetAttributeAsync("data-counted-visits"));
            Assert.Equal("-2", await negativeRow.GetAttributeAsync("data-remaining-visits"));
            await ExpectVisibleAsync(
                negativeRow.GetByText("+380 67 830 00 01", new() { Exact = true }),
                viewportName,
                "low-remaining Client phone");
            await ExpectVisibleAsync(
                negativeRow.GetByText("Low visits plan 01", new() { Exact = true }),
                viewportName,
                "low-remaining Membership type snapshot");
            await ExpectVisibleAsync(
                negativeRow.GetByText(
                    "This membership has a negative visit balance.",
                    new() { Exact = true }),
                viewportName,
                "negative-balance warning");

            var zeroRow = firstPageRows.Filter(new LocatorFilterOptions
            {
                HasText = scenario.ZeroClientDisplayName,
            });
            Assert.Equal(
                scenario.ZeroClientId.ToString(),
                await zeroRow.GetAttributeAsync("data-client-id"));
            Assert.Equal("2", await zeroRow.GetAttributeAsync("data-visits-limit"));
            Assert.Equal("2", await zeroRow.GetAttributeAsync("data-counted-visits"));
            Assert.Equal("0", await zeroRow.GetAttributeAsync("data-remaining-visits"));
            await ExpectVisibleAsync(
                zeroRow.GetByText(
                    "This membership has no remaining visits.",
                    new() { Exact = true }),
                viewportName,
                "zero-remaining warning");

            var oneRemainingRow = firstPageRows.Filter(new LocatorFilterOptions
            {
                HasText = scenario.OneRemainingClientDisplayName,
            });
            Assert.Equal(
                scenario.OneRemainingClientId.ToString(),
                await oneRemainingRow.GetAttributeAsync("data-client-id"));
            Assert.Equal("3", await oneRemainingRow.GetAttributeAsync("data-visits-limit"));
            Assert.Equal("2", await oneRemainingRow.GetAttributeAsync("data-counted-visits"));
            Assert.Equal(
                "1",
                await oneRemainingRow.GetAttributeAsync("data-remaining-visits"));
            await ExpectVisibleAsync(
                oneRemainingRow.GetByText(
                    $"{scenario.OneRemainingLastVisitAt.UtcDateTime.ToString("g", System.Globalization.CultureInfo.GetCultureInfo(ReceptionAppFixture.WorkflowCulture))} UTC",
                    new() { Exact = true }),
                viewportName,
                "last counted Visit");
            await ExpectVisibleAsync(
                oneRemainingRow.GetByText(
                    "This membership has only one or two visits remaining.",
                    new() { Exact = true }),
                viewportName,
                "low-remaining warning");

            var rowLinks = firstPageRows.Locator(".report-row-actions .secondary-link");
            await AssertMinimumTouchTargetsAsync(
                rowLinks,
                viewportName,
                "low-remaining row link");
            await AssertFitsViewportAsync(page, viewportName, "low-remaining first page");
            await CaptureVisualAsync(page, viewportName, "low-remaining-report");

            var next = page.GetByRole(AriaRole.Link, new() { Name = "Next", Exact = true });
            await next.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains($"offset={scenario.PageSize}", page.Url, StringComparison.Ordinal);
            Assert.Equal(
                scenario.TotalMemberships - scenario.PageSize,
                await page.Locator("[data-low-remaining-rows] > .low-remaining-row")
                    .CountAsync());
            var previous = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Previous", Exact = true });
            await AssertMinimumTouchTargetAsync(
                previous,
                viewportName,
                "previous low-remaining page");
            await previous.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            oneRemainingRow = page.Locator(
                    "[data-low-remaining-rows] > .low-remaining-row")
                .Filter(new LocatorFilterOptions
                {
                    HasText = scenario.OneRemainingClientDisplayName,
                });
            await oneRemainingRow.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Open client", Exact = true })
                .ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains(
                $"clientId={scenario.OneRemainingClientId}",
                page.Url,
                StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("#membership-panel-title", page.Url, StringComparison.Ordinal);
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = scenario.OneRemainingClientDisplayName, Exact = true }),
                viewportName,
                "low-remaining Client profile");
            await AssertFitsViewportAsync(page, viewportName, "low-remaining Client profile");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task InvalidThresholdKeepsFiltersAndNeverShowsMembershipRows()
    {
        Assert.NotNull(_browser);
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
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
                "invalid low-remaining threshold smoke");
            await page.GotoAsync(
                new Uri(
                    _app.BaseAddress,
                    "/Reports/LowRemaining?asOf=2051-04-15&remaining=-1").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await ExpectVisibleAsync(
                page.GetByLabel("As-of date", new() { Exact = true }),
                "tablet",
                "low-remaining retry date filter");
            await ExpectVisibleAsync(
                page.GetByLabel("Remaining visits at most", new() { Exact = true }),
                "tablet",
                "low-remaining retry threshold filter");
            var error = page.GetByRole(AriaRole.Alert);
            await ExpectVisibleAsync(error, "tablet", "low-remaining threshold error");
            Assert.Contains(
                "Enter valid report filters.",
                await error.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.Equal(0, await page.Locator("[data-low-remaining-rows]").CountAsync());
            await AssertFitsViewportAsync(page, "tablet", "invalid low-remaining threshold");
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
