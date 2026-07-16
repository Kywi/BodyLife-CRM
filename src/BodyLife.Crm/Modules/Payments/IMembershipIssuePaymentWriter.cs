using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public interface IMembershipIssuePaymentWriter
{
    MembershipIssuePaymentWriteResult Stage(
        CommandEnvelope envelope,
        Guid clientId,
        Guid membershipId,
        MembershipIssuePayment payment,
        DateTimeOffset recordedAt);
}

public sealed record MembershipIssuePaymentWriteResult(
    Guid PaymentId,
    AuditEntryId AuditEntryId);
