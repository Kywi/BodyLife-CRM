using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public sealed record GetClientPaymentHistorySourceRowsQuery(
    ActorContext Actor,
    Guid ClientId,
    DateTimeOffset? OccurredFromInclusive = null,
    DateTimeOffset? OccurredBeforeExclusive = null,
    int Limit = GetClientPaymentHistorySourceRowsQuery.DefaultLimit,
    int Offset = 0,
    IReadOnlyCollection<AuditEntryId>? AuditEntryIds = null)
    : IBodyLifeQuery<GetClientPaymentHistorySourceRowsResult>
{
    public const int DefaultLimit = GetClientAuditEntriesQuery.DefaultLimit;
    public const int MaxLimit = GetClientAuditEntriesQuery.MaxLimit;
    public const int MaxOffset = GetClientAuditEntriesQuery.MaxOffset;
}
