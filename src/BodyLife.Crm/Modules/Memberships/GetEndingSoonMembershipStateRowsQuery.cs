using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetEndingSoonMembershipStateRowsQuery(
    ActorContext Actor,
    DateOnly AsOfDate,
    int DaysThreshold = GetEndingSoonMembershipStateRowsQuery.DefaultDaysThreshold,
    int Limit = 50,
    int Offset = 0)
    : IBodyLifeQuery<GetEndingSoonMembershipStateRowsResult>
{
    public const int DefaultDaysThreshold = MembershipWarningRules.EndingSoonDaysThreshold;
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
    public const int MaxOffset = 10_000;
    public const int MaxDaysThreshold = 365;
}
