using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record PreviewIssueMembershipQuery(
    ActorContext Actor,
    Guid ClientId,
    Guid MembershipTypeId,
    DateOnly ProposedStartDate)
    : IBodyLifeQuery<PreviewIssueMembershipResult>;
