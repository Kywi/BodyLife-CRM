using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Audit;

public sealed record GetClientAuditEntriesQuery(
    ActorContext Actor,
    Guid ClientId,
    DateTimeOffset? OccurredFromInclusive = null,
    DateTimeOffset? OccurredBeforeExclusive = null,
    IReadOnlyCollection<ClientAuditEntityFilter>? EntityFilters = null,
    int Limit = GetClientAuditEntriesQuery.DefaultLimit,
    int Offset = 0)
    : IBodyLifeQuery<GetClientAuditEntriesResult>
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
    public const int MaxOffset = 10_000;
}
