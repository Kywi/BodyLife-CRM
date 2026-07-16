using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Payments;

public sealed class CorrectPaymentCommandContractsTests
{
    private static readonly ActorContext Actor = new(
        AccountId.New(),
        ActorRole.Admin,
        AccountKind.NamedAdmin,
        SessionId.New(),
        "Reception tablet");

    [Fact]
    public void CommandDefinesStableEntityAndCanonicalRereadContracts()
    {
        var command = CreateCommand(PaymentCorrectionMode.Cancel, replacement: null);

        Assert.IsAssignableFrom<IBodyLifeCommand>(command);
        Assert.Equal("payment_correction", CorrectPaymentCommand.CorrectionEntityType);
        Assert.Equal("payment_cancellation", CorrectPaymentCommand.CancellationEntityType);
        Assert.Equal("payment", CorrectPaymentCommand.PaymentEntityType);
        Assert.Equal("client", CorrectPaymentCommand.CanonicalRereadEntityType);
    }

    [Fact]
    public void ReplaceCarriesExplicitReplacementFactAndOperationalEnvelope()
    {
        var membershipId = Guid.NewGuid();
        var replacement = new PaymentReplacement(
            membershipId,
            new Money(900m, "uah"),
            PaymentContext.MembershipSale,
            new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero),
            "Corrected receipt");
        var command = CreateCommand(PaymentCorrectionMode.Replace, replacement);

        Assert.Equal(PaymentCorrectionMode.Replace, command.Mode);
        Assert.Same(replacement, command.Replacement);
        Assert.NotNull(command.Replacement);
        Assert.Equal(membershipId, command.Replacement!.MembershipId);
        Assert.Equal(new Money(900m, "UAH"), command.Replacement.Amount);
        Assert.Equal(PaymentContext.MembershipSale, command.Replacement.PaymentContext);
        Assert.Equal("Correction reason", command.Envelope.Reason);
        Assert.Equal("Correction note", command.Envelope.Comment);
    }

    [Fact]
    public void CancelCarriesNoSyntheticReplacement()
    {
        var command = CreateCommand(PaymentCorrectionMode.Cancel, replacement: null);

        Assert.Equal(PaymentCorrectionMode.Cancel, command.Mode);
        Assert.Null(command.Replacement);
    }

    [Fact]
    public void CorrectionModesAndDayStatusesHaveStableValues()
    {
        Assert.Equal(1, (int)PaymentCorrectionMode.Replace);
        Assert.Equal(2, (int)PaymentCorrectionMode.Cancel);
        Assert.Equal(1, (int)PaymentDayReconciliationStatus.Open);
        Assert.Equal(2, (int)PaymentDayReconciliationStatus.Reconciled);
    }

    [Fact]
    public async Task DayStatusProviderContractReturnsStatusForBusinessDate()
    {
        IPaymentDayReconciliationStatusProvider provider =
            new StubDayReconciliationStatusProvider();
        var businessDate = new DateOnly(2026, 7, 16);

        var status = await provider.GetStatusAsync(businessDate);

        Assert.Equal(PaymentDayReconciliationStatus.Open, status);
        Assert.Equal(businessDate, Assert.Single(
            ((StubDayReconciliationStatusProvider)provider).RequestedDates));
    }

    private static CorrectPaymentCommand CreateCommand(
        PaymentCorrectionMode mode,
        PaymentReplacement? replacement)
    {
        return new CorrectPaymentCommand(
            new CommandEnvelope(
                Actor,
                new RequestCorrelationId("correct-payment-contract"),
                EntryOrigin.Normal,
                new DateTimeOffset(2026, 7, 17, 11, 0, 0, TimeSpan.Zero),
                "correct-payment-contract",
                "Correction reason",
                "Correction note"),
            Guid.NewGuid(),
            mode,
            replacement);
    }

    private sealed class StubDayReconciliationStatusProvider
        : IPaymentDayReconciliationStatusProvider
    {
        public List<DateOnly> RequestedDates { get; } = [];

        public Task<PaymentDayReconciliationStatus> GetStatusAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
        {
            RequestedDates.Add(businessDate);
            return Task.FromResult(PaymentDayReconciliationStatus.Open);
        }
    }
}
