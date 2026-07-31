using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetClientNegativeVisitCoverageQuery(
    ActorContext Actor,
    Guid ClientId)
    : IBodyLifeQuery<GetClientNegativeVisitCoverageResult>;
