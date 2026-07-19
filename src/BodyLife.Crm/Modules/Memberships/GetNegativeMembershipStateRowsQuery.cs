using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetNegativeMembershipStateRowsQuery(
    ActorContext Actor,
    DateOnly AsOfDate,
    int Limit = GetNegativeMembershipStateRowsQuery.DefaultLimit,
    int Offset = 0)
    : IBodyLifeQuery<GetNegativeMembershipStateRowsResult>
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
    public const int MaxOffset = 10_000;
}
