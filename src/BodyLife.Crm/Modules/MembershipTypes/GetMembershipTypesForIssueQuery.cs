using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.MembershipTypes;

public sealed record GetMembershipTypesForIssueQuery(
    ActorContext Actor,
    bool IncludeInactive = false)
    : IBodyLifeQuery<GetMembershipTypesForIssueResult>;
