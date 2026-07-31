using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public interface INegativeClosurePaymentWriter
{
    NegativeClosurePaymentWriteResult StageExactClosurePayment(
        CommandEnvelope envelope,
        Guid clientId,
        Guid negativeClosureId,
        Money amount,
        Guid? entryBatchId,
        DateTimeOffset recordedAt);
}

public sealed record NegativeClosurePaymentWriteResult(
    Guid PaymentId,
    AuditEntryId AuditEntryId);
