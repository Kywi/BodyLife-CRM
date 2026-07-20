using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Audit;

public sealed record GetAuditTimelineQuery(
    ActorContext Actor,
    Guid? ClientId = null,
    AuditTimelineEntityType? EntityType = null,
    Guid? EntityId = null,
    DateTimeOffset? RecordedFromInclusive = null,
    DateTimeOffset? RecordedBeforeExclusive = null,
    IReadOnlyCollection<string>? ActionTypes = null,
    int Limit = GetAuditTimelineQuery.DefaultLimit,
    int Offset = 0)
    : IBodyLifeQuery<GetAuditTimelineResult>
{
    public const int DefaultLimit = GetClientAuditEntriesQuery.DefaultLimit;
    public const int MaxLimit = GetClientAuditEntriesQuery.MaxLimit;
    public const int MaxOffset = GetClientAuditEntriesQuery.MaxOffset;
    public const int MaxActionTypeCount = GetClientAuditEntriesQuery.MaxActionTypeCount;
    public const int MaxActionTypeLength = GetClientAuditEntriesQuery.MaxActionTypeLength;
}
