using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class ReceptionHomeStateSmokeTests : IClassFixture<ReceptionAppFixture>
{
    private static readonly ViewportCase[] Viewports =
    [
        new("tablet", 1024, 768),
        new("phone", 390, 844),
    ];

    private static readonly CultureCase[] Cultures =
    [
        new(
            "uk-UA",
            "Сьогодні дій рецепції ще не було.",
            "Дані зараз недоступні. Спробуйте оновити сторінку."),
        new(
            "en-US",
            "No reception activity has been recorded today.",
            "Data is unavailable right now. Try refreshing the page."),
    ];

    private readonly ReceptionAppFixture _app;

    public ReceptionHomeStateSmokeTests(ReceptionAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task HomeRendersEachQueryStateForEveryActorCultureAndTargetViewport()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        var accounts = new[]
        {
            new AccountCase("owner", _app.LoginName, _app.Password),
            new AccountCase("named-admin", _app.AdminLoginName, _app.AdminPassword),
            new AccountCase("shared-reception", _app.SharedAdminLoginName, _app.SharedAdminPassword),
        };
        var homeCases = new List<HomeCase>();

        try
        {
            foreach (var account in accounts)
            {
                foreach (var culture in Cultures)
                {
                    foreach (var viewport in Viewports)
                    {
                        homeCases.Add(await CreateHomeCaseAsync(
                            browser,
                            account,
                            culture,
                            viewport));
                    }
                }
            }

            await AssertStateMatrixAsync(
                homeCases,
                activityUnavailable: false,
                attentionUnavailable: false,
                todayUnavailable: false,
                stateName: "empty");

            await _app.SeedMalformedReceptionActivityAsync();
            await AssertStateMatrixAsync(
                homeCases,
                activityUnavailable: true,
                attentionUnavailable: false,
                todayUnavailable: false,
                stateName: "activity-unavailable");

            await _app.SeedMissingReceptionAttentionCacheAsync();
            await AssertStateMatrixAsync(
                homeCases,
                activityUnavailable: true,
                attentionUnavailable: true,
                todayUnavailable: false,
                stateName: "attention-unavailable");

            await _app.SeedMalformedDailyVisitAsync();
            await AssertStateMatrixAsync(
                homeCases,
                activityUnavailable: true,
                attentionUnavailable: true,
                todayUnavailable: true,
                stateName: "today-unavailable");
        }
        finally
        {
            foreach (var homeCase in homeCases)
            {
                await homeCase.Context.CloseAsync();
            }
        }
    }

    private async Task<HomeCase> CreateHomeCaseAsync(
        IBrowser browser,
        AccountCase account,
        CultureCase culture,
        ViewportCase viewport)
    {
        var context = await browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                Locale = culture.Name,
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
            account,
            $"Home · {account.Name} · {culture.Name} · {viewport.Name}");
        return new HomeCase(account, culture, viewport, context, page);
    }

    private async Task AssertStateMatrixAsync(
        IEnumerable<HomeCase> homeCases,
        bool activityUnavailable,
        bool attentionUnavailable,
        bool todayUnavailable,
        string stateName)
    {
        foreach (var homeCase in homeCases)
        {
            await AssertHomeStateAsync(
                homeCase,
                activityUnavailable,
                attentionUnavailable,
                todayUnavailable,
                stateName);
        }
    }

    private async Task AssertHomeStateAsync(
        HomeCase homeCase,
        bool activityUnavailable,
        bool attentionUnavailable,
        bool todayUnavailable,
        string stateName)
    {
        var homeResponse = await homeCase.Page.GotoAsync(
            _app.BaseAddress.ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.NotNull(homeResponse);
        Assert.True(
            homeResponse.Ok,
            $"{homeCase.Account.Name} {homeCase.Culture.Name} {homeCase.Viewport.Name} {stateName} Home returned HTTP {homeResponse.Status}.");

        var activityState = homeCase.Page.Locator(
            ".home-activity .status-message");
        await activityState.WaitForAsync(
            new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 5_000,
            });
        Assert.Equal(
            activityUnavailable
                ? homeCase.Culture.UnavailableText
                : homeCase.Culture.EmptyText,
            (await activityState.InnerTextAsync()).Trim());
        Assert.Equal(
            activityUnavailable,
            HasClass(
                await activityState.GetAttributeAsync("class"),
                "status-danger"));
        Assert.Equal(
            0,
            await homeCase.Page.Locator(
                ".home-activity .activity-list").CountAsync());

        await AssertQueryPanelAsync(
            homeCase,
            ".home-attention",
            attentionUnavailable,
            stateName);
        await AssertQueryPanelAsync(
            homeCase,
            ".home-today",
            todayUnavailable,
            stateName);
        Assert.True(
            await homeCase.Page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
            $"{homeCase.Account.Name} {homeCase.Culture.Name} {homeCase.Viewport.Name} {stateName} Home should not overflow horizontally.");

        if (homeCase.Account.Name == "owner")
        {
            await CaptureAsync(homeCase, stateName);
        }
    }

    private static async Task AssertQueryPanelAsync(
        HomeCase homeCase,
        string panelSelector,
        bool unavailable,
        string stateName)
    {
        var status = homeCase.Page.Locator(
            $"{panelSelector} .status-message.status-danger");
        if (!unavailable)
        {
            Assert.Equal(0, await status.CountAsync());
            return;
        }

        await status.WaitForAsync(
            new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 5_000,
            });
        Assert.Equal(
            homeCase.Culture.UnavailableText,
            (await status.InnerTextAsync()).Trim());
        Assert.True(
            await status.IsVisibleAsync(),
            $"{panelSelector} should show its unavailable state for {homeCase.Account.Name} {homeCase.Culture.Name} {homeCase.Viewport.Name} during {stateName}.");
    }

    private static bool HasClass(string? value, string className)
    {
        return (value ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(className, StringComparer.Ordinal);
    }

    private static async Task LoginAsync(
        IPage page,
        AccountCase account,
        string deviceLabel)
    {
        await page.Locator("#LoginName").FillAsync(account.LoginName);
        await page.Locator("#Password").FillAsync(account.Password);
        await page.Locator("#DeviceLabel").FillAsync(deviceLabel);
        await page.Locator("form.auth-form button[type='submit']").ClickAsync();
        await page.WaitForURLAsync("**/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task CaptureAsync(HomeCase homeCase, string stateName)
    {
        var directory = Environment.GetEnvironmentVariable(
            "BODYLIFE_UI_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        await homeCase.Page.EvaluateAsync(
            """
            () => {
              document.documentElement.scrollTop = 0;
              document.body.scrollTop = 0;
              window.scrollTo(0, 0);
            }
            """);
        await homeCase.Page.WaitForFunctionAsync("() => window.scrollY === 0");
        Directory.CreateDirectory(directory);
        await homeCase.Page.ScreenshotAsync(
            new PageScreenshotOptions
            {
                Animations = ScreenshotAnimations.Disabled,
                FullPage = false,
                Path = Path.Combine(
                    directory,
                    $"wave1-home-{stateName}-{homeCase.Viewport.Name}-{homeCase.Viewport.Width}x{homeCase.Viewport.Height}-{homeCase.Culture.Name}.png"),
            });
    }

    private sealed record AccountCase(
        string Name,
        string LoginName,
        string Password);

    private sealed record CultureCase(
        string Name,
        string EmptyText,
        string UnavailableText);

    private sealed record ViewportCase(
        string Name,
        int Width,
        int Height);

    private sealed record HomeCase(
        AccountCase Account,
        CultureCase Culture,
        ViewportCase Viewport,
        IBrowserContext Context,
        IPage Page);
}
