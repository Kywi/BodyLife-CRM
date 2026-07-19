using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Reports;

public sealed record ListNegativeClientsQuery(
    ActorContext Actor,
    DateOnly AsOfDate,
    int Limit = GetNegativeMembershipStateRowsQuery.DefaultLimit,
    int Offset = 0)
    : IBodyLifeQuery<ListNegativeClientsResult>;
