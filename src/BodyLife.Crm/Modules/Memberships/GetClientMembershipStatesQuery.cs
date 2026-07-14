using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetClientMembershipStatesQuery(
    ActorContext Actor,
    Guid ClientId,
    DateOnly AsOfDate)
    : IBodyLifeQuery<GetClientMembershipStatesResult>;
