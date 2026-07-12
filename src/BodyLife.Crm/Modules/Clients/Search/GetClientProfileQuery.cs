using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record GetClientProfileQuery(
    ActorContext Actor,
    Guid ClientId,
    DateOnly? MembershipAsOfDate = null,
    bool IncludeHistory = false,
    bool IncludeDrillDowns = false)
    : IBodyLifeQuery<GetClientProfileResult>;
