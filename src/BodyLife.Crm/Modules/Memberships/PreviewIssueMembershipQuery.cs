using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record PreviewIssueMembershipQuery(
    ActorContext Actor,
    Guid ClientId,
    Guid MembershipTypeId,
    DateOnly ProposedStartDate,
    MembershipNegativeHandlingDecision? NegativeHandlingDecision = null,
    int? NegativeCoverageCount = null)
    : IBodyLifeQuery<PreviewIssueMembershipResult>;
