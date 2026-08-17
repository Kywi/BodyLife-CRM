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
    [InlineData("desktop", 1440, 900)]
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
            var response = await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });

            Assert.NotNull(response);
            Assert.True(response.Ok, $"{viewportName} request returned HTTP {response.Status}.");

            await ExpectVisibleAsync(page.GetByRole(AriaRole.Heading, new() { Name = "Login" }), viewportName, "login heading");
            var deviceLabel = $"{viewportName} smoke";
            await LoginAsync(page, _app.LoginName, _app.Password, deviceLabel);

            Assert.Equal("Clients - BodyLife CRM", await page.TitleAsync());

            var accountMenu = page.Locator("details.account-menu");
            await ExpectVisibleAsync(accountMenu, viewportName, "current session shell");
            await ExpectVisibleAsync(accountMenu.Locator(".account-menu-summary-copy").GetByText("BodyLife Owner", new() { Exact = true }), viewportName, "current account display name");
            await accountMenu.Locator("summary").ClickAsync();
            await ExpectVisibleAsync(accountMenu.Locator(".account-menu-popover").GetByText("Owner account / Owner", new() { Exact = true }), viewportName, "current account type and role");
            await ExpectVisibleAsync(accountMenu.Locator(".account-menu-session").GetByText(deviceLabel, new() { Exact = true }), viewportName, "current device label");
            await ExpectVisibleAsync(accountMenu.Locator(".account-menu-session").GetByText("Session", new() { Exact = false }), viewportName, "current session id");
            await ExpectVisibleAsync(accountMenu.GetByRole(AriaRole.Button, new() { Name = "Log out", Exact = true }), viewportName, "logout button");
            await accountMenu.Locator("summary").ClickAsync();
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Heading, new() { Name = "Clients", Exact = true }), viewportName, "clients heading");
            var receptionSearch = page.Locator("#reception-search");
            await ExpectVisibleAsync(receptionSearch.GetByRole(AriaRole.Searchbox, new() { Name = "Client search", Exact = true }), viewportName, "client search input");
            await ExpectVisibleAsync(receptionSearch.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }), viewportName, "search button");
            Assert.Equal(1, await page.Locator("form.global-client-search").CountAsync());
            Assert.Equal(1, await page.Locator("input[type='search']").CountAsync());
            Assert.Equal(0, await page.Locator("#global-client-search").CountAsync());
            var canvas = page.Locator("[data-visual-role='clients-canvas']");
            Assert.Equal(1, await canvas.CountAsync());
            Assert.Equal(0, await page.Locator(".search-focal-surface, .search-results-surface, .create-client-surface").CountAsync());
            await ExpectVisibleAsync(
                page.Locator(".clients-search-controls > summary"),
                viewportName,
                "compact search options control");
            var searchResults = page.GetByRole(AriaRole.Region, new() { Name = "Search results" });
            var clientProfile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await ExpectVisibleAsync(searchResults, viewportName, "search results region");
            await ExpectVisibleAsync(
                searchResults.Locator(".search-state-idle"),
                viewportName,
                "honest idle search state");
            var idleTopology = await searchResults.Locator(".search-state-idle").EvaluateAsync<string>("""
                state => {
                    const style = getComputedStyle(state);
                    return JSON.stringify({
                        border: style.borderTopWidth,
                        background: style.backgroundColor,
                        shadow: style.boxShadow,
                    });
                }
                """);
            Assert.Contains("\"border\":\"0px\"", idleTopology, StringComparison.Ordinal);
            Assert.Contains("\"background\":\"rgba(0, 0, 0, 0)\"", idleTopology, StringComparison.Ordinal);
            Assert.Contains("\"shadow\":\"none\"", idleTopology, StringComparison.Ordinal);
            Assert.Equal(0, await clientProfile.CountAsync());

            var headerBounds = await page.Locator(".app-global-header").BoundingBoxAsync();
            var brandBounds = await page.Locator(".app-header-brand").BoundingBoxAsync();
            var searchBounds = await receptionSearch.BoundingBoxAsync();
            var createBounds = await page.Locator(".global-create-client").BoundingBoxAsync();
            var accountBounds = await accountMenu.BoundingBoxAsync();
            Assert.NotNull(headerBounds);
            Assert.NotNull(brandBounds);
            Assert.NotNull(searchBounds);
            Assert.NotNull(createBounds);
            Assert.NotNull(accountBounds);
            if (viewportName == "desktop")
            {
                Assert.True(searchBounds.Width >= 420, "Desktop Clients Search should own the flexible header track.");
                Assert.InRange(createBounds.X - (searchBounds.X + searchBounds.Width), 6, 16);
                Assert.True(searchBounds.X >= brandBounds.X + brandBounds.Width);
                Assert.True(createBounds.X + createBounds.Width <= accountBounds.X);
            }
            else if (viewportName == "tablet")
            {
                Assert.InRange(headerBounds.Height, 76, 82);
                Assert.True(searchBounds.Width >= 300, "Tablet Clients Search should retain the flexible header width.");
                Assert.InRange(Math.Abs(searchBounds.Y - createBounds.Y), 0, 3);
                Assert.InRange(Math.Abs(searchBounds.Y - accountBounds.Y), 0, 3);
            }
            else
            {
                var firstRowBottom = Math.Max(
                    brandBounds.Y + brandBounds.Height,
                    accountBounds.Y + accountBounds.Height);
                Assert.True(searchBounds.Y >= firstRowBottom);
                Assert.True(createBounds.Y >= searchBounds.Y + searchBounds.Height);
            }
            await CaptureViewportVisualAsync(page, viewportName, "search-idle");

            var directCreateLink = page.Locator(".global-create-client");
            var directCreateHref = await directCreateLink.GetAttributeAsync("href");
            Assert.NotNull(directCreateHref);
            Assert.Contains("create=true", directCreateHref, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("#create-client-action-panel", directCreateHref, StringComparison.Ordinal);
            await directCreateLink.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Assert.EndsWith("#create-client-action-panel", page.Url, StringComparison.Ordinal);

            var initialCreatePanel = page.Locator("#create-client-action-panel");
            await ExpectVisibleAsync(initialCreatePanel.Locator("summary"), viewportName, "always available create-client action");
            Assert.True(
                await initialCreatePanel.EvaluateAsync<bool>("element => element.open"),
                "The header direct-create entry should open the existing server form.");
            var directPanelBounds = await initialCreatePanel.BoundingBoxAsync();
            var stickyHeaderBounds = await page.Locator(".app-global-header").BoundingBoxAsync();
            Assert.NotNull(directPanelBounds);
            Assert.NotNull(stickyHeaderBounds);
            Assert.True(
                directPanelBounds.Y >= stickyHeaderBounds.Y + stickyHeaderBounds.Height - 1,
                $"Direct Create should not be hidden under the sticky header on {viewportName}.");
            Assert.True(
                directPanelBounds.Y < height,
                $"Direct Create should land inside the {viewportName} viewport.");
            await AssertMinimumTouchTargetAsync(
                initialCreatePanel.Locator("summary"),
                viewportName,
                "create-client action");
            await ExpectVisibleAsync(
                initialCreatePanel.GetByRole(AriaRole.Heading, new() { Name = "Client details", Exact = true }),
                viewportName,
                "direct create-client heading");
            Assert.Contains(
                "clients-create-panel",
                await initialCreatePanel.GetAttributeAsync("class") ?? string.Empty,
                StringComparison.Ordinal);
            Assert.Equal(1, await canvas.Locator("#create-client-action-panel").CountAsync());
            await CaptureViewportVisualAsync(page, viewportName, "direct-create");
            await initialCreatePanel.Locator("summary").ClickAsync();
            await AssertMinimumTouchTargetAsync(
                page.Locator("#reception-search").GetByRole(AriaRole.Searchbox, new() { Name = "Client search", Exact = true }),
                viewportName,
                "client search input");
            await AssertMinimumTouchTargetAsync(
                page.Locator("#reception-search").GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }),
                viewportName,
                "search button");
            var searchControls = page.Locator(".clients-search-controls");
            await searchControls.Locator("summary").ClickAsync();
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Group, new() { Name = "Search mode" }), viewportName, "search mode control");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Checkbox, new() { Name = "Include inactive" }), viewportName, "inactive-client control");
            await AssertMinimumTouchTargetAsync(page.GetByRole(AriaRole.Link, new() { Name = "Clear", Exact = true }), viewportName, "clear search link");
            await AssertMinimumTouchTargetsAsync(
                page.Locator(".search-mode-segments span"),
                viewportName,
                "search mode option");
            await AssertMinimumTouchTargetAsync(
                page.Locator(".checkbox-control"),
                viewportName,
                "include inactive control");
            await searchControls.Locator("summary").ClickAsync();
            await AssertFitsViewportAsync(page, viewportName, "initial dashboard");

            await SubmitHtmxSearchAsync(page, "BL-1001");

            Assert.Contains("q=BL-1001", page.Url, StringComparison.Ordinal);
            Assert.DoesNotContain("handler=Search", page.Url, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                "BL-1001",
                await page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" }).InputValueAsync());
            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "Kovalenko Olena" }), viewportName, "exact-card profile");
            if (viewportName == "tablet")
            {
                await AssertRouteHeadingBelowHeaderAsync(page);
            }
            Assert.Equal(0, await searchResults.CountAsync());
            Assert.Equal(1, await canvas.Locator("#client-profile").CountAsync());
            var passport = clientProfile.Locator("[data-profile-passport]");
            Assert.Equal(1, await passport.CountAsync());
            Assert.Equal(1, await passport.Locator(".profile-passport-membership").CountAsync());
            Assert.Equal(0, await clientProfile.Locator(".profile-identity-facts, .membership-readiness-strip").CountAsync());
            var visitsTab = clientProfile.Locator("[data-profile-history-tab='visits']");
            var paymentsTab = clientProfile.Locator("[data-profile-history-tab='payments']");
            var visitsPanel = clientProfile.Locator("[data-profile-history-panel='visits']");
            var paymentsPanel = clientProfile.Locator("[data-profile-history-panel='payments']");
            Assert.Equal("true", await visitsTab.GetAttributeAsync("aria-selected"));
            Assert.Equal("false", await paymentsTab.GetAttributeAsync("aria-selected"));
            Assert.Equal("0", await visitsTab.GetAttributeAsync("tabindex"));
            Assert.Equal("-1", await paymentsTab.GetAttributeAsync("tabindex"));
            Assert.True(await visitsPanel.IsVisibleAsync());
            Assert.True(await paymentsPanel.IsHiddenAsync());
            await visitsTab.FocusAsync();
            await visitsTab.PressAsync("ArrowRight");
            Assert.Equal("true", await paymentsTab.GetAttributeAsync("aria-selected"));
            Assert.True(await paymentsPanel.IsVisibleAsync());
            await paymentsTab.PressAsync("Home");
            Assert.Equal("true", await visitsTab.GetAttributeAsync("aria-selected"));
            Assert.True(await visitsPanel.IsVisibleAsync());
            var passportDetails = passport.Locator(".profile-passport-details");
            Assert.False(await passportDetails.EvaluateAsync<bool>("element => element.open"));
            await passportDetails.Locator(":scope > summary").ClickAsync();
            await ExpectVisibleAsync(
                passportDetails.Locator(".client-profile-meta")
                    .GetByText("BL-1001", new() { Exact = true }),
                viewportName,
                "exact-card profile number");
            if (viewportName != "phone")
            {
                Assert.Equal(
                    "sticky",
                    await passport.EvaluateAsync<string>(
                        "element => getComputedStyle(element).position"));
            }
            await passportDetails.Locator(":scope > summary").ClickAsync();
            await ExpectVisibleAsync(clientProfile.GetByText("No current membership", new() { Exact = true }), viewportName, "membership placeholder");
            Assert.Equal(1, await clientProfile.Locator(".profile-action-workspace").CountAsync());
            Assert.Equal(3, await clientProfile.Locator(".profile-action-dock > .profile-action-panel").CountAsync());
            var actionSwitcher = clientProfile.Locator("[data-profile-action-switcher]");
            await ExpectVisibleAsync(actionSwitcher, viewportName, "profile action switcher");
            Assert.Equal(3, await actionSwitcher.Locator("[data-profile-action-target]").CountAsync());
            Assert.True(
                await clientProfile.Locator("#mark-visit-action-panel").EvaluateAsync<bool>("element => element.open"),
                "Mark Visit should be the initially open reception action.");
            Assert.Equal(0, await clientProfile.Locator("#negative-visit-coverage-panel").CountAsync());
            foreach (var actionPanelId in new[]
                     {
                         "mark-visit-action-panel",
                         "issue-membership-action-panel",
                         "add-payment-action-panel",
                     })
            {
                var actionPanel = clientProfile.Locator($"#{actionPanelId}");
                Assert.Equal("profile-action", await actionPanel.GetAttributeAsync("name"));
                Assert.Equal(1, await actionPanel.Locator("form[hx-swap='outerHTML']").CountAsync());
            }
            await AssertMinimumTouchTargetsAsync(
                actionSwitcher.Locator("[data-profile-action-target]"),
                viewportName,
                "profile action");
            var actionContentBox = await clientProfile
                .Locator("#mark-visit-action-panel > .profile-action-content")
                .BoundingBoxAsync();
            Assert.NotNull(actionContentBox);
            var triggerCount = await actionSwitcher.Locator("[data-profile-action-target]").CountAsync();
            for (var triggerIndex = 0; triggerIndex < triggerCount; triggerIndex++)
            {
                var triggerBox = await actionSwitcher
                    .Locator("[data-profile-action-target]")
                    .Nth(triggerIndex)
                    .BoundingBoxAsync();
                Assert.NotNull(triggerBox);
                Assert.True(
                    triggerBox.Y + triggerBox.Height <= actionContentBox.Y + 1,
                    $"{viewportName}: every action trigger must remain above the active form.");
            }
            Assert.Equal(
                "rgb(32, 38, 43)",
                await clientProfile.Locator("[data-mark-visit-submit]")
                    .EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
            await AssertMinimumTouchTargetAsync(
                passport.Locator("[data-profile-passport-mark-visit]"),
                viewportName,
                "passport Mark Visit action");
            var passportGeometry = await clientProfile.EvaluateAsync<string>(
                """
                profile => {
                    const passport = profile.querySelector('[data-profile-passport]');
                    const workflow = profile.querySelector('.profile-workflow-column');
                    const passportBox = passport.getBoundingClientRect();
                    const workflowBox = workflow.getBoundingClientRect();
                    return JSON.stringify({
                        position: getComputedStyle(passport).position,
                        passportRight: passportBox.left > workflowBox.left,
                        passportFirst: passportBox.top <= workflowBox.top + 1,
                    });
                }
                """);
            if (viewportName == "phone")
            {
                Assert.Contains("\"position\":\"static\"", passportGeometry, StringComparison.Ordinal);
                Assert.Contains("\"passportFirst\":true", passportGeometry, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("\"position\":\"sticky\"", passportGeometry, StringComparison.Ordinal);
                Assert.Contains("\"passportRight\":true", passportGeometry, StringComparison.Ordinal);
            }
            var profileRegions = await clientProfile.EvaluateAsync<string[]>(
                """
                profile => {
                    const style = (selector) => {
                        const region = selector === '#client-profile'
                            ? profile
                            : profile.querySelector(selector);
                        const computed = getComputedStyle(region);
                        return {
                            background: computed.backgroundColor,
                            rail: computed.borderInlineStartWidth,
                            railColor: computed.borderInlineStartColor,
                        };
                    };
                    return [
                        style('#client-profile'),
                        style('.profile-passport'),
                        style('.profile-passport-membership'),
                        style('.profile-action-workspace'),
                        style('.profile-activity-ledger'),
                    ].flatMap(region => [region.background, region.rail, region.railColor]);
                }
                """);
            Assert.Equal(15, profileRegions.Length);
            Assert.NotEqual("rgba(0, 0, 0, 0)", profileRegions[0]);
            Assert.Equal("4px", profileRegions[1]);
            Assert.Equal("rgb(20, 109, 168)", profileRegions[2]);
            for (var regionIndex = 1; regionIndex < 5; regionIndex++)
            {
                Assert.Equal("0px", profileRegions[(regionIndex * 3) + 1]);
            }
            await AssertFitsViewportAsync(page, viewportName, "exact-card profile");
            if (viewportName == "tablet")
            {
                await AlignRouteHeadingBelowHeaderAsync(page);
            }
            await CaptureViewportVisualAsync(page, viewportName, "exact-profile");

            await SubmitHtmxSearchAsync(page, "BL-PAYMENT-HISTORY");

            await ExpectVisibleAsync(
                clientProfile.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Payment History" }),
                viewportName,
                "payment-history profile");
            await clientProfile.Locator("[data-profile-history-tab='payments']").ClickAsync();
            Assert.Equal("true", await clientProfile.Locator("[data-profile-history-tab='payments']").GetAttributeAsync("aria-selected"));
            var recentPayments = clientProfile.GetByRole(
                AriaRole.Region,
                new() { Name = "Recent payments" });
            await ExpectVisibleAsync(
                recentPayments,
                viewportName,
                "recent Payments history");
            Assert.Equal(4, await recentPayments.Locator(".recent-payment-row").CountAsync());
            Assert.Equal(
                2,
                await recentPayments.Locator("[data-payment-status='active']").CountAsync());
            Assert.Equal(
                1,
                await recentPayments.Locator("[data-payment-status='canceled']").CountAsync());
            Assert.Equal(
                1,
                await recentPayments.Locator("[data-payment-status='replaced']").CountAsync());

            var trialPayment = recentPayments.Locator(".recent-payment-row")
                .Filter(new LocatorFilterOptions { HasText = "100.00 UAH" });
            await ExpectVisibleAsync(
                trialPayment.Locator(".recent-payment-context").GetByText(
                    "Trial",
                    new() { Exact = true }),
                viewportName,
                "trial Payment context");
            await trialPayment.Locator("[data-profile-history-disclosure] > summary").ClickAsync();
            await ExpectVisibleAsync(
                trialPayment.Locator(".recent-payment-comment").GetByText(
                    "Trial cash entry",
                    new() { Exact = true }),
                viewportName,
                "trial Payment comment");

            var canceledPayment = recentPayments.Locator(
                "[data-payment-status='canceled']");
            await ExpectVisibleAsync(
                canceledPayment.GetByText("250.00 UAH", new() { Exact = true }),
                viewportName,
                "canceled Payment amount");
            await canceledPayment.Locator("[data-profile-history-disclosure] > summary").ClickAsync();
            await ExpectVisibleAsync(
                canceledPayment.GetByText("Duplicate cash entry", new() { Exact = true }),
                viewportName,
                "Payment cancellation reason");
            await ExpectVisibleAsync(
                canceledPayment.GetByText("Paper fallback", new() { Exact = true }),
                viewportName,
                "Payment cancellation source");

            var replacementPayment = recentPayments.Locator(".recent-payment-row")
                .Filter(new LocatorFilterOptions { HasText = "900.00 UAH" });
            await replacementPayment.Locator("[data-profile-history-disclosure] > summary").ClickAsync();
            await ExpectVisibleAsync(
                replacementPayment.Locator(".recent-payment-meta").GetByText(
                    "Eight visits / 30 days",
                    new() { Exact = true }),
                viewportName,
                "Payment Membership snapshot");
            await ExpectVisibleAsync(
                replacementPayment.GetByText(
                    "Corrected replacement",
                    new() { Exact = true }),
                viewportName,
                "replacement correction direction");
            await ExpectVisibleAsync(
                replacementPayment.GetByText(
                    "Changed: amount, occurred time",
                    new() { Exact = true }),
                viewportName,
                "replacement changed fields");
            await ExpectVisibleAsync(
                replacementPayment.GetByText(
                    "Manual backfill",
                    new() { Exact = true }),
                viewportName,
                "replacement correction source");

            var originalPayment = recentPayments.Locator(
                "[data-payment-status='replaced']");
            await originalPayment.Locator("[data-profile-history-disclosure] > summary").ClickAsync();
            await ExpectVisibleAsync(
                originalPayment.GetByText("1,000.00 UAH", new() { Exact = true }),
                viewportName,
                "original Payment amount");
            await ExpectVisibleAsync(
                originalPayment.GetByText("Replaced payment", new() { Exact = true }),
                viewportName,
                "outgoing correction direction");
            await ExpectVisibleAsync(
                originalPayment.GetByText("Paper fallback", new() { Exact = true }),
                viewportName,
                "original Payment source");
            Assert.Equal(
                2,
                await recentPayments.Locator("[data-correct-payment-panel]").CountAsync());
            Assert.Equal(
                0,
                await canceledPayment.Locator("[data-correct-payment-panel]").CountAsync());
            Assert.Equal(
                0,
                await originalPayment.Locator("[data-correct-payment-panel]").CountAsync());
            var ledgerGeometry = await clientProfile.EvaluateAsync<string>(
                """
                profile => {
                    const ledger = profile.querySelector('.profile-activity-ledger');
                    const tablist = profile.querySelector('[data-profile-history-tabs]');
                    const panel = profile.querySelector('[data-profile-history-panel="payments"]');
                    const row = profile.querySelector('.recent-payment-row');
                    const ledgerStyle = getComputedStyle(ledger);
                    const rowStyle = getComputedStyle(row);
                    const tabBox = tablist.getBoundingClientRect();
                    const panelBox = panel.getBoundingClientRect();
                    return JSON.stringify({
                        ledgerColumns: ledgerStyle.gridTemplateColumns,
                        ledgerGap: ledgerStyle.gap,
                        aligned: Math.abs(tabBox.left - panelBox.left) < 1 && Math.abs(tabBox.width - panelBox.width) < 1,
                        rowRadius: rowStyle.borderTopLeftRadius,
                        rowBorder: rowStyle.borderTopWidth,
                        rowShadow: rowStyle.boxShadow,
                    });
                }
                """);
            Assert.Contains("\"rowBorder\":\"1px\"", ledgerGeometry, StringComparison.Ordinal);
            Assert.Contains("\"rowShadow\":\"none\"", ledgerGeometry, StringComparison.Ordinal);
            Assert.DoesNotContain("\"rowRadius\":\"0px\"", ledgerGeometry, StringComparison.Ordinal);
            Assert.DoesNotContain("\"ledgerGap\":\"0px\"", ledgerGeometry, StringComparison.Ordinal);
            Assert.Contains("\"aligned\":true", ledgerGeometry, StringComparison.Ordinal);
            Assert.Equal(1, await recentPayments.Locator("[data-profile-history-disclosure][open]").CountAsync());
            await AssertFitsViewportAsync(page, viewportName, "Payment history profile");
            await CaptureVisualAsync(page, viewportName, "payment-history");

            await page.EvaluateAsync("window.scrollTo(0, document.documentElement.scrollHeight)");
            Assert.True(
                await page.EvaluateAsync<double>("window.scrollY") > 0,
                $"The {viewportName} profile fixture should be scrollable before testing header Search recovery.");
            await SubmitHtmxSearchAsync(page, "Kovalenko");

            var searchedWorkspaceBounds = await canvas.BoundingBoxAsync();
            var searchedHeaderBounds = await page.Locator(".app-global-header").BoundingBoxAsync();
            Assert.NotNull(searchedWorkspaceBounds);
            Assert.NotNull(searchedHeaderBounds);
            Assert.True(
                searchedWorkspaceBounds.Y >= searchedHeaderBounds.Y + searchedHeaderBounds.Height - 1,
                $"Header Search results should return below the sticky header on {viewportName}.");
            Assert.True(
                searchedWorkspaceBounds.Y < height,
                $"Header Search results should return inside the {viewportName} viewport.");

            await ExpectVisibleAsync(
                searchResults.Locator(".search-result-row").First,
                viewportName,
                "compact semantic search result row");

            Assert.Equal(0, await clientProfile.CountAsync());
            Assert.Equal(3, await searchResults.Locator(".client-result-row").CountAsync());
            await ExpectVisibleAsync(searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Marta", Exact = true }), viewportName, "Marta result");
            await ExpectVisibleAsync(searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Olena", Exact = true }), viewportName, "Olena result");
            await ExpectVisibleAsync(searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Taras", Exact = true }), viewportName, "Taras result");
            await AssertMinimumTouchTargetsAsync(
                searchResults.Locator(".client-result-row"),
                viewportName,
                "client result row");
            var resultTopology = await searchResults.EvaluateAsync<string>("""
                region => {
                    const style = getComputedStyle(region);
                    const workspace = getComputedStyle(document.querySelector('#reception-workspace'));
                    const row = region.querySelector('.search-result-row');
                    const rowStyle = getComputedStyle(row);
                    const rail = getComputedStyle(row, '::before');
                    return JSON.stringify({
                        workspaceBorder: workspace.borderTopWidth,
                        workspaceBackground: workspace.backgroundColor,
                        regionBorder: style.borderTopWidth,
                        regionBackground: style.backgroundColor,
                        rowBorderStart: rowStyle.borderInlineStartWidth,
                        rowRadius: rowStyle.borderTopLeftRadius,
                        rowShadow: rowStyle.boxShadow,
                        rowPseudoContent: rail.content,
                    });
                }
                """);
            Assert.Contains("\"workspaceBorder\":\"0px\"", resultTopology, StringComparison.Ordinal);
            Assert.Contains("\"workspaceBackground\":\"rgba(0, 0, 0, 0)\"", resultTopology, StringComparison.Ordinal);
            Assert.Contains("\"regionBorder\":\"0px\"", resultTopology, StringComparison.Ordinal);
            Assert.Contains("\"regionBackground\":\"rgba(0, 0, 0, 0)\"", resultTopology, StringComparison.Ordinal);
            Assert.Contains("\"rowBorderStart\":\"4px\"", resultTopology, StringComparison.Ordinal);
            Assert.DoesNotContain("\"rowRadius\":\"0px\"", resultTopology, StringComparison.Ordinal);
            Assert.DoesNotContain("\"rowShadow\":\"none\"", resultTopology, StringComparison.Ordinal);
            Assert.Contains("\"rowPseudoContent\":\"none\"", resultTopology, StringComparison.Ordinal);
            await AssertFitsViewportAsync(page, viewportName, "multiple results");
            await CaptureViewportVisualAsync(page, viewportName, "multiple-results");

            await ClickHtmxProfileAsync(
                page,
                searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Marta", Exact = true }));

            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "Kovalenko Marta" }), viewportName, "selected profile");
            Assert.Contains("clientId=", page.Url, StringComparison.Ordinal);
            Assert.DoesNotContain("handler=Profile", page.Url, StringComparison.OrdinalIgnoreCase);
            var selectedWorkspaceBounds = await canvas.BoundingBoxAsync();
            var selectedHeaderBounds = await page.Locator(".app-global-header").BoundingBoxAsync();
            Assert.NotNull(selectedWorkspaceBounds);
            Assert.NotNull(selectedHeaderBounds);
            Assert.True(
                selectedWorkspaceBounds.Y >= selectedHeaderBounds.Y + selectedHeaderBounds.Height - 1,
                $"Selected profile should remain below the sticky header on {viewportName}.");

            await SubmitHtmxSearchAsync(page, "Kovalenko");
            await ClickHtmxProfileAsync(
                page,
                searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Taras", Exact = true }));

            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "Kovalenko Taras" }), viewportName, "no-card profile");
            await ExpectVisibleAsync(
                clientProfile.GetByLabel("Profile warnings").GetByText("No current card", new() { Exact = true }),
                viewportName,
                "no-card warning");

            await SubmitHtmxSearchAsync(page, "NO-SUCH-CLIENT");

            await ExpectVisibleAsync(canvas.GetByText("No clients found", new() { Exact = true }), viewportName, "no-match state");
            Assert.Equal(0, await clientProfile.CountAsync());
            var noMatchCreatePanel = page.Locator("#create-client-action-panel");
            Assert.True(
                await noMatchCreatePanel.EvaluateAsync<bool>("element => element.open"),
                "A successful no-match search should open the direct create-client action.");
            if (viewportName == "tablet")
            {
                var noMatchHeadingBounds = await page.Locator("#reception-title").BoundingBoxAsync();
                var noMatchHeaderBounds = await page.Locator(".app-global-header").BoundingBoxAsync();
                Assert.NotNull(noMatchHeadingBounds);
                Assert.NotNull(noMatchHeaderBounds);
                Assert.True(
                    noMatchHeadingBounds.Y >= noMatchHeaderBounds.Y + noMatchHeaderBounds.Height - 1,
                    "The Clients heading should remain below the sticky tablet header after a no-result swap.");
            }
            await CaptureViewportVisualAsync(page, viewportName, "no-results-create");

            await noMatchCreatePanel.GetByLabel("Surname", new() { Exact = true }).FillAsync("No result");
            await noMatchCreatePanel.GetByLabel("Name", new() { Exact = true }).FillAsync("Context");
            await noMatchCreatePanel.GetByLabel("Card number", new() { Exact = true }).FillAsync("BL-CREATE-TAKEN");
            await noMatchCreatePanel.GetByRole(AriaRole.Button, new() { Name = "Create client", Exact = true }).ClickAsync();
            await ExpectVisibleAsync(
                page.Locator("#create-client-action-panel").GetByText("Client not created", new() { Exact = true }),
                viewportName,
                "no-match create validation error");
            await ExpectVisibleAsync(
                page.Locator("#create-client-action-panel").GetByText("No clients found", new() { Exact = true }),
                viewportName,
                "no-match context after create validation swap");

            await page.Locator(".clients-search-controls > summary").ClickAsync();
            await page.Locator("input[name='mode'][value='Card']").CheckAsync();
            await SubmitHtmxSearchAsync(page, "BL-CARD-PREFILL-NO-MATCH");
            var cardNoMatchCreatePanel = page.Locator("#create-client-action-panel");
            Assert.True(await cardNoMatchCreatePanel.EvaluateAsync<bool>("element => element.open"));
            Assert.Equal(
                "BL-CARD-PREFILL-NO-MATCH",
                await cardNoMatchCreatePanel.GetByLabel("Card number", new() { Exact = true }).InputValueAsync());
            await AssertFitsViewportAsync(page, viewportName, "no-match state");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("tablet", 1024, 768)]
    [InlineData("phone", 390, 844)]
    public async Task ReceptionSearchFailureKeepsTheCanonicalSearchAvailable(
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
            var invalidQuery = new string('X', 201);
            var target = new Uri(
                _app.BaseAddress,
                $"/Reception/Index?q={Uri.EscapeDataString(invalidQuery)}");
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(page, _app.LoginName, _app.Password, $"{viewportName} failed search");
            await page.GotoAsync(target.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });

            var receptionSearch = page.Locator("#reception-search");
            var failure = page.Locator("#reception-workspace .search-state-failure");
            await ExpectVisibleAsync(failure, viewportName, "search failure state");
            await ExpectVisibleAsync(
                failure.GetByText("Search unavailable", new() { Exact = true }),
                viewportName,
                "honest search failure heading");
            await ExpectVisibleAsync(
                receptionSearch.GetByRole(AriaRole.Searchbox, new() { Name = "Client search", Exact = true }),
                viewportName,
                "search remains available after failure");
            await AssertFitsViewportAsync(page, viewportName, "search failure");
            await CaptureViewportVisualAsync(page, viewportName, "search-failure");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("tablet", 1024, 768)]
    [InlineData("phone", 390, 844)]
    public async Task MembershipExtensionHistoryRendersCanonicalSourcesOnTargetViewport(
        string viewportName,
        int width,
        int height)
    {
        Assert.NotNull(_browser);
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
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(
                page,
                _app.LoginName,
                _app.Password,
                $"{viewportName} extension history smoke");
            await SubmitHtmxSearchAsync(page, "BL-EXTENSION-HISTORY");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await ExpectVisibleAsync(
                profile.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Extension History", Exact = true }),
                viewportName,
                "extension-history client profile");
            var history = profile.Locator("[data-membership-extension-history]");
            await ExpectVisibleAsync(history, viewportName, "Membership extension history");
            await OpenMembershipHistoryAsync(profile);
            await ExpectVisibleAsync(
                history.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Extension history", Exact = true }),
                viewportName,
                "extension-history heading");

            Assert.Equal(
                1,
                await history.Locator("[data-membership-extension-group]").CountAsync());
            var sourceRows = history.Locator(".membership-extension-row");
            Assert.Equal(4, await sourceRows.CountAsync());
            Assert.Equal(
                2,
                await history.Locator("[data-extension-source-kind='freeze']").CountAsync());
            Assert.Equal(
                2,
                await history.Locator("[data-extension-source-kind='non-working-day']").CountAsync());
            Assert.Equal(
                2,
                await history.Locator("[data-extension-source-status='active']").CountAsync());
            Assert.Equal(
                1,
                await history.Locator("[data-extension-source-status='canceled']").CountAsync());
            Assert.Equal(
                1,
                await history.Locator("[data-extension-source-status='corrected']").CountAsync());

            var activeFreezeMetadata = history
                .Locator("[data-extension-source-kind='freeze'][data-extension-source-status='active']")
                .Locator(".membership-extension-meta");
            await ExpectVisibleAsync(
                activeFreezeMetadata.GetByText("Medical recovery", new() { Exact = true }),
                viewportName,
                "active Freeze reason");
            await ExpectVisibleAsync(
                history.GetByText("Travel plan", new() { Exact = true }),
                viewportName,
                "canceled Freeze reason");
            await ExpectVisibleAsync(
                history.GetByText(
                    "maintenance - Ventilation service",
                    new() { Exact = true }),
                viewportName,
                "active NonWorkingDay reason");
            await ExpectVisibleAsync(
                history.GetByText(
                    "repair - Floor inspection",
                    new() { Exact = true }),
                viewportName,
                "corrected NonWorkingDay reason");

            var renderedRanges = await history
                .Locator("[data-extension-inclusive-range]")
                .AllTextContentsAsync();
            Assert.Equal(4, renderedRanges.Count);
            Assert.All(
                renderedRanges,
                value => Assert.Matches(
                    @"^\d{1,2}/\d{1,2}/\d{4} to \d{1,2}/\d{1,2}/\d{4}$",
                    value.Trim()));
            await AssertFitsViewportAsync(page, viewportName, "Membership extension history");
            await CaptureVisualAsync(page, viewportName, "membership-extension-history");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task CreateClientRequiresCardAndDuplicateReviewBeforeCanonicalTabletReread()
    {
        Assert.NotNull(_browser);
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize
            {
                Width = 1024,
                Height = 768,
            },
        });

        try
        {
            var initialClientCount = await _app.CountClientsAsync();
            var page = await context.NewPageAsync();
            await DelayCreateClientRequestsAsync(page);
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(page, _app.LoginName, _app.Password, "tablet create smoke");
            await SubmitHtmxSearchAsync(page, "RETAINED-NONMATCHING-CREATE-CONTEXT");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            var createPanel = page.Locator("#create-client-action-panel");
            await ExpectVisibleAsync(
                createPanel.Locator("summary"),
                "tablet",
                "create-client action");
            Assert.NotNull(await createPanel.GetAttributeAsync("open"));
            await createPanel.GetByLabel("Surname", new() { Exact = true })
                .FillAsync("CreateDuplicate");
            await createPanel.GetByLabel("Name", new() { Exact = true })
                .FillAsync("Tablet");
            await createPanel.GetByLabel("Phone", new() { Exact = true })
                .FillAsync("+380 67 777 88 93");
            await createPanel.GetByLabel("Card number", new() { Exact = true })
                .FillAsync("BL-CREATE-TAKEN");
            await createPanel.GetByLabel("Reception note", new() { Exact = true })
                .FillAsync("Created from the tablet workflow.");

            await SubmitHtmxCreateClientAsync(page);

            await ExpectVisibleAsync(
                createPanel.GetByText(
                    "This card is already assigned to another current client.",
                    new() { Exact = true }),
                "tablet",
                "occupied create-card error");
            Assert.Equal(
                "CreateDuplicate",
                await createPanel.GetByLabel("Surname", new() { Exact = true }).InputValueAsync());
            Assert.True(
                await createPanel.EvaluateAsync<bool>("element => element.open"),
                "A failed create-client submit should keep its form open for correction.");
            Assert.Equal(initialClientCount, await _app.CountClientsAsync());
            await createPanel.GetByLabel("Card number", new() { Exact = true })
                .FillAsync("BL-CREATE-TABLET");

            await SubmitHtmxCreateClientAsync(page);

            await ExpectVisibleAsync(
                createPanel.GetByRole(AriaRole.Alert),
                "tablet",
                "create duplicate acknowledgement error");
            await ExpectVisibleAsync(
                createPanel.GetByRole(AriaRole.Heading, new() { Name = "Duplicate review" }),
                "tablet",
                "create duplicate review heading");
            Assert.True(
                await createPanel.EvaluateAsync<bool>("element => element.open"),
                "Duplicate review should keep the create-client form open.");
            Assert.Equal(2, await createPanel.Locator(".duplicate-warning-item").CountAsync());
            Assert.Equal(initialClientCount, await _app.CountClientsAsync());
            await AssertFitsViewportAsync(page, "tablet", "create duplicate review form");
            await CaptureVisualAsync(page, "tablet", "create-client-duplicate-review");

            var acknowledgementControls = createPanel.GetByRole(
                AriaRole.Checkbox,
                new() { Name = "I reviewed this match and confirm that a new client is required." });
            var reasonInputs = createPanel.GetByLabel(
                "Acknowledgement reason",
                new() { Exact = true });
            Assert.Equal(2, await acknowledgementControls.CountAsync());
            Assert.Equal(2, await reasonInputs.CountAsync());

            for (var index = 0; index < 2; index++)
            {
                await acknowledgementControls.Nth(index).CheckAsync();
                await reasonInputs.Nth(index).FillAsync($"Verified new client {index + 1}");
            }

            await SubmitHtmxCreateClientAsync(page);

            await ExpectVisibleAsync(
                profile.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "CreateDuplicate Tablet" }),
                "tablet",
                "canonical created profile");
            await ExpectVisibleAsync(
                profile.GetByText("Client created."),
                "tablet",
                "create success message");
            Assert.Equal(
                "RETAINED-NONMATCHING-CREATE-CONTEXT",
                await page.Locator("#client-search").InputValueAsync());
            Assert.Equal(0, await page.Locator("#create-client-action-panel").CountAsync());
            await ExpectVisibleAsync(page.Locator(".global-create-client"), "tablet", "persistent header create action");
            Assert.Contains("clientId=", page.Url, StringComparison.Ordinal);
            var clientId = await _app.FindClientIdByCurrentCardAsync("BL-CREATE-TABLET");
            Assert.NotNull(clientId);
            Assert.Equal(initialClientCount + 1, await _app.CountClientsAsync());
            Assert.Equal(1L, await _app.CountClientCreateAuditEntriesAsync(clientId.Value));
            Assert.Equal(1L, await _app.CountCreateClientIdempotencyKeysAsync(clientId.Value));
            Assert.Equal(2L, await _app.CountDuplicateAcknowledgementsAsync(clientId.Value));
            Assert.Equal(1L, await _app.CountCardAssignmentsAsync(clientId.Value));
            await AssertFitsViewportAsync(page, "tablet", "created client profile");
            await CaptureVisualAsync(page, "tablet", "create-client-success");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task CreateClientWithoutCardRereadsCanonicalPhoneProfile()
    {
        Assert.NotNull(_browser);
        const string phone = "+380 67 900 90 95";
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize
            {
                Width = 390,
                Height = 844,
            },
        });

        try
        {
            var initialClientCount = await _app.CountClientsAsync();
            var page = await context.NewPageAsync();
            await DelayCreateClientRequestsAsync(page);
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(page, _app.LoginName, _app.Password, "phone create smoke");
            await SubmitHtmxSearchAsync(page, "Brandnew Phone");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            var createPanel = page.Locator("#create-client-action-panel");
            await ExpectVisibleAsync(
                createPanel.Locator("summary"),
                "phone",
                "phone create-client action");
            await createPanel.GetByLabel("Surname", new() { Exact = true }).FillAsync("Brandnew");
            await createPanel.GetByLabel("Name", new() { Exact = true }).FillAsync("Phone");
            await createPanel.GetByLabel("Phone", new() { Exact = true }).FillAsync(phone);
            await createPanel.GetByLabel("Reception note", new() { Exact = true })
                .FillAsync("Created without a card.");
            Assert.Equal(
                string.Empty,
                await createPanel.GetByLabel("Card number", new() { Exact = true }).InputValueAsync());
            await AssertFitsViewportAsync(page, "phone", "create client form");
            await CaptureVisualAsync(page, "phone", "create-client-form");

            await SubmitHtmxCreateClientAsync(page);

            await ExpectVisibleAsync(
                profile.GetByRole(AriaRole.Heading, new() { Name = "Brandnew Phone" }),
                "phone",
                "canonical cardless profile");
            await ExpectVisibleAsync(
                profile.GetByLabel("Profile warnings")
                    .GetByText("No current card", new() { Exact = true }),
                "phone",
                "created no-card warning");
            Assert.Equal(0, await page.GetByRole(AriaRole.Region, new() { Name = "Search results" }).CountAsync());
            var clientId = await _app.FindClientIdByPhoneAsync(phone);
            Assert.NotNull(clientId);
            Assert.Equal(initialClientCount + 1, await _app.CountClientsAsync());
            Assert.Equal(1L, await _app.CountClientCreateAuditEntriesAsync(clientId.Value));
            Assert.Equal(1L, await _app.CountCreateClientIdempotencyKeysAsync(clientId.Value));
            Assert.Equal(0L, await _app.CountDuplicateAcknowledgementsAsync(clientId.Value));
            Assert.Equal(0L, await _app.CountCardAssignmentsAsync(clientId.Value));
            await AssertFitsViewportAsync(page, "phone", "created cardless profile");
            await CaptureVisualAsync(page, "phone", "create-client-success");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("tablet", 1024, 768, "BL-EDIT-TABLET")]
    [InlineData("phone", 390, 844, "BL-EDIT-PHONE")]
    public async Task UpdateClientRequiresExactDuplicateAcknowledgementsAndRereadsWorkspace(
        string viewportName,
        int width,
        int height,
        string cardNumber)
    {
        Assert.NotNull(_browser);
        var clientId = viewportName == "tablet"
            ? _app.TabletEditableClientId
            : _app.PhoneEditableClientId;
        var duplicateName = viewportName == "tablet"
            ? "TabletMatch"
            : "PhoneMatch";
        var duplicatePhone = viewportName == "tablet"
            ? "+380 67 777 88 91"
            : "+380 67 777 88 92";
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
            await DelayUpdateClientRequestsAsync(page);
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(
                page,
                _app.LoginName,
                _app.Password,
                $"{viewportName} update smoke");
            await SubmitHtmxSearchAsync(page, cardNumber);

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await OpenClientDetailsAsync(profile);
            var actionPanel = profile.Locator("#profile-action-panel");
            await ExpectVisibleAsync(actionPanel.Locator("summary"), viewportName, "edit-client action");
            await actionPanel.Locator("summary").ClickAsync();
            await actionPanel.GetByLabel("Surname", new() { Exact = true }).FillAsync("Duplicate");
            await actionPanel.GetByLabel("Name", new() { Exact = true }).FillAsync(duplicateName);
            await actionPanel.GetByLabel("Phone", new() { Exact = true }).FillAsync(duplicatePhone);
            await actionPanel.GetByLabel("Reception note", new() { Exact = true })
                .FillAsync($"Updated from {viewportName}.");

            await SubmitHtmxUpdateAsync(page);

            await ExpectVisibleAsync(
                actionPanel.GetByRole(AriaRole.Alert),
                viewportName,
                "duplicate acknowledgement error");
            await ExpectVisibleAsync(
                actionPanel.GetByRole(AriaRole.Heading, new() { Name = "Duplicate review" }),
                viewportName,
                "duplicate review heading");
            Assert.Equal(2, await actionPanel.Locator(".duplicate-warning-item").CountAsync());
            Assert.Equal(0L, await _app.CountClientUpdateAuditEntriesAsync(clientId));
            Assert.Equal(0L, await _app.CountUpdateClientIdempotencyKeysAsync(clientId));
            Assert.Equal(0L, await _app.CountDuplicateAcknowledgementsAsync(clientId));
            await AssertFitsViewportAsync(page, viewportName, "duplicate review form");
            await CaptureVisualAsync(page, viewportName, "update-client-duplicate-review");

            var acknowledgementControls = actionPanel.GetByRole(
                AriaRole.Checkbox,
                new() { Name = "I verified that this is the correct client update." });
            var reasonInputs = actionPanel.GetByLabel("Acknowledgement reason", new() { Exact = true });
            Assert.Equal(2, await acknowledgementControls.CountAsync());
            Assert.Equal(2, await reasonInputs.CountAsync());

            for (var index = 0; index < 2; index++)
            {
                await acknowledgementControls.Nth(index).CheckAsync();
                await reasonInputs.Nth(index).FillAsync($"Verified at reception {index + 1}");
            }

            await SubmitHtmxUpdateAsync(page);

            await ExpectVisibleAsync(
                profile.GetByRole(AriaRole.Heading, new() { Name = $"Duplicate {duplicateName}" }),
                viewportName,
                "canonical updated profile");
            await ExpectVisibleAsync(
                profile.GetByText("Client updated."),
                viewportName,
                "update success message");
            Assert.Equal(0, await page.GetByRole(AriaRole.Region, new() { Name = "Search results" }).CountAsync());
            Assert.Equal(1L, await _app.CountClientUpdateAuditEntriesAsync(clientId));
            Assert.Equal(1L, await _app.CountUpdateClientIdempotencyKeysAsync(clientId));
            Assert.Equal(2L, await _app.CountDuplicateAcknowledgementsAsync(clientId));
            await AssertFitsViewportAsync(page, viewportName, "updated client profile");
            await CaptureVisualAsync(page, viewportName, "update-client-success");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task ChangeAndClearCardRereadsProfileAndExactSearchOnTablet()
    {
        Assert.NotNull(_browser);
        var clientId = _app.CardChangeClientId;
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize
            {
                Width = 1024,
                Height = 768,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            await DelayCardAssignmentRequestsAsync(page);
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(page, _app.LoginName, _app.Password, "tablet card smoke");
            await SubmitHtmxSearchAsync(page, "BL-CARD-OLD");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await OpenClientDetailsAsync(profile);
            var cardPanel = profile.Locator("#card-action-panel");
            await ExpectVisibleAsync(cardPanel.Locator("summary"), "tablet", "manage-card action");
            await cardPanel.Locator("summary").ClickAsync();
            Assert.NotNull(await cardPanel.GetByLabel("Reason", new() { Exact = true })
                .GetAttributeAsync("required"));
            await cardPanel.GetByLabel("New card number", new() { Exact = true })
                .FillAsync("BL-CARD-TAKEN");
            await cardPanel.GetByLabel("Reason", new() { Exact = true })
                .FillAsync("Occupied-card validation");

            await SubmitHtmxCardAssignmentAsync(page);

            await ExpectVisibleAsync(
                cardPanel.GetByText(
                    "This card is already assigned to another current client.",
                    new() { Exact = true }),
                "tablet",
                "occupied-card error");
            Assert.Equal("BL-CARD-OLD", await _app.ReadCurrentCardNumberAsync(clientId));
            Assert.Equal(1L, await _app.CountCardAssignmentsAsync(clientId));
            Assert.Equal(0L, await _app.CountCardAuditEntriesAsync(clientId, "card.changed"));
            Assert.Equal(0L, await _app.CountCardCommandIdempotencyKeysAsync(clientId));
            await AssertFitsViewportAsync(page, "tablet", "occupied-card error");
            await CaptureVisualAsync(page, "tablet", "card-occupied-error");

            await cardPanel.GetByLabel("New card number", new() { Exact = true })
                .FillAsync("BL-CARD-NEW");
            await cardPanel.GetByLabel("Reason", new() { Exact = true })
                .FillAsync("Replace worn card");
            await SubmitHtmxCardAssignmentAsync(page);

            await ExpectVisibleAsync(
                profile.GetByText("Card changed."),
                "tablet",
                "card-change success message");
            Assert.Equal(
                "BL-CARD-NEW",
                await profile.Locator(".client-profile-meta dd").First.TextContentAsync());
            Assert.Equal(0, await page.GetByRole(AriaRole.Region, new() { Name = "Search results" }).CountAsync());
            Assert.Equal("BL-CARD-NEW", await _app.ReadCurrentCardNumberAsync(clientId));
            Assert.Equal(2L, await _app.CountCardAssignmentsAsync(clientId));
            Assert.Equal(1L, await _app.CountCardAuditEntriesAsync(clientId, "card.changed"));
            Assert.Equal(1L, await _app.CountCardCommandIdempotencyKeysAsync(clientId));
            await AssertFitsViewportAsync(page, "tablet", "changed-card profile");
            await CaptureVisualAsync(page, "tablet", "card-change-success");

            await SubmitHtmxSearchAsync(page, "BL-CARD-NEW");
            await ExpectVisibleAsync(
                profile.GetByRole(AriaRole.Heading, new() { Name = "Card Change" }),
                "tablet",
                "new exact-card profile");
            await OpenClientDetailsAsync(profile);
            await cardPanel.Locator("summary").ClickAsync();
            await cardPanel.GetByLabel("Reason", new() { Exact = true })
                .FillAsync("Card returned");
            await cardPanel.GetByRole(AriaRole.Checkbox, new() { Name = "Clear current card" })
                .CheckAsync();
            Assert.True(await cardPanel.GetByLabel("New card number", new() { Exact = true })
                .IsDisabledAsync());

            await SubmitHtmxCardAssignmentAsync(page);

            await ExpectVisibleAsync(
                profile.GetByText("Card cleared."),
                "tablet",
                "card-clear success message");
            Assert.Equal(
                "No current card",
                await profile.Locator(".client-profile-meta dd").First.TextContentAsync());
            await ExpectVisibleAsync(
                profile.GetByLabel("Profile warnings")
                    .GetByText("No current card", new() { Exact = true }),
                "tablet",
                "no-current-card warning");
            Assert.Equal(0, await page.GetByRole(AriaRole.Region, new() { Name = "Search results" }).CountAsync());
            Assert.Null(await _app.ReadCurrentCardNumberAsync(clientId));
            Assert.Equal(2L, await _app.CountCardAssignmentsAsync(clientId));
            Assert.Equal(1L, await _app.CountCardAuditEntriesAsync(clientId, "card.cleared"));
            Assert.Equal(2L, await _app.CountCardCommandIdempotencyKeysAsync(clientId));
            await AssertFitsViewportAsync(page, "tablet", "cleared-card profile");
            await CaptureVisualAsync(page, "tablet", "card-clear-success");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task AssignFirstCardWithoutReasonWorksOnPhone()
    {
        Assert.NotNull(_browser);
        var clientId = _app.CardAssignClientId;
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize
            {
                Width = 390,
                Height = 844,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            await DelayCardAssignmentRequestsAsync(page);
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(page, _app.LoginName, _app.Password, "phone card smoke");
            await SubmitHtmxSearchAsync(page, "Cardless Phone");

            var results = page.GetByRole(AriaRole.Region, new() { Name = "Search results" });
            await ClickHtmxProfileAsync(
                page,
                results.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Open Cardless Phone", Exact = true }));

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await OpenClientDetailsAsync(profile);
            var cardPanel = profile.Locator("#card-action-panel");
            await cardPanel.Locator("summary").ClickAsync();
            Assert.Equal(0, await cardPanel.GetByLabel("Reason", new() { Exact = true }).CountAsync());
            Assert.Equal(0, await cardPanel.GetByRole(
                AriaRole.Checkbox,
                new() { Name = "Clear current card" }).CountAsync());
            Assert.NotNull(await cardPanel.GetByLabel("New card number", new() { Exact = true })
                .GetAttributeAsync("required"));
            await cardPanel.GetByLabel("New card number", new() { Exact = true })
                .FillAsync("BL-CARD-PHONE");
            await AssertFitsViewportAsync(page, "phone", "first-card form");
            await CaptureVisualAsync(page, "phone", "card-assign-form");

            await SubmitHtmxCardAssignmentAsync(page);

            await ExpectVisibleAsync(
                profile.GetByText("Card assigned."),
                "phone",
                "first-card success message");
            Assert.Equal(
                "BL-CARD-PHONE",
                await profile.Locator(".client-profile-meta dd").First.TextContentAsync());
            Assert.Equal(0, await page.GetByRole(AriaRole.Region, new() { Name = "Search results" }).CountAsync());
            Assert.Equal("BL-CARD-PHONE", await _app.ReadCurrentCardNumberAsync(clientId));
            Assert.Equal(1L, await _app.CountCardAssignmentsAsync(clientId));
            Assert.Equal(1L, await _app.CountCardAuditEntriesAsync(clientId, "card.assigned"));
            Assert.Equal(1L, await _app.CountCardCommandIdempotencyKeysAsync(clientId));

            await SubmitHtmxSearchAsync(page, "BL-CARD-PHONE");
            await ExpectVisibleAsync(
                profile.GetByRole(AriaRole.Heading, new() { Name = "Cardless Phone" }),
                "phone",
                "assigned exact-card profile");
            await AssertFitsViewportAsync(page, "phone", "first-card profile");
            await CaptureVisualAsync(page, "phone", "card-assign-success");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task StaleCardAssignmentRefreshesCanonicalFormBeforeRetry()
    {
        Assert.NotNull(_browser);
        var clientId = _app.CardStaleClientId;
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize
            {
                Width = 1024,
                Height = 768,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            await DelayCardAssignmentRequestsAsync(page);
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(page, _app.LoginName, _app.Password, "stale card smoke");
            await SubmitHtmxSearchAsync(page, "BL-CARD-STALE");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await OpenClientDetailsAsync(profile);
            var cardPanel = profile.Locator("#card-action-panel");
            await cardPanel.Locator("summary").ClickAsync();
            await cardPanel.GetByLabel("New card number", new() { Exact = true })
                .FillAsync("BL-CARD-AFTER-STALE");
            await cardPanel.GetByLabel("Reason", new() { Exact = true })
                .FillAsync("Attempted stale replacement");
            await _app.ReplaceCurrentCardForStaleTestAsync(clientId, "BL-CARD-CANONICAL");

            await SubmitHtmxCardAssignmentAsync(page);

            await ExpectVisibleAsync(
                cardPanel.GetByText(
                    "Data changed while you were submitting. Canonical Reception data was refreshed; review and try again.",
                    new() { Exact = true }),
                "tablet",
                "stale-card error");
            Assert.NotNull(await cardPanel.GetAttributeAsync("open"));
            Assert.Equal(
                "BL-CARD-CANONICAL",
                await cardPanel.Locator(".card-current-state dd").TextContentAsync());
            Assert.Equal(
                string.Empty,
                await cardPanel.GetByLabel("New card number", new() { Exact = true }).InputValueAsync());
            Assert.Equal(
                string.Empty,
                await cardPanel.GetByLabel("Reason", new() { Exact = true }).InputValueAsync());
            Assert.Equal(0L, await _app.CountCardAuditEntriesAsync(clientId, "card.changed"));
            Assert.Equal(0L, await _app.CountCardCommandIdempotencyKeysAsync(clientId));
            Assert.Equal(2L, await _app.CountCardAssignmentsAsync(clientId));

            await cardPanel.GetByLabel("New card number", new() { Exact = true })
                .FillAsync("BL-CARD-AFTER-STALE");
            await cardPanel.GetByLabel("Reason", new() { Exact = true })
                .FillAsync("Saved after canonical refresh");
            await SubmitHtmxCardAssignmentAsync(page);

            await ExpectVisibleAsync(
                profile.GetByText("Card changed."),
                "tablet",
                "stale-card retry success");
            Assert.Equal(
                "BL-CARD-AFTER-STALE",
                await profile.Locator(".client-profile-meta dd").First.TextContentAsync());
            Assert.Equal("BL-CARD-AFTER-STALE", await _app.ReadCurrentCardNumberAsync(clientId));
            Assert.Equal(3L, await _app.CountCardAssignmentsAsync(clientId));
            Assert.Equal(1L, await _app.CountCardAuditEntriesAsync(clientId, "card.changed"));
            Assert.Equal(1L, await _app.CountCardCommandIdempotencyKeysAsync(clientId));
            await AssertFitsViewportAsync(page, "tablet", "stale-card retry");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task StaleUpdateRefreshesCanonicalFormBeforeRetry()
    {
        Assert.NotNull(_browser);
        var clientId = _app.StaleEditableClientId;
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
            ViewportSize = new ViewportSize
            {
                Width = 1024,
                Height = 768,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            await DelayUpdateClientRequestsAsync(page);
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(page, _app.LoginName, _app.Password, "stale update smoke");
            await SubmitHtmxSearchAsync(page, "BL-EDIT-STALE");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await OpenClientDetailsAsync(profile);
            var actionPanel = profile.Locator("#profile-action-panel");
            await actionPanel.Locator("summary").ClickAsync();
            await _app.AdvanceClientUpdatedAtAsync(clientId);
            await actionPanel.GetByLabel("Reception note", new() { Exact = true })
                .FillAsync("Attempted stale edit.");

            await SubmitHtmxUpdateAsync(page);

            await ExpectVisibleAsync(
                actionPanel.GetByText(
                    "Data changed while you were submitting. Canonical Reception data was refreshed; review and try again.",
                    new() { Exact = true }),
                "tablet",
                "stale-state error");
            Assert.NotNull(await actionPanel.GetAttributeAsync("open"));
            Assert.Equal(
                "Stale source.",
                await actionPanel.GetByLabel("Reception note", new() { Exact = true }).InputValueAsync());
            Assert.Equal(0L, await _app.CountClientUpdateAuditEntriesAsync(clientId));
            Assert.Equal(0L, await _app.CountUpdateClientIdempotencyKeysAsync(clientId));

            await actionPanel.GetByLabel("Reception note", new() { Exact = true })
                .FillAsync("Saved after canonical refresh.");
            await SubmitHtmxUpdateAsync(page);

            await OpenClientDetailsAsync(profile);
            var canonicalNote = profile.Locator(".client-comment");
            await ExpectVisibleAsync(canonicalNote, "tablet", "canonical retry result");
            Assert.Equal("Saved after canonical refresh.", await canonicalNote.TextContentAsync());
            Assert.Equal(1L, await _app.CountClientUpdateAuditEntriesAsync(clientId));
            Assert.Equal(1L, await _app.CountUpdateClientIdempotencyKeysAsync(clientId));
            Assert.Equal(0L, await _app.CountDuplicateAcknowledgementsAsync(clientId));
            await AssertFitsViewportAsync(page, "tablet", "stale-state retry");
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
            Locale = ReceptionAppFixture.WorkflowCulture,
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
            await page.Locator("#reception-search").GetByRole(AriaRole.Searchbox, new() { Name = "Client search", Exact = true }).FillAsync("BL-1001");
            await page.Locator("#reception-search").GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
            await page.WaitForURLAsync("**?q=BL-1001**");

            Assert.Equal("Clients - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(AriaRole.Region, new() { Name = "Client profile" })
                    .GetByRole(AriaRole.Heading, new() { Name = "Kovalenko Olena" }),
                "tablet",
                "full-page exact-card profile");
            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            Assert.True(await profile.Locator("[data-profile-action-switcher]").IsHiddenAsync());
            Assert.True(await profile.Locator("[data-profile-history-tabs]").IsHiddenAsync());
            await ExpectVisibleAsync(
                profile.GetByRole(AriaRole.Region, new() { Name = "Recent visits" }),
                "tablet",
                "no-JavaScript Visit history");
            await ExpectVisibleAsync(
                profile.GetByRole(AriaRole.Region, new() { Name = "Recent payments" }),
                "tablet",
                "no-JavaScript Payment history");
            var markVisitPanel = profile.Locator("#mark-visit-action-panel");
            Assert.True(await markVisitPanel.EvaluateAsync<bool>("element => element.open"));
            await ExpectVisibleAsync(markVisitPanel.Locator("summary"), "tablet", "no-JavaScript Mark Visit summary");
            await ExpectVisibleAsync(markVisitPanel.Locator("form"), "tablet", "no-JavaScript Mark Visit form");
            foreach (var panelId in new[]
                     {
                         "issue-membership-action-panel",
                         "add-payment-action-panel",
                     })
            {
                var panel = profile.Locator($"#{panelId}");
                await ExpectVisibleAsync(panel.Locator("summary"), "tablet", $"no-JavaScript {panelId} summary");
                await panel.Locator("summary").ClickAsync();
                await ExpectVisibleAsync(panel.Locator("form"), "tablet", $"no-JavaScript {panelId} form");
                Assert.Equal("post", await panel.Locator("form").GetAttributeAsync("method"));
                Assert.Contains("handler=", await panel.Locator("form").GetAttributeAsync("action"));
            }
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
            Locale = ReceptionAppFixture.WorkflowCulture,
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
        await page.GotoAsync(new Uri(new Uri(page.Url), "/Reception/Index").ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
    }

    private static async Task SubmitHtmxSearchAsync(IPage page, string query)
    {
        await page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" }).FillAsync(query);
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("handler=Search", StringComparison.OrdinalIgnoreCase));
        await page.Locator("#reception-search").GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
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

    private static async Task AssertRouteHeadingBelowHeaderAsync(IPage page)
    {
        var routeHeadingBounds = await page.Locator(".reception-route-heading")
            .BoundingBoxAsync();
        var stickyHeaderBounds = await page.Locator(".app-global-header")
            .BoundingBoxAsync();
        Assert.NotNull(routeHeadingBounds);
        Assert.NotNull(stickyHeaderBounds);
        Assert.True(
            routeHeadingBounds.Y >= stickyHeaderBounds.Y + stickyHeaderBounds.Height - 1,
            $"Tablet Clients heading should remain below the sticky header after a Profile swap "
            + $"(heading top {routeHeadingBounds.Y:F1}px, header bottom "
            + $"{stickyHeaderBounds.Y + stickyHeaderBounds.Height:F1}px).");
    }

    private static Task AlignRouteHeadingBelowHeaderAsync(IPage page)
    {
        return page.EvaluateAsync(
            """
            () => {
                const heading = document.querySelector('.reception-route-heading');
                const header = document.querySelector('.app-global-header');
                if (!heading || !header) return;
                const documentTop = heading.getBoundingClientRect().top + window.scrollY;
                window.scrollTo(0, Math.max(0, documentTop - header.getBoundingClientRect().height - 16));
            }
            """);
    }

    private static async Task OpenClientDetailsAsync(ILocator profile)
    {
        var details = profile.Locator(".profile-passport-details");
        if (await details.GetAttributeAsync("open") is null)
        {
            await details.Locator(":scope > summary").ClickAsync();
        }
    }

    private static async Task OpenMembershipHistoryAsync(ILocator profile)
    {
        var details = profile.Locator("[data-membership-extension-history]");
        if (await details.GetAttributeAsync("open") is null)
        {
            await details.Locator(":scope > summary").ClickAsync();
        }
    }

    private static async Task SubmitHtmxUpdateAsync(IPage page)
    {
        var actionPanel = page.Locator("#profile-action-panel");
        var form = actionPanel.Locator("form");
        Assert.Equal("this:drop", await form.GetAttributeAsync("hx-sync"));
        Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("handler=UpdateClient", StringComparison.OrdinalIgnoreCase));
        var disabledTask = page.WaitForFunctionAsync(
            "() => document.querySelector('#profile-action-panel button[type=\"submit\"]')?.disabled === true");
        var submitButton = actionPanel.GetByRole(
            AriaRole.Button,
            new() { Name = "Save changes" });
        await submitButton.ClickAsync();
        await disabledTask;
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static Task DelayUpdateClientRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=UpdateClient*",
            async route =>
            {
                await Task.Delay(500);
                await route.ContinueAsync();
            });
    }

    private static async Task SubmitHtmxCreateClientAsync(IPage page)
    {
        var createPanel = page.Locator("#create-client-action-panel");
        var form = createPanel.Locator("form");
        Assert.Equal("this:drop", await form.GetAttributeAsync("hx-sync"));
        Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("handler=CreateClient", StringComparison.OrdinalIgnoreCase));
        var disabledTask = page.WaitForFunctionAsync(
            "() => document.querySelector('#create-client-action-panel button[type=\"submit\"]')?.disabled === true");
        await createPanel.GetByRole(AriaRole.Button, new() { Name = "Create client" })
            .ClickAsync();
        await disabledTask;
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static Task DelayCreateClientRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=CreateClient*",
            async route =>
            {
                await Task.Delay(500);
                await route.ContinueAsync();
            });
    }

    private static async Task SubmitHtmxCardAssignmentAsync(IPage page)
    {
        var cardPanel = page.Locator("#card-action-panel");
        var form = cardPanel.Locator("form");
        Assert.Equal("this:drop", await form.GetAttributeAsync("hx-sync"));
        Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains(
                "handler=AssignOrChangeCard",
                StringComparison.OrdinalIgnoreCase));
        var disabledTask = page.WaitForFunctionAsync(
            "() => document.querySelector('#card-action-panel button[type=\"submit\"]')?.disabled === true");
        await cardPanel.GetByRole(AriaRole.Button, new() { Name = "Save card" }).ClickAsync();
        await disabledTask;
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static Task DelayCardAssignmentRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=AssignOrChangeCard*",
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

    private static async Task AssertMinimumTouchTargetsAsync(
        ILocator locators,
        string viewportName,
        string label)
    {
        var count = await locators.CountAsync();
        Assert.True(count > 0, $"At least one {label} should exist on {viewportName} viewport.");

        for (var index = 0; index < count; index++)
        {
            await AssertMinimumTouchTargetAsync(
                locators.Nth(index),
                viewportName,
                $"{label} {index + 1}");
        }
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

    private static async Task CaptureViewportVisualAsync(
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
        await page.EvaluateAsync("document.activeElement?.blur()");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = false,
            Path = Path.Combine(screenshotDirectory, $"{viewportName}-{state}.png"),
        });
    }
}
