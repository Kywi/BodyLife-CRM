using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Reports;

public sealed record ListEndingSoonMembershipsQuery(
    ActorContext Actor,
    DateOnly AsOfDate,
    int DaysThreshold = GetEndingSoonMembershipStateRowsQuery.DefaultDaysThreshold,
    int Limit = GetEndingSoonMembershipStateRowsQuery.DefaultLimit,
    int Offset = 0)
    : IBodyLifeQuery<ListEndingSoonMembershipsResult>;
