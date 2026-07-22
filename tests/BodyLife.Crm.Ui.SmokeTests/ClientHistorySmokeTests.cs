using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class ClientHistorySmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public ClientHistorySmokeTests(ReceptionAppFixture app)
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
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task OwnerAndAdminCanInspectCanonicalClientHistory(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureClientHistoryScenarioAsync();
        Assert.Equal(scenario.PageSize + 2, scenario.TotalEntries);
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize
            {
                Width = width,
                Height = height,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            await LoginAsync(
                page,
                useOwner ? _app.LoginName : _app.AdminLoginName,
                useOwner ? _app.Password : _app.AdminPassword,
                $"{viewportName} client history smoke");
            var occurredDate = scenario.OccurredDate.ToString("yyyy-MM-dd");
            await page.GotoAsync(
                new Uri(
                    _app.BaseAddress,
                    $"/Audit/ClientHistory?clientId={scenario.ClientId}&from={occurredDate}&to={occurredDate}")
                    .ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            Assert.Equal("Client history - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Client history", Exact = true }),
                viewportName,
                "client history heading");
            await ExpectVisibleAsync(
                page.GetByText(
                    useOwner ? "BodyLife Owner" : "Smoke Named Admin",
                    new() { Exact = true }),
                viewportName,
                "current history viewer account");
            await ExpectVisibleAsync(
                page.Locator("[data-client-history-name]").GetByText(
                    scenario.ClientDisplayName,
                    new() { Exact = true }),
                viewportName,
                "client history identity");
            await ExpectVisibleAsync(
                page.Locator("[data-client-history-card]").GetByText(
                    scenario.CardNumber,
                    new() { Exact = true }),
                viewportName,
                "client history card");

            var clientId = page.GetByLabel("Client ID", new() { Exact = true });
            var sourceType = page.GetByLabel("Source type", new() { Exact = true });
            var occurredFrom = page.GetByLabel(
                "Occurred from",
                new() { Exact = true });
            var occurredThrough = page.GetByLabel(
                "Occurred through",
                new() { Exact = true });
            var loadHistory = page.GetByRole(
                AriaRole.Button,
                new() { Name = "Load history", Exact = true });
            await AssertMinimumTouchTargetAsync(clientId, viewportName, "Client filter");
            await AssertMinimumTouchTargetAsync(sourceType, viewportName, "source filter");
            await AssertMinimumTouchTargetAsync(
                occurredFrom,
                viewportName,
                "occurred-from filter");
            await AssertMinimumTouchTargetAsync(
                occurredThrough,
                viewportName,
                "occurred-through filter");
            await AssertMinimumTouchTargetAsync(
                loadHistory,
                viewportName,
                "load history action");

            var firstPageRows = page.Locator(
                "[data-client-history-list] > [data-client-history-row]");
            Assert.Equal(scenario.PageSize, await firstPageRows.CountAsync());
            var firstPageKinds = await firstPageRows.EvaluateAllAsync<string[]>(
                "rows => rows.map(row => row.dataset.sourceKind)");
            Assert.Contains("MembershipIssued", firstPageKinds);
            Assert.Contains("MembershipOpeningStateCreated", firstPageKinds);
            Assert.Contains("VisitMarked", firstPageKinds);
            Assert.Contains("VisitCanceled", firstPageKinds);
            Assert.Contains("PaymentCreated", firstPageKinds);
            Assert.Contains("PaymentCorrected", firstPageKinds);
            Assert.Contains("FreezeAdded", firstPageKinds);
            Assert.Contains("FreezeCanceled", firstPageKinds);
            Assert.Contains("NonWorkingDayAdded", firstPageKinds);

            var featured = page.Locator(
                $"[data-audit-entry-id='{scenario.FeaturedAuditEntryId}']");
            Assert.Equal("PaperFallback", await featured.GetAttributeAsync("data-entry-origin"));
            Assert.Equal(
                "SharedReceptionAdmin",
                await featured.GetAttributeAsync("data-account-kind"));
            Assert.Equal("true", await featured.GetAttributeAsync("data-changed-after-close"));
            await ExpectVisibleAsync(
                featured.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Payment corrected", Exact = true }),
                viewportName,
                "payment correction source");
            await ExpectVisibleAsync(
                featured.GetByText("Paper fallback", new() { Exact = true }),
                viewportName,
                "paper fallback label");
            await ExpectVisibleAsync(
                featured.GetByText("Changed after close", new() { Exact = true }),
                viewportName,
                "changed-after-close label");
            await ExpectVisibleAsync(
                featured.GetByText("Shared Reception/Admin", new() { Exact = true }),
                viewportName,
                "shared account label");
            await ExpectVisibleAsync(
                featured.GetByText(scenario.SharedDeviceLabel, new() { Exact = true }),
                viewportName,
                "shared device label");
            await ExpectVisibleAsync(
                featured.GetByText(
                    scenario.SharedSessionId.ToString("N")[..8],
                    new() { Exact = true }),
                viewportName,
                "shared session label");
            await ExpectVisibleAsync(
                featured.GetByText(
                    BodyLife.Crm.SharedKernel.BusinessTimeZone.ConvertInstantToLocal(scenario.FeaturedOccurredAt).ToString("g", System.Globalization.CultureInfo.GetCultureInfo(ReceptionAppFixture.WorkflowCulture)),
                    new() { Exact = true }).First,
                viewportName,
                "fallback occurred time");
            await ExpectVisibleAsync(
                featured.GetByText(
                    BodyLife.Crm.SharedKernel.BusinessTimeZone.ConvertInstantToLocal(scenario.FeaturedRecordedAt).ToString("g", System.Globalization.CultureInfo.GetCultureInfo(ReceptionAppFixture.WorkflowCulture)),
                    new() { Exact = true }),
                viewportName,
                "fallback recorded time");
            await ExpectVisibleAsync(
                featured.GetByText(
                    $"{scenario.OriginalPaymentAmount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo(ReceptionAppFixture.WorkflowCulture))} UAH",
                    new() { Exact = true }),
                viewportName,
                "original payment amount");
            await ExpectVisibleAsync(
                featured.GetByText(
                    $"{scenario.ReplacementPaymentAmount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo(ReceptionAppFixture.WorkflowCulture))} UAH",
                    new() { Exact = true }),
                viewportName,
                "replacement payment amount");
            var change = featured.Locator("[data-client-history-change]");
            await ExpectVisibleAsync(
                change.GetByText(
                    "Cash amount was entered incorrectly",
                    new() { Exact = true }),
                viewportName,
                "payment correction reason");
            await ExpectVisibleAsync(
                change.GetByText("Amount, Occurred time", new() { Exact = true }),
                viewportName,
                "payment changed fields");
            await ExpectVisibleAsync(
                featured.GetByText(
                    "Entered after reception connectivity returned",
                    new() { Exact = true }),
                viewportName,
                "fallback audit comment");

            var originalPayment = page.Locator(
                $"[data-audit-entry-id='{scenario.OriginalPaymentAuditEntryId}']");
            await ExpectVisibleAsync(
                originalPayment.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Payment recorded", Exact = true }),
                viewportName,
                "original payment source");
            await ExpectVisibleAsync(
                originalPayment.GetByText("Replaced", new() { Exact = true }).First,
                viewportName,
                "original payment replaced status");
            Assert.Equal(
                2,
                await page.Locator("[data-source-kind='VisitMarked']").CountAsync());
            Assert.Equal(
                1,
                await page.Locator("[data-source-kind='VisitCanceled']").CountAsync());
            Assert.Equal(
                1,
                await page.Locator("[data-source-kind='FreezeAdded']").CountAsync());
            Assert.Equal(
                1,
                await page.Locator("[data-source-kind='FreezeCanceled']").CountAsync());

            var identifiers = featured.Locator(".client-history-identifiers > summary");
            await AssertMinimumTouchTargetAsync(
                identifiers,
                viewportName,
                "source identifiers toggle");
            await identifiers.ClickAsync();
            await ExpectVisibleAsync(
                featured.GetByText(
                    scenario.FeaturedAuditEntryId.ToString(),
                    new() { Exact = true }),
                viewportName,
                "audit source identifier");
            await AssertFitsViewportAsync(page, viewportName, "expanded history identifiers");
            await CaptureVisualAsync(page, viewportName, "client-history");

            var next = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Next", Exact = true });
            await AssertMinimumTouchTargetAsync(next, viewportName, "next history page");
            await next.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Assert.Contains($"offset={scenario.PageSize}", page.Url, StringComparison.Ordinal);
            Assert.Equal(
                scenario.TotalEntries - scenario.PageSize,
                await page.Locator(
                    "[data-client-history-list] > [data-client-history-row]")
                    .CountAsync());
            Assert.Equal(occurredDate, await occurredFrom.InputValueAsync());
            Assert.Equal(occurredDate, await occurredThrough.InputValueAsync());
            var previous = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Previous", Exact = true });
            await AssertMinimumTouchTargetAsync(previous, viewportName, "previous history page");

            await sourceType.SelectOptionAsync("Payment");
            await loadHistory.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Assert.DoesNotContain("offset=", page.Url, StringComparison.OrdinalIgnoreCase);
            var paymentRows = page.Locator(
                "[data-client-history-list] > [data-client-history-row]");
            Assert.Equal(2, await paymentRows.CountAsync());
            var filteredKinds = await paymentRows.EvaluateAllAsync<string[]>(
                "rows => rows.map(row => row.dataset.sourceKind)");
            Assert.All(filteredKinds, kind => Assert.StartsWith("Payment", kind));
            await AssertFitsViewportAsync(page, viewportName, "filtered payment history");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task ProfileRecordLinksPreserveClientScopeForHistoryAndAudit(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureClientHistoryScenarioAsync();
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize
            {
                Width = width,
                Height = height,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            await LoginAsync(
                page,
                useOwner ? _app.LoginName : _app.AdminLoginName,
                useOwner ? _app.Password : _app.AdminPassword,
                $"{viewportName} profile record links smoke");
            await OpenClientProfileAsync(
                page,
                scenario.CardNumber,
                scenario.ClientDisplayName);

            var profileUrl = page.Url;
            var profile = page.GetByRole(
                AriaRole.Region,
                new() { Name = "Client profile", Exact = true });
            var records = profile.GetByRole(
                AriaRole.Navigation,
                new() { Name = "Client records", Exact = true });
            await ExpectVisibleAsync(records, viewportName, "Client record navigation");

            var historyLink = records.GetByRole(
                AriaRole.Link,
                new() { Name = "Client history", Exact = true });
            var auditLink = records.GetByRole(
                AriaRole.Link,
                new() { Name = "Audit timeline", Exact = true });
            await AssertClientScopedLinkAsync(
                historyLink,
                scenario.ClientId,
                viewportName,
                "Client history link");
            await AssertClientScopedLinkAsync(
                auditLink,
                scenario.ClientId,
                viewportName,
                "Audit timeline link");
            await AssertFitsViewportAsync(page, viewportName, "profile record navigation");
            await CaptureVisualAsync(page, viewportName, "profile-record-links");

            await historyLink.ClickAsync();
            await page.WaitForURLAsync("**/Audit/ClientHistory?**");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Client history", Exact = true }),
                viewportName,
                "linked Client history");
            Assert.Equal(
                scenario.ClientId.ToString(),
                await page.GetByLabel("Client ID", new() { Exact = true })
                    .InputValueAsync());

            await page.GotoAsync(
                profileUrl,
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            profile = page.GetByRole(
                AriaRole.Region,
                new() { Name = "Client profile", Exact = true });
            records = profile.GetByRole(
                AriaRole.Navigation,
                new() { Name = "Client records", Exact = true });
            auditLink = records.GetByRole(
                AriaRole.Link,
                new() { Name = "Audit timeline", Exact = true });

            await auditLink.ClickAsync();
            await page.WaitForURLAsync("**/Audit/Timeline?**");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Audit timeline", Exact = true }),
                viewportName,
                "linked Audit timeline");
            Assert.Equal(
                scenario.ClientId.ToString(),
                await page.GetByLabel("Client ID", new() { Exact = true })
                    .InputValueAsync());
            Assert.True(
                await page.Locator("[data-audit-entry-list] > [data-audit-entry-id]")
                    .CountAsync() > 0,
                "Client-scoped Audit timeline should retain matching canonical entries.");
            await AssertFitsViewportAsync(page, viewportName, "linked Audit timeline");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task InvalidOffsetKeepsHistoryFiltersAndReturnsNoPartialRows()
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureClientHistoryScenarioAsync();
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize { Width = 1024, Height = 768 },
        });

        try
        {
            var page = await context.NewPageAsync();
            await LoginAsync(
                page,
                _app.LoginName,
                _app.Password,
                "invalid client history offset smoke");
            var occurredDate = scenario.OccurredDate.ToString("yyyy-MM-dd");
            await page.GotoAsync(
                new Uri(
                    _app.BaseAddress,
                    $"/Audit/ClientHistory?clientId={scenario.ClientId}&entity=Visit&from={occurredDate}&to={occurredDate}&offset=-1")
                    .ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            var error = page.GetByRole(AriaRole.Alert);
            await ExpectVisibleAsync(error, "tablet", "invalid history offset error");
            Assert.Contains(
                "Enter valid client, entity, date, and page filters.",
                await error.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.Equal(
                "Visit",
                await page.GetByLabel("Source type", new() { Exact = true })
                    .InputValueAsync());
            Assert.Equal(
                occurredDate,
                await page.GetByLabel("Occurred from", new() { Exact = true })
                    .InputValueAsync());
            Assert.Equal(0, await page.Locator("[data-client-history-list]").CountAsync());
            await AssertFitsViewportAsync(page, "tablet", "invalid history filter");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task UnknownClientReturnsCanonicalErrorWithoutPartialHistory()
    {
        Assert.NotNull(_browser);
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
        });

        try
        {
            var page = await context.NewPageAsync();
            await LoginAsync(
                page,
                _app.AdminLoginName,
                _app.AdminPassword,
                "missing client history smoke");
            await page.GotoAsync(
                new Uri(
                    _app.BaseAddress,
                    $"/Audit/ClientHistory?clientId={Guid.NewGuid()}")
                    .ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            var error = page.GetByRole(AriaRole.Alert);
            await ExpectVisibleAsync(error, "desktop", "missing Client history error");
            Assert.Contains(
                "The requested audit data was not found.",
                await error.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.Equal(0, await page.Locator("[data-client-history-list]").CountAsync());
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task ClientHistoryRequiresAuthentication()
    {
        Assert.NotNull(_browser);
        var context = await _browser.NewContextAsync();

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(
                new Uri(_app.BaseAddress, "/Audit/ClientHistory").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            Assert.Contains("/Login", page.Url, StringComparison.Ordinal);
            Assert.Contains(
                "ReturnUrl=%2FAudit%2FClientHistory",
                page.Url,
                StringComparison.Ordinal);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task LoginAsync(
        IPage page,
        string loginName,
        string password,
        string deviceLabel)
    {
        await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" }).FillAsync(loginName);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
    }

    private static async Task OpenClientProfileAsync(
        IPage page,
        string cardNumber,
        string displayName)
    {
        await page.GetByRole(
                AriaRole.Searchbox,
                new() { Name = "Client search" })
            .FillAsync(cardNumber);
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("handler=Search", StringComparison.OrdinalIgnoreCase));
        await page.GetByRole(
                AriaRole.Button,
                new() { Name = "Search", Exact = true })
            .ClickAsync();
        var response = await responseTask;
        Assert.True(response.Ok, $"Profile search returned HTTP {response.Status}.");
        Assert.Equal("true", response.Request.Headers["hx-request"]);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.htmx-request') === null");
        await page.GetByRole(
                AriaRole.Region,
                new() { Name = "Client profile", Exact = true })
            .GetByRole(
                AriaRole.Heading,
                new() { Name = displayName, Exact = true })
            .WaitForAsync();
    }

    private static async Task AssertClientScopedLinkAsync(
        ILocator link,
        Guid clientId,
        string viewportName,
        string label)
    {
        await ExpectVisibleAsync(link, viewportName, label);
        await AssertMinimumTouchTargetAsync(link, viewportName, label);
        var href = await link.GetAttributeAsync("href");
        Assert.NotNull(href);
        Assert.Contains(
            $"clientId={clientId}",
            href,
            StringComparison.OrdinalIgnoreCase);
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
            bounds.Width >= 44,
            $"{label} should be at least 44px wide on {viewportName}, but was {bounds.Width:F1}px.");
        Assert.True(
            bounds.Height >= 44,
            $"{label} should be at least 44px high on {viewportName}, but was {bounds.Height:F1}px.");
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
            Path = Path.Combine(screenshotDirectory, $"{viewportName}-{state}.png"),
        });
    }
}
