using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetMembershipStateQuery(
    ActorContext Actor,
    Guid MembershipId,
    DateOnly AsOfDate)
    : IBodyLifeQuery<GetMembershipStateResult>;
