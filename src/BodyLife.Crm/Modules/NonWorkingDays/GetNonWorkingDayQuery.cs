using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record GetNonWorkingDayQuery(
    ActorContext Actor,
    Guid PeriodId)
    : IBodyLifeQuery<GetNonWorkingDayResult>;
