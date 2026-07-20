using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Visits;

public sealed record GetClientVisitHistorySourceRowsQuery(
    ActorContext Actor,
    Guid ClientId,
    DateTimeOffset? OccurredFromInclusive = null,
    DateTimeOffset? OccurredBeforeExclusive = null,
    int Limit = GetClientVisitHistorySourceRowsQuery.DefaultLimit,
    int Offset = 0,
    IReadOnlyCollection<AuditEntryId>? AuditEntryIds = null)
    : IBodyLifeQuery<GetClientVisitHistorySourceRowsResult>
{
    public const int DefaultLimit = GetClientAuditEntriesQuery.DefaultLimit;
    public const int MaxLimit = GetClientAuditEntriesQuery.MaxLimit;
    public const int MaxOffset = GetClientAuditEntriesQuery.MaxOffset;
}
