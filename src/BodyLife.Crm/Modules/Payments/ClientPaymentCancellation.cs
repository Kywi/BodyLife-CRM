using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.Payments;

public sealed record ClientPaymentCancellation(
    Guid CancellationId,
    string Reason,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    Guid RecordedByAccountId,
    Guid SessionId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId);
