using System.Globalization;
using BodyLife.Crm.SharedKernel;
using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class IssuedMembershipSaleCorrectionSmokeTests
    : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IssuedMembershipSaleCorrectionSmokeTests(ReceptionAppFixture app)
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
    public async Task TabletOwnerReplacesExactIssuedSaleOnceAndRereadsCanonicalProfile()
    {
        var context = await CreateContextAsync(1024, 768);

        try
        {
            var page = await OpenReceptionAsync(context, "tablet issued-sale replace");
            await IssueSaleAsync(page, ReceptionAppFixture.IssuedSaleReplaceCard);
            var originalMembership = await _app.ReadLatestIssuedMembershipAsync(
                _app.IssuedSaleReplaceClientId);
            var originalPayment = await _app.ReadLatestActivePaymentAsync(
                _app.IssuedSaleReplaceClientId);
            var correction = await OpenCorrectionAsync(
                page,
                originalMembership.MembershipId);

            Assert.Equal(
                1,
                await correction.Locator("input[name='__RequestVerificationToken']")
                    .CountAsync());
            Assert.Equal(
                0,
                await correction.Locator("input[name='form.Mode']:checked").CountAsync());
            Assert.Equal(
                "this:drop",
                await correction.Locator("form").GetAttributeAsync("hx-sync"));
            Assert.Contains(
                "950.00 UAH",
                await correction.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.Equal(
                originalPayment.PaymentId.ToString("N")[..8],
                (await correction.Locator("[data-issued-sale-payment-id]")
                    .TextContentAsync())?.Trim());

            var replacementDate = BusinessTimeZone.GetBusinessDate(
                    DateTimeOffset.UtcNow)
                .AddDays(1);
            correction = await PrepareReplacePreviewAsync(
                page,
                originalMembership.MembershipId,
                replacementDate,
                "The wrong membership start date was entered",
                "Tablet issued-sale replacement");

            Assert.Equal(
                "950.00 UAH",
                (await correction.Locator("[data-issued-sale-replacement-price]")
                    .TextContentAsync())?.Trim());
            Assert.Contains(
                "does not calculate a refund",
                await correction.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.Equal(
                0,
                await correction.Locator("input[name*='Amount']").CountAsync());
            Assert.False(string.IsNullOrWhiteSpace(
                await correction.Locator(
                        "input[name='form.ExpectedMembershipTypeUpdatedAt']")
                    .InputValueAsync()));
            Assert.Equal(
                replacementDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                await correction.GetByLabel("Start date", new() { Exact = true })
                    .InputValueAsync());
            await AssertFitsViewportAsync(page, "tablet", "issued-sale replacement preview");

            await DelayMutationRequestsAsync(page);
            await SubmitCorrectionAsync(page, "Replace sale", repeatTapWhileBusy: true);
            await ExpectVisibleAsync(
                OperationStatusTestHelper.Success(page).GetByText(
                    "Issued membership sale replaced",
                    new() { Exact = false }),
                "tablet",
                "replacement success message");

            var snapshot = await _app.ReadIssuedSaleCorrectionSnapshotAsync(
                _app.IssuedSaleReplaceClientId);
            Assert.Equal(1, snapshot.CorrectionCount);
            Assert.Equal("replace", snapshot.CorrectionMode);
            Assert.Equal(originalMembership.MembershipId, snapshot.OriginalMembershipId);
            Assert.Equal("corrected", snapshot.OriginalMembershipStatus);
            Assert.Equal(originalPayment.PaymentId, snapshot.OriginalPaymentId);
            Assert.Equal("replaced", snapshot.OriginalPaymentStatus);
            Assert.NotNull(snapshot.ReplacementMembershipId);
            Assert.Equal("active", snapshot.ReplacementMembershipStatus);
            Assert.NotNull(snapshot.ReplacementPaymentId);
            Assert.Equal("active", snapshot.ReplacementPaymentStatus);
            Assert.Equal(1, snapshot.ReplacedAuditCount);
            Assert.Equal(0, snapshot.SaleCanceledAuditCount);
            Assert.Equal(1, snapshot.ReplacementPaymentCreatedAuditCount);
            Assert.Equal(1, snapshot.IdempotencyCount);

            var canonicalCorrection = page.Locator(
                $"[data-issued-membership-id='{snapshot.ReplacementMembershipId}']");
            await ExpectVisibleAsync(
                canonicalCorrection,
                "tablet",
                "replacement canonical correction entry");
            Assert.Equal(
                0,
                await page.Locator(
                    $"[data-issued-membership-id='{originalMembership.MembershipId}']")
                    .CountAsync());
            await AssertFitsViewportAsync(page, "tablet", "replacement canonical profile");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task PhoneStaleCancelRefreshesCanonicalProfileAndOnlyOneCorrectionCommits()
    {
        var staleContext = await CreateContextAsync(390, 844);
        var concurrentContext = await CreateContextAsync(390, 844);

        try
        {
            var stalePage = await OpenReceptionAsync(
                staleContext,
                "phone stale issued-sale cancel");
            await IssueSaleAsync(stalePage, ReceptionAppFixture.IssuedSaleStaleCard);
            var originalMembership = await _app.ReadLatestIssuedMembershipAsync(
                _app.IssuedSaleStaleClientId);
            var originalPayment = await _app.ReadLatestActivePaymentAsync(
                _app.IssuedSaleStaleClientId);
            await PrepareCancelPreviewAsync(
                stalePage,
                originalMembership.MembershipId,
                "The sale was recorded for the wrong client");

            var concurrentPage = await OpenReceptionAsync(
                concurrentContext,
                "phone concurrent issued-sale cancel");
            await SubmitHtmxSearchAsync(
                concurrentPage,
                ReceptionAppFixture.IssuedSaleStaleCard);
            await PrepareCancelPreviewAsync(
                concurrentPage,
                originalMembership.MembershipId,
                "The sale was recorded for the wrong client");
            await SubmitCorrectionAsync(
                concurrentPage,
                "Cancel sale",
                repeatTapWhileBusy: false);
            await ExpectVisibleAsync(
                OperationStatusTestHelper.Success(concurrentPage).GetByText(
                    "Issued membership sale canceled",
                    new() { Exact = false }),
                "phone",
                "cancel success message");
            Assert.Equal(
                0,
                await concurrentPage.Locator("[data-issued-sale-correction]")
                    .CountAsync());

            await SubmitCorrectionAsync(
                stalePage,
                "Cancel sale",
                repeatTapWhileBusy: false);
            await ExpectVisibleAsync(
                stalePage.Locator(".profile-operation-message").GetByText(
                    "Data changed while you were submitting. Canonical Reception data was refreshed; review and try again.",
                    new() { Exact = true }),
                "phone",
                "stale sale warning");
            Assert.Equal(
                0,
                await stalePage.Locator("[data-issued-sale-correction]").CountAsync());

            var snapshot = await _app.ReadIssuedSaleCorrectionSnapshotAsync(
                _app.IssuedSaleStaleClientId);
            Assert.Equal(1, snapshot.CorrectionCount);
            Assert.Equal("cancel", snapshot.CorrectionMode);
            Assert.Equal(originalMembership.MembershipId, snapshot.OriginalMembershipId);
            Assert.Equal("canceled", snapshot.OriginalMembershipStatus);
            Assert.Equal(originalPayment.PaymentId, snapshot.OriginalPaymentId);
            Assert.Equal("canceled", snapshot.OriginalPaymentStatus);
            Assert.Null(snapshot.ReplacementMembershipId);
            Assert.Null(snapshot.ReplacementPaymentId);
            Assert.Equal(0, snapshot.ReplacedAuditCount);
            Assert.Equal(1, snapshot.SaleCanceledAuditCount);
            Assert.Equal(0, snapshot.ReplacementPaymentCreatedAuditCount);
            Assert.Equal(1, snapshot.IdempotencyCount);
            await AssertFitsViewportAsync(stalePage, "phone", "stale cancel refresh");
        }
        finally
        {
            await staleContext.CloseAsync();
            await concurrentContext.CloseAsync();
        }
    }

    [Theory]
    [InlineData(
        "tablet",
        1024,
        768,
        ReceptionAppFixture.IssuedSaleDependencyTabletCard)]
    [InlineData(
        "phone",
        390,
        844,
        ReceptionAppFixture.IssuedSaleDependencyPhoneCard)]
    public async Task CountedVisitDependencyRemainsAVisibleCorrectionBlocker(
        string viewportName,
        int width,
        int height,
        string cardNumber)
    {
        var clientId = viewportName == "tablet"
            ? _app.IssuedSaleDependencyTabletClientId
            : _app.IssuedSaleDependencyPhoneClientId;
        var context = await CreateContextAsync(width, height);

        try
        {
            var page = await OpenReceptionAsync(
                context,
                $"{viewportName} issued-sale dependency");
            await IssueSaleAsync(page, cardNumber);
            var originalMembership = await _app.ReadLatestIssuedMembershipAsync(clientId);
            var originalPayment = await _app.ReadLatestActivePaymentAsync(clientId);
            var visitId = await _app.InsertExternalCountedVisitAsync(
                clientId,
                originalMembership.MembershipId);

            await page.ReloadAsync(new PageReloadOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            var correction = await OpenCorrectionAsync(
                page,
                originalMembership.MembershipId);
            var dependency = correction.Locator(
                $"[data-issued-sale-dependencies] " +
                $"li[data-dependency-type='visit'][data-dependency-id='{visitId}']");
            await ExpectVisibleAsync(
                dependency,
                viewportName,
                "counted Visit dependency");
            Assert.Contains(
                "Counted visit",
                await dependency.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.Equal(
                0,
                await correction.Locator("input[name='form.Confirmed']").CountAsync());
            Assert.Equal(
                0,
                await correction.Locator("button[type='submit']").CountAsync());

            await TriggerPreviewAsync(
                page,
                () => correction.GetByLabel("Cancel sale", new() { Exact = true })
                    .CheckAsync());
            correction = await OpenCorrectionAsync(
                page,
                originalMembership.MembershipId);
            await ExpectVisibleAsync(
                correction.Locator(
                    $"[data-issued-sale-dependencies] " +
                    $"li[data-dependency-id='{visitId}']"),
                viewportName,
                "previewed counted Visit dependency");
            Assert.Equal(
                0,
                await correction.Locator("input[name='form.Confirmed']").CountAsync());
            Assert.Equal(
                0,
                await correction.Locator("button[type='submit']").CountAsync());

            var snapshot = await _app.ReadIssuedSaleCorrectionSnapshotAsync(clientId);
            Assert.Equal(0, snapshot.CorrectionCount);
            Assert.Equal(0, snapshot.IdempotencyCount);
            Assert.Equal(originalPayment.PaymentId,
                (await _app.ReadLatestActivePaymentAsync(clientId)).PaymentId);
            Assert.Equal(
                "active",
                (await _app.ReadLatestIssuedMembershipAsync(clientId)).Status);
            await AssertFitsViewportAsync(
                page,
                viewportName,
                "issued-sale dependency blocker");
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
        var response = await page.GotoAsync(
            _app.BaseAddress.ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
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

    private async Task IssueSaleAsync(IPage page, string cardNumber)
    {
        await SubmitHtmxSearchAsync(page, cardNumber);
        var panel = page.Locator("#issue-membership-action-panel");
        if (await panel.GetAttributeAsync("open") is null)
        {
            await page.Locator("[data-profile-action-target='issue-membership-action-panel']")
                .ClickAsync();
        }

        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains(
                "handler=IssueMembershipPreview",
                StringComparison.OrdinalIgnoreCase));
        await panel.GetByLabel("Membership type", new() { Exact = true })
            .SelectOptionAsync(_app.IssueMembershipTypeId.ToString());
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);

        panel = page.Locator("#issue-membership-action-panel");
        await panel.GetByLabel("Comment (optional)", new() { Exact = true })
            .FillAsync("Issued-sale correction smoke source");
        responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains(
                "handler=IssueMembership",
                StringComparison.OrdinalIgnoreCase));
        await panel.GetByRole(
                AriaRole.Button,
                new() { Name = "Issue membership", Exact = true })
            .ClickAsync();
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
        await ExpectVisibleAsync(
            OperationStatusTestHelper.Success(page).GetByText(
                "Membership issued with cash payment.",
                new() { Exact = false }),
            "workflow",
            "issue sale success message");
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

    private static async Task<ILocator> OpenCorrectionAsync(
        IPage page,
        Guid membershipId)
    {
        var correction = page.Locator(
            $"[data-issued-membership-id='{membershipId}']");
        await correction.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
        });
        var details = correction.Locator("details");
        if (await details.GetAttributeAsync("open") is null)
        {
            await details.Locator("summary").ClickAsync();
        }

        await correction.Locator("form").WaitForAsync();
        return correction;
    }

    private async Task<ILocator> PrepareReplacePreviewAsync(
        IPage page,
        Guid membershipId,
        DateOnly replacementStartDate,
        string reason,
        string comment)
    {
        var correction = await OpenCorrectionAsync(page, membershipId);
        await TriggerPreviewAsync(
            page,
            () => correction.GetByLabel("Replace sale", new() { Exact = true })
                .CheckAsync());
        correction = await OpenCorrectionAsync(page, membershipId);
        Assert.Equal(
            string.Empty,
            await correction.GetByLabel(
                    "Replacement membership type",
                    new() { Exact = true })
                .InputValueAsync());

        await TriggerPreviewAsync(
            page,
            () => correction.GetByLabel(
                    "Replacement membership type",
                    new() { Exact = true })
                .SelectOptionAsync(_app.IssueMembershipTypeId.ToString()));
        correction = await OpenCorrectionAsync(page, membershipId);
        var replacementDate = replacementStartDate.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);
        await TriggerPreviewAsync(
            page,
            () => correction.GetByLabel("Start date", new() { Exact = true })
                .EvaluateAsync(
                    """
                    (element, value) => {
                      element.value = value;
                      element.dispatchEvent(new Event('change', { bubbles: true }));
                    }
                    """,
                    replacementDate));
        correction = await OpenCorrectionAsync(page, membershipId);
        await correction.GetByLabel("Reason", new() { Exact = true }).FillAsync(reason);
        await correction.GetByLabel("Comment (optional)", new() { Exact = true })
            .FillAsync(comment);
        await TriggerPreviewAsync(
            page,
            () => correction.GetByRole(
                    AriaRole.Button,
                    new() { Name = "Refresh correction preview", Exact = true })
                .ClickAsync());
        correction = await OpenCorrectionAsync(page, membershipId);
        Assert.False(string.IsNullOrWhiteSpace(
            await correction.GetByLabel("Event time", new() { Exact = true })
                .InputValueAsync()));
        await correction.Locator("input[name='form.Confirmed']").CheckAsync();
        return correction;
    }

    private static async Task<ILocator> PrepareCancelPreviewAsync(
        IPage page,
        Guid membershipId,
        string reason)
    {
        var correction = await OpenCorrectionAsync(page, membershipId);
        await TriggerPreviewAsync(
            page,
            () => correction.GetByLabel("Cancel sale", new() { Exact = true })
                .CheckAsync());
        correction = await OpenCorrectionAsync(page, membershipId);
        await correction.GetByLabel("Reason", new() { Exact = true }).FillAsync(reason);
        await TriggerPreviewAsync(
            page,
            () => correction.GetByRole(
                    AriaRole.Button,
                    new() { Name = "Refresh correction preview", Exact = true })
                .ClickAsync());
        correction = await OpenCorrectionAsync(page, membershipId);
        await ExpectVisibleAsync(
            correction.GetByText(
                "The original membership and its exact payment will be canceled without a replacement.",
                new() { Exact = true }),
            "phone",
            "cancel source-fact preview");
        Assert.Contains(
            "does not calculate a refund",
            await correction.InnerTextAsync(),
            StringComparison.Ordinal);
        Assert.Equal(
            0,
            await correction.Locator("[data-issued-sale-replacement-price]")
                .CountAsync());
        await correction.Locator("input[name='form.Confirmed']").CheckAsync();
        return correction;
    }

    private static async Task TriggerPreviewAsync(
        IPage page,
        Func<Task> trigger)
    {
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains(
                "handler=IssuedMembershipSaleCorrectionPreview",
                StringComparison.OrdinalIgnoreCase));
        await trigger();
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static async Task SubmitCorrectionAsync(
        IPage page,
        string buttonName,
        bool repeatTapWhileBusy)
    {
        var form = page.Locator("[data-issued-sale-correction-form]");
        var submit = form.GetByRole(
            AriaRole.Button,
            new() { Name = buttonName, Exact = true });
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains(
                "handler=IssuedMembershipSaleCorrection",
                StringComparison.OrdinalIgnoreCase)
            && !response.Url.Contains("Preview", StringComparison.OrdinalIgnoreCase));
        var disabledTask = page.WaitForFunctionAsync(repeatTapWhileBusy
            ?
            """
            () => {
              const button = document.querySelector(
                '[data-issued-sale-correction-form] button[type="submit"]');
              if (!button?.disabled) return false;
              button.click();
              return true;
            }
            """
            :
            """
            () => document.querySelector(
              '[data-issued-sale-correction-form] button[type="submit"]')?.disabled === true
            """);
        await submit.ClickAsync();
        await disabledTask;
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static Task DelayMutationRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=IssuedMembershipSaleCorrection*",
            async route =>
            {
                if (route.Request.Method == "POST"
                    && !route.Request.Url.Contains(
                        "Preview",
                        StringComparison.OrdinalIgnoreCase))
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
