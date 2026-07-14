using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Visits;

public sealed record GetClientVisitRowsQuery(
    ActorContext Actor,
    Guid ClientId,
    int Limit = 20)
    : IBodyLifeQuery<GetClientVisitRowsResult>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
}
