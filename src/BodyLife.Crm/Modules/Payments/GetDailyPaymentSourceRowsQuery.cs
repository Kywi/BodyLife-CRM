using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public sealed record GetDailyPaymentSourceRowsQuery(
    ActorContext Actor,
    DateOnly BusinessDate)
    : IBodyLifeQuery<GetDailyPaymentSourceRowsResult>;
