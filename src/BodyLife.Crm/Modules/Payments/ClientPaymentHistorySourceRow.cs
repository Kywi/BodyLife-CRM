using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;

namespace BodyLife.Crm.Modules.Payments;

public sealed record ClientPaymentHistorySourceRow(
    ClientPaymentHistorySourceKind Kind,
    Guid ClientId,
    Guid PaymentId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    EntryOrigin EntryOrigin,
    PaymentHistorySource? CreatedPayment,
    PaymentCorrectionHistorySource? Correction,
    PaymentCancellationHistorySource? Cancellation,
    ClientAuditEntry AuditEntry);
