using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Visits;

public sealed record VisitCancellationHistorySource(
    Guid CancellationId,
    Guid VisitId,
    Guid ClientId,
    string Reason,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    AccountId RecordedByAccountId,
    SessionId RecordedSessionId,
    Guid? EntryBatchId);
