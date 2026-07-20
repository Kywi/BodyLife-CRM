using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Visits;

public sealed record MarkedVisitHistorySource(
    Guid VisitId,
    Guid ClientId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    AccountId RecordedByAccountId,
    SessionId RecordedSessionId,
    VisitKind VisitKind,
    Guid? EntryBatchId,
    string? Comment,
    ClientVisitRowStatus CurrentStatus,
    ClientVisitConsumption? CurrentConsumption,
    Guid? CurrentCancellationId);
