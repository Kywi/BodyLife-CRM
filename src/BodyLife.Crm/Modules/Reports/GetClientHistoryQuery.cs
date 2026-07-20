using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Reports;

public sealed record GetClientHistoryQuery(
    ActorContext Actor,
    Guid ClientId,
    DateTimeOffset? OccurredFromInclusive = null,
    DateTimeOffset? OccurredBeforeExclusive = null,
    IReadOnlyCollection<ClientHistoryEntityFilter>? EntityFilters = null,
    int Limit = GetClientHistoryQuery.DefaultLimit,
    int Offset = 0)
    : IBodyLifeQuery<GetClientHistoryResult>
{
    public const int DefaultLimit = GetClientAuditEntriesQuery.DefaultLimit;
    public const int MaxLimit = GetClientAuditEntriesQuery.MaxLimit;
    public const int MaxOffset = GetClientAuditEntriesQuery.MaxOffset;
}
