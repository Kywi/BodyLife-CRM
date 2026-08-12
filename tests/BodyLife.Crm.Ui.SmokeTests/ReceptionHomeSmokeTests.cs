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

    [Fact]
    public async Task PopulatedHomeRendersForEveryActorCultureAndTargetViewport()
    {
        await _app.EnsureReceptionHomeScenarioAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        var accounts = new[]
        {
            (Name: "owner", Login: _app.LoginName, Password: _app.Password),
            (Name: "named-admin", Login: _app.AdminLoginName, Password: _app.AdminPassword),
            (Name: "shared-reception", Login: _app.SharedAdminLoginName, Password: _app.SharedAdminPassword),
        };
        var cultures = new[] { "uk-UA", "en-US" };
        var viewports = new[]
        {
            (Name: "tablet", Width: 1024, Height: 768),
            (Name: "phone", Width: 390, Height: 844),
        };

        foreach (var account in accounts)
        {
            foreach (var culture in cultures)
            {
                foreach (var viewport in viewports)
                {
                    await using var context = await browser.NewContextAsync(
                        new BrowserNewContextOptions
                        {
                            Locale = culture,
                            ViewportSize = new ViewportSize
                            {
                                Width = viewport.Width,
                                Height = viewport.Height,
                            },
                        });
                    var page = await context.NewPageAsync();
                    await page.GotoAsync(
                        _app.BaseAddress.ToString(),
                        new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                    await LoginAsync(
                        page,
                        account.Login,
                        account.Password,
                        $"Home · populated · {account.Name} · {culture} · {viewport.Name}");

                    Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
                    Assert.Equal(5, await page.Locator(".home-activity .activity-list > li").CountAsync());
                    Assert.Equal(2, await page.Locator(".home-attention a").CountAsync());
                    Assert.Equal(3, await page.Locator(".home-today .today-metrics dd").CountAsync());
                    Assert.Equal(0, await page.Locator(".home-page .status-message.status-danger").CountAsync());
                    await AssertFitsViewportAsync(
                        page,
                        $"{account.Name} {culture} {viewport.Name}",
                        "populated Home");
                }
            }
        }
    }

    [Theory]
    [InlineData("desktop", 1440, 900, 1)]
    [InlineData("tablet", 1024, 768, 1)]
    [InlineData("phone", 390, 844, 1)]
    public async Task HomeMatchesTheCurrentHomeCandidateAndKeepsItsWorkflowsReachable(
        string viewportName,
        int width,
        int height,
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

        var navigation = page.Locator("[data-nav-group='operations']");
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
            await page.Locator(".sidebar-navigation a[aria-current='page']").CountAsync());

        if (viewportName == "desktop")
        {
            var drawer = page.Locator("[data-app-drawer]");
            var drawerBounds = await drawer.BoundingBoxAsync();
            Assert.NotNull(drawerBounds);
            Assert.InRange(drawerBounds.Width, 236, 244);
            await AssertVisibleAsync(
                navigation.GetByRole(
                    AriaRole.Link,
                    new LocatorGetByRoleOptions { Name = "Клієнти", Exact = true }),
                viewportName,
                "desktop navigation labels");
        }

        var logo = page.Locator(".app-header-brand img");
        await AssertVisibleAsync(logo, viewportName, "BodyLife logo");
        var logoBounds = await logo.BoundingBoxAsync();
        Assert.NotNull(logoBounds);
        Assert.InRange(logoBounds.Width, 27, 29);
        Assert.True(
            logoBounds.Height > logoBounds.Width,
            $"{viewportName} logo should retain the supplied portrait mark.");
        Assert.Contains(
            "/images/bodylife-logo.png",
            await logo.GetAttributeAsync("src") ?? string.Empty,
            StringComparison.Ordinal);

        var currentSession = page.Locator("details.account-menu");
        await AssertVisibleAsync(
            currentSession,
            viewportName,
            "truthful current session");
        var currentSessionSummary = currentSession.Locator("summary");
        var compactAccountLabel = currentSessionSummary
            .Locator(".account-menu-summary-copy span");
        Assert.Equal("Власник", await compactAccountLabel.InnerTextAsync());
        if (viewportName == "phone")
        {
            await AssertVisibleAsync(
                compactAccountLabel,
                viewportName,
                "compact account role");
        }

        await currentSessionSummary.ClickAsync();
        Assert.Contains(
            "Обліковий запис власника / Власник",
            await currentSession.InnerTextAsync(),
            StringComparison.Ordinal);
        Assert.Contains(
            deviceLabel,
            await currentSession.InnerTextAsync(),
            StringComparison.Ordinal);
        await currentSessionSummary.ClickAsync();

        if (viewportName is "tablet" or "phone")
        {
            var drawer = page.Locator("[data-app-drawer]");
            Assert.True(
                await drawer.EvaluateAsync<bool>("element => element.inert"),
                $"{viewportName} drawer should be inert while closed.");

            var headerBounds = await page.Locator(".app-global-header")
                .BoundingBoxAsync();
            var toggleBounds = await page.Locator("[data-drawer-toggle]")
                .BoundingBoxAsync();
            var brandBounds = await page.Locator(".app-header-brand")
                .BoundingBoxAsync();
            var searchBounds = await page.Locator(".global-client-search")
                .BoundingBoxAsync();
            var createBounds = await page.Locator(".global-create-client")
                .BoundingBoxAsync();
            var accountBounds = await currentSession.BoundingBoxAsync();
            Assert.NotNull(headerBounds);
            Assert.NotNull(toggleBounds);
            Assert.NotNull(brandBounds);
            Assert.NotNull(searchBounds);
            Assert.NotNull(createBounds);
            Assert.NotNull(accountBounds);

            if (viewportName == "tablet")
            {
                Assert.InRange(headerBounds.Height, 76, 82);
                Assert.InRange(
                    searchBounds.X - (brandBounds.X + brandBounds.Width),
                    8,
                    20);
                Assert.True(
                    searchBounds.Width >= 300,
                    "Tablet Search should receive the flexible header width.");
                Assert.InRange(Math.Abs(searchBounds.Y - createBounds.Y), 0, 3);
                Assert.InRange(Math.Abs(searchBounds.Y - accountBounds.Y), 0, 3);
            }
            else
            {
                var firstRowBottom = new[]
                {
                    toggleBounds.Y + toggleBounds.Height,
                    brandBounds.Y + brandBounds.Height,
                    accountBounds.Y + accountBounds.Height,
                }.Max();
                Assert.True(
                    searchBounds.Y >= firstRowBottom,
                    "Phone Search should start below the brand/account row.");
                Assert.True(
                    createBounds.Y >= searchBounds.Y + searchBounds.Height,
                    "Phone Create client should follow Search on its own row.");
            }
        }

        if (viewportName != "desktop")
        {
            await OpenDrawerAsync(page);
        }
        await AssertVisibleAsync(
            page.Locator("[data-nav-group='owner']"),
            viewportName,
            "Owner tools navigation");
        if (viewportName != "desktop")
        {
            await CloseDrawerAsync(page);
        }

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

        var timestampContrast = await activityRows.First
            .Locator(".activity-times")
            .EvaluateAsync<double>(
                """
                element => {
                  const channels = (color) => (color.match(/[\d.]+/g) ?? [])
                    .slice(0, 3)
                    .map((channel) => Number(channel) / 255);
                  const luminance = (color) => channels(color)
                    .map((channel) => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4)
                    .reduce((total, channel, index) => total + channel * [0.2126, 0.7152, 0.0722][index], 0);
                  const foreground = luminance(getComputedStyle(element).color);
                  const background = luminance(getComputedStyle(element.closest('li')).backgroundColor);
                  return (Math.max(foreground, background) + 0.05)
                    / (Math.min(foreground, background) + 0.05);
                }
                """);
        Assert.True(
            timestampContrast >= 4.5,
            $"{viewportName} Activity timestamps should meet WCAG AA contrast; actual ratio was {timestampContrast:F2}.");

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

        var searchForm = page.Locator("form.global-client-search");
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

        if (viewportName != "desktop")
        {
            await OpenDrawerAsync(page);
        }
        await AssertMinimumTouchTargetsAsync(
            navigation.Locator("a.navigation-link"),
            viewportName,
            "primary navigation");
        await AssertMinimumTouchTargetsAsync(
            page.Locator("[data-nav-group='owner'] a.navigation-link"),
            viewportName,
            "Owner tools");
        if (viewportName != "desktop")
        {
            await CloseDrawerAsync(page);
        }
        await AssertMinimumTouchTargetAsync(
            page.Locator(".global-client-search button"),
            viewportName,
            "global search");
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

        var searchButton = page.Locator(".global-client-search button");
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

        var skipLink = page.Locator(".skip-link");
        var hiddenSkipLinkBounds = await skipLink.BoundingBoxAsync();
        Assert.NotNull(hiddenSkipLinkBounds);
        Assert.True(
            hiddenSkipLinkBounds.Y < 0,
            $"{viewportName} skip link should stay off-canvas before focus.");
        await skipLink.FocusAsync();
        var focusedSkipLinkBounds = await skipLink.BoundingBoxAsync();
        Assert.NotNull(focusedSkipLinkBounds);
        Assert.True(
            focusedSkipLinkBounds.Y >= 0,
            $"{viewportName} skip link should become visible on focus.");
        Assert.True(await skipLink.EvaluateAsync<bool>("element => element === document.activeElement"));
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");

        await AssertFitsViewportAsync(page, viewportName, "Home");
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
        Assert.Equal(0, await page.Locator("#global-client-search").CountAsync());
        Assert.Equal(1, await page.Locator("#client-search").CountAsync());
        Assert.Equal(1, await page.Locator("form.global-client-search").CountAsync());

        var fallbackQuery = $"wave1-no-match-{viewportName}";
        await page.Locator("#client-search").FillAsync(fallbackQuery);
        var searchResponse = page.WaitForResponseAsync(response =>
            response.Url.Contains("handler=Search", StringComparison.OrdinalIgnoreCase));
        await page.Locator("#reception-search button").ClickAsync();
        Assert.True((await searchResponse).Ok);
        await page.WaitForURLAsync($"**q={fallbackQuery}**");
        var fallbackUri = new Uri(page.Url);
        Assert.Equal("/Reception", fallbackUri.AbsolutePath);
        Assert.Contains("q=wave1-no-match", fallbackUri.Query, StringComparison.Ordinal);
        Assert.Equal(fallbackQuery, await page.Locator("#client-search").InputValueAsync());
    }

    private async Task LoginAsync(IPage page, string deviceLabel)
    {
        await LoginAsync(page, _app.LoginName, _app.Password, deviceLabel);
    }

    private static async Task LoginAsync(
        IPage page,
        string loginName,
        string password,
        string deviceLabel)
    {
        await page.Locator("#LoginName").FillAsync(loginName);
        await page.Locator("#Password").FillAsync(password);
        await page.Locator("#DeviceLabel").FillAsync(deviceLabel);
        await page.Locator("form.auth-form button[type='submit']").ClickAsync();
        await page.WaitForURLAsync("**/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task OpenDrawerAsync(IPage page)
    {
        var toggle = page.Locator("[data-drawer-toggle]");
        await toggle.ClickAsync();
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        await AssertVisibleAsync(page.Locator("[data-app-drawer].is-open"), "drawer", "open navigation drawer");
    }

    private static async Task CloseDrawerAsync(IPage page)
    {
        await page.Locator("[data-drawer-close]").ClickAsync();
        Assert.Equal("false", await page.Locator("[data-drawer-toggle]").GetAttributeAsync("aria-expanded"));
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
                FullPage = false,
                Path = Path.Combine(
                    screenshotDirectory,
                    $"wave1-home-{viewportName}-{width}x{height}-uk.png"),
            });
        await page.ScreenshotAsync(
            new PageScreenshotOptions
            {
                Animations = ScreenshotAnimations.Disabled,
                FullPage = true,
                Path = Path.Combine(
                    screenshotDirectory,
                    $"wave1-home-{viewportName}-full-{width}x{height}-uk.png"),
            });
    }
}
