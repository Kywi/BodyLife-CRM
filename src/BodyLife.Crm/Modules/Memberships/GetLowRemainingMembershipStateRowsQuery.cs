using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetLowRemainingMembershipStateRowsQuery(
    ActorContext Actor,
    DateOnly AsOfDate,
    int RemainingVisitsThreshold =
        GetLowRemainingMembershipStateRowsQuery.DefaultRemainingVisitsThreshold,
    int Limit = 50,
    int Offset = 0)
    : IBodyLifeQuery<GetLowRemainingMembershipStateRowsResult>
{
    public const int DefaultRemainingVisitsThreshold =
        MembershipWarningRules.LowRemainingVisitsThreshold;
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
    public const int MaxOffset = 10_000;
}
