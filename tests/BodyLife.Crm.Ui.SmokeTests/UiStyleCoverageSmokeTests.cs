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
    [InlineData("tablet", 1024, 768, true)]
    [InlineData("phone", 390, 844, false)]
    public async Task AuthenticatedShellShowsRoleAppropriateNavigationWithoutHorizontalOverflow(
        string viewportName,
        int width,
        int height,
        bool isOwner)
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
                isOwner ? _app.LoginName : _app.AdminLoginName,
                isOwner ? _app.Password : _app.AdminPassword,
                $"{viewportName} branded shell");

            var logo = page.Locator(".sidebar-brand img");
            await ExpectVisibleAsync(logo, viewportName, "authenticated BodyLife logo");
            Assert.Contains(
                "/images/bodylife-logo.png",
                await logo.GetAttributeAsync("src") ?? string.Empty,
                StringComparison.Ordinal);

            var mainNavigation = page.Locator("section[aria-labelledby='navigation-main']");
            var receptionLink = mainNavigation
                .GetByRole(AriaRole.Link, new() { Name = "Reception", Exact = true });
            await ExpectVisibleAsync(receptionLink, viewportName, "Reception navigation");
            Assert.Equal("page", await receptionLink.GetAttributeAsync("aria-current"));
            Assert.Equal(
                1,
                await page.Locator(".sidebar-navigation a[aria-current='page']").CountAsync());
            await AssertMinimumTouchTargetsAsync(
                page.Locator(".sidebar-navigation .navigation-link"),
                viewportName,
                "navigation action");

            if (isOwner)
            {
                var ownerGroup = page.Locator("[aria-labelledby='navigation-owner']");
                await ExpectVisibleAsync(ownerGroup, viewportName, "Owner tools navigation");
                Assert.Equal(3, await ownerGroup.GetByRole(AriaRole.Link).CountAsync());
            }
            else
            {
                Assert.Equal(0, await page.Locator("#navigation-owner").CountAsync());
                Assert.Equal(0, await page.Locator("a[href^='/Owner/']").CountAsync());
            }

            await page.GotoAsync(
                new Uri(_app.BaseAddress, "/Reports/EndingSoon").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            var reportsLink = page.Locator("section[aria-labelledby='navigation-main']")
                .GetByRole(AriaRole.Link, new() { Name = "Reports", Exact = true });
            await ExpectVisibleAsync(reportsLink, viewportName, "Reports section navigation");
            Assert.Equal("location", await reportsLink.GetAttributeAsync("aria-current"));
            Assert.Contains(
                "is-section-active",
                await reportsLink.GetAttributeAsync("class") ?? string.Empty,
                StringComparison.Ordinal);

            await AssertFitsViewportAsync(page, viewportName, "authenticated navigation shell");
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

    private static async Task AssertFitsViewportAsync(IPage page, string viewportName, string state)
    {
        var fitsViewport = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= window.innerWidth + 1");
        Assert.True(fitsViewport, $"{viewportName} {state} should not require horizontal scrolling.");
    }
}
