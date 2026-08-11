using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class UiStyleCoverageSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public UiStyleCoverageSmokeTests(ReceptionAppFixture app)
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
    [InlineData("tablet", 1024, 768, true, false)]
    [InlineData("phone", 390, 844, false, false)]
    [InlineData("shared-phone", 390, 844, false, true)]
    public async Task AuthenticatedShellShowsRoleAppropriateNavigationWithoutHorizontalOverflow(
        string viewportName,
        int width,
        int height,
        bool isOwner,
        bool isSharedAccount)
    {
        Assert.NotNull(_browser);
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(
                page,
                isOwner ? _app.LoginName : isSharedAccount ? _app.SharedAdminLoginName : _app.AdminLoginName,
                isOwner ? _app.Password : isSharedAccount ? _app.SharedAdminPassword : _app.AdminPassword,
                $"{viewportName} branded shell");

            var logo = page.Locator(".app-header-brand img");
            await ExpectVisibleAsync(logo, viewportName, "authenticated BodyLife logo");
            Assert.Contains(
                "/images/bodylife-logo.png",
                await logo.GetAttributeAsync("src") ?? string.Empty,
                StringComparison.Ordinal);

            var mainNavigation = page.Locator("section[aria-labelledby='navigation-main']");
            var homeLink = mainNavigation.GetByRole(AriaRole.Link, new() { Name = "Home", Exact = true });
            var receptionLink = mainNavigation.GetByRole(AriaRole.Link, new() { Name = "Clients", Exact = true });
            var globalSearch = page.Locator("#global-client-search");
            await ExpectVisibleAsync(globalSearch, viewportName, "global client search");
            Assert.Equal(1, await page.Locator("#global-client-search").CountAsync());
            Assert.Equal(0, await page.Locator("#client-search").CountAsync());
            Assert.Equal("Auto", await page.Locator(".global-client-search input[name='mode']").InputValueAsync());
            Assert.Equal("get", await page.Locator(".global-client-search").GetAttributeAsync("method"));
            var accountMenu = page.Locator("details.account-menu");
            await ExpectVisibleAsync(accountMenu, viewportName, "honest account menu");
            Assert.Contains(
                isOwner ? "Owner account" : isSharedAccount ? "Shared Reception/Admin" : "Named Admin",
                await accountMenu.InnerTextAsync(),
                StringComparison.Ordinal);
            var accountSummary = accountMenu.Locator("summary");
            await accountSummary.ClickAsync();
            Assert.True(await accountMenu.EvaluateAsync<bool>("element => element.open"));
            await page.Keyboard.PressAsync("Escape");
            Assert.False(await accountMenu.EvaluateAsync<bool>("element => element.open"));
            Assert.True(await accountSummary.EvaluateAsync<bool>("element => document.activeElement === element"));
            await page.Locator("[data-drawer-toggle]").ClickAsync();
            await ExpectVisibleAsync(page.Locator("[data-app-drawer].is-open"), viewportName, "tablet drawer");
            Assert.True(await page.Locator(".app-main-column").EvaluateAsync<bool>("element => element.inert"));
            var drawerFocusables = page.Locator(
                "[data-app-drawer] a[href]:visible, " +
                "[data-app-drawer] button:not([disabled]):visible, " +
                "[data-app-drawer] input:not([disabled]):visible, " +
                "[data-app-drawer] select:not([disabled]):visible, " +
                "[data-app-drawer] summary:visible, " +
                "[data-app-drawer] textarea:not([disabled]):visible, " +
                "[data-app-drawer] [tabindex]:not([tabindex='-1']):visible");
            var drawerFocusableCount = await drawerFocusables.CountAsync();
            Assert.True(drawerFocusableCount > 1, "The open drawer should contain a complete focus loop.");
            var firstDrawerFocusable = drawerFocusables.First;
            var lastDrawerFocusable = drawerFocusables.Nth(drawerFocusableCount - 1);
            await lastDrawerFocusable.FocusAsync();
            await page.Keyboard.PressAsync("Tab");
            var activeAfterForwardWrap = await page.EvaluateAsync<string>(
                "() => `${document.activeElement?.tagName ?? 'none'}#${document.activeElement?.id ?? ''}.${document.activeElement?.className ?? ''}`");
            Assert.True(
                await firstDrawerFocusable.EvaluateAsync<bool>("element => document.activeElement === element"),
                $"Forward Tab should wrap to the first drawer control, but focused {activeAfterForwardWrap}.");
            await firstDrawerFocusable.FocusAsync();
            await page.Keyboard.PressAsync("Shift+Tab");
            Assert.True(await lastDrawerFocusable.EvaluateAsync<bool>("element => document.activeElement === element"));
            await ExpectVisibleAsync(homeLink, viewportName, "Home navigation");
            Assert.Equal("page", await homeLink.GetAttributeAsync("aria-current"));
            Assert.Null(await receptionLink.GetAttributeAsync("aria-current"));
            Assert.Equal(
                1,
                await page.Locator(".sidebar-navigation a[aria-current='page']").CountAsync());
            await AssertMinimumTouchTargetsAsync(
                mainNavigation.Locator(".navigation-link"),
                viewportName,
                "primary navigation action");

            if (isOwner)
            {
                var ownerGroup = page.Locator("[aria-labelledby='navigation-owner']");
                await ExpectVisibleAsync(ownerGroup, viewportName, "Owner tools navigation");
                Assert.Equal("DETAILS", await ownerGroup.EvaluateAsync<string>("element => element.tagName"));
                Assert.Equal(3, await ownerGroup.Locator("a.navigation-link").CountAsync());
                await AssertMinimumTouchTargetsAsync(
                    ownerGroup.Locator("summary"),
                    viewportName,
                    "Owner tools disclosure");
                await ownerGroup.Locator("summary").ClickAsync();
                await ExpectVisibleAsync(
                    ownerGroup.GetByRole(AriaRole.Link).First,
                    viewportName,
                    "Owner tools destination");
                await AssertMinimumTouchTargetsAsync(
                    ownerGroup.GetByRole(AriaRole.Link),
                    viewportName,
                    "Owner tools destination");
                await ownerGroup.Locator("summary").ClickAsync();
            }
            else
            {
                Assert.Equal(0, await page.Locator("#navigation-owner").CountAsync());
                Assert.Equal(0, await page.Locator("a[href^='/Owner/']").CountAsync());
            }

            await page.Keyboard.PressAsync("Escape");
            Assert.Equal("false", await page.Locator("[data-drawer-toggle]").GetAttributeAsync("aria-expanded"));
            Assert.False(await page.Locator(".app-main-column").EvaluateAsync<bool>("element => element.inert"));
            Assert.True(await page.Locator("[data-drawer-toggle]").EvaluateAsync<bool>("element => document.activeElement === element"));
            await page.Locator("[data-drawer-toggle]").ClickAsync();
            await page.Mouse.ClickAsync(width - 8, Math.Min(height / 2, 400));
            Assert.Equal("false", await page.Locator("[data-drawer-toggle]").GetAttributeAsync("aria-expanded"));
            await page.Locator("[data-drawer-toggle]").ClickAsync();
            await page.Locator("[data-drawer-close]").ClickAsync();

            await AssertNavigationStateAsync(page, "/Reception/Index", "Clients", "page");
            await AssertNavigationStateAsync(page, "/Reports/Daily", "Reports", "page");
            await AssertNavigationStateAsync(page, "/Audit/Timeline", "History", "page");
            Assert.Equal("false", await page.Locator("[data-drawer-toggle]").GetAttributeAsync("aria-expanded"));

            await page.GotoAsync(
                new Uri(_app.BaseAddress, "/Reports/EndingSoon").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            var reportsLink = page.Locator("section[aria-labelledby='navigation-main']")
                .GetByRole(AriaRole.Link, new() { Name = "Reports", Exact = true });
            await page.Locator("[data-drawer-toggle]").ClickAsync();
            await ExpectVisibleAsync(reportsLink, viewportName, "Reports section navigation");
            Assert.Equal("location", await reportsLink.GetAttributeAsync("aria-current"));
            Assert.Contains(
                "is-section-active",
                await reportsLink.GetAttributeAsync("class") ?? string.Empty,
                StringComparison.Ordinal);
            await page.Locator("[data-drawer-close]").ClickAsync();

            await page.GotoAsync(
                new Uri(_app.BaseAddress, $"/Audit/ClientHistory?clientId={_app.FreezeTabletClientId}").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("[data-drawer-toggle]").ClickAsync();
            var historyLink = page.Locator("section[aria-labelledby='navigation-main']")
                .GetByRole(AriaRole.Link, new() { Name = "History", Exact = true });
            Assert.Equal("location", await historyLink.GetAttributeAsync("aria-current"));
            await page.Locator("[data-drawer-close]").ClickAsync();

            await AssertFitsViewportAsync(page, viewportName, "authenticated navigation shell");
            await AssertSemanticTextContrastAsync(page, viewportName);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("tablet", 1024, 768)]
    [InlineData("phone", 390, 844)]
    public async Task PublicLoginAndStatusShellKeepTheBrandedLogoWithinTargetViewport(
        string viewportName,
        int width,
        int height)
    {
        Assert.NotNull(_browser);
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(new Uri(_app.BaseAddress, "/Login").ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });

            var publicLogo = page.Locator(".public-brand img");
            await ExpectVisibleAsync(publicLogo, viewportName, "public BodyLife logo");
            Assert.Contains(
                "/images/bodylife-logo.png",
                await publicLogo.GetAttributeAsync("src") ?? string.Empty,
                StringComparison.Ordinal);
            await AssertFitsViewportAsync(page, viewportName, "login public shell");

            await page.GotoAsync(new Uri(_app.BaseAddress, "/AccessDenied").ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await ExpectVisibleAsync(page.Locator("#access-denied-title"), viewportName, "access denied status");
            await ExpectVisibleAsync(page.Locator(".public-brand img"), viewportName, "status BodyLife logo");
            await AssertFitsViewportAsync(page, viewportName, "access denied public shell");

            await page.GotoAsync(new Uri(_app.BaseAddress, "/Error").ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await ExpectVisibleAsync(page.Locator("#error-title"), viewportName, "error status");
            await ExpectVisibleAsync(page.Locator(".public-brand img"), viewportName, "error BodyLife logo");
            await AssertFitsViewportAsync(page, viewportName, "error public shell");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private static async Task LoginAsync(IPage page, string loginName, string password, string deviceLabel)
    {
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" }).FillAsync(loginName);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private async Task AssertNavigationStateAsync(
        IPage page,
        string path,
        string linkName,
        string expectedState)
    {
        await page.GotoAsync(new Uri(_app.BaseAddress, path).ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.Locator("[data-drawer-toggle]").ClickAsync();
        var link = page.Locator("section[aria-labelledby='navigation-main']")
            .GetByRole(AriaRole.Link, new() { Name = linkName, Exact = true });
        Assert.Equal(expectedState, await link.GetAttributeAsync("aria-current"));
        await page.Locator("[data-drawer-close]").ClickAsync();
    }

    private static async Task ExpectVisibleAsync(ILocator locator, string viewportName, string label)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5_000,
        });
        Assert.True(await locator.IsVisibleAsync(), $"{label} should be visible on {viewportName} viewport.");
    }

    private static async Task AssertMinimumTouchTargetsAsync(ILocator locators, string viewportName, string label)
    {
        var count = await locators.CountAsync();
        Assert.True(count > 0, $"At least one {label} should exist on {viewportName} viewport.");

        for (var index = 0; index < count; index++)
        {
            var bounds = await locators.Nth(index).BoundingBoxAsync();
            Assert.NotNull(bounds);
            Assert.True(
                bounds.Width >= 44 && bounds.Height >= 44,
                $"{label} {index + 1} should be at least 44px in both dimensions on {viewportName} viewport.");
        }
    }

    private static async Task AssertSemanticTextContrastAsync(IPage page, string viewportName)
    {
        var ratios = await page.EvaluateAsync<double[]>(
            """
            () => {
                const root = getComputedStyle(document.documentElement);
                const channels = (color) => {
                  const value = color.startsWith("#") ? color.slice(1) : color;
                  return [0, 2, 4].map((offset) => Number.parseInt(value.slice(offset, offset + 2), 16) / 255);
                };
                const luminance = (color) => channels(color)
                  .map((channel) => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4)
                  .reduce((total, channel, index) => total + channel * [0.2126, 0.7152, 0.0722][index], 0);
                const contrast = (foreground, background) => {
                  const lighter = Math.max(luminance(foreground), luminance(background));
                  const darker = Math.min(luminance(foreground), luminance(background));
                  return (lighter + 0.05) / (darker + 0.05);
                };

                return [
                  contrast(root.getPropertyValue("--attention-amber").trim(), "#fff5da"),
                  contrast(root.getPropertyValue("--text-tertiary").trim(), "#ffffff"),
                ];
            }
            """);

        Assert.All(
            ratios,
            ratio => Assert.True(
                ratio >= 4.5,
                $"Semantic text colors should meet WCAG AA contrast on the {viewportName} viewport; actual ratio was {ratio:F2}."));
    }

    private static async Task AssertFitsViewportAsync(IPage page, string viewportName, string state)
    {
        var fitsViewport = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= window.innerWidth + 1");
        Assert.True(fitsViewport, $"{viewportName} {state} should not require horizontal scrolling.");
    }
}
