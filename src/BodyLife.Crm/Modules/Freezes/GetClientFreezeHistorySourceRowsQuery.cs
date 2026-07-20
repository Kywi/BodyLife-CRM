using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Freezes;

public sealed record GetClientFreezeHistorySourceRowsQuery(
    ActorContext Actor,
    Guid ClientId,
    DateTimeOffset? OccurredFromInclusive = null,
    DateTimeOffset? OccurredBeforeExclusive = null,
    int Limit = GetClientFreezeHistorySourceRowsQuery.DefaultLimit,
    int Offset = 0)
    : IBodyLifeQuery<GetClientFreezeHistorySourceRowsResult>
{
    public const int DefaultLimit = GetClientAuditEntriesQuery.DefaultLimit;
    public const int MaxLimit = GetClientAuditEntriesQuery.MaxLimit;
    public const int MaxOffset = GetClientAuditEntriesQuery.MaxOffset;
}
