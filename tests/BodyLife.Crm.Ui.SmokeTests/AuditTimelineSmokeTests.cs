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
    public async Task AddedNonWorkingDayShowsConfirmedImmutableAffectedScope(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var addition = scenario.Explanations.NonWorkingDayAddition;
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
                $"{viewportName} added non-working day audit smoke");

            var explanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "NonWorkingPeriod",
                "non_working_day.added",
                addition.AuditEntryId,
                "non-working-day-added",
                viewportName,
                entityId: addition.PeriodId);
            await ExpectVisibleAsync(
                explanation.GetByRole(
                    AriaRole.Heading,
                    new()
                    {
                        Name = "Non-working period and affected scope recorded",
                        Exact = true,
                    }),
                viewportName,
                "Added non-working day explanation title");
            Assert.Equal(
                addition.PreviewScopeLabel,
                await ExplanationFactAsync(
                    explanation,
                    "Confirmed preview",
                    "Preview scope"));
            Assert.Equal(
                TimestampLabel(addition.PreviewIssuedAt),
                await ExplanationFactAsync(
                    explanation,
                    "Confirmed preview",
                    "Preview issued"));
            Assert.Equal(
                TimestampLabel(addition.PreviewExpiresAt),
                await ExplanationFactAsync(
                    explanation,
                    "Confirmed preview",
                    "Preview expires"));
            Assert.Equal(
                addition.AffectedCount.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    explanation,
                    "Confirmed preview",
                    "Affected memberships"));
            Assert.Equal(
                addition.PeriodId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Recorded period",
                    "Non-working period"));
            Assert.Equal(
                DateRangeLabel(addition.Range.StartDate, addition.Range.EndDate),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded period",
                    "Period"));
            Assert.Equal(
                DaysLabel(addition.Range.InclusiveDays),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded period",
                    "Inclusive days"));
            Assert.Equal(
                addition.ReasonCode,
                await ExplanationFactAsync(
                    explanation,
                    "Recorded period",
                    "Reason code"));
            Assert.Equal(
                addition.ReasonComment,
                await ExplanationFactAsync(
                    explanation,
                    "Recorded period",
                    "Reason comment"));
            var applicationDetails = await ExplanationFactAsync(
                explanation,
                "Recorded period",
                "Application details");
            Assert.Contains(
                $"Membership {addition.FirstMembershipId.ToString("N")[..8]} / "
                    + $"Client {addition.FirstClientId.ToString("N")[..8]}: "
                    + DateRangeLabel(
                        addition.Range.StartDate,
                        addition.Range.EndDate),
                applicationDetails,
                StringComparison.Ordinal);
            Assert.Equal(
                $"{addition.AffectedCount} of {addition.AffectedCount}",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded period",
                    "Recalculated memberships"));
            Assert.Equal(
                "Active",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded period",
                    "Status"));
            Assert.Equal(
                TimestampLabel(addition.RecordedAt),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded period",
                    "Recorded"));
            await ExpectVisibleAsync(
                explanation.GetByText(
                    "Non-working period, Confirmed affected scope",
                    new() { Exact = true }),
                viewportName,
                "Added non-working day changed fields");

            var row = explanation.Locator("xpath=ancestor::li");
            var envelope = row.Locator(".audit-envelope-details");
            Assert.Null(await envelope.GetAttributeAsync("open"));
            Assert.False(await envelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = envelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "Added non-working day audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                envelope.Locator(".audit-json-grid"),
                viewportName,
                "Added non-working day raw envelope");
            var rawEnvelope = await envelope.Locator(".audit-json-grid").InnerTextAsync();
            Assert.Contains("scopeFingerprint", rawEnvelope, StringComparison.Ordinal);
            Assert.Contains("applications", rawEnvelope, StringComparison.Ordinal);
            Assert.Contains("recalculation", rawEnvelope, StringComparison.Ordinal);
            await AssertFitsViewportAsync(
                page,
                viewportName,
                "Added non-working day explanation");
            await CaptureVisualAsync(
                page,
                viewportName,
                "non-working-day-addition-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task CreatedClientShowsIdentityCardAndAcceptedDuplicateWarning(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var client = scenario.Explanations.ClientCreation;
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
                $"{viewportName} created Client audit smoke");

            var explanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "Client",
                "client.created",
                client.AuditEntryId,
                "client-created",
                viewportName,
                entityId: client.ClientId);
            await ExpectVisibleAsync(
                explanation.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Client profile created", Exact = true }),
                viewportName,
                "created Client explanation title");
            Assert.Equal(
                "Not present",
                await ExplanationFactAsync(
                    explanation,
                    "Before creation",
                    "Client"));
            Assert.Equal(
                "None",
                await ExplanationFactAsync(
                    explanation,
                    "Before creation",
                    "Current card"));
            Assert.Equal(
                "0",
                await ExplanationFactAsync(
                    explanation,
                    "Before creation",
                    "Warnings acknowledged"));
            Assert.Equal(
                client.ClientId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Created profile",
                    "Client"));
            Assert.Equal(
                client.DisplayName,
                await ExplanationFactAsync(
                    explanation,
                    "Created profile",
                    "Name"));
            Assert.Equal(
                client.Phone,
                await ExplanationFactAsync(
                    explanation,
                    "Created profile",
                    "Phone"));
            Assert.Equal(
                "Active",
                await ExplanationFactAsync(
                    explanation,
                    "Created profile",
                    "Operational status"));
            Assert.Equal(
                client.Comment,
                await ExplanationFactAsync(
                    explanation,
                    "Created profile",
                    "Comment"));
            Assert.Equal(
                client.CardNumber,
                await ExplanationFactAsync(
                    explanation,
                    "Created profile",
                    "Current card"));
            Assert.Equal(
                client.CardAssignmentId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Created profile",
                    "Card assignment"));
            Assert.Equal(
                "1",
                await ExplanationFactAsync(
                    explanation,
                    "Created profile",
                    "Warnings acknowledged"));
            Assert.Equal(
                $"Duplicate phone for Client {client.MatchedClientId.ToString("N")[..8]}: " +
                client.AcknowledgementReason,
                await ExplanationFactAsync(
                    explanation,
                    "Created profile",
                    "Acknowledgement details"));
            await ExpectVisibleAsync(
                explanation.GetByText("Client profile", new() { Exact = true }),
                viewportName,
                "created Client changed field");

            var envelope = explanation
                .Locator("xpath=ancestor::li")
                .Locator(".audit-envelope-details");
            Assert.Null(await envelope.GetAttributeAsync("open"));
            Assert.False(await envelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = envelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "created Client audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                envelope.Locator(".audit-json-grid"),
                viewportName,
                "created Client raw envelope");
            var envelopeText = await envelope.Locator(".audit-json-grid").InnerTextAsync();
            Assert.Contains(
                "duplicateWarningAcknowledgements",
                envelopeText,
                StringComparison.Ordinal);
            Assert.DoesNotContain("normalizedFullName", envelopeText, StringComparison.Ordinal);
            Assert.DoesNotContain("phoneNormalized", envelopeText, StringComparison.Ordinal);
            Assert.DoesNotContain("cardNumberNormalized", envelopeText, StringComparison.Ordinal);
            await AssertFitsViewportAsync(page, viewportName, "created Client explanation");
            await CaptureVisualAsync(page, viewportName, "client-created-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task CreatedPaymentShowsStoredCashSourceContext(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var payment = scenario.Explanations.PaymentCreation;
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
                $"{viewportName} created Payment audit smoke");

            var explanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "Payment",
                "payment.created",
                payment.AuditEntryId,
                "payment-created",
                viewportName,
                entityId: payment.PaymentId);
            await ExpectVisibleAsync(
                explanation.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Cash payment recorded", Exact = true }),
                viewportName,
                "created Payment explanation title");
            Assert.Equal(
                "Not present",
                await ExplanationFactAsync(
                    explanation,
                    "Before payment",
                    "Payment"));
            Assert.Equal(
                payment.PaymentId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Recorded payment",
                    "Payment"));
            Assert.Equal(
                payment.ClientId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Recorded payment",
                    "Client"));
            Assert.Equal(
                MoneyLabel(payment.Amount, payment.Currency),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded payment",
                    "Amount"));
            Assert.Equal(
                "Cash",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded payment",
                    "Method"));
            Assert.Equal(
                "Membership sale",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded payment",
                    "Context"));
            Assert.Equal(
                payment.MembershipId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Recorded payment",
                    "Membership"));
            Assert.Equal(
                TimestampLabel(payment.OccurredAt),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded payment",
                    "Occurred"));
            Assert.Equal(
                "Active",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded payment",
                    "Status"));
            await ExpectVisibleAsync(
                explanation.GetByText("Payment", new() { Exact = true }).Last,
                viewportName,
                "created Payment changed field");

            var envelope = explanation
                .Locator("xpath=ancestor::li")
                .Locator(".audit-envelope-details");
            Assert.Null(await envelope.GetAttributeAsync("open"));
            Assert.False(await envelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = envelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "created Payment audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                envelope.Locator(".audit-json-grid"),
                viewportName,
                "created Payment raw envelope");
            var envelopeText = await envelope.Locator(".audit-json-grid").InnerTextAsync();
            Assert.Contains("recordedAt", envelopeText, StringComparison.Ordinal);
            Assert.Contains("entryOrigin", envelopeText, StringComparison.Ordinal);
            Assert.Contains(payment.PaymentContext, envelopeText, StringComparison.Ordinal);
            await AssertFitsViewportAsync(page, viewportName, "created Payment explanation");
            await CaptureVisualAsync(page, viewportName, "payment-created-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task MarkedMembershipVisitShowsConsumptionAndStoredStateChange(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var visit = scenario.Explanations.VisitMark;
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
                $"{viewportName} marked Visit audit smoke");

            var explanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "Visit",
                "visit.marked",
                visit.AuditEntryId,
                "visit-marked",
                viewportName,
                entityId: visit.VisitId);
            await ExpectVisibleAsync(
                explanation.GetByRole(
                    AriaRole.Heading,
                    new()
                    {
                        Name = "Membership visit and consumption recorded",
                        Exact = true,
                    }),
                viewportName,
                "marked Membership Visit explanation title");
            Assert.Equal(
                visit.MembershipId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Before visit",
                    "Membership"));
            Assert.Equal(
                "Not present",
                await ExplanationFactAsync(
                    explanation,
                    "Before visit",
                    "Consumption"));
            Assert.Equal(
                visit.BeforeCountedVisits.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    explanation,
                    "Before visit",
                    "Counted visits"));
            Assert.Equal(
                visit.BeforeRemainingVisits.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    explanation,
                    "Before visit",
                    "Remaining visits"));
            Assert.Equal(
                "Zero remaining",
                await ExplanationFactAsync(
                    explanation,
                    "Before visit",
                    "Membership warnings"));
            Assert.Equal(
                "Membership visit",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded visit",
                    "Visit type"));
            Assert.Equal(
                visit.ClientId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Recorded visit",
                    "Client"));
            Assert.Equal(
                $"Counted / {visit.ConsumptionId.ToString("N")[..8]}",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded visit",
                    "Consumption"));
            Assert.Equal(
                "Explicit Membership",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded visit",
                    "Selection"));
            Assert.Equal(
                "Zero remaining",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded visit",
                    "Warning acknowledgements"));
            Assert.Equal(
                visit.AfterCountedVisits.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded visit",
                    "Counted visits"));
            Assert.Equal(
                visit.AfterRemainingVisits.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded visit",
                    "Remaining visits"));
            Assert.Equal(
                visit.AfterNegativeBalance.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded visit",
                    "Negative balance"));
            Assert.Equal(
                DateLabel(visit.FirstNegativeVisitDate),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded visit",
                    "First negative visit date"));
            Assert.Equal(
                "Negative balance",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded visit",
                    "Membership warnings"));
            await ExpectVisibleAsync(
                explanation.GetByText(
                    "Visit, counted consumption, Membership state",
                    new() { Exact = true }).Last,
                viewportName,
                "marked Visit changed fields");

            var primaryFacts = string.Join(
                ' ',
                await explanation.Locator(".audit-change-facts").AllInnerTextsAsync());
            Assert.DoesNotContain("lastCountedVisitAt", primaryFacts, StringComparison.Ordinal);
            Assert.DoesNotContain("extensionDays", primaryFacts, StringComparison.Ordinal);
            var envelope = explanation
                .Locator("xpath=ancestor::li")
                .Locator(".audit-envelope-details");
            Assert.Null(await envelope.GetAttributeAsync("open"));
            Assert.False(await envelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = envelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "marked Visit audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                envelope.Locator(".audit-json-grid"),
                viewportName,
                "marked Visit raw envelope");
            var envelopeText = await envelope.Locator(".audit-json-grid").InnerTextAsync();
            Assert.Contains("firstNegativeVisitId", envelopeText, StringComparison.Ordinal);
            Assert.Contains("lastCountedVisitAt", envelopeText, StringComparison.Ordinal);
            Assert.Contains("extensionDays", envelopeText, StringComparison.Ordinal);
            await AssertFitsViewportAsync(page, viewportName, "marked Visit explanation");
            await CaptureVisualAsync(page, viewportName, "marked-visit-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task MembershipIssueLeadsWithImmutableTermsAndInitialState(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var issue = scenario.Explanations.MembershipIssue;
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
                $"{viewportName} Membership issue audit smoke");

            var explanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "Membership",
                "membership.issued",
                issue.AuditEntryId,
                "membership-issued",
                viewportName,
                entityId: issue.MembershipId);
            await ExpectVisibleAsync(
                explanation.GetByRole(
                    AriaRole.Heading,
                    new()
                    {
                        Name = "Membership issued with immutable terms",
                        Exact = true,
                    }),
                viewportName,
                "Membership issue explanation title");
            Assert.Equal(
                "Not present",
                await ExplanationFactAsync(explanation, "Before issue", "Membership"));
            Assert.Equal(
                "None",
                await ExplanationFactAsync(
                    explanation,
                    "Before issue",
                    "Existing negative balance"));
            Assert.Equal(
                issue.MembershipId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Issued Membership",
                    "Membership"));
            Assert.Equal(
                issue.ClientId.ToString("N")[..8],
                await ExplanationFactAsync(explanation, "Issued Membership", "Client"));
            Assert.Equal(
                issue.MembershipTypeId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Issued Membership",
                    "Membership type"));
            Assert.Equal(
                issue.TypeName,
                await ExplanationFactAsync(
                    explanation,
                    "Issued Membership",
                    "Type snapshot"));
            Assert.Equal(
                DaysLabel(issue.DurationDays),
                await ExplanationFactAsync(
                    explanation,
                    "Issued Membership",
                    "Duration"));
            Assert.Equal(
                issue.VisitsLimit.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    explanation,
                    "Issued Membership",
                    "Visit limit"));
            Assert.Equal(
                MoneyLabel(issue.PriceAmount, issue.PriceCurrency),
                await ExplanationFactAsync(
                    explanation,
                    "Issued Membership",
                    "Snapshot price"));
            Assert.Equal(
                DateLabel(issue.StartDate),
                await ExplanationFactAsync(
                    explanation,
                    "Issued Membership",
                    "Start date"));
            Assert.Equal(
                DateLabel(issue.BaseEndDate),
                await ExplanationFactAsync(
                    explanation,
                    "Issued Membership",
                    "Base end date"));
            Assert.Equal(
                issue.InitialRemainingVisits.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    explanation,
                    "Issued Membership",
                    "Initial remaining visits"));
            Assert.Equal(
                DateLabel(issue.InitialEffectiveEndDate),
                await ExplanationFactAsync(
                    explanation,
                    "Issued Membership",
                    "Initial effective end date"));
            Assert.Equal(
                "Not required",
                await ExplanationFactAsync(
                    explanation,
                    "Issued Membership",
                    "Negative handling"));
            Assert.Equal(
                "None",
                await ExplanationFactAsync(explanation, "Issued Membership", "Payment"));
            await ExpectVisibleAsync(
                explanation.GetByText("Issued Membership", new() { Exact = true }).Last,
                viewportName,
                "Membership issue changed field");

            var primaryFacts = string.Join(
                ' ',
                await explanation.Locator(".audit-change-facts").AllInnerTextsAsync());
            Assert.DoesNotContain(
                "recalculationVersion",
                primaryFacts,
                StringComparison.Ordinal);
            var envelope = explanation
                .Locator("xpath=ancestor::li")
                .Locator(".audit-envelope-details");
            Assert.Null(await envelope.GetAttributeAsync("open"));
            Assert.False(await envelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = envelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "Membership issue audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                envelope.Locator(".audit-json-grid"),
                viewportName,
                "Membership issue raw envelope");
            var envelopeText = await envelope.Locator(".audit-json-grid").InnerTextAsync();
            Assert.Contains("snapshot", envelopeText, StringComparison.Ordinal);
            Assert.Contains("initialState", envelopeText, StringComparison.Ordinal);
            Assert.Contains("recalculationVersion", envelopeText, StringComparison.Ordinal);
            await AssertFitsViewportAsync(page, viewportName, "Membership issue explanation");
            await CaptureVisualAsync(page, viewportName, "membership-issue-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task CreatedMembershipOpeningStateSeparatesSourceFromRecalculation(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var opening = scenario.Explanations.MembershipOpeningStateCreation;
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
                $"{viewportName} created Membership opening-state audit smoke");

            var explanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "MembershipOpeningState",
                "membership_opening_state.created",
                opening.AuditEntryId,
                "membership-opening-state-created",
                viewportName,
                entityId: opening.OpeningStateId);
            await ExpectVisibleAsync(
                explanation.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Membership opening state recorded", Exact = true }),
                viewportName,
                "created Membership opening-state explanation title");
            Assert.Equal(
                "Not present",
                await ExplanationFactAsync(
                    explanation,
                    "Before declaration",
                    "Opening state"));
            Assert.Equal(
                opening.OpeningStateId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Opening state"));
            Assert.Equal(
                opening.MembershipId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Membership"));
            Assert.Equal(
                opening.ClientId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Client"));
            Assert.Equal(
                DateLabel(opening.OpeningAsOfDate),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Opening as of"));
            Assert.Equal(
                opening.DeclaredRemainingVisits.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Declared remaining visits"));
            Assert.Equal(
                opening.DeclaredNegativeBalance.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Declared negative balance"));
            Assert.Equal(
                DateLabel(opening.KnownEffectiveEndDate),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Known effective end"));
            Assert.Equal(
                DaysLabel(opening.KnownExtensionDays),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Known extension"));
            Assert.Equal(
                opening.SourceReference,
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Source reference"));
            Assert.Equal(
                opening.EntryBatchId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Entry batch"));
            Assert.Equal(
                "Manual backfill",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Entry origin"));
            Assert.Equal(
                TimestampLabel(opening.OccurredAt),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Occurred"));
            Assert.Equal(
                opening.DeclaredRemainingVisits.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Recalculated remaining visits"));
            Assert.Equal(
                opening.RecalculationVersion.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded opening state",
                    "Recalculation version"));
            await ExpectVisibleAsync(
                explanation.GetByText(
                    "Opening state, Membership state cache",
                    new() { Exact = true }),
                viewportName,
                "created Membership opening-state changed fields");

            var envelope = explanation
                .Locator("xpath=ancestor::li")
                .Locator(".audit-envelope-details");
            Assert.Null(await envelope.GetAttributeAsync("open"));
            Assert.False(await envelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = envelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "created Membership opening-state audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                envelope.Locator(".audit-json-grid"),
                viewportName,
                "created Membership opening-state raw envelope");
            var envelopeText = await envelope.Locator(".audit-json-grid").InnerTextAsync();
            Assert.Contains("sourceReference", envelopeText, StringComparison.Ordinal);
            Assert.Contains("recalculatedState", envelopeText, StringComparison.Ordinal);
            Assert.Contains("entryBatchId", envelopeText, StringComparison.Ordinal);
            await AssertFitsViewportAsync(
                page,
                viewportName,
                "created Membership opening-state explanation");
            await CaptureVisualAsync(
                page,
                viewportName,
                "membership-opening-state-created-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
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
                page.GetByLabel("Current session").GetByText(
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
                    TimestampLabel(scenario.FeaturedOccurredAt),
                    new() { Exact = true }),
                viewportName,
                "fallback occurred time");
            await ExpectVisibleAsync(
                featured.GetByText(
                    TimestampLabel(scenario.FeaturedRecordedAt),
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
            Assert.Contains("explicit_non_membership_context", envelopeText, StringComparison.Ordinal);
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
    public async Task CreatedMembershipTypeShowsFullFutureCatalogSnapshot(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var membershipType = scenario.Explanations.MembershipTypeCreation;
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
                $"{viewportName} created Membership type audit smoke");

            var explanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "MembershipType",
                "membership_type.created",
                membershipType.AuditEntryId,
                "membership-type-created",
                viewportName,
                entityId: membershipType.MembershipTypeId);
            await ExpectVisibleAsync(
                explanation.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Membership type created", Exact = true }),
                viewportName,
                "created Membership type explanation title");
            Assert.Equal(
                "Not present",
                await ExplanationFactAsync(
                    explanation,
                    "Before creation",
                    "Membership type"));
            Assert.Equal(
                membershipType.MembershipTypeId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Created catalog",
                    "Membership type"));
            Assert.Equal(
                membershipType.Name,
                await ExplanationFactAsync(explanation, "Created catalog", "Name"));
            Assert.Equal(
                DaysLabel(membershipType.DurationDays),
                await ExplanationFactAsync(explanation, "Created catalog", "Duration"));
            Assert.Equal(
                membershipType.VisitsLimit.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(explanation, "Created catalog", "Visit limit"));
            Assert.Equal(
                MoneyLabel(
                    membershipType.PriceAmount,
                    membershipType.PriceCurrency),
                await ExplanationFactAsync(explanation, "Created catalog", "Price"));
            Assert.Equal(
                "Active",
                await ExplanationFactAsync(explanation, "Created catalog", "Status"));
            Assert.Equal(
                membershipType.Comment,
                await ExplanationFactAsync(
                    explanation,
                    "Created catalog",
                    "Catalog comment"));
            Assert.Equal(
                TimestampLabel(membershipType.CreatedAt),
                await ExplanationFactAsync(explanation, "Created catalog", "Created"));
            await ExpectVisibleAsync(
                explanation.GetByText(
                    "Membership type catalog",
                    new() { Exact = true }),
                viewportName,
                "created Membership type changed field");

            var envelope = explanation
                .Locator("xpath=ancestor::li")
                .Locator(".audit-envelope-details");
            Assert.Null(await envelope.GetAttributeAsync("open"));
            Assert.False(await envelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = envelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "created Membership type audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                envelope.Locator(".audit-json-grid"),
                viewportName,
                "created Membership type raw envelope");
            var envelopeText = await envelope.Locator(".audit-json-grid").InnerTextAsync();
            Assert.Contains("createdAt", envelopeText, StringComparison.Ordinal);
            Assert.Contains("deactivatedAt", envelopeText, StringComparison.Ordinal);
            Assert.DoesNotContain("initialState", envelopeText, StringComparison.Ordinal);
            await AssertFitsViewportAsync(
                page,
                viewportName,
                "created Membership type explanation");
            await CaptureVisualAsync(
                page,
                viewportName,
                "membership-type-created-explanation");
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

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task NonWorkingDayChangesLeadWithReadablePeriodAndScopeSummaries(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var nonWorkingDays = scenario.Explanations.NonWorkingDays;
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
                $"{viewportName} non-working day audit smoke");

            var correctionExplanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "NonWorkingPeriod",
                "non_working_day.corrected",
                nonWorkingDays.CorrectedAuditEntryId,
                "non-working-day-corrected",
                viewportName,
                entityId: nonWorkingDays.CorrectedOriginalPeriodId);
            await ExpectVisibleAsync(
                correctionExplanation.GetByRole(
                    AriaRole.Heading,
                    new()
                    {
                        Name = "Original non-working period preserved; replacement added",
                        Exact = true,
                    }),
                viewportName,
                "Non-working day correction explanation title");
            Assert.Equal(
                DateRangeLabel(
                    nonWorkingDays.CorrectedOriginalPeriod.StartDate,
                    nonWorkingDays.CorrectedOriginalPeriod.EndDate),
                await ExplanationFactAsync(
                    correctionExplanation,
                    "Original period",
                    "Period"));
            Assert.Equal(
                nonWorkingDays.CorrectedOldAffectedCount.ToString(
                    CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    correctionExplanation,
                    "Original period",
                    "Affected memberships"));
            Assert.Equal(
                "weather_closure",
                await ExplanationFactAsync(
                    correctionExplanation,
                    "Original period",
                    "Reason code"));
            Assert.Equal(
                "Date range replaced",
                await ExplanationFactAsync(
                    correctionExplanation,
                    "Replacement period",
                    "Correction type"));
            Assert.Equal(
                DateRangeLabel(
                    nonWorkingDays.CorrectedReplacementPeriod.StartDate,
                    nonWorkingDays.CorrectedReplacementPeriod.EndDate),
                await ExplanationFactAsync(
                    correctionExplanation,
                    "Replacement period",
                    "Period"));
            Assert.Equal(
                nonWorkingDays.CorrectedNewAffectedCount.ToString(
                    CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    correctionExplanation,
                    "Replacement period",
                    "Affected memberships"));
            Assert.Equal(
                "maintenance",
                await ExplanationFactAsync(
                    correctionExplanation,
                    "Replacement period",
                    "Reason code"));
            Assert.Equal(
                $"{nonWorkingDays.CorrectedAffectedUnionCount} of {nonWorkingDays.CorrectedAffectedUnionCount}",
                await ExplanationFactAsync(
                    correctionExplanation,
                    "Replacement period",
                    "Recalculated memberships"));
            await ExpectVisibleAsync(
                correctionExplanation.GetByText(
                    "Date range, Reason, Affected scope",
                    new() { Exact = true }),
                viewportName,
                "Non-working day correction changed fields");

            var correctionRow = correctionExplanation.Locator("xpath=ancestor::li");
            var envelope = correctionRow.Locator(".audit-envelope-details");
            Assert.Null(await envelope.GetAttributeAsync("open"));
            Assert.False(await envelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = envelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "Non-working day correction audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                envelope.Locator(".audit-json-grid"),
                viewportName,
                "Non-working day correction raw envelope");
            Assert.Contains(
                "replacementApplications",
                await envelope.Locator(".audit-json-grid").InnerTextAsync(),
                StringComparison.Ordinal);
            await AssertFitsViewportAsync(
                page,
                viewportName,
                "Non-working day correction explanation");
            await CaptureVisualAsync(
                page,
                viewportName,
                "non-working-day-correction-explanation");

            var cancellationExplanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "NonWorkingPeriod",
                "non_working_day.canceled",
                nonWorkingDays.CanceledAuditEntryId,
                "non-working-day-canceled",
                viewportName,
                entityId: nonWorkingDays.CanceledOriginalPeriodId);
            await ExpectVisibleAsync(
                cancellationExplanation.GetByRole(
                    AriaRole.Heading,
                    new()
                    {
                        Name = "Original non-working period preserved; cancellation added",
                        Exact = true,
                    }),
                viewportName,
                "Non-working day cancellation explanation title");
            Assert.Equal(
                DateRangeLabel(
                    nonWorkingDays.CanceledPeriod.StartDate,
                    nonWorkingDays.CanceledPeriod.EndDate),
                await ExplanationFactAsync(
                    cancellationExplanation,
                    "Original period",
                    "Period"));
            Assert.Equal(
                nonWorkingDays.CanceledAffectedCount.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    cancellationExplanation,
                    "Original period",
                    "Affected memberships"));
            Assert.Equal(
                "Preserved",
                await ExplanationFactAsync(
                    cancellationExplanation,
                    "After cancellation",
                    "Original fact"));
            Assert.Equal(
                "Canceled",
                await ExplanationFactAsync(
                    cancellationExplanation,
                    "After cancellation",
                    "Status"));
            Assert.Equal(
                "0",
                await ExplanationFactAsync(
                    cancellationExplanation,
                    "After cancellation",
                    "Active applications"));
            Assert.Equal(
                $"{nonWorkingDays.CanceledAffectedCount} of {nonWorkingDays.CanceledAffectedCount}",
                await ExplanationFactAsync(
                    cancellationExplanation,
                    "After cancellation",
                    "Recalculated memberships"));
            await ExpectVisibleAsync(
                cancellationExplanation.GetByText(
                    "Period status, Active affected scope",
                    new() { Exact = true }),
                viewportName,
                "Non-working day cancellation changed fields");
            await AssertFitsViewportAsync(
                page,
                viewportName,
                "Non-working day cancellation explanation");
            await CaptureVisualAsync(
                page,
                viewportName,
                "non-working-day-cancellation-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task AddedFreezeShowsInclusiveSourceAndStoredMembershipState(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var freeze = scenario.Explanations.FreezeAddition;
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
                $"{viewportName} added Freeze audit smoke");

            var explanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "Freeze",
                "freeze.added",
                freeze.AuditEntryId,
                "freeze-added",
                viewportName,
                entityId: freeze.FreezeId);
            await ExpectVisibleAsync(
                explanation.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Freeze source recorded", Exact = true }),
                viewportName,
                "added Freeze explanation title");
            Assert.Equal(
                "Not present",
                await ExplanationFactAsync(explanation, "Before Freeze", "Freeze"));
            Assert.Equal(
                freeze.MembershipId.ToString("N")[..8],
                await ExplanationFactAsync(
                    explanation,
                    "Before Freeze",
                    "Membership"));
            Assert.Equal(
                DaysLabel(freeze.BeforeExtensionDays),
                await ExplanationFactAsync(
                    explanation,
                    "Before Freeze",
                    "Extension days"));
            Assert.Equal(
                DateLabel(freeze.BeforeEffectiveEndDate),
                await ExplanationFactAsync(
                    explanation,
                    "Before Freeze",
                    "Effective end"));
            Assert.Equal(
                freeze.FreezeId.ToString("N")[..8],
                await ExplanationFactAsync(explanation, "Recorded Freeze", "Freeze"));
            Assert.Equal(
                freeze.ClientId.ToString("N")[..8],
                await ExplanationFactAsync(explanation, "Recorded Freeze", "Client"));
            Assert.Equal(
                DateRangeLabel(freeze.Range.StartDate, freeze.Range.EndDate),
                await ExplanationFactAsync(explanation, "Recorded Freeze", "Period"));
            Assert.Equal(
                DaysLabel(freeze.Range.InclusiveDays),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded Freeze",
                    "Inclusive days"));
            Assert.Equal(
                freeze.Reason,
                await ExplanationFactAsync(
                    explanation,
                    "Recorded Freeze",
                    "Freeze reason"));
            Assert.Equal(
                TimestampLabel(freeze.OccurredAt),
                await ExplanationFactAsync(explanation, "Recorded Freeze", "Occurred"));
            Assert.Equal(
                "Normal entry",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded Freeze",
                    "Entry origin"));
            Assert.Equal(
                "Active",
                await ExplanationFactAsync(
                    explanation,
                    "Recorded Freeze",
                    "Source status"));
            Assert.Equal(
                DaysLabel(freeze.AfterExtensionDays),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded Freeze",
                    "Extension days"));
            Assert.Equal(
                DateLabel(freeze.AfterEffectiveEndDate),
                await ExplanationFactAsync(
                    explanation,
                    "Recorded Freeze",
                    "Effective end"));
            await ExpectVisibleAsync(
                explanation.GetByText(
                    "Freeze source, Membership extension state",
                    new() { Exact = true }),
                viewportName,
                "added Freeze changed fields");

            var envelope = explanation
                .Locator("xpath=ancestor::li")
                .Locator(".audit-envelope-details");
            Assert.Null(await envelope.GetAttributeAsync("open"));
            Assert.False(await envelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = envelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "added Freeze audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                envelope.Locator(".audit-json-grid"),
                viewportName,
                "added Freeze raw envelope");
            var envelopeText = await envelope.Locator(".audit-json-grid").InnerTextAsync();
            Assert.Contains("inclusiveDays", envelopeText, StringComparison.Ordinal);
            Assert.Contains("membershipState", envelopeText, StringComparison.Ordinal);
            Assert.Contains("entryOrigin", envelopeText, StringComparison.Ordinal);
            await AssertFitsViewportAsync(page, viewportName, "added Freeze explanation");
            await CaptureVisualAsync(page, viewportName, "freeze-added-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task FreezeCancellationLeadsWithPreservedRangeAndStoredMembershipState(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var freeze = scenario.Explanations.FreezeCancellation;
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
                $"{viewportName} Freeze cancellation audit smoke");

            var explanation = await OpenExplanationAsync(
                page,
                clientId: null,
                "Freeze",
                "freeze.canceled",
                freeze.AuditEntryId,
                "freeze-canceled",
                viewportName,
                entityId: freeze.FreezeId);
            await ExpectVisibleAsync(
                explanation.GetByRole(
                    AriaRole.Heading,
                    new()
                    {
                        Name = "Original Freeze preserved; cancellation added",
                        Exact = true,
                    }),
                viewportName,
                "Freeze cancellation explanation title");
            Assert.Equal(
                DateRangeLabel(freeze.Range.StartDate, freeze.Range.EndDate),
                await ExplanationFactAsync(
                    explanation,
                    "Original freeze",
                    "Period"));
            Assert.Equal(
                DaysLabel(freeze.Range.InclusiveDays),
                await ExplanationFactAsync(
                    explanation,
                    "Original freeze",
                    "Inclusive days"));
            Assert.Equal(
                freeze.Reason,
                await ExplanationFactAsync(
                    explanation,
                    "Original freeze",
                    "Freeze reason"));
            Assert.Equal(
                DaysLabel(freeze.BeforeExtensionDays),
                await ExplanationFactAsync(
                    explanation,
                    "Original freeze",
                    "Extension days"));
            Assert.Equal(
                DateLabel(freeze.BeforeEffectiveEndDate),
                await ExplanationFactAsync(
                    explanation,
                    "Original freeze",
                    "Effective end"));
            Assert.Equal(
                "Preserved",
                await ExplanationFactAsync(
                    explanation,
                    "After cancellation",
                    "Original fact"));
            Assert.Equal(
                "Canceled",
                await ExplanationFactAsync(
                    explanation,
                    "After cancellation",
                    "Status"));
            Assert.Equal(
                DaysLabel(freeze.AfterExtensionDays),
                await ExplanationFactAsync(
                    explanation,
                    "After cancellation",
                    "Extension days"));
            Assert.Equal(
                DateLabel(freeze.AfterEffectiveEndDate),
                await ExplanationFactAsync(
                    explanation,
                    "After cancellation",
                    "Effective end"));
            await ExpectVisibleAsync(
                explanation.GetByText(
                    "Freeze status, Membership extension state",
                    new() { Exact = true }),
                viewportName,
                "Freeze cancellation changed fields");

            var row = explanation.Locator("xpath=ancestor::li");
            var envelope = row.Locator(".audit-envelope-details");
            Assert.Null(await envelope.GetAttributeAsync("open"));
            Assert.False(await envelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = envelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "Freeze cancellation audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                envelope.Locator(".audit-json-grid"),
                viewportName,
                "Freeze cancellation raw envelope");
            Assert.Contains(
                "membershipState",
                await envelope.Locator(".audit-json-grid").InnerTextAsync(),
                StringComparison.Ordinal);
            await AssertFitsViewportAsync(
                page,
                viewportName,
                "Freeze cancellation explanation");
            await CaptureVisualAsync(
                page,
                viewportName,
                "freeze-cancellation-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task ClientAndCardChangesLeadWithReadableStoredSnapshots(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var clientAndCards = scenario.Explanations.ClientAndCards;
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
                $"{viewportName} client and card audit smoke");

            var clientUpdate = await OpenExplanationAsync(
                page,
                clientAndCards.ClientId,
                "Client",
                "client.updated",
                clientAndCards.ClientUpdateAuditEntryId,
                "client-updated",
                viewportName);
            await ExpectVisibleAsync(
                clientUpdate.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Client profile updated", Exact = true }),
                viewportName,
                "Client update explanation title");
            Assert.Equal(
                clientAndCards.OriginalDisplayName,
                await ExplanationFactAsync(clientUpdate, "Original profile", "Name"));
            Assert.Equal(
                clientAndCards.OriginalPhone,
                await ExplanationFactAsync(clientUpdate, "Original profile", "Phone"));
            Assert.Equal(
                "Active",
                await ExplanationFactAsync(
                    clientUpdate,
                    "Original profile",
                    "Operational status"));
            Assert.Equal(
                clientAndCards.UpdatedDisplayName,
                await ExplanationFactAsync(clientUpdate, "Updated profile", "Name"));
            Assert.Equal(
                clientAndCards.UpdatedPhone,
                await ExplanationFactAsync(clientUpdate, "Updated profile", "Phone"));
            Assert.Equal(
                "Inactive",
                await ExplanationFactAsync(
                    clientUpdate,
                    "Updated profile",
                    "Operational status"));
            Assert.Equal(
                "1",
                await ExplanationFactAsync(
                    clientUpdate,
                    "Updated profile",
                    "Warnings acknowledged"));
            Assert.Equal(
                $"Duplicate phone for Client {clientAndCards.MatchedClientId.ToString("N")[..8]}: Confirmed family member",
                await ExplanationFactAsync(
                    clientUpdate,
                    "Updated profile",
                    "Acknowledgement details"));
            await ExpectVisibleAsync(
                clientUpdate.GetByText(
                    "Name, Phone, Operational status, Comment, Duplicate warnings acknowledged",
                    new() { Exact = true }),
                viewportName,
                "Client update changed fields");
            var clientUpdateEnvelope = clientUpdate
                .Locator("xpath=ancestor::li")
                .Locator(".audit-envelope-details");
            Assert.Null(await clientUpdateEnvelope.GetAttributeAsync("open"));
            Assert.False(await clientUpdateEnvelope.Locator(".audit-json-grid").IsVisibleAsync());
            await AssertFitsViewportAsync(page, viewportName, "Client update explanation");
            await CaptureVisualAsync(page, viewportName, "client-update-explanation");

            var cardAssignment = await OpenExplanationAsync(
                page,
                clientAndCards.ClientId,
                "Client",
                "card.assigned",
                clientAndCards.CardAssignedAuditEntryId,
                "card-assigned",
                viewportName);
            await ExpectVisibleAsync(
                cardAssignment.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Card assigned to Client", Exact = true }),
                viewportName,
                "Card assignment explanation title");
            Assert.Equal(
                "None",
                await ExplanationFactAsync(
                    cardAssignment,
                    "Before assignment",
                    "Current card"));
            Assert.Equal(
                clientAndCards.AssignedCardNumber,
                await ExplanationFactAsync(cardAssignment, "Current card", "Card number"));

            var cardChange = await OpenExplanationAsync(
                page,
                clientAndCards.ClientId,
                "Client",
                "card.changed",
                clientAndCards.CardChangedAuditEntryId,
                "card-changed",
                viewportName);
            Assert.Equal(
                clientAndCards.AssignedCardNumber,
                await ExplanationFactAsync(cardChange, "Previous card", "Card number"));
            Assert.Equal(
                clientAndCards.ReplacementCardNumber,
                await ExplanationFactAsync(cardChange, "Current card", "Card number"));
            await ExpectVisibleAsync(
                cardChange.GetByText(
                    "Card number, Card assignment",
                    new() { Exact = true }),
                viewportName,
                "Card change changed fields");
            var cardChangeEnvelope = cardChange
                .Locator("xpath=ancestor::li")
                .Locator(".audit-envelope-details");
            Assert.Null(await cardChangeEnvelope.GetAttributeAsync("open"));
            var cardEnvelopeToggle = cardChangeEnvelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                cardEnvelopeToggle,
                viewportName,
                "Card change audit envelope");
            await cardEnvelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                cardChangeEnvelope.Locator(".audit-json-grid"),
                viewportName,
                "Card change raw envelope");
            Assert.Contains(
                "cardNumberNormalized",
                await cardChangeEnvelope.Locator(".audit-json-grid").InnerTextAsync(),
                StringComparison.Ordinal);

            var cardClear = await OpenExplanationAsync(
                page,
                clientAndCards.ClientId,
                "Client",
                "card.cleared",
                clientAndCards.CardClearedAuditEntryId,
                "card-cleared",
                viewportName);
            Assert.Equal(
                clientAndCards.ReplacementCardNumber,
                await ExplanationFactAsync(cardClear, "Previous card", "Card number"));
            Assert.Equal(
                "Preserved in history",
                await ExplanationFactAsync(
                    cardClear,
                    "After clearing",
                    "Previous assignment"));
            Assert.Equal(
                "None",
                await ExplanationFactAsync(cardClear, "After clearing", "Current card"));
            await ExpectVisibleAsync(
                cardClear.GetByText("Current card", new() { Exact = true }).Last,
                viewportName,
                "Card clear changed field");
            await AssertFitsViewportAsync(page, viewportName, "Card clear explanation");
            await CaptureVisualAsync(page, viewportName, "card-clear-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task StaffAccountCreationShowsProfileWithoutCredentialMaterial(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var staff = scenario.Explanations.StaffAccounts;
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
                $"{viewportName} staff account creation audit smoke");

            var creation = await OpenExplanationAsync(
                page,
                clientId: null,
                "StaffAccount",
                "staff_account.created",
                staff.CreatedAuditEntryId,
                "staff-account-created",
                viewportName,
                entityId: staff.StaffAccountId);
            await ExpectVisibleAsync(
                creation.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Staff account created", Exact = true }),
                viewportName,
                "Staff account creation explanation title");
            Assert.Equal(
                "Not present",
                await ExplanationFactAsync(creation, "Before creation", "Account state"));
            Assert.Equal(
                staff.StaffAccountId.ToString("N")[..8],
                await ExplanationFactAsync(
                    creation,
                    "Created staff account",
                    "Staff account"));
            Assert.Equal(
                staff.OriginalDisplayName,
                await ExplanationFactAsync(
                    creation,
                    "Created staff account",
                    "Display name"));
            Assert.Equal(
                "Named Admin",
                await ExplanationFactAsync(
                    creation,
                    "Created staff account",
                    "Account type"));
            Assert.Equal(
                "Active",
                await ExplanationFactAsync(creation, "Created staff account", "Status"));
            await ExpectVisibleAsync(
                creation.GetByText("Staff account", new() { Exact = true }).Last,
                viewportName,
                "Staff account creation changed field");
            var primaryFacts = string.Join(
                ' ',
                await creation.Locator(".audit-change-facts dt").AllInnerTextsAsync());
            Assert.DoesNotContain("credential", primaryFacts, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("login", primaryFacts, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", primaryFacts, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", primaryFacts, StringComparison.OrdinalIgnoreCase);

            var creationRow = creation.Locator("xpath=ancestor::li");
            var creationEnvelope = creationRow.Locator(".audit-envelope-details");
            Assert.Null(await creationEnvelope.GetAttributeAsync("open"));
            Assert.False(await creationEnvelope.Locator(".audit-json-grid").IsVisibleAsync());
            var envelopeToggle = creationEnvelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "Staff account creation audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                creationEnvelope.Locator(".audit-json-grid"),
                viewportName,
                "Staff account creation raw envelope");
            var envelopeText = await creationEnvelope.Locator(".audit-json-grid").InnerTextAsync();
            Assert.Contains("displayName", envelopeText, StringComparison.Ordinal);
            Assert.Contains("accountType", envelopeText, StringComparison.Ordinal);
            Assert.Contains(staff.CreatedAccountType, envelopeText, StringComparison.Ordinal);
            Assert.Contains("role", envelopeText, StringComparison.Ordinal);
            Assert.Contains("isActive", envelopeText, StringComparison.Ordinal);
            Assert.DoesNotContain("credential", envelopeText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("loginName", envelopeText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", envelopeText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", envelopeText, StringComparison.OrdinalIgnoreCase);
            await AssertFitsViewportAsync(page, viewportName, "Staff account creation");
            await CaptureVisualAsync(
                page,
                viewportName,
                "staff-account-creation-explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task StaffAccountSettingsLeadWithReadableProfileAndAccessChanges(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var staff = scenario.Explanations.StaffAccounts;
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
                $"{viewportName} staff account audit smoke");

            var displayNameUpdate = await OpenExplanationAsync(
                page,
                clientId: null,
                "StaffAccount",
                "staff_account.display_name_updated",
                staff.DisplayNameUpdatedAuditEntryId,
                "staff-account-display-name-updated",
                viewportName,
                entityId: staff.StaffAccountId);
            await ExpectVisibleAsync(
                displayNameUpdate.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Staff display name updated", Exact = true }),
                viewportName,
                "Staff display-name explanation title");
            Assert.Equal(
                staff.StaffAccountId.ToString("N")[..8],
                await ExplanationFactAsync(
                    displayNameUpdate,
                    "Original staff profile",
                    "Staff account"));
            Assert.Equal(
                staff.OriginalDisplayName,
                await ExplanationFactAsync(
                    displayNameUpdate,
                    "Original staff profile",
                    "Display name"));
            Assert.Equal(
                staff.UpdatedDisplayName,
                await ExplanationFactAsync(
                    displayNameUpdate,
                    "Updated staff profile",
                    "Display name"));
            await ExpectVisibleAsync(
                displayNameUpdate.GetByText("Display name", new() { Exact = true }).Last,
                viewportName,
                "Staff display-name changed field");
            var displayNameEnvelope = displayNameUpdate
                .Locator("xpath=ancestor::li")
                .Locator(".audit-envelope-details");
            Assert.Null(await displayNameEnvelope.GetAttributeAsync("open"));
            Assert.False(await displayNameEnvelope.Locator(".audit-json-grid").IsVisibleAsync());
            await AssertFitsViewportAsync(page, viewportName, "Staff display-name explanation");
            await CaptureVisualAsync(page, viewportName, "staff-display-name-explanation");

            var deactivation = await OpenExplanationAsync(
                page,
                clientId: null,
                "StaffAccount",
                "staff_account.deactivated",
                staff.DeactivatedAuditEntryId,
                "staff-account-deactivated",
                viewportName,
                entityId: staff.StaffAccountId);
            await ExpectVisibleAsync(
                deactivation.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Staff account deactivated", Exact = true }),
                viewportName,
                "Staff deactivation explanation title");
            Assert.Equal(
                "Active",
                await ExplanationFactAsync(
                    deactivation,
                    "Before deactivation",
                    "Status"));
            Assert.Equal(
                "Inactive",
                await ExplanationFactAsync(
                    deactivation,
                    "After deactivation",
                    "Status"));
            Assert.Equal(
                staff.EndedSessionCount.ToString(CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    deactivation,
                    "After deactivation",
                    "Active sessions ended"));
            await ExpectVisibleAsync(
                deactivation.GetByText(
                    "Account status, Active sessions",
                    new() { Exact = true }),
                viewportName,
                "Staff deactivation changed fields");
            var deactivationRow = deactivation.Locator("xpath=ancestor::li");
            await ExpectVisibleAsync(
                deactivationRow.GetByText(staff.DeactivationReason, new() { Exact = true }),
                viewportName,
                "Staff deactivation reason");
            var deactivationEnvelope = deactivationRow.Locator(".audit-envelope-details");
            var envelopeToggle = deactivationEnvelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "Staff deactivation audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                deactivationEnvelope.Locator(".audit-json-grid"),
                viewportName,
                "Staff deactivation raw envelope");
            var envelopeText = await deactivationEnvelope
                .Locator(".audit-json-grid")
                .InnerTextAsync();
            Assert.Contains("endedSessionCount", envelopeText, StringComparison.Ordinal);
            Assert.DoesNotContain("password", envelopeText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("loginName", envelopeText, StringComparison.OrdinalIgnoreCase);
            await AssertFitsViewportAsync(page, viewportName, "Staff deactivation explanation");
            await CaptureVisualAsync(page, viewportName, "staff-deactivation-explanation");

            var activation = await OpenExplanationAsync(
                page,
                clientId: null,
                "StaffAccount",
                "staff_account.activated",
                staff.ActivatedAuditEntryId,
                "staff-account-activated",
                viewportName,
                entityId: staff.StaffAccountId);
            Assert.Equal(
                "Inactive",
                await ExplanationFactAsync(activation, "Before activation", "Status"));
            Assert.Equal(
                "Active",
                await ExplanationFactAsync(activation, "After activation", "Status"));
            Assert.Equal(
                0,
                await activation.GetByText(
                    "Active sessions ended",
                    new() { Exact = true }).CountAsync());
            await ExpectVisibleAsync(
                activation.GetByText("Account status", new() { Exact = true }),
                viewportName,
                "Staff activation changed field");
            await AssertFitsViewportAsync(page, viewportName, "Staff activation explanation");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("owner-tablet", 1024, 768, true)]
    [InlineData("admin-phone", 390, 844, false)]
    public async Task StaffCredentialChangesShowOnlyStateAndSessionImpact(
        string viewportName,
        int width,
        int height,
        bool useOwner)
    {
        Assert.NotNull(_browser);
        var scenario = await _app.EnsureAuditTimelineScenarioAsync();
        var staff = scenario.Explanations.StaffAccounts;
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
                $"{viewportName} staff credential audit smoke");

            var configuration = await OpenExplanationAsync(
                page,
                clientId: null,
                "StaffAccount",
                "staff_credentials.configured",
                staff.CredentialsConfiguredAuditEntryId,
                "staff-credentials-configured",
                viewportName,
                entityId: staff.StaffAccountId);
            await ExpectVisibleAsync(
                configuration.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Staff credentials configured", Exact = true }),
                viewportName,
                "Credential configuration explanation title");
            Assert.Equal(
                "Not configured",
                await ExplanationFactAsync(
                    configuration,
                    "Before configuration",
                    "Credential state"));
            Assert.Equal(
                "Configured",
                await ExplanationFactAsync(
                    configuration,
                    "After configuration",
                    "Credential state"));
            Assert.Equal(
                "0",
                await ExplanationFactAsync(
                    configuration,
                    "After configuration",
                    "Active sessions ended"));
            await ExpectVisibleAsync(
                configuration.GetByText("Credential state", new() { Exact = true }).Last,
                viewportName,
                "Credential configuration changed field");
            var configurationEnvelope = configuration
                .Locator("xpath=ancestor::li")
                .Locator(".audit-envelope-details");
            Assert.Null(await configurationEnvelope.GetAttributeAsync("open"));
            Assert.False(await configurationEnvelope.Locator(".audit-json-grid").IsVisibleAsync());
            await AssertFitsViewportAsync(page, viewportName, "Credential configuration");
            await CaptureVisualAsync(
                page,
                viewportName,
                "staff-credential-configuration-explanation");

            var reset = await OpenExplanationAsync(
                page,
                clientId: null,
                "StaffAccount",
                "staff_credentials.reset",
                staff.CredentialsResetAuditEntryId,
                "staff-credentials-reset",
                viewportName,
                entityId: staff.StaffAccountId);
            await ExpectVisibleAsync(
                reset.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Staff credentials reset", Exact = true }),
                viewportName,
                "Credential reset explanation title");
            Assert.Equal(
                "Configured",
                await ExplanationFactAsync(reset, "Before reset", "Credential state"));
            Assert.Equal(
                "Configured",
                await ExplanationFactAsync(reset, "After reset", "Credential state"));
            Assert.Equal(
                staff.CredentialResetEndedSessionCount.ToString(
                    CultureInfo.InvariantCulture),
                await ExplanationFactAsync(
                    reset,
                    "After reset",
                    "Active sessions ended"));
            await ExpectVisibleAsync(
                reset.GetByText(
                    "Credentials, Active sessions",
                    new() { Exact = true }),
                viewportName,
                "Credential reset changed fields");
            var resetRow = reset.Locator("xpath=ancestor::li");
            await ExpectVisibleAsync(
                resetRow.GetByText(staff.CredentialResetReason, new() { Exact = true }),
                viewportName,
                "Credential reset reason");
            var resetEnvelope = resetRow.Locator(".audit-envelope-details");
            var envelopeToggle = resetEnvelope.Locator("summary");
            await AssertMinimumTouchTargetAsync(
                envelopeToggle,
                viewportName,
                "Credential reset audit envelope");
            await envelopeToggle.ClickAsync();
            await ExpectVisibleAsync(
                resetEnvelope.Locator(".audit-json-grid"),
                viewportName,
                "Credential reset raw envelope");
            var envelopeText = await resetEnvelope.Locator(".audit-json-grid").InnerTextAsync();
            Assert.Contains("credentialsConfigured", envelopeText, StringComparison.Ordinal);
            Assert.Contains("endedSessionCount", envelopeText, StringComparison.Ordinal);
            Assert.DoesNotContain("loginName", envelopeText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", envelopeText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", envelopeText, StringComparison.OrdinalIgnoreCase);
            await AssertFitsViewportAsync(page, viewportName, "Credential reset");
            await CaptureVisualAsync(
                page,
                viewportName,
                "staff-credential-reset-explanation");
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
                "Enter valid client, entity, date, and page filters.",
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
        string viewportName,
        Guid? entityId = null)
    {
        var clientFilter = clientId is { } value
            ? $"clientId={value}&"
            : string.Empty;
        var entityIdFilter = entityId is { } id
            ? $"entityId={id}&"
            : string.Empty;
        await page.GotoAsync(
            new Uri(
                _app.BaseAddress,
                $"/Audit/Timeline?{clientFilter}{entityIdFilter}entity={entity}&action={action}")
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

    private static string MoneyLabel(decimal amount, string currency = "UAH")
    {
        return $"{amount.ToString("N2", WorkflowCulture)} {currency}";
    }

    private static string DateLabel(DateOnly date)
    {
        return date.ToString("d", WorkflowCulture);
    }

    private static string DateRangeLabel(DateOnly startDate, DateOnly endDate)
    {
        return $"{DateLabel(startDate)} to {DateLabel(endDate)}";
    }

    private static string TimestampLabel(DateTimeOffset timestamp)
    {
        return $"{timestamp.UtcDateTime.ToString("g", WorkflowCulture)} UTC";
    }

    private static string DaysLabel(int days)
    {
        return days == 1 ? "1 day" : $"{days.ToString(WorkflowCulture)} days";
    }

    private static CultureInfo WorkflowCulture { get; } =
        CultureInfo.GetCultureInfo(ReceptionAppFixture.WorkflowCulture);

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
