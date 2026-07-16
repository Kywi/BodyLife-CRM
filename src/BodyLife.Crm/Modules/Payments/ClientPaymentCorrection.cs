using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.Payments;

public sealed record ClientPaymentCorrection(
    Guid CorrectionId,
    Guid OriginalPaymentId,
    Guid ReplacementPaymentId,
    IReadOnlyList<string> ChangedFields,
    string Reason,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    Guid RecordedByAccountId,
    Guid SessionId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId);
