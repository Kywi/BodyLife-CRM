using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Pages.Audit;

namespace BodyLife.Crm.Web.Tests.Pages.Audit;

public sealed class AuditEntryExplanationViewModelTests
{
    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly DateTimeOffset OriginalOccurredAt = new(
        2026,
        7,
        18,
        9,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void VisitCancellationShowsPreservedFactAndStoredMembershipStateChange()
    {
        var visitId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var consumptionId = Guid.NewGuid();
        var cancellationId = Guid.NewGuid();
        var before = new
        {
            Visit = Visit(
                visitId,
                membershipId,
                consumptionId,
                status: "active",
                consumptionStatus: "active"),
            MembershipState = MembershipState(membershipId, -1, 1),
        };
        var after = new
        {
            Cancellation = new
            {
                CancellationId = cancellationId,
                VisitId = visitId,
            },
            Visit = new
            {
                VisitId = visitId,
                Status = "canceled",
                ConsumptionId = consumptionId,
                ConsumptionStatus = "canceled",
            },
            MembershipState = MembershipState(membershipId, 0, 0),
        };

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "visit.canceled",
                    AuditTimelineEntityType.Visit,
                    visitId,
                    before,
                    after)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("visit-canceled", explanation.Kind);
        Assert.Equal("Original visit", explanation.BeforeLabel);
        Assert.Equal("After cancellation", explanation.AfterLabel);
        Assert.Equal("Membership visit", FactValue(explanation.BeforeFacts, "Visit type"));
        Assert.Equal("Active", FactValue(explanation.BeforeFacts, "Consumption"));
        Assert.Equal("-1", FactValue(explanation.BeforeFacts, "Remaining visits"));
        Assert.Equal("Canceled", FactValue(explanation.AfterFacts, "Consumption"));
        Assert.Equal("0", FactValue(explanation.AfterFacts, "Remaining visits"));
        Assert.Equal("Visit status, consumption status, Membership state", explanation.ChangedFields);
    }

