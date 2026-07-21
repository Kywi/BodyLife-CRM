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

    [Theory]
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
}
