using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public sealed record ClientPaymentRow(
    Guid PaymentId,
    Guid ClientId,
    Guid? MembershipId,
    string? MembershipTypeNameSnapshot,
    Money Amount,
    PaymentMethod Method,
    PaymentContext PaymentContext,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    Guid RecordedByAccountId,
    Guid SessionId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId,
    string? Comment,
    ClientPaymentRowStatus Status,
    ClientPaymentCancellation? Cancellation,
    ClientPaymentCorrection? CorrectionFromOriginal,
    ClientPaymentCorrection? CorrectionToReplacement);
