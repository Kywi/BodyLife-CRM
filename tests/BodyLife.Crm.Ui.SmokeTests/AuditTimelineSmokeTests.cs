using System.Globalization;
using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class AuditTimelineSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public AuditTimelineSmokeTests(ReceptionAppFixture app)
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
    public async Task OwnerAndAdminCanInspectFilteredAppendOnlyTimeline(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        Assert.Equal(scenario.PageSize + 2, scenario.TotalEntries);
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
            await LoginAsync(
                page,
                useOwner ? _app.LoginName : _app.AdminLoginName,
                useOwner ? _app.Password : _app.AdminPassword,
                $"{viewportName} audit timeline smoke");

            var auditNavigation = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Audit timeline", Exact = true });
            await AssertMinimumTouchTargetAsync(
                auditNavigation,
                viewportName,
                "audit timeline navigation");
            await auditNavigation.ClickAsync();
            await page.WaitForURLAsync("**/Audit/Timeline**");

            Assert.Equal("Audit timeline - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Audit timeline", Exact = true }),
                viewportName,
                "audit timeline heading");
            await ExpectVisibleAsync(
                page.GetByText(
                    useOwner ? "BodyLife Owner" : "Smoke Named Admin",
                    new() { Exact = true }),
                viewportName,
                "current audit viewer account");

            var clientId = page.GetByLabel("Client ID", new() { Exact = true });
            var entityType = page.GetByLabel("Entity type", new() { Exact = true });
            var recordedFrom = page.GetByLabel(
                "Recorded from (UTC)",
                new() { Exact = true });
            var recordedThrough = page.GetByLabel(
                "Recorded through (UTC)",
                new() { Exact = true });
            var action = page.GetByLabel("Business action", new() { Exact = true });
            var applyFilters = page.GetByRole(
                AriaRole.Button,
                new() { Name = "Apply filters", Exact = true });
            await AssertMinimumTouchTargetAsync(clientId, viewportName, "Client filter");
            await AssertMinimumTouchTargetAsync(entityType, viewportName, "entity filter");
            await AssertMinimumTouchTargetAsync(
                recordedFrom,
                viewportName,
                "recorded-from filter");
            await AssertMinimumTouchTargetAsync(
                recordedThrough,
                viewportName,
                "recorded-through filter");
            await AssertMinimumTouchTargetAsync(action, viewportName, "action filter");
            await AssertMinimumTouchTargetAsync(
                applyFilters,
                viewportName,
                "apply audit filters");

            var recordedDate = scenario.RecordedDate.ToString("yyyy-MM-dd");
            await clientId.FillAsync(scenario.ClientId.ToString());
            await entityType.SelectOptionAsync("Visit");
            await recordedFrom.FillAsync(recordedDate);
            await recordedThrough.FillAsync(recordedDate);
            await action.SelectOptionAsync("visit.marked");
            await applyFilters.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains(
                $"clientId={scenario.ClientId}",
                page.Url,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("entity=Visit", page.Url, StringComparison.Ordinal);
            Assert.Contains("action=visit.marked", page.Url, StringComparison.Ordinal);
            var firstPageRows = page.Locator("[data-audit-entry-list] > .audit-entry");
            Assert.Equal(scenario.PageSize, await firstPageRows.CountAsync());
            var actions = await firstPageRows.EvaluateAllAsync<string[]>(
                "rows => rows.map(row => row.dataset.actionType)");
            Assert.All(actions, value => Assert.Equal("visit.marked", value));
            var entityTypes = await firstPageRows.EvaluateAllAsync<string[]>(
                "rows => rows.map(row => row.dataset.entityType)");
            Assert.All(entityTypes, value => Assert.Equal("Visit", value));

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
                    new() { Name = "Visit marked", Exact = true }),
                viewportName,
                "featured audit action");
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
                    $"{scenario.FeaturedOccurredAt:yyyy-MM-dd HH:mm:ss} UTC",
                    new() { Exact = true }),
                viewportName,
                "fallback occurred time");
            await ExpectVisibleAsync(
                featured.GetByText(
                    $"{scenario.FeaturedRecordedAt:yyyy-MM-dd HH:mm:ss} UTC",
                    new() { Exact = true }),
                viewportName,
                "fallback recorded time");
            await ExpectVisibleAsync(
                featured.GetByText("Recovered from paper register", new() { Exact = true }),
                viewportName,
                "fallback reason");
            await ExpectVisibleAsync(
                featured.GetByText(
                    "Entered after reception connectivity returned",
                    new() { Exact = true }),
                viewportName,
                "fallback comment");

            var envelope = featured.Locator(".audit-envelope-details > summary");
            await AssertMinimumTouchTargetAsync(envelope, viewportName, "audit envelope toggle");
            await envelope.ClickAsync();
            await ExpectVisibleAsync(
                featured.GetByText(scenario.FeaturedCorrelationId, new() { Exact = true }),
                viewportName,
                "request correlation id");
            var envelopeText = await featured.Locator(".audit-json-grid").InnerTextAsync();
            Assert.Contains(scenario.ClientId.ToString(), envelopeText, StringComparison.Ordinal);
            Assert.Contains("remainingVisits", envelopeText, StringComparison.Ordinal);
            await AssertFitsViewportAsync(page, viewportName, "expanded audit envelope");
            await CaptureVisualAsync(page, viewportName, "audit-timeline");

            var next = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Next", Exact = true });
            await AssertMinimumTouchTargetAsync(next, viewportName, "next audit page");
            await next.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            Assert.Contains($"offset={scenario.PageSize}", page.Url, StringComparison.Ordinal);
            Assert.Equal(
                scenario.TotalEntries - scenario.PageSize,
                await page.Locator("[data-audit-entry-list] > .audit-entry").CountAsync());
            Assert.Equal("visit.marked", await action.InputValueAsync());
            Assert.Equal(recordedDate, await recordedFrom.InputValueAsync());
            Assert.Equal(recordedDate, await recordedThrough.InputValueAsync());
            var previous = page.GetByRole(
                AriaRole.Link,
                new() { Name = "Previous", Exact = true });
            await AssertMinimumTouchTargetAsync(previous, viewportName, "previous audit page");
            await AssertFitsViewportAsync(page, viewportName, "audit second page");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task CorrectionEntriesLeadWithOwnerReadableBeforeAndAfterSummaries(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
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
            await LoginAsync(
                page,
                useOwner ? _app.LoginName : _app.AdminLoginName,
                useOwner ? _app.Password : _app.AdminPassword,
                $"{viewportName} audit explanations smoke");

            var visitExplanation = await OpenExplanationAsync(
                page,
                scenario.ClientId,
                "Visit",
                "visit.canceled",
                scenario.Explanations.VisitCancellationAuditEntryId,
                "visit-canceled",
                viewportName);
            await ExpectVisibleAsync(
                visitExplanation.GetByRole(
                    AriaRole.Heading,
                    new()
                    {
                        Name = "Original Visit preserved; cancellation added",
                        Exact = true,
                    }),
                viewportName,
                "Visit cancellation explanation title");
            Assert.Equal(
                scenario.Explanations.BeforeVisitRemaining.ToString(),
                await ExplanationFactAsync(
                    visitExplanation,
                    "Original visit",
                    "Remaining visits"));
            Assert.Equal(
                scenario.Explanations.AfterVisitRemaining.ToString(),
                await ExplanationFactAsync(
                    visitExplanation,
                    "After cancellation",
                    "Remaining visits"));
            Assert.Equal(
                "Preserved",
                await ExplanationFactAsync(
                    visitExplanation,
                    "After cancellation",
                    "Original fact"));
            await ExpectVisibleAsync(
                visitExplanation.GetByText(
                    "Visit status, consumption status, Membership state",
                    new() { Exact = true }),
                viewportName,
                "Visit changed fields");
            await AssertFitsViewportAsync(page, viewportName, "Visit explanation");
            await CaptureVisualAsync(page, viewportName, "visit-cancellation-explanation");

            var correctionExplanation = await OpenExplanationAsync(
                page,
                scenario.ClientId,
                "Payment",
                "payment.corrected",
                scenario.Explanations.PaymentCorrectionAuditEntryId,
                "payment-corrected",
                viewportName);
            Assert.Equal(
                MoneyLabel(scenario.Explanations.OriginalPaymentAmount),
                await ExplanationFactAsync(
                    correctionExplanation,
                    "Original payment",
                    "Amount"));
            Assert.Equal(
                MoneyLabel(scenario.Explanations.ReplacementPaymentAmount),
                await ExplanationFactAsync(
                    correctionExplanation,
                    "Replacement payment",
                    "Amount"));
            Assert.Equal(
                "Replaced",
                await ExplanationFactAsync(
                    correctionExplanation,
                    "Replacement payment",
                    "Original status"));
            await ExpectVisibleAsync(
                correctionExplanation.GetByText(
                    "Amount, Occurred time, Comment",
                    new() { Exact = true }),
                viewportName,
                "Payment correction changed fields");

            var correctionRow = correctionExplanation.Locator("xpath=ancestor::li");
            var envelope = correctionRow.Locator(".audit-envelope-details");
            Assert.Null(await envelope.GetAttributeAsync("open"));
            Assert.False(await envelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = envelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "Payment correction audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                envelope.Locator(".audit-json-grid"),
                viewportName,
                "Payment correction raw envelope");
            Assert.Contains(
                "replacementPayment",
                await envelope.Locator(".audit-json-grid").InnerTextAsync(),
                StringComparison.Ordinal);
            await AssertFitsViewportAsync(page, viewportName, "Payment correction explanation");
            await CaptureVisualAsync(page, viewportName, "payment-correction-explanation");

            var cancellationExplanation = await OpenExplanationAsync(
                page,
                scenario.ClientId,
                "Payment",
                "payment.canceled",
                scenario.Explanations.PaymentCancellationAuditEntryId,
                "payment-canceled",
                viewportName);
            var canceledAmount = MoneyLabel(
                scenario.Explanations.CanceledPaymentAmount);
            Assert.Equal(
                canceledAmount,
                await ExplanationFactAsync(
                    cancellationExplanation,
                    "Original payment",
                    "Amount"));
            Assert.Equal(
                canceledAmount,
                await ExplanationFactAsync(
                    cancellationExplanation,
                    "After cancellation",
                    "Amount"));
            Assert.Equal(
                "Active",
                await ExplanationFactAsync(
                    cancellationExplanation,
                    "Original payment",
                    "Status"));
            Assert.Equal(
                "Canceled",
                await ExplanationFactAsync(
                    cancellationExplanation,
                    "After cancellation",
                    "Status"));
            await ExpectVisibleAsync(
                cancellationExplanation.GetByText(
                    "Payment status",
                    new() { Exact = true }),
                viewportName,
                "Payment cancellation changed fields");
            await AssertFitsViewportAsync(page, viewportName, "Payment cancellation explanation");
            await CaptureVisualAsync(page, viewportName, "payment-cancellation-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task MembershipTypeSettingsEntriesLeadWithReadableCatalogChanges(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
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
            await LoginAsync(
                page,
                useOwner ? _app.LoginName : _app.AdminLoginName,
                useOwner ? _app.Password : _app.AdminPassword,
                $"{viewportName} membership type audit smoke");

            var editExplanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "MembershipType",
                "membership_type.edited",
                scenario.Explanations.MembershipTypeEditAuditEntryId,
                "membership-type-edited",
                viewportName);
            await ExpectVisibleAsync(
                editExplanation.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Membership type catalog updated", Exact = true }),
                viewportName,
                "Membership type edit explanation title");
            Assert.Equal(
                scenario.Explanations.OriginalMembershipTypeName,
                await ExplanationFactAsync(editExplanation, "Original catalog", "Name"));
            Assert.Equal(
                "30 days",
                await ExplanationFactAsync(editExplanation, "Original catalog", "Duration"));
            Assert.Equal(
                MoneyLabel(scenario.Explanations.OriginalMembershipTypePrice),
                await ExplanationFactAsync(editExplanation, "Original catalog", "Price"));
            Assert.Equal(
                scenario.Explanations.UpdatedMembershipTypeName,
                await ExplanationFactAsync(editExplanation, "Updated catalog", "Name"));
            Assert.Equal(
                "45 days",
                await ExplanationFactAsync(editExplanation, "Updated catalog", "Duration"));
            Assert.Equal(
                "12",
                await ExplanationFactAsync(editExplanation, "Updated catalog", "Visit limit"));
            Assert.Equal(
                MoneyLabel(scenario.Explanations.UpdatedMembershipTypePrice),
                await ExplanationFactAsync(editExplanation, "Updated catalog", "Price"));
            await ExpectVisibleAsync(
                editExplanation.GetByText(
                    "Name, Duration, Visit limit, Price, Catalog comment",
                    new() { Exact = true }),
                viewportName,
                "Membership type changed fields");

            var editRow = editExplanation.Locator("xpath=ancestor::li");
            var envelope = editRow.Locator(".audit-envelope-details");
            Assert.Null(await envelope.GetAttributeAsync("open"));
            Assert.False(await envelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = envelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "Membership type edit audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                envelope.Locator(".audit-json-grid"),
                viewportName,
                "Membership type edit raw envelope");
            Assert.Contains(
                "durationDays",
                await envelope.Locator(".audit-json-grid").InnerTextAsync(),
                StringComparison.Ordinal);
            await AssertFitsViewportAsync(page, viewportName, "Membership type edit explanation");
            await CaptureVisualAsync(page, viewportName, "membership-type-edit-explanation");

            var deactivationExplanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "MembershipType",
                "membership_type.deactivated",
                scenario.Explanations.MembershipTypeDeactivationAuditEntryId,
                "membership-type-deactivated",
                viewportName);
            await ExpectVisibleAsync(
                deactivationExplanation.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Membership type deactivated", Exact = true }),
                viewportName,
                "Membership type deactivation explanation title");
            Assert.Equal(
                scenario.Explanations.UpdatedMembershipTypeName,
                await ExplanationFactAsync(
                    deactivationExplanation,
                    "Before deactivation",
                    "Name"));
            Assert.Equal(
                "Active",
                await ExplanationFactAsync(
                    deactivationExplanation,
                    "Before deactivation",
                    "Status"));
            Assert.Equal(
                scenario.Explanations.UpdatedMembershipTypeName,
                await ExplanationFactAsync(
                    deactivationExplanation,
                    "After deactivation",
                    "Name"));
            Assert.Equal(
                "Inactive",
                await ExplanationFactAsync(
                    deactivationExplanation,
                    "After deactivation",
                    "Status"));
            Assert.NotEmpty(
                await ExplanationFactAsync(
                    deactivationExplanation,
                    "After deactivation",
                    "Deactivated"));
            await ExpectVisibleAsync(
                deactivationExplanation.GetByText(
                    "Catalog status",
                    new() { Exact = true }),
                viewportName,
                "Membership type deactivation changed field");
            await AssertFitsViewportAsync(
                page,
                viewportName,
                "Membership type deactivation explanation");
            await CaptureVisualAsync(
                page,
                viewportName,
                "membership-type-deactivation-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task InvalidOffsetKeepsAuditFiltersAndReturnsNoPartialTimeline()
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
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
            await LoginAsync(
                page,
                _app.LoginName,
                _app.Password,
                "invalid audit offset smoke");
            var recordedDate = scenario.RecordedDate.ToString("yyyy-MM-dd");
            await page.GotoAsync(
                new Uri(
                    _app.BaseAddress,
                    $"/Audit/Timeline?clientId={scenario.ClientId}&entity=Visit&from={recordedDate}&to={recordedDate}&action=visit.marked&offset=-1")
                    .ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            var error = page.GetByRole(AriaRole.Alert);
            await ExpectVisibleAsync(error, "tablet", "invalid audit offset error");
            Assert.Contains(
                "Offset must be between 0 and 10000.",
                await error.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.Equal(
                recordedDate,
                await page.GetByLabel("Recorded from (UTC)", new() { Exact = true })
                    .InputValueAsync());
            Assert.Equal(
                "visit.marked",
                await page.GetByLabel("Business action", new() { Exact = true })
                    .InputValueAsync());
            Assert.Equal(0, await page.Locator("[data-audit-entry-list]").CountAsync());
            await AssertFitsViewportAsync(page, "tablet", "invalid audit filter");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task AuditTimelineRequiresAuthentication()
    {
        Assert.NotNull(_browser);
        var context = await _browser.NewContextAsync();

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(
                new Uri(_app.BaseAddress, "/Audit/Timeline").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            Assert.Contains("/Login", page.Url, StringComparison.Ordinal);
            Assert.Contains("ReturnUrl=%2FAudit%2FTimeline", page.Url, StringComparison.Ordinal);
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

    private async Task<ILocator> OpenExplanationAsync(
        IPage page,
        Guid? clientId,
        string entity,
        string action,
        Guid auditEntryId,
        string explanationKind,
        string viewportName)
    {
        var clientFilter = clientId is { } value
            ? $"clientId={value}&"
            : string.Empty;
        await page.GotoAsync(
            new Uri(
                _app.BaseAddress,
                $"/Audit/Timeline?{clientFilter}entity={entity}&action={action}")
                .ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var rows = page.Locator("[data-audit-entry-list] > .audit-entry");
        Assert.Equal(1, await rows.CountAsync());
        var row = page.Locator($"[data-audit-entry-id='{auditEntryId}']");
        var explanation = row.Locator("[data-audit-change-explanation]");
        await ExpectVisibleAsync(
            explanation,
            viewportName,
            $"{action} readable explanation");
        Assert.Equal(
            explanationKind,
            await explanation.GetAttributeAsync("data-explanation-kind"));
        Assert.Equal(
            "true",
            await explanation.GetAttributeAsync("data-explanation-available"));
        Assert.Null(
            await row.Locator(".audit-envelope-details").GetAttributeAsync("open"));
        return explanation;
    }

    private static async Task<string> ExplanationFactAsync(
        ILocator explanation,
        string columnLabel,
        string factLabel)
    {
        var column = explanation.GetByRole(
            AriaRole.Region,
            new() { Name = columnLabel, Exact = true });
        var term = column.GetByText(factLabel, new() { Exact = true });
        return await term.Locator("xpath=following-sibling::dd").InnerTextAsync();
    }

    private static string MoneyLabel(decimal amount)
    {
        return $"{amount.ToString("0.##", CultureInfo.InvariantCulture)} UAH";
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
