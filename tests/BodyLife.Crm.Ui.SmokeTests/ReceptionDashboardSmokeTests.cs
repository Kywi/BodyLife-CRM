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
    public async Task ReceptionSearchAndProfileReadPathWorksOnTargetViewport(
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
            var response = await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });

            Assert.NotNull(response);
            Assert.True(response.Ok, $"{viewportName} request returned HTTP {response.Status}.");

            await ExpectVisibleAsync(page.GetByRole(AriaRole.Heading, new() { Name = "Login" }), viewportName, "login heading");
            var deviceLabel = $"{viewportName} smoke";
            await LoginAsync(page, _app.LoginName, _app.Password, deviceLabel);

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
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Group, new() { Name = "Search mode" }), viewportName, "search mode control");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Checkbox, new() { Name = "Include inactive" }), viewportName, "inactive-client control");
            var searchResults = page.GetByRole(AriaRole.Region, new() { Name = "Search results" });
            var clientProfile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await ExpectVisibleAsync(searchResults, viewportName, "search results region");
            await ExpectVisibleAsync(clientProfile, viewportName, "client profile region");
            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "No client selected" }), viewportName, "initial profile state");
            await AssertFitsViewportAsync(page, viewportName, "initial dashboard");

            await SubmitHtmxSearchAsync(page, "BL-1001");

            Assert.Contains("q=BL-1001", page.Url, StringComparison.Ordinal);
            Assert.DoesNotContain("handler=Search", page.Url, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                "BL-1001",
                await page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" }).InputValueAsync());
            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "Kovalenko Olena" }), viewportName, "exact-card profile");
            await ExpectVisibleAsync(clientProfile.GetByText("BL-1001", new() { Exact = true }), viewportName, "exact-card profile number");
            await ExpectVisibleAsync(clientProfile.GetByText("No current membership", new() { Exact = true }), viewportName, "membership placeholder");
            await AssertFitsViewportAsync(page, viewportName, "exact-card profile");
            await CaptureVisualAsync(page, viewportName, "exact-profile");

            await SubmitHtmxSearchAsync(page, "Kovalenko");

            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "No client selected" }), viewportName, "ambiguous search profile state");
            Assert.Equal(3, await searchResults.Locator(".client-result-row").CountAsync());
            await ExpectVisibleAsync(searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Marta", Exact = true }), viewportName, "Marta result");
            await ExpectVisibleAsync(searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Olena", Exact = true }), viewportName, "Olena result");
            await ExpectVisibleAsync(searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Taras", Exact = true }), viewportName, "Taras result");
            await AssertFitsViewportAsync(page, viewportName, "multiple results");
            await CaptureVisualAsync(page, viewportName, "multiple-results");

            await ClickHtmxProfileAsync(
                page,
                searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Marta", Exact = true }));

            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "Kovalenko Marta" }), viewportName, "selected profile");
            Assert.Contains("clientId=", page.Url, StringComparison.Ordinal);
            Assert.DoesNotContain("handler=Profile", page.Url, StringComparison.OrdinalIgnoreCase);

            await ClickHtmxProfileAsync(
                page,
                searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Taras", Exact = true }));

            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "Kovalenko Taras" }), viewportName, "no-card profile");
            await ExpectVisibleAsync(
                clientProfile.GetByLabel("Profile warnings").GetByText("No current card", new() { Exact = true }),
                viewportName,
                "no-card warning");

            await SubmitHtmxSearchAsync(page, "NO-SUCH-CLIENT");

            await ExpectVisibleAsync(searchResults.GetByText("No clients found", new() { Exact = true }), viewportName, "no-match state");
            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "No client selected" }), viewportName, "no-match profile state");
            await AssertFitsViewportAsync(page, viewportName, "no-match state");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task ReceptionSearchFallsBackToFullPageWithoutJavascript()
    {
        Assert.NotNull(_browser);
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            JavaScriptEnabled = false,
            ViewportSize = new ViewportSize
            {
                Width = 1024,
                Height = 768,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(page, _app.LoginName, _app.Password, "no-js smoke");
            await page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" }).FillAsync("BL-1001");
            await page.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
            await page.WaitForURLAsync("**?q=BL-1001**");

            Assert.Equal("Reception - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(AriaRole.Region, new() { Name = "Client profile" })
                    .GetByRole(AriaRole.Heading, new() { Name = "Kovalenko Olena" }),
                "tablet",
                "full-page exact-card profile");
            await AssertFitsViewportAsync(page, "tablet", "no-JavaScript fallback");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task ExpiredDatabaseSessionRequiresLoginAgain()
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
            var deviceLabel = $"expiry-{Guid.NewGuid():N}";
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" }).FillAsync(_app.LoginName);
            await page.GetByLabel("Password", new() { Exact = true }).FillAsync(_app.Password);
            await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
            await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
            await page.WaitForURLAsync("**/");
            await _app.ExpireSessionAsync(deviceLabel);

            await page.ReloadAsync();
            await page.WaitForURLAsync("**/Login**");

            await ExpectVisibleAsync(
                page.GetByRole(AriaRole.Heading, new() { Name = "Login" }),
                "tablet",
                "login after session expiry");
            Assert.Contains("ReturnUrl=%2F", page.Url, StringComparison.Ordinal);
            Assert.True(await _app.IsSessionEndedAsync(deviceLabel));
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

    private static async Task LoginAsync(
        IPage page,
        string loginName,
        string password,
        string deviceLabel)
    {
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" }).FillAsync(loginName);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task SubmitHtmxSearchAsync(IPage page, string query)
    {
        await page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" }).FillAsync(query);
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("handler=Search", StringComparison.OrdinalIgnoreCase));
        await page.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static async Task ClickHtmxProfileAsync(IPage page, ILocator profileLink)
    {
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("handler=Profile", StringComparison.OrdinalIgnoreCase));
        await profileLink.ClickAsync();
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static void AssertHtmxResponse(IResponse response)
    {
        Assert.True(response.Ok, $"htmx request returned HTTP {response.Status}.");
        Assert.True(response.Request.Headers.TryGetValue("hx-request", out var htmxRequest));
        Assert.Equal("true", htmxRequest);
    }

    private static async Task WaitForHtmxSettleAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.htmx-request') === null");
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

    private static async Task CaptureVisualAsync(
        IPage page,
        string viewportName,
        string state)
    {
        var screenshotDirectory = Environment.GetEnvironmentVariable("BODYLIFE_UI_SCREENSHOT_DIR");

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
