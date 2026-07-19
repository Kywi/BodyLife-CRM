using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Reports;

public sealed record ListLowRemainingMembershipsQuery(
    ActorContext Actor,
    DateOnly AsOfDate,
    int RemainingVisitsThreshold =
        GetLowRemainingMembershipStateRowsQuery.DefaultRemainingVisitsThreshold,
    int Limit = GetLowRemainingMembershipStateRowsQuery.DefaultLimit,
    int Offset = 0)
    : IBodyLifeQuery<ListLowRemainingMembershipsResult>;
