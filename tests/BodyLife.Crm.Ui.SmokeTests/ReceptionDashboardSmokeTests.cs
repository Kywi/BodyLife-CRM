using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class ReceptionDashboardSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public ReceptionDashboardSmokeTests(ReceptionAppFixture app)
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
    public async Task ReceptionEntryRendersOnTargetViewport(string viewportName, int width, int height)
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
            var response = await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });

            Assert.NotNull(response);
            Assert.True(response.Ok, $"{viewportName} request returned HTTP {response.Status}.");

            await ExpectVisibleAsync(page.GetByRole(AriaRole.Heading, new() { Name = "Login" }), viewportName, "login heading");
            var deviceLabel = $"{viewportName} smoke";
            await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" }).FillAsync(_app.LoginName);
            await page.GetByLabel("Password", new() { Exact = true }).FillAsync(_app.Password);
            await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
            await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
            await page.WaitForURLAsync("**/");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Equal("Reception - BodyLife CRM", await page.TitleAsync());

            await ExpectVisibleAsync(page.GetByLabel("Current session"), viewportName, "current session shell");
            await ExpectVisibleAsync(page.GetByText("BodyLife Owner"), viewportName, "current account display name");
            await ExpectVisibleAsync(page.GetByText("Owner account / Owner"), viewportName, "current account type and role");
            await ExpectVisibleAsync(page.GetByText(deviceLabel), viewportName, "current device label");
            await ExpectVisibleAsync(page.GetByText("Session"), viewportName, "current session id");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Button, new() { Name = "Log out" }), viewportName, "logout button");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Heading, new() { Name = "Reception" }), viewportName, "reception heading");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" }), viewportName, "client search input");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Button, new() { Name = "Search" }), viewportName, "search button");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Region, new() { Name = "Client status" }), viewportName, "client status panel");

            var searchInput = page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" });
            await searchInput.FillAsync("BL-001");
            await page.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains("q=BL-001", page.Url, StringComparison.Ordinal);
            Assert.Equal("BL-001", await searchInput.InputValueAsync());

            var fitsViewport = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1");
            Assert.True(fitsViewport, $"{viewportName} layout should not require horizontal scrolling.");
        }
        finally
        {
            await context.CloseAsync();
        }
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
}
