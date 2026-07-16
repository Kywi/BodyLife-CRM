using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public sealed record GetClientPaymentRowsQuery(
    ActorContext Actor,
    Guid ClientId,
    int Limit = 20)
    : IBodyLifeQuery<GetClientPaymentRowsResult>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
}
