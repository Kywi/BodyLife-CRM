using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Visits;

public sealed record GetMarkVisitOptionsQuery(
    ActorContext Actor,
    Guid ClientId,
    DateTimeOffset OccurredAt)
    : IBodyLifeQuery<GetMarkVisitOptionsResult>;
