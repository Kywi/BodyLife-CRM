using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetClientMembershipExtensionExplanationsQuery(
    ActorContext Actor,
    Guid ClientId,
    bool IncludeInactiveSources = false)
    : IBodyLifeQuery<GetClientMembershipExtensionExplanationsResult>;
