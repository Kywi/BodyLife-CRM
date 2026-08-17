using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class NegativeVisitCoverageSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public NegativeVisitCoverageSmokeTests(ReceptionAppFixture app)
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

    [Fact]
    public async Task TabletOwnerClosesTwoOldestVisitsOnceAndRereadsCanonicalRemainder()
    {
        var context = await CreateContextAsync(1024, 768);

        try
        {
            var page = await OpenReceptionAsync(context, "tablet negative coverage");
            await SubmitHtmxSearchAsync(page, ReceptionAppFixture.NegativeCoverageTabletCard);
            var panel = await RequireCoveragePanelAsync(page, expectedBalance: 3);

            Assert.Equal(3, await panel.Locator("[data-negative-coverage-options] li").CountAsync());
            Assert.Equal(1, await panel.Locator(
                "#negative-coverage-close-form input[name='__RequestVerificationToken']").CountAsync());
            Assert.Equal(0, await panel.Locator("input[type='radio']:checked").CountAsync());
            await AssertFitsViewportAsync(page, "tablet", "initial negative coverage");

            await PreviewCloseAsync(page, quantity: 2);
            panel = page.Locator("#negative-visit-coverage-panel");
            await ExpectVisibleAsync(
                panel.GetByText("Exact cash payment: 250.00 UAH", new() { Exact = true }),
                "tablet",
                "exact one-off Payment preview");
            Assert.Equal(2, await panel.Locator(".negative-coverage-preview ol li").CountAsync());
            await ExpectVisibleAsync(
                panel.GetByText("Remaining negative balance: 1; unknown: 0", new() { Exact = true }),
                "tablet",
                "partial negative remainder");

            await DelayMutationRequestsAsync(page, "CloseNegativeVisitsOneOff");
            await SubmitCloseAsync(page, repeatTapWhileBusy: true);
            panel = await RequireCoveragePanelAsync(page, expectedBalance: 1);
            await ExpectVisibleAsync(
                OperationStatusTestHelper.Success(page).GetByText(
                    "Negative visits closed with one-off coverage",
                    new() { Exact = false }),
                "tablet",
                "close success message");
            Assert.Equal(1, await panel.Locator("[data-negative-open-visits] li").CountAsync());
            Assert.Equal(1, await panel.Locator(".negative-coverage-correction").CountAsync());
            await AssertFitsViewportAsync(page, "tablet", "canonical close result");

            var state = await _app.ReadMembershipStateAsync(
                _app.NegativeCoverageTabletMembershipId);
            Assert.Equal(1, state.CountedVisits);
            Assert.Equal(-1, state.RemainingVisits);
            Assert.Equal(1, state.NegativeBalance);

            var snapshot = await _app.ReadNegativeCoverageMutationSnapshotAsync(
                _app.NegativeCoverageTabletClientId);
            Assert.Equal(1, snapshot.ClosureCount);
            Assert.Equal(1, snapshot.ActiveClosureCount);
            Assert.Equal(1, snapshot.PaymentCount);
            Assert.Equal(1, snapshot.ActivePaymentCount);
            Assert.Equal(250m, snapshot.TotalPaymentAmount);
            Assert.Equal(250m, snapshot.ActivePaymentAmount);
            Assert.Equal(2, snapshot.ActiveItemCount);
            Assert.Equal(1, snapshot.CreatedClosureAuditCount);
            Assert.Equal(1, snapshot.PaymentCreatedAuditCount);
            Assert.Equal(1, snapshot.CloseIdempotencyCount);
            Assert.Equal(0, snapshot.CorrectionIdempotencyCount);
            Assert.NotNull(snapshot.ActiveClosureId);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task PhoneOwnerReplacesThenCancelsCoverageWithCanonicalRereads()
    {
        var context = await CreateContextAsync(390, 844);

        try
        {
            var page = await OpenReceptionAsync(context, "phone negative coverage");
            await SubmitHtmxSearchAsync(page, ReceptionAppFixture.NegativeCoveragePhoneCard);
            await RequireCoveragePanelAsync(page, expectedBalance: 2);
            await PreviewCloseAsync(page, quantity: 2);
            await SubmitCloseAsync(page, repeatTapWhileBusy: false);
            var panel = await RequireCoveragePanelAsync(page, expectedBalance: 0);
            Assert.Equal(0, await panel.Locator("[data-negative-open-visits] li").CountAsync());

            var afterClose = await _app.ReadNegativeCoverageMutationSnapshotAsync(
                _app.NegativeCoveragePhoneClientId);
            Assert.NotNull(afterClose.ActiveClosureId);
            Assert.Equal(1, afterClose.CloseIdempotencyCount);
            Assert.Equal(2, afterClose.ActiveItemCount);

            var correction = await OpenOnlyCorrectionAsync(panel);
            Assert.Equal(0, await correction.Locator("input[name='form.Mode']:checked").CountAsync());
            await PreviewCorrectionAsync(
                page,
                mode: "Replace",
                reason: "One visit should remain uncovered",
                replacementQuantity: 1);

            correction = page.Locator(".negative-coverage-correction");
            await ExpectVisibleAsync(
                correction.GetByText(
                    "Restored negative balance: 2; resulting balance: 1",
                    new() { Exact = true }),
                "phone",
                "replacement balance preview");
            Assert.Equal(2, await correction.Locator("[data-negative-restored-visits] li").CountAsync());
            Assert.Equal(1, await correction.Locator("[data-negative-replacement-visits] li").CountAsync());
            await ExpectVisibleAsync(
                correction.GetByText("Original payment", new() { Exact = true }),
                "phone",
                "original Payment context");
            await ExpectVisibleAsync(
                correction.GetByText("Replacement exact payment", new() { Exact = true }),
                "phone",
                "replacement Payment context");
            Assert.Contains("250.00 UAH", await correction.InnerTextAsync(), StringComparison.Ordinal);
            Assert.Contains("125.00 UAH", await correction.InnerTextAsync(), StringComparison.Ordinal);
            Assert.Contains("does not calculate a refund", await correction.InnerTextAsync(), StringComparison.Ordinal);

            await DelayMutationRequestsAsync(page, "CorrectNegativeVisitCoverage");
            var replacementResponseHtml = await SubmitCorrectionAsync(
                page,
                repeatTapWhileBusy: true);
            var afterReplace = await _app.ReadNegativeCoverageMutationSnapshotAsync(
                _app.NegativeCoveragePhoneClientId);
            var pageHtml = await page.ContentAsync();
            Assert.True(
                pageHtml.Contains("negative-visit-coverage-panel", StringComparison.Ordinal),
                $"Correction response was not swapped into the canonical workspace." +
                $"{Environment.NewLine}Response:{Environment.NewLine}{replacementResponseHtml}" +
                $"{Environment.NewLine}DOM:{Environment.NewLine}{pageHtml}" +
                $"{Environment.NewLine}{_app.ReadCapturedOutputForDiagnostics()}");
            panel = page.Locator("#negative-visit-coverage-panel");
            var replacementPanelText = await panel.InnerTextAsync();
            Assert.True(
                afterReplace.ClosureCount == 2,
                $"Expected replacement closure source facts.{Environment.NewLine}" +
                replacementPanelText + Environment.NewLine +
                _app.ReadCapturedOutputForDiagnostics());
            Assert.Equal(1, afterReplace.ActiveClosureCount);
            Assert.Equal(1, afterReplace.ReplacedClosureCount);
            Assert.Equal(1, afterReplace.CorrectionCount);
            Assert.Equal(2, afterReplace.PaymentCount);
            Assert.Equal(1, afterReplace.ActivePaymentCount);
            Assert.Equal(375m, afterReplace.TotalPaymentAmount);
            Assert.Equal(125m, afterReplace.ActivePaymentAmount);
            Assert.Equal(1, afterReplace.ActiveItemCount);
            Assert.Equal(2, afterReplace.CreatedClosureAuditCount);
            Assert.Equal(1, afterReplace.ReplacedClosureAuditCount);
            Assert.Equal(2, afterReplace.PaymentCreatedAuditCount);
            Assert.Equal(1, afterReplace.CorrectionIdempotencyCount);

            Assert.Contains(
                "Current negative balance: 1",
                replacementPanelText,
                StringComparison.Ordinal);
            Assert.Equal(1, await panel.Locator("[data-negative-open-visits] li").CountAsync());
            await AssertFitsViewportAsync(page, "phone", "replacement canonical result");

            correction = await OpenOnlyCorrectionAsync(panel);
            Assert.Equal(0, await correction.Locator("input[name='form.Mode']:checked").CountAsync());
            await PreviewCorrectionAsync(
                page,
                mode: "Cancel",
                reason: "Replacement coverage was entered by mistake",
                replacementQuantity: null);

            correction = page.Locator(".negative-coverage-correction");
            await ExpectVisibleAsync(
                correction.GetByText(
                    "Restored negative balance: 2; resulting balance: 2",
                    new() { Exact = true }),
                "phone",
                "cancel balance preview");
            Assert.Equal(1, await correction.Locator("[data-negative-restored-visits] li").CountAsync());
            Assert.Equal(0, await correction.Locator("[data-negative-replacement-visits]").CountAsync());
            Assert.Equal(0, await correction.GetByText(
                "Replacement exact payment",
                new() { Exact = true }).CountAsync());

            await SubmitCorrectionAsync(page, repeatTapWhileBusy: false);
            panel = await RequireCoveragePanelAsync(page, expectedBalance: 2);
            Assert.Equal(2, await panel.Locator("[data-negative-open-visits] li").CountAsync());
            Assert.Equal(0, await panel.Locator(".negative-coverage-correction").CountAsync());
            await AssertFitsViewportAsync(page, "phone", "cancel canonical result");

            var state = await _app.ReadMembershipStateAsync(
                _app.NegativeCoveragePhoneMembershipId);
            Assert.Equal(2, state.CountedVisits);
            Assert.Equal(-2, state.RemainingVisits);
            Assert.Equal(2, state.NegativeBalance);

            var afterCancel = await _app.ReadNegativeCoverageMutationSnapshotAsync(
                _app.NegativeCoveragePhoneClientId);
            Assert.Equal(2, afterCancel.ClosureCount);
            Assert.Equal(0, afterCancel.ActiveClosureCount);
            Assert.Equal(1, afterCancel.ReplacedClosureCount);
            Assert.Equal(1, afterCancel.CanceledClosureCount);
            Assert.Equal(2, afterCancel.CorrectionCount);
            Assert.Equal(2, afterCancel.PaymentCount);
            Assert.Equal(0, afterCancel.ActivePaymentCount);
            Assert.Equal(375m, afterCancel.TotalPaymentAmount);
            Assert.Equal(0m, afterCancel.ActivePaymentAmount);
            Assert.Equal(0, afterCancel.ActiveItemCount);
            Assert.Equal(2, afterCancel.CreatedClosureAuditCount);
            Assert.Equal(1, afterCancel.ReplacedClosureAuditCount);
            Assert.Equal(1, afterCancel.CanceledClosureAuditCount);
            Assert.Equal(2, afterCancel.PaymentCreatedAuditCount);
            Assert.Equal(1, afterCancel.CloseIdempotencyCount);
            Assert.Equal(2, afterCancel.CorrectionIdempotencyCount);
            Assert.Null(afterCancel.ActiveClosureId);

            await page.GotoAsync(
                new Uri(
                    _app.BaseAddress,
                    $"/Audit/ClientHistory?clientId={_app.NegativeCoveragePhoneClientId}&entity=NegativeCoverage")
                    .ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            Assert.Equal("Client history - BodyLife CRM", await page.TitleAsync());
            Assert.Equal(
                "NegativeCoverage",
                await page.GetByLabel("Source type", new() { Exact = true }).InputValueAsync());
            var historyRows = page.Locator(
                "[data-client-history-list] > [data-client-history-row]");
            Assert.Equal(4, await historyRows.CountAsync());
            var historyKinds = await historyRows.EvaluateAllAsync<string[]>(
                "rows => rows.map(row => row.dataset.sourceKind)");
            Assert.Equal(2, historyKinds.Count(kind => kind == "NegativeCoverageCreated"));
            Assert.Single(historyKinds, kind => kind == "NegativeCoverageReplaced");
            Assert.Single(historyKinds, kind => kind == "NegativeCoverageCanceled");
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Negative-visit coverage replaced", Exact = true }),
                "phone",
                "replacement history row");
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Negative-visit coverage canceled", Exact = true }),
                "phone",
                "cancellation history row");
            await ExpectVisibleAsync(
                page.GetByText("250.00 UAH", new() { Exact = true }).First,
                "phone",
                "original exact cash history fact");
            await ExpectVisibleAsync(
                page.GetByText("125.00 UAH", new() { Exact = true }).First,
                "phone",
                "replacement exact cash history fact");
            await AssertFitsViewportAsync(page, "phone", "negative coverage client history");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task StaleCloseRefreshesCanonicalWorkspaceAndKeepsWarning()
    {
        var staleContext = await CreateContextAsync(1024, 768);
        var concurrentContext = await CreateContextAsync(1024, 768);

        try
        {
            var stalePage = await OpenReceptionAsync(
                staleContext,
                "tablet stale negative coverage");
            await SubmitHtmxSearchAsync(
                stalePage,
                ReceptionAppFixture.NegativeCoverageStaleCard);
            await RequireCoveragePanelAsync(stalePage, expectedBalance: 2);
            await PreviewCloseAsync(stalePage, quantity: 2);

            var concurrentPage = await OpenReceptionAsync(
                concurrentContext,
                "tablet concurrent negative coverage");
            await SubmitHtmxSearchAsync(
                concurrentPage,
                ReceptionAppFixture.NegativeCoverageStaleCard);
            await RequireCoveragePanelAsync(concurrentPage, expectedBalance: 2);
            await PreviewCloseAsync(concurrentPage, quantity: 1);
            await SubmitCloseAsync(concurrentPage, repeatTapWhileBusy: false);
            await RequireCoveragePanelAsync(concurrentPage, expectedBalance: 1);

            await SubmitCloseAsync(stalePage, repeatTapWhileBusy: false);
            var refreshedPanel = await RequireCoveragePanelAsync(
                stalePage,
                expectedBalance: 1);
            await ExpectVisibleAsync(
                stalePage.Locator(".profile-operation-message").GetByText(
                    "Data changed while you were submitting. Canonical Reception data was refreshed; review and try again.",
                    new() { Exact = true }),
                "tablet",
                "stale coverage warning");
            Assert.Equal(
                1,
                await refreshedPanel.Locator(
                    "li[data-field='expectedOldestOpenNegativeVisitId']")
                    .CountAsync());
            Assert.Equal(
                1,
                await refreshedPanel.Locator("[data-negative-open-visits] li")
                    .CountAsync());
            Assert.Equal(
                0,
                await refreshedPanel.Locator("input[name='form.Confirmed']")
                    .CountAsync());

            var snapshot = await _app.ReadNegativeCoverageMutationSnapshotAsync(
                _app.NegativeCoverageStaleClientId);
            Assert.Equal(1, snapshot.ClosureCount);
            Assert.Equal(1, snapshot.ActiveClosureCount);
            Assert.Equal(1, snapshot.PaymentCount);
            Assert.Equal(1, snapshot.ActiveItemCount);
            Assert.Equal(1, snapshot.CloseIdempotencyCount);
            await AssertFitsViewportAsync(
                stalePage,
                "tablet",
                "stale canonical coverage refresh");
        }
        finally
        {
            await staleContext.CloseAsync();
            await concurrentContext.CloseAsync();
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
        await page.GotoAsync(
            new Uri(_app.BaseAddress, "/Reception/Index").ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#reception-title").WaitForAsync();
        return page;
    }

    private static async Task SubmitHtmxSearchAsync(IPage page, string query)
    {
        await page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" })
            .FillAsync(query);
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("handler=Search", StringComparison.OrdinalIgnoreCase));
        await page.Locator("#reception-search").GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static async Task<ILocator> RequireCoveragePanelAsync(
        IPage page,
        int expectedBalance)
    {
        var panel = page.Locator("#negative-visit-coverage-panel");
        await panel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
        });
        await panel.GetByText(
            $"Current negative balance: {expectedBalance}",
            new() { Exact = true }).WaitForAsync();
        return panel;
    }

    private static async Task PreviewCloseAsync(IPage page, int quantity)
    {
        var form = page.Locator("#negative-coverage-close-form");
        await form.Locator("#negative-close-0").FillAsync(quantity.ToString());
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains(
                "handler=NegativeVisitCoverageClosePreview",
                StringComparison.OrdinalIgnoreCase));
        await form.GetByRole(
            AriaRole.Button,
            new() { Name = "Preview coverage", Exact = true }).ClickAsync();
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static async Task SubmitCloseAsync(IPage page, bool repeatTapWhileBusy)
    {
        var form = page.Locator("#negative-coverage-close-form");
        await form.Locator("input[name='form.Confirmed']").CheckAsync();
        var submitButton = form.GetByRole(
            AriaRole.Button,
            new() { Name = "Close negative visits", Exact = true });
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains(
                "handler=CloseNegativeVisitsOneOff",
                StringComparison.OrdinalIgnoreCase));
        var disabledTask = page.WaitForFunctionAsync(repeatTapWhileBusy
            ? """
              () => {
                const button = document.querySelector(
                  '#negative-coverage-close-form button[type="submit"]');
                if (!button?.disabled) return false;
                button.click();
                return true;
              }
              """
            : "() => document.querySelector('#negative-coverage-close-form button[type=\"submit\"]')?.disabled === true");
        await submitButton.ClickAsync();
        await disabledTask;

        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static async Task<ILocator> OpenOnlyCorrectionAsync(ILocator panel)
    {
        var correction = panel.Locator(".negative-coverage-correction");
        Assert.Equal(1, await correction.CountAsync());
        if (await correction.GetAttributeAsync("open") is null)
        {
            await correction.Locator("summary").ClickAsync();
        }

        await correction.Locator("form").WaitForAsync();
        return correction;
    }

    private static async Task PreviewCorrectionAsync(
        IPage page,
        string mode,
        string reason,
        int? replacementQuantity)
    {
        var correction = page.Locator(".negative-coverage-correction");
        var form = correction.Locator("form");
        await form.Locator($"input[name='form.Mode'][value='{mode}']").CheckAsync();
        await form.Locator("textarea[name='form.Reason']").FillAsync(reason);
        if (replacementQuantity is { } quantity)
        {
            await form.Locator("input[name='form.ReplacementOneOffLines[0].Quantity']")
                .FillAsync(quantity.ToString());
        }

        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains(
                "handler=NegativeVisitCoverageCorrectionPreview",
                StringComparison.OrdinalIgnoreCase));
        await form.GetByRole(
            AriaRole.Button,
            new() { Name = "Preview coverage", Exact = true }).ClickAsync();
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static async Task<string> SubmitCorrectionAsync(
        IPage page,
        bool repeatTapWhileBusy)
    {
        var form = page.Locator(".negative-coverage-correction form");
        await form.Locator("input[name='form.Confirmed']").CheckAsync();
        var submitButton = form.GetByRole(
            AriaRole.Button,
            new() { Name = "Apply correction", Exact = true });
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains(
                "handler=CorrectNegativeVisitCoverage",
                StringComparison.OrdinalIgnoreCase));
        var disabledTask = page.WaitForFunctionAsync(repeatTapWhileBusy
            ? """
              () => {
                const button = document.querySelector(
                  '.negative-coverage-correction button[type="submit"]');
                if (!button?.disabled) return false;
                button.click();
                return true;
              }
              """
            : "() => document.querySelector('.negative-coverage-correction button[type=\"submit\"]')?.disabled === true");
        await submitButton.ClickAsync();
        await disabledTask;

        var response = await responseTask;
        AssertHtmxResponse(response);
        var responseHtml = await response.TextAsync();
        await WaitForHtmxSettleAsync(page);
        return responseHtml;
    }

    private static Task DelayMutationRequestsAsync(IPage page, string handler)
    {
        return page.RouteAsync(
            $"**/*handler={handler}*",
            async route =>
            {
                if (route.Request.Method == "POST")
                {
                    await Task.Delay(500);
                }

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
}
