using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

public sealed class NegativeClosurePaymentWriter(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender)
    : INegativeClosurePaymentWriter
{
    public NegativeClosurePaymentWriteResult StageExactClosurePayment(
        CommandEnvelope envelope,
        Guid clientId,
        Guid negativeClosureId,
        Money amount,
        Guid? entryBatchId,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (negativeClosureId == Guid.Empty)
        {
            throw new ArgumentException(
                "Negative closure id is required.",
                nameof(negativeClosureId));
        }

        if (amount.Amount <= 0)
        {
            throw new ArgumentException(
                "Payment amount must be positive.",
                nameof(amount));
        }

        var paymentId = Guid.NewGuid();
        var payment = new PaymentRecord
        {
            Id = paymentId,
            ClientId = clientId,
            MembershipId = null,
            NegativeClosureId = negativeClosureId,
            Amount = amount.Amount,
            Currency = amount.Currency,
            Method = "cash",
            PaymentContext = "negative_closure",
            OccurredAt = envelope.OccurredAt ?? recordedAt,
            RecordedAt = recordedAt,
            RecordedByAccountId = envelope.Actor.AccountId.Value,
            SessionId = envelope.Actor.SessionId.Value,
            EntryOrigin = PaymentCommandSupport.MapEntryOrigin(envelope.EntryOrigin),
            EntryBatchId = entryBatchId,
            Comment = NormalizeOptional(envelope.Comment),
            Status = "active",
        };
        dbContext.Set<PaymentRecord>().Add(payment);

        var auditEntryId = auditAppender.Append(
            envelope,
            PaymentAuditActions.Created,
            PaymentAuditActions.EntityType,
            paymentId,
            recordedAt,
            relatedEntityRefs: new
            {
                ClientId = clientId,
                NegativeClosureId = negativeClosureId,
            },
            afterSummary: new
            {
                Payment = new
                {
                    PaymentId = paymentId,
                    payment.ClientId,
                    payment.NegativeClosureId,
                    payment.Amount,
                    payment.Currency,
                    payment.Method,
                    payment.PaymentContext,
                    payment.OccurredAt,
                    payment.RecordedAt,
                    payment.EntryOrigin,
                    payment.EntryBatchId,
                    payment.Comment,
                    payment.Status,
                },
                Explanation = new
                {
                    Kind = "negative_visit_one_off_closure",
                    NegativeClosureId = negativeClosureId,
                    IsStandalonePayment = false,
                },
            });

        return new NegativeClosurePaymentWriteResult(paymentId, auditEntryId);
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
