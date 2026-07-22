using BodyLife.Crm.SharedKernel;
using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class AddFreezeSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public AddFreezeSmokeTests(ReceptionAppFixture app)
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
    [InlineData("tablet", 1024, 768, "BL-FREEZE-TABLET", "Freeze Tablet")]
    [InlineData("phone", 390, 844, "BL-FREEZE-PHONE", "Freeze Phone")]
    public async Task OwnerAddsOneCanonicalFreezeOnTargetViewport(
        string viewportName,
        int width,
        int height,
        string cardNumber,
        string clientDisplayName)
    {
        var clientId = viewportName == "tablet"
            ? _app.FreezeTabletClientId
            : _app.FreezePhoneClientId;
        var membershipId = viewportName == "tablet"
            ? _app.FreezeTabletMembershipId
            : _app.FreezePhoneMembershipId;
        var membershipStateBefore = await _app.ReadMembershipStateAsync(membershipId);
        var context = await CreateContextAsync(width, height);

        try
        {
            var page = await OpenReceptionAsync(
                context,
                $"{viewportName} add freeze");
            await SubmitHtmxSearchAsync(page, cardNumber);

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await ExpectVisibleAsync(
                profile.GetByRole(
                    AriaRole.Heading,
                    new() { Name = clientDisplayName, Exact = true }),
                viewportName,
                "Freeze client profile");
            var panel = profile.Locator("#add-freeze-action-panel");
            await OpenAddFreezePanelAsync(panel, viewportName);
            var form = panel.Locator("form");
            Assert.Equal(
                1,
                await form.Locator("input[name='__RequestVerificationToken']").CountAsync());
            Assert.Equal("this:drop", await form.GetAttributeAsync("hx-sync"));
            Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));
            await ExpectVisibleAsync(
                panel.GetByText(
                    "Start must be within the selected Membership window. End may cross its current effective end date.",
                    new() { Exact = true }),
                viewportName,
                "inclusive Membership range rule");
            await ExpectVisibleAsync(
                panel.GetByText(
                    "An active counted Membership Visit inside the range blocks the Freeze.",
                    new() { Exact = true }),
                viewportName,
                "counted Visit conflict rule");

            var membership = panel.GetByLabel("Membership", new() { Exact = true });
            var membershipOption = membership.Locator("option").Filter(
                new LocatorFilterOptions
                {
                    HasText = viewportName == "tablet"
                        ? "Freeze tablet snapshot"
                        : "Freeze phone snapshot",
                });
            Assert.Equal(1, await membershipOption.CountAsync());
            await membership.SelectOptionAsync(membershipId.ToString());

            var idempotencyKey = await form
                .Locator("input[name='form.IdempotencyKey']")
                .InputValueAsync();
            Assert.False(string.IsNullOrWhiteSpace(idempotencyKey));

            var startDate = BusinessTimeZone.GetBusinessDate(DateTimeOffset.UtcNow).AddDays(1);
            var endDate = startDate.AddDays(1);
            var reason = $"{viewportName} medical pause";
            await panel.GetByLabel("Freeze start date", new() { Exact = true })
                .FillAsync(FormatDate(startDate));
            await panel.GetByLabel("Freeze end date", new() { Exact = true })
                .FillAsync(FormatDate(startDate.AddDays(-1)));
            await panel.GetByLabel("Reason", new() { Exact = true }).FillAsync(reason);
            await SubmitHtmxAddFreezeAsync(page);

            panel = profile.Locator("#add-freeze-action-panel");
            await ExpectVisibleAsync(
                panel.GetByRole(AriaRole.Alert),
                viewportName,
                "Freeze validation error");
            await ExpectVisibleAsync(
                panel.GetByText(
                    "The end date must not be before the start date.",
                    new() { Exact = true }),
                viewportName,
                "inclusive range validation");
            Assert.NotNull(await panel.GetAttributeAsync("open"));
            Assert.Equal(
                idempotencyKey,
                await panel.Locator("input[name='form.IdempotencyKey']").InputValueAsync());
            Assert.Equal(0L, await _app.CountActiveFreezesAsync(clientId));
            Assert.Equal(0L, await _app.CountAddFreezeAuditEntriesAsync(clientId));
            Assert.Equal(0L, await _app.CountAddFreezeIdempotencyKeysAsync(clientId));
            Assert.Equal(
                membershipStateBefore,
                await _app.ReadMembershipStateAsync(membershipId));

            await panel.GetByLabel("Freeze end date", new() { Exact = true })
                .FillAsync(FormatDate(endDate));
            await panel.GetByLabel("Comment (optional)", new() { Exact = true })
                .FillAsync($"{viewportName} reception Freeze.");
            await DelayAddFreezeRequestsAsync(page);
            await AssertMinimumTouchTargetAsync(
                panel.GetByRole(AriaRole.Button, new() { Name = "Add freeze" }),
                viewportName,
                "Add Freeze button");
            await AssertFitsViewportAsync(page, viewportName, "Add Freeze form");
            await CaptureVisualAsync(page, viewportName, "add-freeze-form");

            await SubmitHtmxAddFreezeAsync(page, repeatTapWhileBusy: true);

            await ExpectVisibleAsync(
                profile.GetByText("Freeze added."),
                viewportName,
                "Freeze success message");
            var freezeRow = profile.Locator(
                "[data-extension-source-kind='freeze'][data-extension-source-status='active']");
            await ExpectVisibleAsync(freezeRow, viewportName, "canonical Freeze history row");
            await ExpectVisibleAsync(
                freezeRow
                    .Locator(".membership-extension-meta")
                    .GetByText(reason, new() { Exact = true }),
                viewportName,
                "canonical Freeze reason");
            await ExpectVisibleAsync(
                freezeRow.Locator("[data-extension-inclusive-range]"),
                viewportName,
                "canonical inclusive Freeze range");
            Assert.Equal(
                $"{DisplayDate(startDate)} to {DisplayDate(endDate)}",
                (await freezeRow
                    .Locator("[data-extension-inclusive-range]")
                    .InnerTextAsync())
                    .Trim());

            Assert.Equal(1L, await _app.CountActiveFreezesAsync(clientId));
            Assert.Equal(1L, await _app.CountAddFreezeAuditEntriesAsync(clientId));
            Assert.Equal(1L, await _app.CountAddFreezeIdempotencyKeysAsync(clientId));
            var freeze = await _app.ReadLatestActiveFreezeAsync(clientId);
            Assert.Equal(membershipId, freeze.MembershipId);
            Assert.Equal(startDate, freeze.StartDate);
            Assert.Equal(endDate, freeze.EndDate);
            Assert.Equal(reason, freeze.Reason);
            Assert.Equal("active", freeze.Status);

            var membershipStateAfter = await _app.ReadMembershipStateAsync(membershipId);
            Assert.Equal(
                membershipStateBefore.EffectiveEndDate.AddDays(2),
                membershipStateAfter.EffectiveEndDate);
            await ExpectVisibleAsync(
                profile.Locator(".membership-summary-grid").GetByText(
                    DisplayDate(membershipStateAfter.EffectiveEndDate),
                    new() { Exact = true }),
                viewportName,
                "canonical effective end date");
            await AssertFitsViewportAsync(page, viewportName, "canonical Freeze profile");
            await CaptureVisualAsync(page, viewportName, "add-freeze-success");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, int height)
    {
        Assert.NotNull(_browser);

        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize
            {
                Width = width,
                Height = height,
            },
        });
    }

    private async Task<IPage> OpenReceptionAsync(
        IBrowserContext context,
        string deviceLabel)
    {
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Reception request returned HTTP {response.Status}.");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" })
            .FillAsync(_app.LoginName);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(_app.Password);
        await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
        await page.GetByRole(AriaRole.Heading, new() { Name = "Reception" }).WaitForAsync();
        return page;
    }

    private static async Task SubmitHtmxSearchAsync(IPage page, string query)
    {
        await page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" })
            .FillAsync(query);
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("handler=Search", StringComparison.OrdinalIgnoreCase));
        await page.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static async Task OpenAddFreezePanelAsync(
        ILocator panel,
        string viewportName)
    {
        await ExpectVisibleAsync(panel.Locator("summary"), viewportName, "Add Freeze action");
        if (await panel.GetAttributeAsync("open") is null)
        {
            await panel.Locator("summary").ClickAsync();
        }

        await ExpectVisibleAsync(panel.Locator("form"), viewportName, "Add Freeze form");
    }

    private static async Task SubmitHtmxAddFreezeAsync(
        IPage page,
        bool repeatTapWhileBusy = false)
    {
        var panel = page.Locator("#add-freeze-action-panel");
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("handler=AddFreeze", StringComparison.OrdinalIgnoreCase));
        var disabledTask = page.WaitForFunctionAsync(
            "() => document.querySelector('#add-freeze-action-panel button[type=\"submit\"]')?.disabled === true");
        var submitButton = panel.Locator("[data-add-freeze-submit]");
        await submitButton.ClickAsync();
        await disabledTask;

        if (repeatTapWhileBusy)
        {
            Assert.True(await submitButton.IsDisabledAsync());
            await submitButton.EvaluateAsync("button => button.click()");
        }

        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static Task DelayAddFreezeRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=AddFreeze*",
            async route =>
            {
                await Task.Delay(500);
                await route.ContinueAsync();
            });
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

    private static async Task AssertMinimumTouchTargetAsync(
        ILocator locator,
        string viewportName,
        string label)
    {
        var bounds = await locator.BoundingBoxAsync();
        Assert.NotNull(bounds);
        Assert.True(
            bounds.Width >= 44 && bounds.Height >= 44,
            $"{label} should be at least 44px in both dimensions on {viewportName}.");
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

    private static string FormatDate(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string DisplayDate(DateOnly date)
    {
        return date.ToString(
            "d",
            System.Globalization.CultureInfo.GetCultureInfo(ReceptionAppFixture.WorkflowCulture));
    }
}
