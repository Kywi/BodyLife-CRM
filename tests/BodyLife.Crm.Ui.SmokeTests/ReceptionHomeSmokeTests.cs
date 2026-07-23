using System.Globalization;
using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class ReceptionHomeSmokeTests : IClassFixture<ReceptionAppFixture>
{
    private readonly ReceptionAppFixture _app;

    public ReceptionHomeSmokeTests(ReceptionAppFixture app)
    {
        _app = app;
    }

    [Theory]
    [InlineData("tablet", 1024, 768, 76, 1)]
    [InlineData("phone", 390, 844, 56, 1)]
    [InlineData("reference-desktop", 736, 526, 76, 1.5)]
    [InlineData("reference-phone", 320, 844, 56, 1.5)]
    public async Task HomeMatchesTheApprovedReceptionAnchorAndKeepsItsWorkflowsReachable(
        string viewportName,
        int width,
        int height,
        int expectedLogoWidth,
        double deviceScaleFactor)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions
            {
                Headless = true,
            });
        await using var context = await browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                Locale = "uk-UA",
                DeviceScaleFactor = (float)deviceScaleFactor,
                ViewportSize = new ViewportSize
                {
                    Width = width,
                    Height = height,
                },
            });
        var page = await context.NewPageAsync();

        var unauthenticatedResponse = await page.GotoAsync(
            _app.BaseAddress.ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.NotNull(unauthenticatedResponse);
        Assert.True(
            unauthenticatedResponse.Ok,
            $"{viewportName} login request returned HTTP {unauthenticatedResponse.Status}.");
        Assert.Equal("/Login", new Uri(page.Url).AbsolutePath);

        var deviceLabel = $"Рецепція · {viewportName}";
        await LoginAsync(page, deviceLabel);
        var scenario = await _app.EnsureReceptionHomeScenarioAsync();

        var homeResponse = await page.GotoAsync(
            _app.BaseAddress.ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.NotNull(homeResponse);
        Assert.True(
            homeResponse.Ok,
            $"{viewportName} Home request returned HTTP {homeResponse.Status}.");
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);
        Assert.Equal(1, await page.Locator("main").CountAsync());

        var navigation = page.Locator(
            "section[aria-labelledby='navigation-main']");
        var homeLink = navigation.GetByRole(
            AriaRole.Link,
            new LocatorGetByRoleOptions { Name = "Головна", Exact = true });
        var clientsLink = navigation.GetByRole(
            AriaRole.Link,
            new LocatorGetByRoleOptions { Name = "Клієнти", Exact = true });

        Assert.Equal("page", await homeLink.GetAttributeAsync("aria-current"));
        Assert.Null(await clientsLink.GetAttributeAsync("aria-current"));
        Assert.Equal(
            1,
            await navigation.Locator("a[aria-current='page']").CountAsync());

        var logo = page.Locator(".sidebar-brand img");
        await AssertVisibleAsync(logo, viewportName, "BodyLife logo");
        var logoBounds = await logo.BoundingBoxAsync();
        Assert.NotNull(logoBounds);
        Assert.InRange(logoBounds.Width, expectedLogoWidth - 1, expectedLogoWidth + 1);
        Assert.True(
            logoBounds.Height > logoBounds.Width,
            $"{viewportName} logo should retain the supplied portrait mark.");
        Assert.Contains(
            "/images/bodylife-logo.png",
            await logo.GetAttributeAsync("src") ?? string.Empty,
            StringComparison.Ordinal);

        var currentSession = page.Locator(".top-context-bar");
        await AssertVisibleAsync(
            currentSession,
            viewportName,
            "truthful current session");
        Assert.Contains(
            deviceLabel,
            await currentSession.InnerTextAsync(),
            StringComparison.Ordinal);
        await AssertVisibleAsync(
            page.Locator("details.owner-tools"),
            viewportName,
            "Owner tools disclosure");

        var activityRows = page.Locator(".activity-list > li");
        Assert.Equal(5, await activityRows.CountAsync());
        Assert.Equal(
            5,
            await page.Locator(
                    ".activity-list > li[data-membership-selection='Single']")
                .CountAsync());
        await AssertVisibleClientAsync(
            page,
            scenario.ActiveClientDisplayName,
            viewportName);
        await AssertVisibleClientAsync(
            page,
            scenario.EndingSoonClientDisplayName,
            viewportName);
        await AssertVisibleClientAsync(
            page,
            scenario.NegativeClientDisplayName,
            viewportName);

        var activityText = await page.Locator(".home-activity").InnerTextAsync();
        Assert.Contains("Виправлення / скасування", activityText, StringComparison.Ordinal);
        Assert.Contains("Ручне перенесення", activityText, StringComparison.Ordinal);
        Assert.Contains("Паперовий запис", activityText, StringComparison.Ordinal);
        Assert.Contains("Скоро завершується", activityText, StringComparison.Ordinal);
        Assert.Contains("Від'ємний баланс", activityText, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualBackfill", activityText, StringComparison.Ordinal);
        Assert.DoesNotContain("PaperFallback", activityText, StringComparison.Ordinal);
        Assert.Contains("Внесено", activityText, StringComparison.Ordinal);
        Assert.Contains("Подія", activityText, StringComparison.Ordinal);

        var provenanceRows = page.Locator(
            ".activity-list > li[data-entry-origin='ManualBackfill'], "
            + ".activity-list > li[data-entry-origin='PaperFallback']");
        var provenanceRowCount = await provenanceRows.CountAsync();
        Assert.True(
            provenanceRowCount >= 2,
            "The Home fixture should expose both backfill and fallback provenance.");
        for (var index = 0; index < provenanceRowCount; index++)
        {
            var row = provenanceRows.Nth(index);
            var recordedAt = await row.Locator(".activity-recorded-at time")
                .GetAttributeAsync("datetime");
            var occurredAt = await row.Locator(".activity-occurred-at time")
                .GetAttributeAsync("datetime");
            Assert.False(string.IsNullOrWhiteSpace(recordedAt));
            Assert.False(string.IsNullOrWhiteSpace(occurredAt));
            Assert.NotEqual(recordedAt, occurredAt);
        }

        var activityDestinations = await activityRows
            .Locator("a.activity-open")
            .EvaluateAllAsync<string[]>(
                "links => links.map(link => link.getAttribute('href') ?? '')");
        Assert.Contains(
            activityDestinations,
            destination => destination.Contains(
                scenario.ActiveClientId.ToString(),
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            activityDestinations,
            destination => destination.Contains(
                scenario.EndingSoonClientId.ToString(),
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            activityDestinations,
            destination => destination.Contains(
                scenario.NegativeClientId.ToString(),
                StringComparison.OrdinalIgnoreCase));

        var searchForm = page.Locator(".home-rail form[role='search']");
        Assert.Equal("get", await searchForm.GetAttributeAsync("method"));
        Assert.Equal(
            "/Reception",
            (await searchForm.GetAttributeAsync("action"))?.TrimEnd('/'));
        Assert.Equal(
            "Auto",
            await searchForm.Locator("input[name='mode']").InputValueAsync());

        var metricValues = await page.Locator(".today-metrics dd")
            .AllInnerTextsAsync();
        Assert.Equal(3, metricValues.Count);
        Assert.All(
            metricValues,
            value => Assert.Matches(@"\d", value));
        Assert.Contains("₴", metricValues[2], StringComparison.Ordinal);

        var attention = page.Locator(".attention-warning, .attention-danger");
        Assert.Equal(2, await attention.CountAsync());
        await AssertVisibleAsync(
            page.Locator(".attention-warning a[href='/Reports/EndingSoon']"),
            viewportName,
            "ending-soon attention destination");
        await AssertVisibleAsync(
            page.Locator(".attention-danger a[href='/Reports/NegativeClients']"),
            viewportName,
            "negative-client attention destination");

        await AssertMinimumTouchTargetsAsync(
            navigation.Locator("a.navigation-link"),
            viewportName,
            "primary navigation");
        await AssertMinimumTouchTargetAsync(
            page.Locator("details.owner-tools > summary"),
            viewportName,
            "Owner tools");
        await AssertMinimumTouchTargetAsync(
            page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions
                {
                    Name = "Знайти клієнта",
                    Exact = true,
                }),
            viewportName,
            "quick search");
        await AssertMinimumTouchTargetAsync(
            page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions
                {
                    Name = "Створити клієнта",
                    Exact = true,
                }),
            viewportName,
            "direct create");
        await AssertMinimumTouchTargetsAsync(
            activityRows.Locator("a.activity-open"),
            viewportName,
            "activity client link");
        await AssertMinimumTouchTargetAsync(
            page.GetByRole(
                AriaRole.Link,
                new PageGetByRoleOptions
                {
                    Name = "Вся історія",
                    Exact = true,
                }),
            viewportName,
            "all history");
        await AssertMinimumTouchTargetAsync(
            page.Locator("a.home-report-link"),
            viewportName,
            "daily report");

        var searchButton = page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions
            {
                Name = "Знайти клієнта",
                Exact = true,
            });
        await searchButton.FocusAsync();
        var outlineStyle = await searchButton.EvaluateAsync<string>(
            "element => getComputedStyle(element).outlineStyle");
        var outlineWidth = await searchButton.EvaluateAsync<double>(
            "element => Number.parseFloat(getComputedStyle(element).outlineWidth)");
        Assert.NotEqual("none", outlineStyle);
        Assert.True(
            outlineWidth >= 2,
            $"{viewportName} Home focus outline should be at least 2px.");
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");

        await AssertFitsViewportAsync(page, viewportName, "Home");
        if (width <= 720)
        {
            Assert.Equal(
                "1",
                await page.Locator(".app-frame").EvaluateAsync<string>(
                    "element => getComputedStyle(element).gridTemplateColumns.split(' ').length.toString()"));
            var sidebarBounds = await page.Locator(".app-sidebar").BoundingBoxAsync();
            var homeBounds = await page.Locator(".home-page").BoundingBoxAsync();
            Assert.NotNull(sidebarBounds);
            Assert.NotNull(homeBounds);
            Assert.True(
                homeBounds.Y >= sidebarBounds.Y + sidebarBounds.Height,
                "Phone Home should stack below its navigation card.");
        }

        await PrepareDeterministicCaptureAsync(page, scenario.BusinessDate);
        await CaptureVisualAsync(page, viewportName, width, height);

        var createLink = page.GetByRole(
            AriaRole.Link,
            new PageGetByRoleOptions
            {
                Name = "Створити клієнта",
                Exact = true,
            });
        await createLink.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Equal(
            "/Reception",
            new Uri(page.Url).AbsolutePath);
        Assert.Contains(
            "create=true",
            new Uri(page.Url).Query,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(
            await page.Locator("#create-client-action-panel")
                .EvaluateAsync<bool>("element => element.open"),
            "Direct client creation should open without a failed search.");
    }

    private async Task LoginAsync(IPage page, string deviceLabel)
    {
        await page.Locator("#LoginName").FillAsync(_app.LoginName);
        await page.Locator("#Password").FillAsync(_app.Password);
        await page.Locator("#DeviceLabel").FillAsync(deviceLabel);
        await page.Locator("form.auth-form button[type='submit']").ClickAsync();
        await page.WaitForURLAsync("**/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task AssertVisibleClientAsync(
        IPage page,
        string displayName,
        string viewportName)
    {
        await AssertVisibleAsync(
            page.GetByText(
                displayName,
                new PageGetByTextOptions { Exact = true }).First,
            viewportName,
            $"{displayName} activity");
    }

    private static async Task AssertVisibleAsync(
        ILocator locator,
        string viewportName,
        string label)
    {
        await locator.WaitForAsync(
            new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 5_000,
            });
        Assert.True(
            await locator.IsVisibleAsync(),
            $"{label} should be visible on {viewportName}.");
    }

    private static async Task AssertMinimumTouchTargetsAsync(
        ILocator locators,
        string viewportName,
        string label)
    {
        var count = await locators.CountAsync();
        Assert.True(count > 0, $"At least one {label} should exist.");

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

    private static async Task AssertFitsViewportAsync(
        IPage page,
        string viewportName,
        string state)
    {
        var overflow = await page.EvaluateAsync<double>(
            "() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(
            overflow <= 1,
            $"{viewportName} {state} should not require horizontal scrolling; overflow was {overflow:F1}px.");
    }

    private static async Task PrepareDeterministicCaptureAsync(
        IPage page,
        DateOnly businessDate)
    {
        var stableDate = businessDate.ToString(
            "d MMMM",
            CultureInfo.GetCultureInfo("uk-UA"));
        await page.Locator(".home-date-badge").EvaluateAsync(
            """
            (element, text) => {
                const icon = element.querySelector('svg');
                element.replaceChildren();
                if (icon) {
                    element.append(icon);
                }
                element.append(document.createTextNode(` ${text} · 10:30`));
            }
            """,
            stableDate);
        await page.Locator(".session-id").EvaluateAsync(
            "(element) => element.textContent = 'Сеанс 12ab34cd'");
        await page.Locator(".skip-link").EvaluateAsync(
            "(element) => element.style.display = 'none'");
    }

    private static async Task CaptureVisualAsync(
        IPage page,
        string viewportName,
        int width,
        int height)
    {
        var screenshotDirectory = Environment.GetEnvironmentVariable(
            "BODYLIFE_UI_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            return;
        }

        Directory.CreateDirectory(screenshotDirectory);
        await page.ScreenshotAsync(
            new PageScreenshotOptions
            {
                Animations = ScreenshotAnimations.Disabled,
                FullPage = true,
                Path = Path.Combine(
                    screenshotDirectory,
                    $"wave1-home-{viewportName}-{width}x{height}-uk.png"),
            });
    }
}