    [Fact]
    public void OneOffVisitCancellationDoesNotInventMembershipState()
    {
        var visitId = Guid.NewGuid();
        var before = new
        {
            Visit = Visit(
                visitId,
                membershipId: null,
                consumptionId: null,
                status: "active",
                consumptionStatus: null,
                visitKind: "one_off"),
            MembershipState = (object?)null,
        };
        var after = new
        {
            Cancellation = new
            {
                CancellationId = Guid.NewGuid(),
                VisitId = visitId,
            },
            Visit = new
            {
                VisitId = visitId,
                Status = "canceled",
                ConsumptionId = (Guid?)null,
                ConsumptionStatus = (string?)null,
            },
            MembershipState = (object?)null,
        };

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "visit.canceled",
                    AuditTimelineEntityType.Visit,
                    visitId,
                    before,
                    after)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("One-off visit", FactValue(explanation.BeforeFacts, "Visit type"));
        Assert.Equal("No Membership", FactValue(explanation.BeforeFacts, "Membership"));
        Assert.Equal("Not applicable", FactValue(explanation.AfterFacts, "Consumption"));
        Assert.DoesNotContain(
            explanation.AfterFacts,
            fact => fact.Label == "Remaining visits");
        Assert.Contains("No Membership consumption", explanation.Narrative);
    }

    [Fact]
    public void PaymentCorrectionShowsOriginalReplacementAndChangedFields()
    {
        var originalPaymentId = Guid.NewGuid();
        var replacementPaymentId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var beforePayment = Payment(
            originalPaymentId,
            membershipId,
            amount: 1200m,
            OriginalOccurredAt,
            status: "active");
        var replacementOccurredAt = OriginalOccurredAt.AddHours(1);
        var before = new { Payment = beforePayment };
        var after = new
        {
            Correction = new
            {
                CorrectionId = Guid.NewGuid(),
                OriginalPaymentId = originalPaymentId,
                ReplacementPaymentId = replacementPaymentId,
                ChangedFields = new[] { "amount", "occurred_at", "comment" },
            },
            OriginalPayment = beforePayment with { Status = "replaced" },
            ReplacementPayment = Payment(
                replacementPaymentId,
                membershipId,
                amount: 950m,
                replacementOccurredAt,
                status: "active"),
        };

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "payment.corrected",
                    AuditTimelineEntityType.Payment,
                    originalPaymentId,
                    before,
                    after)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("payment-corrected", explanation.Kind);
        Assert.Equal("1200 UAH", FactValue(explanation.BeforeFacts, "Amount"));
        Assert.Equal("Active", FactValue(explanation.BeforeFacts, "Status"));
        Assert.Equal("950 UAH", FactValue(explanation.AfterFacts, "Amount"));
        Assert.Equal("Replaced", FactValue(explanation.AfterFacts, "Original status"));
        Assert.Equal("Amount, Occurred time, Comment", explanation.ChangedFields);
    }

    [Fact]
    public void PaymentCancellationShowsStatusChangeWithoutRewritingPayment()
    {
        var paymentId = Guid.NewGuid();
        var payment = Payment(
            paymentId,
            Guid.NewGuid(),
            amount: 500m,
            OriginalOccurredAt,
            status: "active");
        var before = new { Payment = payment };
        var after = new
        {
            Cancellation = new
            {
                CancellationId = Guid.NewGuid(),
                PaymentId = paymentId,
            },
            Payment = payment with { Status = "canceled" },
        };

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "payment.canceled",
                    AuditTimelineEntityType.Payment,
                    paymentId,
                    before,
                    after)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("500 UAH", FactValue(explanation.BeforeFacts, "Amount"));
        Assert.Equal("Active", FactValue(explanation.BeforeFacts, "Status"));
        Assert.Equal("500 UAH", FactValue(explanation.AfterFacts, "Amount"));
        Assert.Equal("Canceled", FactValue(explanation.AfterFacts, "Status"));
        Assert.Equal("Payment status", explanation.ChangedFields);
    }

    [Fact]
    public void MembershipTypeEditShowsChangedFutureCatalogValues()
    {
        var membershipTypeId = Guid.NewGuid();
        var original = MembershipType(
            "Eight visits",
            durationDays: 30,
            visitsLimit: 8,
            priceAmount: 1200m,
            comment: "Original catalog values");
        var updated = original with
        {
            Name = "Evening Twelve",
            DurationDays = 45,
            VisitsLimit = 12,
            Price = new MembershipTypePriceAuditFixture(1600.50m, "UAH"),
            Comment = "Future evening sales",
            UpdatedAt = original.UpdatedAt.AddDays(1),
        };

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "membership_type.edited",
                    AuditTimelineEntityType.MembershipType,
                    membershipTypeId,
                    original,
                    updated)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("membership-type-edited", explanation.Kind);
        Assert.Equal("Original catalog", explanation.BeforeLabel);
        Assert.Equal("Updated catalog", explanation.AfterLabel);
        Assert.Equal("Eight visits", FactValue(explanation.BeforeFacts, "Name"));
        Assert.Equal("30 days", FactValue(explanation.BeforeFacts, "Duration"));
        Assert.Equal("1200 UAH", FactValue(explanation.BeforeFacts, "Price"));
        Assert.Equal("Evening Twelve", FactValue(explanation.AfterFacts, "Name"));
        Assert.Equal("45 days", FactValue(explanation.AfterFacts, "Duration"));
        Assert.Equal("1600.5 UAH", FactValue(explanation.AfterFacts, "Price"));
        Assert.Equal("Active", FactValue(explanation.AfterFacts, "Status"));
        Assert.Equal(
            "Name, Duration, Visit limit, Price, Catalog comment",
            explanation.ChangedFields);
    }

    [Fact]
    public void MembershipTypeDeactivationShowsPreservedCatalogAndStatusTransition()
    {
        var membershipTypeId = Guid.NewGuid();
        var original = MembershipType(
            "Eight visits",
            durationDays: 30,
            visitsLimit: 8,
            priceAmount: 1200m,
            comment: null);
        var deactivatedAt = original.UpdatedAt.AddHours(1);
        var deactivated = original with
        {
            IsActive = false,
            UpdatedAt = deactivatedAt,
            DeactivatedAt = deactivatedAt,
        };

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "membership_type.deactivated",
                    AuditTimelineEntityType.MembershipType,
                    membershipTypeId,
                    original,
                    deactivated)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("membership-type-deactivated", explanation.Kind);
        Assert.Equal("Eight visits", FactValue(explanation.BeforeFacts, "Name"));
        Assert.Equal("Active", FactValue(explanation.BeforeFacts, "Status"));
        Assert.Equal("None", FactValue(explanation.BeforeFacts, "Catalog comment"));
        Assert.Equal("Eight visits", FactValue(explanation.AfterFacts, "Name"));
        Assert.Equal("Inactive", FactValue(explanation.AfterFacts, "Status"));
        Assert.Equal("Catalog status", explanation.ChangedFields);
    }

    [Fact]
    public void MembershipTypeEditWithLifecycleMutationFailsClosed()
    {
        var original = MembershipType(
            "Eight visits",
            durationDays: 30,
            visitsLimit: 8,
            priceAmount: 1200m,
            comment: null);
        var deactivatedAt = original.UpdatedAt.AddHours(1);
        var invalidEdit = original with
        {
            Name = "Evening Eight",
            IsActive = false,
            UpdatedAt = deactivatedAt,
            DeactivatedAt = deactivatedAt,
        };

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "membership_type.edited",
                    AuditTimelineEntityType.MembershipType,
                    Guid.NewGuid(),
                    original,
                    invalidEdit)));

        Assert.False(explanation.IsAvailable);
        Assert.Equal("Readable change summary unavailable", explanation.Title);
    }

    [Fact]
    public void NonWorkingDayRangeCorrectionShowsReplacementAndStoredAffectedCounts()
    {
        var fixture = NonWorkingDayAudit(replaceReason: false);

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "non_working_day.corrected",
                    AuditTimelineEntityType.NonWorkingPeriod,
                    fixture.PeriodId,
                    fixture.Before,
                    fixture.CorrectedAfter)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("non-working-day-corrected", explanation.Kind);
        Assert.Equal("Original period", explanation.BeforeLabel);
        Assert.Equal("Replacement period", explanation.AfterLabel);
        Assert.Equal(
            "2026-01-30 to 2026-02-02",
            FactValue(explanation.BeforeFacts, "Period"));
        Assert.Equal("Weather Closure", FactValue(explanation.BeforeFacts, "Reason code"));
        Assert.Equal("2", FactValue(explanation.BeforeFacts, "Affected memberships"));
        Assert.Equal(
            "Date range replaced",
            FactValue(explanation.AfterFacts, "Correction type"));
        Assert.Equal("Corrected", FactValue(explanation.AfterFacts, "Original status"));
        Assert.Equal(
            "2026-02-03 to 2026-02-04",
            FactValue(explanation.AfterFacts, "Period"));
        Assert.Equal("Maintenance", FactValue(explanation.AfterFacts, "Reason code"));
        Assert.Equal("3", FactValue(explanation.AfterFacts, "Affected memberships"));
        Assert.Equal("3 of 3", FactValue(explanation.AfterFacts, "Recalculated memberships"));
        Assert.Equal("Date range, Reason, Affected scope", explanation.ChangedFields);
    }

    [Fact]
    public void NonWorkingDayReasonCorrectionRequiresPreservedRangeAndScope()
    {
        var fixture = NonWorkingDayAudit(replaceReason: true);

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "non_working_day.corrected",
                    AuditTimelineEntityType.NonWorkingPeriod,
                    fixture.PeriodId,
                    fixture.Before,
                    fixture.CorrectedAfter)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal(
            "Reason replaced",
            FactValue(explanation.AfterFacts, "Correction type"));
        Assert.Equal(
            FactValue(explanation.BeforeFacts, "Period"),
            FactValue(explanation.AfterFacts, "Period"));
        Assert.Equal("Corrected Weather", FactValue(explanation.AfterFacts, "Reason code"));
        Assert.Equal("2", FactValue(explanation.AfterFacts, "Affected memberships"));
        Assert.Equal("Reason", explanation.ChangedFields);
    }

    [Fact]
    public void NonWorkingDayCancellationShowsPreservedPeriodAndScopeRemoval()
    {
        var fixture = NonWorkingDayAudit(replaceReason: false);

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "non_working_day.canceled",
                    AuditTimelineEntityType.NonWorkingPeriod,
                    fixture.PeriodId,
                    fixture.CanceledBefore,
                    fixture.CanceledAfter)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("non-working-day-canceled", explanation.Kind);
        Assert.Equal("Original period", explanation.BeforeLabel);
        Assert.Equal("After cancellation", explanation.AfterLabel);
        Assert.Equal("2", FactValue(explanation.BeforeFacts, "Affected memberships"));
        Assert.Equal("Preserved", FactValue(explanation.AfterFacts, "Original fact"));
        Assert.Equal("Canceled", FactValue(explanation.AfterFacts, "Status"));
        Assert.Equal("0", FactValue(explanation.AfterFacts, "Active applications"));
        Assert.Equal("2 of 2", FactValue(explanation.AfterFacts, "Recalculated memberships"));
        Assert.Equal("Period status, Active affected scope", explanation.ChangedFields);
    }

    [Fact]
    public void NonWorkingDayCorrectionWithInconsistentRecalculationFailsClosed()
    {
        var fixture = NonWorkingDayAudit(
            replaceReason: false,
            correctionSucceededCountDelta: -1);

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "non_working_day.corrected",
                    AuditTimelineEntityType.NonWorkingPeriod,
                    fixture.PeriodId,
                    fixture.Before,
                    fixture.CorrectedAfter)));

        Assert.False(explanation.IsAvailable);
        Assert.Equal("Readable change summary unavailable", explanation.Title);
    }

    [Fact]
    public void FreezeCancellationShowsPreservedRangeAndStoredMembershipState()
    {
        var fixture = FreezeCancellationAudit(membershipStateChanges: true);

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "freeze.canceled",
                    AuditTimelineEntityType.Freeze,
                    fixture.FreezeId,
                    fixture.Before,
                    fixture.After)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("freeze-canceled", explanation.Kind);
        Assert.Equal("Original freeze", explanation.BeforeLabel);
        Assert.Equal("After cancellation", explanation.AfterLabel);
        Assert.Equal(
            "2026-02-10 to 2026-02-12",
            FactValue(explanation.BeforeFacts, "Period"));
        Assert.Equal("3", FactValue(explanation.BeforeFacts, "Inclusive days"));
        Assert.Equal("Medical recovery", FactValue(explanation.BeforeFacts, "Freeze reason"));
        Assert.Equal("Active", FactValue(explanation.BeforeFacts, "Status"));
        Assert.Equal("5", FactValue(explanation.BeforeFacts, "Extension days"));
        Assert.Equal("2026-03-05", FactValue(explanation.BeforeFacts, "Effective end"));
        Assert.Equal("Preserved", FactValue(explanation.AfterFacts, "Original fact"));
        Assert.Equal("Canceled", FactValue(explanation.AfterFacts, "Status"));
        Assert.Equal("2", FactValue(explanation.AfterFacts, "Extension days"));
        Assert.Equal("2026-03-02", FactValue(explanation.AfterFacts, "Effective end"));
        Assert.Equal(
            "Freeze status, Membership extension state",
            explanation.ChangedFields);
    }

    [Fact]
    public void FreezeCancellationAllowsUnchangedStateWhenOverlapRemains()
    {
        var fixture = FreezeCancellationAudit(membershipStateChanges: false);

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "freeze.canceled",
                    AuditTimelineEntityType.Freeze,
                    fixture.FreezeId,
                    fixture.Before,
                    fixture.After)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal(
            FactValue(explanation.BeforeFacts, "Extension days"),
            FactValue(explanation.AfterFacts, "Extension days"));
        Assert.Equal(
            FactValue(explanation.BeforeFacts, "Effective end"),
            FactValue(explanation.AfterFacts, "Effective end"));
        Assert.Equal("Freeze status", explanation.ChangedFields);
        Assert.Contains("overlapping active extensions", explanation.Narrative);
    }

    [Fact]
    public void FreezeCancellationWithMismatchedMembershipStateFailsClosed()
    {
        var fixture = FreezeCancellationAudit(
            membershipStateChanges: true,
            mismatchAfterMembership: true);

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "freeze.canceled",
                    AuditTimelineEntityType.Freeze,
                    fixture.FreezeId,
                    fixture.Before,
                    fixture.After)));

        Assert.False(explanation.IsAvailable);
        Assert.Equal("Readable change summary unavailable", explanation.Title);
    }

    [Fact]
    public void ClientUpdateShowsStoredProfileChangesAndDuplicateAcknowledgement()
    {
        var clientId = Guid.NewGuid();
        var matchedClientId = Guid.NewGuid();
        var acknowledgementId = Guid.NewGuid();
        var original = ClientIdentity(
            "Koval",
            "Iryna",
            patronymic: null,
            phone: "067 111 22 33",
            operationalStatus: "active",
            comment: "Prefers morning visits",
            updatedAt: OriginalOccurredAt.AddMinutes(10));
        var updated = new
        {
            Surname = "Kovalchuk",
            Name = "Iryna",
            Patronymic = "Mykolaivna",
            Phone = "+38 (067) 765-43-21",
            OperationalStatus = "inactive",
            Comment = "Paused by Owner request",
            UpdatedAt = OriginalOccurredAt.AddMinutes(10).AddTicks(10),
            DuplicateWarningAcknowledgements = new[]
            {
                new
                {
                    WarningType = "duplicate_phone",
                    MatchedClientId = matchedClientId,
                    Reason = "Confirmed family member",
                },
            },
        };
        var related = new
        {
            DuplicateWarningAcknowledgementIds = new[] { acknowledgementId },
            MatchedClientIds = new[] { matchedClientId },
        };

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "client.updated",
                    AuditTimelineEntityType.Client,
                    clientId,
                    original,
                    updated,
                    related: related)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("client-updated", explanation.Kind);
        Assert.Equal("Original profile", explanation.BeforeLabel);
        Assert.Equal("Updated profile", explanation.AfterLabel);
        Assert.Equal("Koval Iryna", FactValue(explanation.BeforeFacts, "Name"));
        Assert.Equal("Active", FactValue(explanation.BeforeFacts, "Operational status"));
        Assert.Equal(
            "Kovalchuk Iryna Mykolaivna",
            FactValue(explanation.AfterFacts, "Name"));
        Assert.Equal("Inactive", FactValue(explanation.AfterFacts, "Operational status"));
        Assert.Equal("1", FactValue(explanation.AfterFacts, "Warnings acknowledged"));
        Assert.Equal(
            $"Duplicate phone for Client {matchedClientId.ToString("N")[..8]}: Confirmed family member",
            FactValue(explanation.AfterFacts, "Acknowledgement details"));
        Assert.Equal(
            "Name, Phone, Operational status, Comment, Duplicate warnings acknowledged",
            explanation.ChangedFields);
        Assert.Contains("Card assignment is tracked separately", explanation.Narrative);
    }

    [Fact]
    public void ClientUpdateCanRecordAcknowledgementWithoutIdentityFieldChange()
    {
        var clientId = Guid.NewGuid();
        var matchedClientId = Guid.NewGuid();
        var original = ClientIdentity(
            "Koval",
            "Iryna",
            patronymic: null,
            phone: null,
            operationalStatus: "active",
            comment: null,
            updatedAt: OriginalOccurredAt.AddDays(-1));
        var updated = new
        {
            original.Surname,
            original.Name,
            original.Patronymic,
            original.Phone,
            original.OperationalStatus,
            original.Comment,
            UpdatedAt = OriginalOccurredAt.AddMinutes(5),
            DuplicateWarningAcknowledgements = new[]
            {
                new
                {
                    WarningType = "similar_name",
                    MatchedClientId = matchedClientId,
                    Reason = "Identity checked at reception",
                },
            },
        };

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "client.updated",
                    AuditTimelineEntityType.Client,
                    clientId,
                    original,
                    updated,
                    related: new
                    {
                        DuplicateWarningAcknowledgementIds = new[] { Guid.NewGuid() },
                        MatchedClientIds = new[] { matchedClientId },
                    })));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("Duplicate warnings acknowledged", explanation.ChangedFields);
        Assert.Equal("None", FactValue(explanation.BeforeFacts, "Phone"));
        Assert.Equal("None", FactValue(explanation.AfterFacts, "Comment"));
    }

    [Fact]
    public void ClientUpdateWithMismatchedAcknowledgementReferencesFailsClosed()
    {
        var clientId = Guid.NewGuid();
        var matchedClientId = Guid.NewGuid();
        var original = ClientIdentity(
            "Koval",
            "Iryna",
            patronymic: null,
            phone: null,
            operationalStatus: "active",
            comment: null,
            updatedAt: OriginalOccurredAt.AddDays(-1));
        var updated = new
        {
            original.Surname,
            original.Name,
            original.Patronymic,
            original.Phone,
            original.OperationalStatus,
            original.Comment,
            UpdatedAt = OriginalOccurredAt.AddMinutes(5),
            DuplicateWarningAcknowledgements = new[]
            {
                new
                {
                    WarningType = "duplicate_phone",
                    MatchedClientId = matchedClientId,
                    Reason = "Confirmed",
                },
            },
        };

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "client.updated",
                    AuditTimelineEntityType.Client,
                    clientId,
                    original,
                    updated,
                    related: new
                    {
                        DuplicateWarningAcknowledgementIds = Array.Empty<Guid>(),
                        MatchedClientIds = new[] { matchedClientId },
                    })));

        Assert.False(explanation.IsAvailable);
        Assert.Equal("Readable change summary unavailable", explanation.Title);
    }

    [Fact]
    public void CardAssignmentShowsRawCurrentCardAndAssignmentReference()
    {
        var clientId = Guid.NewGuid();
        var assignment = CardAssignment(
            "BL 100-20",
            "BL10020",
            OriginalOccurredAt);

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "card.assigned",
                    AuditTimelineEntityType.Client,
                    clientId,
                    new { },
                    assignment,
                    related: new
                    {
                        PreviousCardAssignmentId = (Guid?)null,
                        CurrentCardAssignmentId = (Guid?)assignment.Id,
                    },
                    reason: null,
                    comment: null)));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("card-assigned", explanation.Kind);
        Assert.Equal("None", FactValue(explanation.BeforeFacts, "Current card"));
        Assert.Equal("BL 100-20", FactValue(explanation.AfterFacts, "Card number"));
        Assert.Equal(assignment.Id.ToString("N")[..8], FactValue(
            explanation.AfterFacts,
            "Assignment"));
        Assert.DoesNotContain(
            explanation.AfterFacts,
            fact => fact.Value == assignment.CardNumberNormalized);
    }

    [Fact]
    public void CardChangeAllowsSameNumberReissueAsNewAssignment()
    {
        var clientId = Guid.NewGuid();
        var original = CardAssignment(
            "BL 100-20",
            "BL10020",
            OriginalOccurredAt.AddDays(-1));
        var replacement = CardAssignment(
            "BL-100 20",
            "BL10020",
            OriginalOccurredAt);

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "card.changed",
                    AuditTimelineEntityType.Client,
                    clientId,
                    original,
                    replacement,
                    related: new
                    {
                        PreviousCardAssignmentId = (Guid?)original.Id,
                        CurrentCardAssignmentId = (Guid?)replacement.Id,
                    })));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("card-changed", explanation.Kind);
        Assert.Equal("BL 100-20", FactValue(explanation.BeforeFacts, "Card number"));
        Assert.Equal("BL-100 20", FactValue(explanation.AfterFacts, "Card number"));
        Assert.Equal("Card assignment", explanation.ChangedFields);
        Assert.Contains("same card number", explanation.Narrative);
    }

    [Fact]
    public void CardClearShowsPreservedPreviousAssignmentAndNoCurrentCard()
    {
        var clientId = Guid.NewGuid();
        var original = CardAssignment(
            "BL 100-20",
            "BL10020",
            OriginalOccurredAt.AddDays(-1));

        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "card.cleared",
                    AuditTimelineEntityType.Client,
                    clientId,
                    original,
                    new { },
                    related: new
                    {
                        PreviousCardAssignmentId = (Guid?)original.Id,
                        CurrentCardAssignmentId = (Guid?)null,
                    })));

        Assert.True(explanation.IsAvailable);
        Assert.Equal("card-cleared", explanation.Kind);
        Assert.Equal("BL 100-20", FactValue(explanation.BeforeFacts, "Card number"));
        Assert.Equal(
            "Preserved in history",
            FactValue(explanation.AfterFacts, "Previous assignment"));
        Assert.Equal("None", FactValue(explanation.AfterFacts, "Current card"));
        Assert.Equal("Current card", explanation.ChangedFields);
    }

    [Theory]
    [InlineData("client.updated", AuditTimelineEntityType.Payment)]
    [InlineData("card.assigned", AuditTimelineEntityType.Payment)]
    [InlineData("card.changed", AuditTimelineEntityType.Payment)]
    [InlineData("card.cleared", AuditTimelineEntityType.Payment)]
    [InlineData("membership_type.edited", AuditTimelineEntityType.Client)]
    [InlineData("membership_type.deactivated", AuditTimelineEntityType.Client)]
    [InlineData("non_working_day.corrected", AuditTimelineEntityType.Payment)]
    [InlineData("non_working_day.canceled", AuditTimelineEntityType.Payment)]
    [InlineData("freeze.canceled", AuditTimelineEntityType.Payment)]
    [InlineData("visit.canceled", AuditTimelineEntityType.Payment)]
    [InlineData("payment.corrected", AuditTimelineEntityType.Visit)]
    [InlineData("payment.canceled", AuditTimelineEntityType.Visit)]
    public void SupportedActionWithWrongEntityFailsClosed(
        string actionType,
        AuditTimelineEntityType entityType)
    {
        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(actionType, entityType, Guid.NewGuid(), new { }, new { })));

        Assert.False(explanation.IsAvailable);
        Assert.Equal("Readable change summary unavailable", explanation.Title);
        Assert.Empty(explanation.BeforeFacts);
        Assert.Empty(explanation.AfterFacts);
    }

    [Fact]
    public void MalformedSupportedSummaryFailsClosed()
    {
        var explanation = Assert.IsType<AuditEntryExplanationViewModel>(
            AuditEntryExplanationViewModel.Create(
                Entry(
                    "payment.corrected",
                    AuditTimelineEntityType.Payment,
                    Guid.NewGuid(),
                    before: "{not-json",
                    after: "{}",
                    serialize: false)));

        Assert.False(explanation.IsAvailable);
        Assert.Equal("The stored business summary could not be displayed safely.", explanation.Narrative);
        Assert.Null(explanation.ChangedFields);
    }

    [Fact]
    public void UnsupportedActionKeepsExistingTimelineRenderingOnly()
    {
        Assert.Null(AuditEntryExplanationViewModel.Create(
            Entry(
                "visit.marked",
                AuditTimelineEntityType.Visit,
                Guid.NewGuid(),
                new { },
                new { })));
    }

    private static AuditTimelineEntry Entry(
        string actionType,
        AuditTimelineEntityType entityType,
        Guid entityId,
        object before,
        object after,
        bool serialize = true,
        object? related = null,
        string? reason = "Correction reason",
        string? comment = "Correction comment")
    {
        return new AuditTimelineEntry(
            AuditEntryId.New(),
            actionType,
            entityType,
            entityId,
            AccountId.New(),
            AccountKind.Owner,
            ActorRole.Owner,
            SessionId.New(),
            "Owner workstation",
            OriginalOccurredAt,
            OriginalOccurredAt.AddMinutes(5),
            EntryOrigin.Normal,
            reason,
            comment,
            related is null ? "{}" : Serialize(related),
            serialize ? Serialize(before) : (string)before,
            serialize ? Serialize(after) : (string)after,
            new RequestCorrelationId("audit-explanation-test"),
            "audit-explanation-key",
            ChangedAfterClose: false);
    }

    private static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, AuditJsonOptions);
    }

    private static object Visit(
        Guid visitId,
        Guid? membershipId,
        Guid? consumptionId,
        string status,
        string? consumptionStatus,
        string visitKind = "membership")
    {
        return new
        {
            VisitId = visitId,
            ClientId = Guid.NewGuid(),
            VisitKind = visitKind,
            MembershipId = membershipId,
            ConsumptionId = consumptionId,
            OccurredAt = OriginalOccurredAt,
            RecordedAt = OriginalOccurredAt.AddMinutes(5),
            EntryOrigin = "normal",
            EntryBatchId = (Guid?)null,
            Comment = "Original Visit",
            Status = status,
            ConsumptionStatus = consumptionStatus,
        };
    }

    private static object MembershipState(
        Guid membershipId,
        int remainingVisits,
        int negativeBalance)
    {
        return new
        {
            MembershipId = membershipId,
            RemainingVisits = remainingVisits,
            NegativeBalance = negativeBalance,
        };
    }

    private static PaymentAuditFixture Payment(
        Guid paymentId,
        Guid? membershipId,
        decimal amount,
        DateTimeOffset occurredAt,
        string status)
    {
        return new PaymentAuditFixture(
            paymentId,
            Guid.NewGuid(),
            membershipId,
            amount,
            "UAH",
            "cash",
            "membership_sale",
            occurredAt,
            occurredAt.AddMinutes(5),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "normal",
            EntryBatchId: null,
            "Payment comment",
            status);
    }

    private static MembershipTypeAuditFixture MembershipType(
        string name,
        int durationDays,
        int visitsLimit,
        decimal priceAmount,
        string? comment)
    {
        return new MembershipTypeAuditFixture(
            name,
            durationDays,
            visitsLimit,
            new MembershipTypePriceAuditFixture(priceAmount, "UAH"),
            IsActive: true,
            comment,
            OriginalOccurredAt.AddDays(-30),
            OriginalOccurredAt.AddDays(-1),
            DeactivatedAt: null);
    }

    private static ClientIdentityAuditFixture ClientIdentity(
        string surname,
        string name,
        string? patronymic,
        string? phone,
        string operationalStatus,
        string? comment,
        DateTimeOffset updatedAt)
    {
        return new ClientIdentityAuditFixture(
            surname,
            name,
            patronymic,
            phone,
            operationalStatus,
            comment,
            updatedAt);
    }

    private static CardAssignmentAuditFixture CardAssignment(
        string cardNumber,
        string cardNumberNormalized,
        DateTimeOffset assignedAt)
    {
        return new CardAssignmentAuditFixture(
            Guid.NewGuid(),
            cardNumber,
            cardNumberNormalized,
            assignedAt);
    }

    private static NonWorkingDayAuditFixture NonWorkingDayAudit(
        bool replaceReason,
        int correctionSucceededCountDelta = 0)
    {
        var periodId = Guid.NewGuid();
        var originalStartDate = new DateOnly(2026, 1, 30);
        var originalEndDate = new DateOnly(2026, 2, 2);
        var replacementStartDate = replaceReason
            ? originalStartDate
            : new DateOnly(2026, 2, 3);
        var replacementEndDate = replaceReason
            ? originalEndDate
            : new DateOnly(2026, 2, 4);
        var firstMembershipId = Guid.NewGuid();
        var secondMembershipId = Guid.NewGuid();
        var thirdMembershipId = Guid.NewGuid();
        var firstClientId = Guid.NewGuid();
        var secondClientId = Guid.NewGuid();
        var thirdClientId = Guid.NewGuid();
        var createdByAccountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var createdAt = OriginalOccurredAt.AddDays(-30);
        var previewedAt = OriginalOccurredAt.AddHours(-1);
        var confirmedAt = OriginalOccurredAt.AddMinutes(5);
        var originalPeriod = new NonWorkingDaySourcePeriodAuditFixture(
            periodId,
            originalStartDate,
            originalEndDate,
            InclusiveDays: 4,
            "weather_closure",
            "Snow closure",
            createdAt,
            createdByAccountId,
            sessionId,
            "active");
        NonWorkingDayBeforeApplicationAuditFixture[] originalApplications =
        [
            BeforeApplication(
                firstMembershipId,
                firstClientId,
                originalStartDate,
                originalEndDate,
                previewedAt,
                confirmedAt),
            BeforeApplication(
                secondMembershipId,
                secondClientId,
                originalStartDate,
                originalEndDate,
                previewedAt,
                confirmedAt),
        ];
        var replacementMembershipIds = replaceReason
            ? new[] { firstMembershipId, secondMembershipId }
            : new[] { firstMembershipId, secondMembershipId, thirdMembershipId };
        var replacementClientIds = replaceReason
            ? new[] { firstClientId, secondClientId }
            : new[] { firstClientId, secondClientId, thirdClientId };
        var replacementApplications = replacementMembershipIds
            .Zip(
                replacementClientIds,
                (membershipId, clientId) => new NonWorkingDayReplacementApplicationAuditFixture(
                    Guid.NewGuid(),
                    membershipId,
                    clientId,
                    replacementStartDate,
                    replacementEndDate))
            .ToArray();
        var replacementPeriod = new NonWorkingDayReplacementPeriodAuditFixture(
            Guid.NewGuid(),
            replacementStartDate,
            replacementEndDate,
            InclusiveDays: replaceReason ? 4 : 2,
            replaceReason ? "corrected_weather" : "maintenance",
            replaceReason ? "Corrected forecast" : "Boiler replacement",
            confirmedAt,
            "active");
        var affectedUnionIds = originalApplications
            .Select(application => application.MembershipId)
            .Concat(replacementMembershipIds)
            .Distinct()
            .ToArray();
        var before = new
        {
            Period = originalPeriod,
            Applications = originalApplications,
            Preview = new
            {
                ConfirmationFingerprint = "non-working-day-test-fingerprint",
                IssuedAt = previewedAt,
                ExpiresAt = confirmedAt.AddMinutes(10),
                OldAffectedCount = originalApplications.Length,
                NewAffectedCount = replacementApplications.Length,
            },
        };
        var canceledBefore = new
        {
            Period = originalPeriod,
            Applications = originalApplications,
            Preview = new
            {
                ConfirmationFingerprint = "non-working-day-cancel-test-fingerprint",
                IssuedAt = previewedAt,
                ExpiresAt = confirmedAt.AddMinutes(10),
                OldAffectedCount = originalApplications.Length,
                NewAffectedCount = 0,
            },
        };
        var correctedAfter = new
        {
            Mode = replaceReason ? "replace_reason" : "replace_range",
            OriginalPeriod = originalPeriod with { Status = "corrected" },
            ReplacementPeriod = replacementPeriod,
            ReplacementApplications = replacementApplications,
            Cancellation = (object?)null,
            OldAffectedCount = originalApplications.Length,
            NewAffectedCount = replacementApplications.Length,
            AffectedUnionCount = affectedUnionIds.Length,
            Recalculation = new
            {
                RequestedCount = affectedUnionIds.Length,
                SucceededCount = affectedUnionIds.Length + correctionSucceededCountDelta,
                MembershipIds = affectedUnionIds,
            },
        };
        var canceledAfter = new
        {
            Mode = "cancel",
            OriginalPeriod = originalPeriod with { Status = "canceled" },
            ReplacementPeriod = (object?)null,
            ReplacementApplications =
                Array.Empty<NonWorkingDayReplacementApplicationAuditFixture>(),
            Cancellation = new
            {
                CancellationId = Guid.NewGuid(),
                NonWorkingPeriodId = periodId,
                Reason = "Correction reason",
                RecordedAt = confirmedAt,
            },
            OldAffectedCount = originalApplications.Length,
            NewAffectedCount = 0,
            AffectedUnionCount = originalApplications.Length,
            Recalculation = new
            {
                RequestedCount = originalApplications.Length,
                SucceededCount = originalApplications.Length,
                MembershipIds = originalApplications
                    .Select(application => application.MembershipId)
                    .ToArray(),
            },
        };
        return new NonWorkingDayAuditFixture(
            periodId,
            before,
            canceledBefore,
            correctedAfter,
            canceledAfter);
    }

    private static FreezeCancellationAuditFixture FreezeCancellationAudit(
        bool membershipStateChanges,
        bool mismatchAfterMembership = false)
    {
        var freezeId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 2, 10);
        var endDate = new DateOnly(2026, 2, 12);
        var original = new FreezeSourceAuditFixture(
            freezeId,
            clientId,
            membershipId,
            startDate,
            endDate,
            InclusiveDays: 3,
            "Medical recovery",
            OriginalOccurredAt.AddDays(-20),
            OriginalOccurredAt.AddDays(-20).AddMinutes(5),
            "normal",
            EntryBatchId: null,
            "active");
        var beforeMembership = new FreezeMembershipStateAuditFixture(
            membershipId,
            clientId,
            RemainingVisits: 7,
            NegativeBalance: 0,
            ExtensionDays: 5,
            new DateOnly(2026, 3, 5),
            ["ending_soon"]);
        var afterMembership = beforeMembership with
        {
            MembershipId = mismatchAfterMembership ? Guid.NewGuid() : membershipId,
            ExtensionDays = membershipStateChanges ? 2 : beforeMembership.ExtensionDays,
            EffectiveEndDate = membershipStateChanges
                ? new DateOnly(2026, 3, 2)
                : beforeMembership.EffectiveEndDate,
        };
        var before = new
        {
            Freeze = original,
            MembershipState = beforeMembership,
        };
        var after = new
        {
            Cancellation = new
            {
                CancellationId = Guid.NewGuid(),
                FreezeId = freezeId,
                Reason = "Correction reason",
                OccurredAt = OriginalOccurredAt,
                RecordedAt = OriginalOccurredAt.AddMinutes(5),
                EntryOrigin = "normal",
                EntryBatchId = (Guid?)null,
                ChangedAfterClose = false,
            },
            Freeze = new
            {
                FreezeId = freezeId,
                ClientId = clientId,
                MembershipId = membershipId,
                StartDate = startDate,
                EndDate = endDate,
                InclusiveDays = 3,
                Reason = "Medical recovery",
                Status = "canceled",
            },
            MembershipState = afterMembership,
        };

        return new FreezeCancellationAuditFixture(freezeId, before, after);
    }

    private static NonWorkingDayBeforeApplicationAuditFixture BeforeApplication(
        Guid membershipId,
        Guid clientId,
        DateOnly startDate,
        DateOnly endDate,
        DateTimeOffset previewedAt,
        DateTimeOffset confirmedAt)
    {
        return new NonWorkingDayBeforeApplicationAuditFixture(
            Guid.NewGuid(),
            membershipId,
            clientId,
            startDate,
            endDate,
            previewedAt,
            confirmedAt,
            "active");
    }

    private static string FactValue(
        IEnumerable<AuditEntryExplanationFactViewModel> facts,
        string label)
    {
        return Assert.Single(facts, fact => fact.Label == label).Value;
    }

    private sealed record PaymentAuditFixture(
        Guid PaymentId,
        Guid ClientId,
        Guid? MembershipId,
        decimal Amount,
        string Currency,
        string Method,
        string PaymentContext,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status);

    private sealed record MembershipTypeAuditFixture(
        string Name,
        int DurationDays,
        int VisitsLimit,
        MembershipTypePriceAuditFixture Price,
        bool IsActive,
        string? Comment,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? DeactivatedAt);

    private sealed record MembershipTypePriceAuditFixture(
        decimal Amount,
        string Currency);

    private sealed record ClientIdentityAuditFixture(
        string Surname,
        string Name,
        string? Patronymic,
        string? Phone,
        string OperationalStatus,
        string? Comment,
        DateTimeOffset UpdatedAt);

    private sealed record CardAssignmentAuditFixture(
        Guid Id,
        string CardNumber,
        string CardNumberNormalized,
        DateTimeOffset AssignedAt);

    private sealed record NonWorkingDayAuditFixture(
        Guid PeriodId,
        object Before,
        object CanceledBefore,
        object CorrectedAfter,
        object CanceledAfter);

    private sealed record NonWorkingDaySourcePeriodAuditFixture(
        Guid PeriodId,
        DateOnly StartDate,
        DateOnly EndDate,
        int InclusiveDays,
        string ReasonCode,
        string? ReasonComment,
        DateTimeOffset CreatedAt,
        Guid CreatedByAccountId,
        Guid SessionId,
        string Status);

    private sealed record NonWorkingDayReplacementPeriodAuditFixture(
        Guid PeriodId,
        DateOnly StartDate,
        DateOnly EndDate,
        int InclusiveDays,
        string ReasonCode,
        string? ReasonComment,
        DateTimeOffset CreatedAt,
        string Status);

    private sealed record NonWorkingDayBeforeApplicationAuditFixture(
        Guid ApplicationId,
        Guid MembershipId,
        Guid ClientId,
        DateOnly StartDate,
        DateOnly EndDate,
        DateTimeOffset PreviewedAt,
        DateTimeOffset ConfirmedAt,
        string Status);

    private sealed record NonWorkingDayReplacementApplicationAuditFixture(
        Guid ApplicationId,
        Guid MembershipId,
        Guid ClientId,
        DateOnly AppliedStartDate,
        DateOnly AppliedEndDate);

    private sealed record FreezeCancellationAuditFixture(
        Guid FreezeId,
        object Before,
        object After);

    private sealed record FreezeSourceAuditFixture(
        Guid FreezeId,
        Guid ClientId,
        Guid MembershipId,
        DateOnly StartDate,
        DateOnly EndDate,
        int InclusiveDays,
        string Reason,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string EntryOrigin,
        Guid? EntryBatchId,
        string Status);

    private sealed record FreezeMembershipStateAuditFixture(
        Guid MembershipId,
        Guid ClientId,
        int RemainingVisits,
        int NegativeBalance,
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        string[] Warnings);
}
