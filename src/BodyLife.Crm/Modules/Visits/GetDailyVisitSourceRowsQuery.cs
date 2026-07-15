using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Visits;

public sealed record GetDailyVisitSourceRowsQuery(
    ActorContext Actor,
    DateOnly BusinessDate)
    : IBodyLifeQuery<GetDailyVisitSourceRowsResult>;
