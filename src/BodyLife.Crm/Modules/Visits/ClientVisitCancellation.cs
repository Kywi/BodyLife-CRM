using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.Visits;

public sealed record ClientVisitCancellation(
    Guid CancellationId,
    string Reason,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    Guid RecordedByAccountId,
    Guid SessionId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId);
