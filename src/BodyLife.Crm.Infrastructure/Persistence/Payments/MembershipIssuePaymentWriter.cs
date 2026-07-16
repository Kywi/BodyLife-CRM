using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Modules.Payments;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

public sealed class MembershipIssuePaymentWriter(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender)
    : IMembershipIssuePaymentWriter
{
    public MembershipIssuePaymentWriteResult Stage(
        CommandEnvelope envelope,
        Guid clientId,
        Guid membershipId,
        MembershipIssuePayment payment,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(payment);

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException("Membership id is required.", nameof(membershipId));
        }

        if (payment.PaymentContext != PaymentContext.MembershipSale)
        {
            throw new ArgumentException(
                "IssueMembership accepts only membership-sale Payment context.",
                nameof(payment));
        }

        if (payment.Amount.Amount <= 0)
        {
            throw new ArgumentException(
                "Payment amount must be greater than zero.",
                nameof(payment));
        }

        var paymentId = Guid.NewGuid();
        var paymentRecord = new PaymentRecord
        {
            Id = paymentId,
            ClientId = clientId,
            MembershipId = membershipId,
            Amount = payment.Amount.Amount,
            Currency = payment.Amount.Currency,
            Method = "cash",
            PaymentContext = PaymentCommandSupport.MapPaymentContext(
                payment.PaymentContext),
            OccurredAt = envelope.OccurredAt?.ToUniversalTime() ?? recordedAt,
            RecordedAt = recordedAt,
            RecordedByAccountId = envelope.Actor.AccountId.Value,
            SessionId = envelope.Actor.SessionId.Value,
            EntryOrigin = PaymentCommandSupport.MapEntryOrigin(envelope.EntryOrigin),
            EntryBatchId = null,
            Comment = NormalizeOptional(envelope.Comment),
            Status = "active",
        };
        dbContext.Set<PaymentRecord>().Add(paymentRecord);

        var auditEntryId = auditAppender.Append(
            envelope,
            PaymentAuditActions.Created,
            PaymentAuditActions.EntityType,
            paymentId,
            recordedAt,
            relatedEntityRefs: new
            {
                ClientId = clientId,
                MembershipId = membershipId,
            },
            afterSummary: new
            {
                Payment = new
                {
                    PaymentId = paymentId,
                    paymentRecord.ClientId,
                    paymentRecord.MembershipId,
                    paymentRecord.Amount,
                    paymentRecord.Currency,
                    paymentRecord.Method,
                    paymentRecord.PaymentContext,
                    paymentRecord.OccurredAt,
                    paymentRecord.RecordedAt,
                    paymentRecord.EntryOrigin,
                    paymentRecord.EntryBatchId,
                    paymentRecord.Comment,
                    paymentRecord.Status,
                },
            });

        return new MembershipIssuePaymentWriteResult(paymentId, auditEntryId);
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
