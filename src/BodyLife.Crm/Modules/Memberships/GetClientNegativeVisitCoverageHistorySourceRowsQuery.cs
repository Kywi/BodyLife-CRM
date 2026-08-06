using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetClientNegativeVisitCoverageHistorySourceRowsQuery(
    ActorContext Actor,
    Guid ClientId,
    DateTimeOffset? OccurredFromInclusive = null,
    DateTimeOffset? OccurredBeforeExclusive = null,
    int Limit = GetClientNegativeVisitCoverageHistorySourceRowsQuery.DefaultLimit,
    int Offset = 0,
    IReadOnlyCollection<AuditEntryId>? AuditEntryIds = null)
    : IBodyLifeQuery<GetClientNegativeVisitCoverageHistorySourceRowsResult>
{
    public const int DefaultLimit = GetClientAuditEntriesQuery.DefaultLimit;
    public const int MaxLimit = GetClientAuditEntriesQuery.MaxLimit;
    public const int MaxOffset = GetClientAuditEntriesQuery.MaxOffset;
}
