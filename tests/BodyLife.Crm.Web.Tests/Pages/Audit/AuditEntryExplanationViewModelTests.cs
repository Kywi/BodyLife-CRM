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

    [Theory]
    [InlineData("membership_type.edited", AuditTimelineEntityType.Client)]
    [InlineData("membership_type.deactivated", AuditTimelineEntityType.Client)]
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
        bool serialize = true)
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
            "Correction reason",
            "Correction comment",
            "{}",
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
}
