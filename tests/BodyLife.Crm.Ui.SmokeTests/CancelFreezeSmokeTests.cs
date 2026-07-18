using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class CancelFreezeSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private const string CancellationConfirmation =
        "I confirm this Freeze was added by mistake and should be canceled.";

    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public CancelFreezeSmokeTests(ReceptionAppFixture app)
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
    [InlineData(
        "tablet",
        1024,
        768,
        "BL-CANCEL-FREEZE-TABLET",
        "Cancel Freeze Tablet",
        "Cancelable tablet snapshot",
        "Tablet schedule changed")]
    [InlineData(
        "phone",
        390,
        844,
        "BL-CANCEL-FREEZE-PHONE",
        "Cancel Freeze Phone",
        "Cancelable phone snapshot",
        "Phone schedule changed")]
    public async Task OwnerCancelsOneCanonicalFreezeOnTargetViewport(
        string viewportName,
        int width,
        int height,
        string cardNumber,
        string clientDisplayName,
        string membershipName,
        string originalReason)
    {
        var clientId = viewportName == "tablet"
            ? _app.CancelFreezeTabletClientId
            : _app.CancelFreezePhoneClientId;
        var membershipId = viewportName == "tablet"
            ? _app.CancelFreezeTabletMembershipId
            : _app.CancelFreezePhoneMembershipId;
        var freezeId = viewportName == "tablet"
            ? _app.CancelFreezeTabletFreezeId
            : _app.CancelFreezePhoneFreezeId;
        var membershipStateBefore = await _app.ReadMembershipStateAsync(membershipId);
        var freezeBefore = await _app.ReadLatestActiveFreezeAsync(clientId);
        var context = await CreateContextAsync(width, height);

        try
        {
            var page = await OpenReceptionAsync(
                context,
                $"{viewportName} cancel freeze");
            await SubmitHtmxSearchAsync(page, cardNumber);

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await ExpectVisibleAsync(
                profile.GetByRole(
                    AriaRole.Heading,
                    new() { Name = clientDisplayName, Exact = true }),
                viewportName,
                "cancel Freeze client profile");
            var freezeRow = profile.Locator(
                $"[data-extension-source-id='{freezeId}'][data-extension-source-status='active']");
            await ExpectVisibleAsync(freezeRow, viewportName, "active Freeze history row");
            await ExpectVisibleAsync(
                freezeRow.Locator(".membership-extension-meta").First.GetByText(
                    originalReason,
                    new() { Exact = true }),
                viewportName,
                "original Freeze reason");

            var panel = await OpenCancelFreezePanelAsync(freezeRow, viewportName);
            var form = panel.Locator("form");
            Assert.Equal(
                1,
                await form.Locator("input[name='__RequestVerificationToken']").CountAsync());
            Assert.Equal("this:drop", await form.GetAttributeAsync("hx-sync"));
            Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));
            await ExpectVisibleAsync(
                panel.GetByText(membershipName, new() { Exact = true }),
                viewportName,
                "affected Membership snapshot");
            await ExpectVisibleAsync(
                panel.GetByText(
                    $"{FormatDate(freezeBefore.StartDate)} to {FormatDate(freezeBefore.EndDate)}",
                    new() { Exact = true }),
                viewportName,
                "canonical Freeze range");
            await ExpectVisibleAsync(
                panel.GetByText(
                    "The Freeze remains visible as canceled and Membership extension state is recalculated.",
                    new() { Exact = true }),
                viewportName,
                "cancellation consequence warning");

            var idempotencyKey = await form
                .Locator("input[name='form.IdempotencyKey']")
                .InputValueAsync();
            Assert.False(string.IsNullOrWhiteSpace(idempotencyKey));
            var cancellationReason = $"{viewportName} Freeze entered by mistake";
            var cancellationComment = $"Canceled from {viewportName} reception.";
            await panel.GetByLabel("Cancellation reason", new() { Exact = true })
                .FillAsync(cancellationReason);
            await panel.GetByLabel(
                    "Cancellation comment (optional)",
                    new() { Exact = true })
                .FillAsync(cancellationComment);

            await SubmitHtmxCancelFreezeAsync(
                page,
                freezeId,
                bypassValidation: true,
                verifyBusy: false);

            panel = freezeRow.Locator($"#cancel-freeze-panel-{freezeId:N}");
            await ExpectVisibleAsync(
                panel.GetByText(
                    "Confirm that this Freeze should be canceled.",
                    new() { Exact = true }),
                viewportName,
                "server confirmation error");
            Assert.NotNull(await panel.GetAttributeAsync("open"));
            Assert.Equal(
                idempotencyKey,
                await panel.Locator("input[name='form.IdempotencyKey']").InputValueAsync());
            Assert.Equal("active", await _app.ReadFreezeStatusAsync(freezeId));
            Assert.Equal(0L, await _app.CountFreezeCancellationsAsync(freezeId));
            Assert.Equal(0L, await _app.CountCancelFreezeAuditEntriesAsync(freezeId));
            Assert.Equal(0L, await _app.CountCancelFreezeIdempotencyKeysAsync(clientId));
            Assert.Equal(
                membershipStateBefore,
                await _app.ReadMembershipStateAsync(membershipId));

            await panel.GetByRole(
                    AriaRole.Checkbox,
                    new() { Name = CancellationConfirmation, Exact = true })
                .CheckAsync();
            await DelayCancelFreezeRequestsAsync(page);
            await AssertMinimumTouchTargetAsync(
                panel.GetByRole(AriaRole.Button, new() { Name = "Cancel freeze" }),
                viewportName,
                "Cancel Freeze button");
            await AssertFitsViewportAsync(page, viewportName, "Cancel Freeze form");
            await CaptureVisualAsync(page, viewportName, "cancel-freeze-form");

            await SubmitHtmxCancelFreezeAsync(
                page,
                freezeId,
                repeatTapWhileBusy: true);

            await ExpectVisibleAsync(
                profile.GetByText("Freeze canceled."),
                viewportName,
                "Freeze cancellation success message");
            var canceledRow = profile.Locator(
                $"[data-extension-source-id='{freezeId}'][data-extension-source-status='canceled']");
            await ExpectVisibleAsync(canceledRow, viewportName, "canceled Freeze history row");
            await ExpectVisibleAsync(
                canceledRow.GetByText("Canceled", new() { Exact = true }),
                viewportName,
                "canceled Freeze status");
            Assert.Equal(0, await canceledRow.Locator("[data-cancel-freeze-panel]").CountAsync());

            Assert.Equal("canceled", await _app.ReadFreezeStatusAsync(freezeId));
            Assert.Equal(1L, await _app.CountFreezeCancellationsAsync(freezeId));
            Assert.Equal(1L, await _app.CountCancelFreezeAuditEntriesAsync(freezeId));
            Assert.Equal(1L, await _app.CountCancelFreezeIdempotencyKeysAsync(clientId));
            Assert.Equal(
                cancellationReason,
                await _app.ReadFreezeCancellationReasonAsync(freezeId));
            var audit = await _app.ReadCancelFreezeAuditAsync(freezeId);
            Assert.Equal(cancellationReason, audit.Reason);
            Assert.Equal(cancellationComment, audit.Comment);
            Assert.False(audit.ChangedAfterClose);

            var membershipStateAfter = await _app.ReadMembershipStateAsync(membershipId);
            Assert.Equal(
                membershipStateBefore.EffectiveEndDate.AddDays(-2),
                membershipStateAfter.EffectiveEndDate);
            await ExpectVisibleAsync(
                profile.Locator(".membership-summary-grid").GetByText(
                    FormatDate(membershipStateAfter.EffectiveEndDate),
                    new() { Exact = true }),
                viewportName,
                "recalculated effective end date");
            await AssertFitsViewportAsync(page, viewportName, "canceled Freeze profile");
            await CaptureVisualAsync(page, viewportName, "cancel-freeze-success");
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

    private static async Task<ILocator> OpenCancelFreezePanelAsync(
        ILocator freezeRow,
        string viewportName)
    {
        var panel = freezeRow.Locator("[data-cancel-freeze-panel]");
        await ExpectVisibleAsync(
            panel.Locator("summary"),
            viewportName,
            "Cancel Freeze action");
        if (await panel.GetAttributeAsync("open") is null)
        {
            await panel.Locator("summary").ClickAsync();
        }

        await ExpectVisibleAsync(panel.Locator("form"), viewportName, "Cancel Freeze form");
        return panel;
    }

    private static async Task SubmitHtmxCancelFreezeAsync(
        IPage page,
        Guid freezeId,
        bool repeatTapWhileBusy = false,
        bool bypassValidation = false,
        bool verifyBusy = true)
    {
        var panelSelector = $"#cancel-freeze-panel-{freezeId:N}";
        var panel = page.Locator(panelSelector);
        var form = panel.Locator("form");
        Assert.Equal("this:drop", await form.GetAttributeAsync("hx-sync"));
        Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("handler=CancelFreeze", StringComparison.OrdinalIgnoreCase));
        Task<IJSHandle>? disabledTask = null;
        if (verifyBusy)
        {
            disabledTask = page.WaitForFunctionAsync(
                $"() => document.querySelector('{panelSelector} button[type=\"submit\"]')?.disabled === true");
        }

        var submitButton = panel.Locator("[data-cancel-freeze-submit]");
        if (bypassValidation)
        {
            await form.EvaluateAsync(
                "form => { form.noValidate = true; form.requestSubmit(); }");
        }
        else
        {
            await submitButton.ClickAsync();
        }

        if (disabledTask is not null)
        {
            await disabledTask;
        }

        if (repeatTapWhileBusy)
        {
            Assert.True(await submitButton.IsDisabledAsync());
            await submitButton.EvaluateAsync("button => button.click()");
        }

        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static Task DelayCancelFreezeRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=CancelFreeze*",
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
}
