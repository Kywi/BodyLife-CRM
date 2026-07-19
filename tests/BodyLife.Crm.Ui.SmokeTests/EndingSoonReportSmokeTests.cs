using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class EndingSoonReportSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public EndingSoonReportSmokeTests(ReceptionAppFixture app)
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
    public async Task EndingSoonReportFiltersAndPagesCanonicalMembershipState(
        string viewportName,
        int width,
        int height)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureEndingSoonReportScenarioAsync();
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
                $"{viewportName} ending-soon report smoke");
            await page.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Daily report", Exact = true })
                .ClickAsync();
            await page.WaitForURLAsync("**/Reports/Daily**");
            var endingSoonLink = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Ending soon", Exact = true });
            await AssertMinimumTouchTargetAsync(
                endingSoonLink,
                viewportName,
                "ending-soon report navigation");
            await endingSoonLink.ClickAsync();
            await page.WaitForURLAsync("**/Reports/EndingSoon**");

            Assert.Equal("Memberships ending soon - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Memberships ending soon", Exact = true }),
                viewportName,
                "ending-soon report heading");

            var asOfDate = page.GetByLabel("As-of date", new() { Exact = true });
            var daysAhead = page.GetByLabel("Days ahead", new() { Exact = true });
            var loadReport = page.GetByRole(
                AriaRole.Button,
                new() { Name = "Load report", Exact = true });
            await AssertMinimumTouchTargetAsync(
                asOfDate,
                viewportName,
                "as-of date input");
            await AssertMinimumTouchTargetAsync(
                daysAhead,
                viewportName,
                "days-ahead input");
            await AssertMinimumTouchTargetAsync(
                loadReport,
                viewportName,
                "load ending-soon report button");

            var selectedDate = scenario.AsOfDate.ToString("yyyy-MM-dd");
            await asOfDate.FillAsync(selectedDate);
            await daysAhead.FillAsync("3");
            await loadReport.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains($"asOf={selectedDate}", page.Url, StringComparison.Ordinal);
            Assert.Contains("days=3", page.Url, StringComparison.Ordinal);
            var filteredRows = page.Locator("[data-ending-soon-rows] > .ending-soon-row");
            Assert.Equal(7, await filteredRows.CountAsync());
            Assert.Equal(
                0,
                await page.GetByRole(AriaRole.Link, new() { Name = "Next", Exact = true })
                    .CountAsync());
            var filteredDaysLeft = await filteredRows.EvaluateAllAsync<int[]>(
                "rows => rows.map(row => Number(row.dataset.daysLeft))");
            Assert.All(filteredDaysLeft, daysLeft => Assert.InRange(daysLeft, 0, 3));

            await daysAhead.FillAsync("7");
            await loadReport.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains("days=7", page.Url, StringComparison.Ordinal);
            var firstPageRows = page.Locator(
                "[data-ending-soon-rows] > .ending-soon-row");
            Assert.Equal(scenario.PageSize, await firstPageRows.CountAsync());
            var zeroRemainingRow = firstPageRows.Filter(new LocatorFilterOptions
            {
                HasText = scenario.ZeroRemainingClientDisplayName,
            });
            Assert.Equal(
                scenario.ZeroRemainingClientId.ToString(),
                await zeroRemainingRow.GetAttributeAsync("data-client-id"));
            Assert.Equal("0", await zeroRemainingRow.GetAttributeAsync("data-days-left"));
            Assert.Equal(
                scenario.AsOfDate.ToString("yyyy-MM-dd"),
                await zeroRemainingRow.GetAttributeAsync("data-effective-end"));
            Assert.Equal(
                "0",
                await zeroRemainingRow.GetAttributeAsync("data-remaining-visits"));
            await ExpectVisibleAsync(
                zeroRemainingRow.GetByText("+380 67 820 00 01", new() { Exact = true }),
                viewportName,
                "ending-soon Client phone");
            await ExpectVisibleAsync(
                zeroRemainingRow.GetByText("Ending plan 01", new() { Exact = true }),
                viewportName,
                "ending-soon Membership type snapshot");
            await ExpectVisibleAsync(
                zeroRemainingRow.GetByText(
                    "Membership has no remaining visits.",
                    new() { Exact = true }),
                viewportName,
                "zero-remaining warning");
            await ExpectVisibleAsync(
                zeroRemainingRow.GetByText(
                    "Membership ends within 7 days.",
                    new() { Exact = true }),
                viewportName,
                "ending-soon warning");

            var extensionRow = firstPageRows.Filter(new LocatorFilterOptions
            {
                HasText = scenario.ExtensionClientDisplayName,
            });
            Assert.Equal(
                scenario.ExtensionClientId.ToString(),
                await extensionRow.GetAttributeAsync("data-client-id"));
            Assert.Equal("4", await extensionRow.GetAttributeAsync("data-days-left"));
            Assert.Equal(
                scenario.ExtensionEffectiveEndDate.ToString("yyyy-MM-dd"),
                await extensionRow.GetAttributeAsync("data-effective-end"));
            await ExpectVisibleAsync(
                extensionRow.GetByText("2", new() { Exact = true }),
                viewportName,
                "canonical extension days");
            var extensionDetails = extensionRow.GetByRole(
                AriaRole.Link,
                new() { Name = "Extension details", Exact = true });
            await ExpectVisibleAsync(
                extensionDetails,
                viewportName,
                "extension explanation navigation");

            var rowLinks = firstPageRows.Locator(".report-row-actions .secondary-link");
            await AssertMinimumTouchTargetsAsync(
                rowLinks,
                viewportName,
                "ending-soon row link");
            await AssertFitsViewportAsync(page, viewportName, "ending-soon first page");
            await CaptureVisualAsync(page, viewportName, "ending-soon-report");

            var next = page.GetByRole(AriaRole.Link, new() { Name = "Next", Exact = true });
            await next.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains($"offset={scenario.PageSize}", page.Url, StringComparison.Ordinal);
            Assert.Equal(
                scenario.TotalMemberships - scenario.PageSize,
                await page.Locator("[data-ending-soon-rows] > .ending-soon-row")
                    .CountAsync());
            var previous = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Previous", Exact = true });
            await AssertMinimumTouchTargetAsync(
                previous,
                viewportName,
                "previous ending-soon page");
            await previous.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            extensionRow = page.Locator("[data-ending-soon-rows] > .ending-soon-row")
                .Filter(new LocatorFilterOptions
                {
                    HasText = scenario.ExtensionClientDisplayName,
                });
            await extensionRow.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Extension details", Exact = true })
                .ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains(
                $"clientId={scenario.ExtensionClientId}",
                page.Url,
                StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(
                "#membership-extension-history-title",
                page.Url,
                StringComparison.Ordinal);
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = scenario.ExtensionClientDisplayName, Exact = true }),
                viewportName,
                "extension Client profile");
            await AssertFitsViewportAsync(page, viewportName, "extension Client profile");
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
                "invalid ending-soon threshold smoke");
            await page.GotoAsync(
                new Uri(
                    _app.BaseAddress,
                    "/Reports/EndingSoon?asOf=2050-03-10&days=366").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await ExpectVisibleAsync(
                page.GetByLabel("As-of date", new() { Exact = true }),
                "tablet",
                "ending-soon retry date filter");
            await ExpectVisibleAsync(
                page.GetByLabel("Days ahead", new() { Exact = true }),
                "tablet",
                "ending-soon retry threshold filter");
            var error = page.GetByRole(AriaRole.Alert);
            await ExpectVisibleAsync(error, "tablet", "ending-soon threshold error");
            Assert.Contains(
                "Days threshold must be between 0 and 365.",
                await error.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.Equal(0, await page.Locator("[data-ending-soon-rows]").CountAsync());
            await AssertFitsViewportAsync(page, "tablet", "invalid ending-soon threshold");
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
