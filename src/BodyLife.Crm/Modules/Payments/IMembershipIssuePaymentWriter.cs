using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public interface IMembershipIssuePaymentWriter
{
    MembershipIssuePaymentWriteResult StageExactSale(
        CommandEnvelope envelope,
        Guid clientId,
        Guid membershipId,
        Money amount,
        Guid? entryBatchId,
        DateTimeOffset recordedAt,
        bool changedAfterClose = false);
}

public sealed record MembershipIssuePaymentWriteResult(
    Guid PaymentId,
    AuditEntryId AuditEntryId);
