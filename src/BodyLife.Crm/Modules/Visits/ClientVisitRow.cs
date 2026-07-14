using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;

namespace BodyLife.Crm.Modules.Visits;

public sealed record ClientVisitRow(
    Guid VisitId,
    Guid ClientId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    Guid RecordedByAccountId,
    Guid SessionId,
    VisitKind VisitKind,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId,
    string? Comment,
    ClientVisitRowStatus Status,
    ClientVisitConsumption? Consumption,
    ClientVisitCancellation? Cancellation,
    QueryPermissionSet AllowedActions);
