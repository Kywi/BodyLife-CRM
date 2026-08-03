using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

public sealed class MembershipIssuePaymentWriter(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender)
    : IMembershipIssuePaymentWriter
{
    public MembershipIssuePaymentWriteResult StageExactSale(
        CommandEnvelope envelope,
        Guid clientId,
        Guid membershipId,
        Money amount,
        Guid? entryBatchId,
        DateTimeOffset recordedAt,
        bool changedAfterClose = false)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException("Membership id is required.", nameof(membershipId));
        }

        if (amount.Amount <= 0)
        {
            throw new ArgumentException(
                "Payment amount must be greater than zero.",
                nameof(amount));
        }

        var paymentId = Guid.NewGuid();
        var paymentRecord = new PaymentRecord
        {
            Id = paymentId,
            ClientId = clientId,
            MembershipId = membershipId,
            Amount = amount.Amount,
            Currency = amount.Currency,
            Method = "cash",
            PaymentContext = "membership_sale",
            OccurredAt = envelope.OccurredAt?.ToUniversalTime() ?? recordedAt,
            RecordedAt = recordedAt,
            RecordedByAccountId = envelope.Actor.AccountId.Value,
            SessionId = envelope.Actor.SessionId.Value,
            EntryOrigin = PaymentCommandSupport.MapEntryOrigin(envelope.EntryOrigin),
            EntryBatchId = entryBatchId,
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
            },
            changedAfterClose: changedAfterClose);

        return new MembershipIssuePaymentWriteResult(paymentId, auditEntryId);
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
