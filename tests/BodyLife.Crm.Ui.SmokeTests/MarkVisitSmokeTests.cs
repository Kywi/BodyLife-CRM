using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class MarkVisitSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private const string ZeroAcknowledgement =
        "I acknowledge that this visit will move the membership below zero.";
    private const string CancellationConfirmation =
        "I confirm this Visit was recorded by mistake and should be canceled.";
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public MarkVisitSmokeTests(ReceptionAppFixture app)
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
    [InlineData("tablet", 1024, 768, "BL-VISIT-TABLET", 3)]
    [InlineData("phone", 390, 844, "BL-VISIT-PHONE", 2)]
    public async Task OwnerCancelsVisitThroughProfileOnTargetViewport(
        string viewportName,
        int width,
        int height,
        string cardNumber,
        int expectedRemainingVisits)
    {
        var clientId = viewportName == "tablet"
            ? _app.VisitTabletClientId
            : _app.VisitPhoneClientId;
        var membershipId = viewportName == "tablet"
            ? _app.VisitTabletMembershipId
            : _app.VisitPhoneMembershipId;
        var context = await CreateContextAsync(width, height);

        try
        {
            var page = await OpenReceptionAsync(
                context,
                $"{viewportName} ordinary visit");
            await SubmitHtmxSearchAsync(page, cardNumber);

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            var panel = profile.Locator("#mark-visit-action-panel");
            await OpenMarkVisitPanelAsync(panel, viewportName);
            var form = panel.Locator("form");
            Assert.Equal(1, await form.Locator(
                "input[name='__RequestVerificationToken']").CountAsync());
            Assert.True(await panel.GetByRole(
                AriaRole.Radio,
                new() { Name = "Membership", Exact = true }).IsCheckedAsync());
            Assert.True(await panel.Locator(
                "[data-visit-membership-choice]").IsCheckedAsync());
            await panel.GetByLabel("Comment (optional)", new() { Exact = true })
                .FillAsync($"Marked from {viewportName} reception.");
            await AssertMinimumTouchTargetAsync(
                panel.GetByRole(AriaRole.Button, new() { Name = "Mark visit" }),
                viewportName,
                "mark visit button");
            await AssertFitsViewportAsync(page, viewportName, "ordinary Mark Visit form");
            await CaptureVisualAsync(page, viewportName, "mark-visit-form");

            await SubmitHtmxMarkVisitAsync(page);

            await ExpectVisibleAsync(
                profile.GetByText("Visit marked."),
                viewportName,
                "Visit success message");
            Assert.Equal(
                expectedRemainingVisits.ToString(System.Globalization.CultureInfo.InvariantCulture),
                await ReadRemainingVisitsAsync(profile));
            Assert.Equal(1L, await _app.CountActiveVisitsAsync(clientId, "membership"));
            Assert.Equal(1L, await _app.CountActiveVisitConsumptionsAsync(clientId));
            Assert.Equal(1L, await _app.CountMarkVisitAuditEntriesAsync(clientId));
            Assert.Equal(1L, await _app.CountMarkVisitIdempotencyKeysAsync(clientId));
            var state = await _app.ReadMembershipStateAsync(membershipId);
            Assert.Equal(1, state.CountedVisits);
            Assert.Equal(expectedRemainingVisits, state.RemainingVisits);
            var activeVisit = profile.Locator("[data-visit-status='active']").First;
            await ExpectVisibleAsync(
                activeVisit,
                viewportName,
                "active canonical Visit row");
            Assert.Equal("true", await activeVisit.GetAttributeAsync("data-can-cancel"));
            await ExpectVisibleAsync(
                activeVisit.GetByText("Membership visit", new() { Exact = true }).First,
                viewportName,
                "membership Visit kind");
            await ExpectVisibleAsync(
                activeVisit.GetByRole(
                    AriaRole.Heading,
                    new()
                    {
                        Name = viewportName == "tablet"
                            ? "Tablet four-visit snapshot"
                            : "Phone three-visit snapshot",
                        Exact = true,
                    }),
                viewportName,
                "issued membership snapshot");
            await ExpectVisibleAsync(
                activeVisit.GetByText($"Marked from {viewportName} reception."),
                viewportName,
                "Visit comment");
            await AssertFitsViewportAsync(page, viewportName, "canonical Visit profile");
            await CaptureVisualAsync(page, viewportName, "mark-visit-success");

            var visitIdValue = await activeVisit.GetAttributeAsync("data-visit-id");
            Assert.True(Guid.TryParse(visitIdValue, out var visitId));
            const string cancellationReason = "Reception correction for Visit history smoke.";
            await DelayCancelVisitRequestsAsync(page);
            var cancelPanel = await OpenCancelVisitPanelAsync(
                activeVisit,
                viewportName);
            var cancelForm = cancelPanel.Locator("form");
            Assert.Equal(1, await cancelForm.Locator(
                "input[name='__RequestVerificationToken']").CountAsync());
            Assert.False(string.IsNullOrWhiteSpace(await cancelForm.Locator(
                "input[name='form.IdempotencyKey']").InputValueAsync()));
            await cancelPanel.GetByLabel("Reason", new() { Exact = true })
                .FillAsync(cancellationReason);
            await cancelPanel.GetByLabel("Comment (optional)", new() { Exact = true })
                .FillAsync($"Canceled from {viewportName} reception.");
            await cancelPanel.GetByRole(
                AriaRole.Checkbox,
                new() { Name = CancellationConfirmation, Exact = true })
                .CheckAsync();
            await AssertMinimumTouchTargetAsync(
                cancelPanel.GetByRole(AriaRole.Button, new() { Name = "Cancel visit" }),
                viewportName,
                "Cancel Visit button");
            await AssertFitsViewportAsync(page, viewportName, "Cancel Visit form");
            await CaptureVisualAsync(page, viewportName, "cancel-visit-form");

            await SubmitHtmxCancelVisitAsync(
                page,
                visitId,
                repeatTapWhileBusy: true,
                verifyBusy: true);

            await ExpectVisibleAsync(
                profile.GetByText("Visit canceled."),
                viewportName,
                "Visit cancellation success message");

            var canceledVisit = profile.Locator(
                $"[data-visit-id='{visitId}'][data-visit-status='canceled']");
            await ExpectVisibleAsync(
                canceledVisit,
                viewportName,
                "canceled canonical Visit row");
            Assert.Equal("false", await canceledVisit.GetAttributeAsync("data-can-cancel"));
            await ExpectVisibleAsync(
                canceledVisit.GetByText("Canceled", new() { Exact = true }),
                viewportName,
                "canceled Visit status");
            await ExpectVisibleAsync(
                canceledVisit.GetByText(cancellationReason, new() { Exact = true }),
                viewportName,
                "Visit cancellation reason");
            Assert.Equal(0L, await _app.CountActiveVisitsAsync(clientId, "membership"));
            Assert.Equal(0L, await _app.CountActiveVisitConsumptionsAsync(clientId));
            Assert.Equal(1L, await _app.CountCancelVisitAuditEntriesAsync(clientId));
            Assert.Equal(1L, await _app.CountCancelVisitIdempotencyKeysAsync(clientId));
            var restoredState = await _app.ReadMembershipStateAsync(membershipId);
            Assert.Equal(0, restoredState.CountedVisits);
            await AssertFitsViewportAsync(page, viewportName, "canceled Visit profile");
            await CaptureVisualAsync(page, viewportName, "visit-history-canceled");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task NamedAdminConfirmationErrorDoesNotCancelBeforeSuccessfulRetry()
    {
        var context = await CreateContextAsync(1024, 768);

        try
        {
            var page = await OpenReceptionAsync(
                context,
                "tablet admin cancel visit",
                _app.AdminLoginName,
                _app.AdminPassword);
            await SubmitHtmxSearchAsync(page, "BL-VISIT-ADMIN");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            var markPanel = profile.Locator("#mark-visit-action-panel");
            await OpenMarkVisitPanelAsync(markPanel, "tablet");
            await SubmitHtmxMarkVisitAsync(page);

            var activeVisit = profile.Locator("[data-visit-status='active']").First;
            await ExpectVisibleAsync(
                activeVisit,
                "tablet",
                "Admin active Visit row");
            Assert.Equal("true", await activeVisit.GetAttributeAsync("data-can-cancel"));
            var visitIdValue = await activeVisit.GetAttributeAsync("data-visit-id");
            Assert.True(Guid.TryParse(visitIdValue, out var visitId));
            var cancelPanel = await OpenCancelVisitPanelAsync(activeVisit, "tablet");
            const string cancellationReason = "Named Admin corrected a mistaken Visit.";
            await cancelPanel.GetByLabel("Reason", new() { Exact = true })
                .FillAsync(cancellationReason);
            var originalIdempotencyKey = await cancelPanel.Locator(
                "input[name='form.IdempotencyKey']").InputValueAsync();

            await SubmitHtmxCancelVisitAsync(
                page,
                visitId,
                bypassValidation: true,
                verifyBusy: false);

            cancelPanel = profile.Locator($"#cancel-visit-panel-{visitId:N}");
            await ExpectVisibleAsync(
                cancelPanel.GetByText(
                    "Confirm that this Visit should be canceled.",
                    new() { Exact = true }),
                "tablet",
                "server confirmation error");
            Assert.NotNull(await cancelPanel.GetAttributeAsync("open"));
            Assert.Equal(
                originalIdempotencyKey,
                await cancelPanel.Locator(
                    "input[name='form.IdempotencyKey']").InputValueAsync());
            Assert.Equal(
                cancellationReason,
                await cancelPanel.GetByLabel("Reason", new() { Exact = true }).InputValueAsync());
            Assert.Equal(1L, await _app.CountActiveVisitsAsync(_app.VisitAdminClientId));
            Assert.Equal(
                0L,
                await _app.CountCancelVisitAuditEntriesAsync(_app.VisitAdminClientId));
            Assert.Equal(
                0L,
                await _app.CountCancelVisitIdempotencyKeysAsync(_app.VisitAdminClientId));

            await cancelPanel.GetByRole(
                AriaRole.Checkbox,
                new() { Name = CancellationConfirmation, Exact = true })
                .CheckAsync();
            await SubmitHtmxCancelVisitAsync(page, visitId, verifyBusy: false);

            await ExpectVisibleAsync(
                profile.GetByText("Visit canceled."),
                "tablet",
                "Admin cancellation success");
            await ExpectVisibleAsync(
                profile.Locator($"[data-visit-id='{visitId}'][data-visit-status='canceled']"),
                "tablet",
                "Admin canceled Visit row");
            Assert.Equal(0L, await _app.CountActiveVisitsAsync(_app.VisitAdminClientId));
            Assert.Equal(
                0L,
                await _app.CountActiveVisitConsumptionsAsync(_app.VisitAdminClientId));
            Assert.Equal(
                1L,
                await _app.CountCancelVisitAuditEntriesAsync(_app.VisitAdminClientId));
            Assert.Equal(
                1L,
                await _app.CountCancelVisitIdempotencyKeysAsync(_app.VisitAdminClientId));
            var restoredState = await _app.ReadMembershipStateAsync(
                _app.VisitAdminMembershipId);
            Assert.Equal(0, restoredState.CountedVisits);
            Assert.Equal(2, restoredState.RemainingVisits);
            await AssertFitsViewportAsync(page, "tablet", "Admin canceled Visit profile");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task ZeroRemainingAcknowledgementAndBusyGuardCreateOneNegativeVisit()
    {
        var context = await CreateContextAsync(1024, 768);

        try
        {
            var page = await OpenReceptionAsync(context, "tablet zero visit");
            await DelayMarkVisitRequestsAsync(page);
            await SubmitHtmxSearchAsync(page, "BL-VISIT-ZERO");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            var panel = profile.Locator("#mark-visit-action-panel");
            await OpenMarkVisitPanelAsync(panel, "tablet");
            await ExpectVisibleAsync(
                panel.GetByText("Membership has no remaining visits.", new() { Exact = true }),
                "tablet",
                "zero remaining warning");
            await panel.GetByRole(
                AriaRole.Checkbox,
                new() { Name = ZeroAcknowledgement, Exact = true })
                .CheckAsync();

            await SubmitHtmxMarkVisitAsync(page, repeatTapWhileBusy: true);

            await ExpectVisibleAsync(
                profile.GetByText("Visit marked."),
                "tablet",
                "negative Visit success");
            Assert.Equal("-1", await ReadRemainingVisitsAsync(profile));
            await ExpectVisibleAsync(
                profile.Locator(".membership-panel").GetByText(
                    "Membership has a negative visit balance.",
                    new() { Exact = true }),
                "tablet",
                "negative balance warning");
            Assert.Equal(1L, await _app.CountActiveVisitsAsync(_app.VisitZeroClientId));
            Assert.Equal(
                1L,
                await _app.CountActiveVisitConsumptionsAsync(_app.VisitZeroClientId));
            Assert.Equal(
                1L,
                await _app.CountMarkVisitAuditEntriesAsync(_app.VisitZeroClientId));
            Assert.Equal(
                1L,
                await _app.CountMarkVisitIdempotencyKeysAsync(_app.VisitZeroClientId));
            var state = await _app.ReadMembershipStateAsync(_app.VisitZeroMembershipId);
            Assert.Equal(-1, state.RemainingVisits);
            Assert.Equal(1, state.NegativeBalance);
            Assert.NotNull(state.FirstNegativeVisitDate);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task OneOffAndTrialStayExplicitAndDoNotConsumeMembershipOnPhone()
    {
        var context = await CreateContextAsync(390, 844);

        try
        {
            var page = await OpenReceptionAsync(context, "phone non-membership visits");
            await SubmitHtmxSearchAsync(page, "BL-VISIT-NONE");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            var panel = profile.Locator("#mark-visit-action-panel");
            await OpenMarkVisitPanelAsync(panel, "phone");
            await ExpectVisibleAsync(
                panel.GetByText(
                    "No lifecycle-active memberships. Choose one-off or trial deliberately.",
                    new() { Exact = true }),
                "phone",
                "explicit non-membership context");
            Assert.True(await panel.GetByRole(
                AriaRole.Button,
                new() { Name = "Mark visit" }).IsDisabledAsync());
            await panel.GetByRole(
                AriaRole.Radio,
                new() { Name = "One-off", Exact = true }).CheckAsync();
            await SubmitHtmxMarkVisitAsync(page);

            await ExpectVisibleAsync(
                profile.GetByText("Visit marked."),
                "phone",
                "one-off success");
            Assert.Equal(
                1L,
                await _app.CountActiveVisitsAsync(
                    _app.VisitNoMembershipClientId,
                    "one_off"));
            Assert.Equal(
                0L,
                await _app.CountActiveVisitConsumptionsAsync(
                    _app.VisitNoMembershipClientId));

            panel = profile.Locator("#mark-visit-action-panel");
            await OpenMarkVisitPanelAsync(panel, "phone");
            await panel.GetByRole(
                AriaRole.Radio,
                new() { Name = "Trial", Exact = true }).CheckAsync();
            await SubmitHtmxMarkVisitAsync(page);

            Assert.Equal(
                1L,
                await _app.CountActiveVisitsAsync(
                    _app.VisitNoMembershipClientId,
                    "trial"));
            Assert.Equal(
                2L,
                await _app.CountActiveVisitsAsync(_app.VisitNoMembershipClientId));
            Assert.Equal(
                0L,
                await _app.CountActiveVisitConsumptionsAsync(
                    _app.VisitNoMembershipClientId));
            Assert.Equal(
                2L,
                await _app.CountMarkVisitAuditEntriesAsync(
                    _app.VisitNoMembershipClientId));
            Assert.Equal(
                2L,
                await _app.CountMarkVisitIdempotencyKeysAsync(
                    _app.VisitNoMembershipClientId));
            await ExpectVisibleAsync(
                profile.GetByText("No current membership", new() { Exact = true }),
                "phone",
                "unchanged Membership state");
            await AssertFitsViewportAsync(page, "phone", "one-off and trial profile");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task ChangedWarningStateRefreshesProfileAndRequiresCurrentAcknowledgement()
    {
        var context = await CreateContextAsync(1024, 768);

        try
        {
            var page = await OpenReceptionAsync(context, "tablet stale warning visit");
            await SubmitHtmxSearchAsync(page, "BL-VISIT-STALE");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            var panel = profile.Locator("#mark-visit-action-panel");
            await OpenMarkVisitPanelAsync(panel, "tablet");
            Assert.Equal(0, await panel.GetByRole(AriaRole.Checkbox).CountAsync());

            await _app.InsertExternalCountedVisitAsync(
                _app.VisitStaleClientId,
                _app.VisitStaleMembershipId);
            await SubmitHtmxMarkVisitAsync(page);

            panel = profile.Locator("#mark-visit-action-panel");
            await ExpectVisibleAsync(
                panel.GetByRole(AriaRole.Alert),
                "tablet",
                "changed-warning command error");
            await ExpectVisibleAsync(
                panel.GetByText(
                    "Membership warnings changed or were not acknowledged exactly. Review the current requirements below.",
                    new() { Exact = true }),
                "tablet",
                "current acknowledgement instruction");
            Assert.NotNull(await panel.GetAttributeAsync("open"));
            Assert.Equal("0", await ReadRemainingVisitsAsync(profile));
            var acknowledgement = panel.GetByRole(
                AriaRole.Checkbox,
                new() { Name = ZeroAcknowledgement, Exact = true });
            Assert.True(await acknowledgement.IsEnabledAsync());
            Assert.False(await acknowledgement.IsCheckedAsync());
            Assert.Equal(1L, await _app.CountActiveVisitsAsync(_app.VisitStaleClientId));
            Assert.Equal(
                0L,
                await _app.CountMarkVisitAuditEntriesAsync(_app.VisitStaleClientId));
            Assert.Equal(
                0L,
                await _app.CountMarkVisitIdempotencyKeysAsync(_app.VisitStaleClientId));

            await acknowledgement.CheckAsync();
            await SubmitHtmxMarkVisitAsync(page);

            Assert.Equal("-1", await ReadRemainingVisitsAsync(profile));
            Assert.Equal(2L, await _app.CountActiveVisitsAsync(_app.VisitStaleClientId));
            Assert.Equal(
                2L,
                await _app.CountActiveVisitConsumptionsAsync(_app.VisitStaleClientId));
            Assert.Equal(
                1L,
                await _app.CountMarkVisitAuditEntriesAsync(_app.VisitStaleClientId));
            Assert.Equal(
                1L,
                await _app.CountMarkVisitIdempotencyKeysAsync(_app.VisitStaleClientId));
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task FreezeAddedAfterFormLoadBlocksMembershipButAllowsExplicitOneOff()
    {
        var context = await CreateContextAsync(1024, 768);

        try
        {
            var page = await OpenReceptionAsync(context, "tablet freeze visit");
            await SubmitHtmxSearchAsync(page, "BL-VISIT-FREEZE");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            var panel = profile.Locator("#mark-visit-action-panel");
            await OpenMarkVisitPanelAsync(panel, "tablet");
            await _app.InsertActiveFreezeForTodayAsync(
                _app.VisitFreezeClientId,
                _app.VisitFreezeMembershipId);
            await SubmitHtmxMarkVisitAsync(page);

            panel = profile.Locator("#mark-visit-action-panel");
            await ExpectVisibleAsync(
                panel.GetByText(
                    "Membership visit is blocked by an active freeze on this date. Choose one-off/trial or correct the freeze.",
                    new() { Exact = true }),
                "tablet",
                "Freeze command error");
            await ExpectVisibleAsync(
                panel.GetByText(
                    "An active freeze includes the visit date and cannot be overridden.",
                    new() { Exact = true }),
                "tablet",
                "canonical Freeze option state");
            var membershipChoice = panel.Locator("[data-visit-membership-choice]");
            Assert.True(await membershipChoice.IsCheckedAsync());
            Assert.True(await membershipChoice.IsDisabledAsync());
            Assert.True(await panel.GetByRole(
                AriaRole.Button,
                new() { Name = "Mark visit" }).IsDisabledAsync());
            Assert.Equal(0L, await _app.CountActiveVisitsAsync(_app.VisitFreezeClientId));
            Assert.Equal(
                0L,
                await _app.CountMarkVisitAuditEntriesAsync(_app.VisitFreezeClientId));
            Assert.Equal(
                0L,
                await _app.CountMarkVisitIdempotencyKeysAsync(_app.VisitFreezeClientId));

            await panel.GetByRole(
                AriaRole.Radio,
                new() { Name = "One-off", Exact = true }).CheckAsync();
            await SubmitHtmxMarkVisitAsync(page);

            Assert.Equal(
                1L,
                await _app.CountActiveVisitsAsync(_app.VisitFreezeClientId, "one_off"));
            Assert.Equal(
                0L,
                await _app.CountActiveVisitConsumptionsAsync(_app.VisitFreezeClientId));
            Assert.Equal(
                1L,
                await _app.CountMarkVisitAuditEntriesAsync(_app.VisitFreezeClientId));
            Assert.Equal(
                1L,
                await _app.CountMarkVisitIdempotencyKeysAsync(_app.VisitFreezeClientId));
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
        string deviceLabel,
        string? loginName = null,
        string? password = null)
    {
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(
            _app.BaseAddress.ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Reception request returned HTTP {response.Status}.");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" })
            .FillAsync(loginName ?? _app.LoginName);
        await page.GetByLabel("Password", new() { Exact = true })
            .FillAsync(password ?? _app.Password);
        await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
        await page.GetByRole(AriaRole.Heading, new() { Name = "Reception" })
            .WaitForAsync();
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

    private static async Task OpenMarkVisitPanelAsync(
        ILocator panel,
        string viewportName)
    {
        await ExpectVisibleAsync(
            panel.Locator("summary"),
            viewportName,
            "Mark Visit action");
        if (await panel.GetAttributeAsync("open") is null)
        {
            await panel.Locator("summary").ClickAsync();
        }

        await ExpectVisibleAsync(
            panel.Locator("form"),
            viewportName,
            "Mark Visit form");
    }

    private static async Task<ILocator> OpenCancelVisitPanelAsync(
        ILocator visitRow,
        string viewportName)
    {
        var panel = visitRow.Locator("[data-cancel-visit-panel]");
        await ExpectVisibleAsync(
            panel.Locator("summary"),
            viewportName,
            "Cancel Visit action");
        if (await panel.GetAttributeAsync("open") is null)
        {
            await panel.Locator("summary").ClickAsync();
        }

        await ExpectVisibleAsync(
            panel.Locator("form"),
            viewportName,
            "Cancel Visit form");
        return panel;
    }

    private static async Task SubmitHtmxMarkVisitAsync(
        IPage page,
        bool repeatTapWhileBusy = false)
    {
        var panel = page.Locator("#mark-visit-action-panel");
        var form = panel.Locator("form");
        Assert.Equal("this:drop", await form.GetAttributeAsync("hx-sync"));
        Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("handler=MarkVisit", StringComparison.OrdinalIgnoreCase));
        var disabledTask = page.WaitForFunctionAsync(
            "() => document.querySelector('#mark-visit-action-panel button[type=\"submit\"]')?.disabled === true");
        var submitButton = panel.Locator("[data-mark-visit-submit]");
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

    private static async Task SubmitHtmxCancelVisitAsync(
        IPage page,
        Guid visitId,
        bool repeatTapWhileBusy = false,
        bool bypassValidation = false,
        bool verifyBusy = true)
    {
        var panelSelector = $"#cancel-visit-panel-{visitId:N}";
        var panel = page.Locator(panelSelector);
        var form = panel.Locator("form");
        Assert.Equal("this:drop", await form.GetAttributeAsync("hx-sync"));
        Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("handler=CancelVisit", StringComparison.OrdinalIgnoreCase));
        Task<IJSHandle>? disabledTask = null;
        if (verifyBusy)
        {
            disabledTask = page.WaitForFunctionAsync(
                $"() => document.querySelector('{panelSelector} button[type=\"submit\"]')?.disabled === true");
        }

        var submitButton = panel.Locator("[data-cancel-visit-submit]");
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

    private static Task DelayMarkVisitRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=MarkVisit*",
            async route =>
            {
                await Task.Delay(500);
                await route.ContinueAsync();
            });
    }

    private static Task DelayCancelVisitRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=CancelVisit*",
            async route =>
            {
                await Task.Delay(500);
                await route.ContinueAsync();
            });
    }

    private static async Task<string> ReadRemainingVisitsAsync(ILocator profile)
    {
        return (await profile.Locator(".membership-summary-grid div")
            .Filter(new LocatorFilterOptions { HasText = "Remaining visits" })
            .Locator("dd")
            .InnerTextAsync())
            .Trim();
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

    private static async Task AssertMinimumTouchTargetAsync(
        ILocator locator,
        string viewportName,
        string label)
    {
        var bounds = await locator.BoundingBoxAsync();
        Assert.NotNull(bounds);
        Assert.True(
            bounds.Width >= 44 && bounds.Height >= 44,
            $"{label} should be at least 44px in both dimensions on {viewportName} viewport.");
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
            Path = Path.Combine(
                screenshotDirectory,
                $"{viewportName}-{state}.png"),
        });
    }
}
