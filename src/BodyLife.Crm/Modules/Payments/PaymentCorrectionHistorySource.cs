using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public sealed record PaymentCorrectionHistorySource(
    Guid CorrectionId,
    Guid ClientId,
    Guid OriginalPaymentId,
    Guid ReplacementPaymentId,
    IReadOnlyList<string> ChangedFields,
    string Reason,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    AccountId RecordedByAccountId,
    SessionId RecordedSessionId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId,
    PaymentHistorySource OriginalPayment,
    PaymentHistorySource ReplacementPayment);
