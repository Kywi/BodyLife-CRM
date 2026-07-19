using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class DailyReportSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public DailyReportSmokeTests(ReceptionAppFixture app)
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
    public async Task DailyReportShowsCanonicalTotalsAndSourceRowsOnTargetViewport(
        string viewportName,
        int width,
        int height)
    {
        Assert.NotNull(_browser);
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
                $"{viewportName} daily report smoke");

            var dailyReportLink = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Daily report", Exact = true });
            await ExpectVisibleAsync(
                dailyReportLink,
                viewportName,
                "daily report navigation");
            await AssertMinimumTouchTargetAsync(
                dailyReportLink,
                viewportName,
                "daily report navigation");
            await dailyReportLink.ClickAsync();
            await page.WaitForURLAsync("**/Reports/Daily**");

            Assert.Equal("Daily report - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(AriaRole.Heading, new() { Name = "Daily report" }),
                viewportName,
                "daily report heading");

            var businessDate = page.GetByLabel("Business date", new() { Exact = true });
            var loadReport = page.GetByRole(
                AriaRole.Button,
                new() { Name = "Load report", Exact = true });
            await AssertMinimumTouchTargetAsync(
                businessDate,
                viewportName,
                "business date input");
            await AssertMinimumTouchTargetAsync(
                loadReport,
                viewportName,
                "load report button");

            var selectedDate = _app.DailyReportBusinessDate.ToString("yyyy-MM-dd");
            await businessDate.FillAsync(selectedDate);
            await loadReport.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains($"date={selectedDate}", page.Url, StringComparison.Ordinal);
            Assert.Equal(selectedDate, await businessDate.InputValueAsync());
            Assert.Equal(
                "1",
                await page.Locator("[data-report-visit-count]").InnerTextAsync());
            Assert.Equal(
                "1",
                await page.Locator("[data-report-payment-count]").InnerTextAsync());
            Assert.Equal(
                "900.00 UAH",
                await page.Locator("[data-report-cash-sum]").InnerTextAsync());
            Assert.Equal(
                "open",
                await page.Locator("[data-report-day-status]").GetAttributeAsync(
                    "data-report-day-status"));

            var visitRows = page.Locator("[data-report-visit-rows] > .report-row");
            Assert.Equal(2, await visitRows.CountAsync());
            Assert.Equal(
                1,
                await page.Locator(
                    "[data-report-visit-rows] > .report-row[data-report-row-status='active']")
                    .CountAsync());
            Assert.Equal(
                1,
                await page.Locator(
                    "[data-report-visit-rows] > .report-row[data-report-row-status='canceled']")
                    .CountAsync());
            var canceledVisit = page.Locator(
                "[data-report-visit-rows] > .report-row[data-report-row-status='canceled']");
            await ExpectVisibleAsync(
                canceledVisit.GetByText("Duplicate report visit", new() { Exact = true }),
                viewportName,
                "Visit cancellation reason");
            await ExpectVisibleAsync(
                canceledVisit.GetByText("Paper fallback", new() { Exact = true }),
                viewportName,
                "Visit source label");

            var paymentRows = page.Locator("[data-report-payment-rows] > .report-row");
            Assert.Equal(3, await paymentRows.CountAsync());
            Assert.Equal(
                1,
                await page.Locator(
                    "[data-report-payment-rows] > .report-row[data-report-row-status='active']")
                    .CountAsync());
            Assert.Equal(
                1,
                await page.Locator(
                    "[data-report-payment-rows] > .report-row[data-report-row-status='canceled']")
                    .CountAsync());
            Assert.Equal(
                1,
                await page.Locator(
                    "[data-report-payment-rows] > .report-row[data-report-row-status='replaced']")
                    .CountAsync());
            Assert.Equal(2, await paymentRows.Locator("[data-report-correction]").CountAsync());
            await ExpectVisibleAsync(
                paymentRows.GetByText("Corrected replacement", new() { Exact = true }),
                viewportName,
                "replacement correction direction");
            await ExpectVisibleAsync(
                paymentRows.GetByText("Replaced payment", new() { Exact = true }),
                viewportName,
                "original correction direction");
            await ExpectVisibleAsync(
                paymentRows.GetByText(
                    "Changed: amount, occurred time",
                    new() { Exact = true }).First,
                viewportName,
                "Payment correction fields");
            await ExpectVisibleAsync(
                paymentRows.GetByText("Duplicate report payment", new() { Exact = true }),
                viewportName,
                "Payment cancellation reason");

            var profileLinks = page.Locator(".report-row-actions .secondary-link");
            Assert.Equal(5, await profileLinks.CountAsync());
            await AssertMinimumTouchTargetsAsync(
                profileLinks,
                viewportName,
                "report profile link");
            await AssertFitsViewportAsync(page, viewportName, "daily report");
            await CaptureVisualAsync(page, viewportName, "daily-report");

            await visitRows.First
                .GetByRole(AriaRole.Link, new() { Name = "Open client", Exact = true })
                .ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains(
                $"clientId={_app.DailyReportClientId}",
                page.Url,
                StringComparison.OrdinalIgnoreCase);
            await ExpectVisibleAsync(
                page.GetByRole(AriaRole.Heading, new() { Name = "Report Daily", Exact = true }),
                viewportName,
                "report Client profile");
            await AssertFitsViewportAsync(page, viewportName, "report Client profile");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task InvalidBusinessDateKeepsSelectorAndNeverShowsPartialTotals()
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
                "invalid daily report date smoke");
            await page.GotoAsync(
                new Uri(_app.BaseAddress, "/Reports/Daily?date=not-a-date").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await ExpectVisibleAsync(
                page.GetByLabel("Business date", new() { Exact = true }),
                "tablet",
                "business date retry selector");
            var error = page.GetByRole(AriaRole.Alert);
            await ExpectVisibleAsync(error, "tablet", "invalid date error");
            await ExpectVisibleAsync(
                error.GetByText("Enter a valid business date.", new() { Exact = true }),
                "tablet",
                "invalid date message");
            Assert.Equal(0, await page.Locator(".report-summary").CountAsync());
            Assert.Equal(0, await page.Locator("[data-report-visit-rows]").CountAsync());
            Assert.Equal(0, await page.Locator("[data-report-payment-rows]").CountAsync());
            await AssertFitsViewportAsync(page, "tablet", "invalid daily report date");
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
