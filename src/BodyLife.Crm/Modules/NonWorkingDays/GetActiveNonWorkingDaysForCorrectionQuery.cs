using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record GetActiveNonWorkingDaysForCorrectionQuery(
    ActorContext Actor)
    : IBodyLifeQuery<GetActiveNonWorkingDaysForCorrectionResult>;
