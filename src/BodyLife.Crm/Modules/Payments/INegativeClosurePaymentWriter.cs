using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;
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
        DateTimeOffset recordedAt,
        PaperFallbackEntryRowReference? paperReference = null,
        Guid? coverageCorrectionId = null,
        bool changedAfterClose = false);
}

public sealed record NegativeClosurePaymentWriteResult(
    Guid PaymentId,
    AuditEntryId AuditEntryId);
