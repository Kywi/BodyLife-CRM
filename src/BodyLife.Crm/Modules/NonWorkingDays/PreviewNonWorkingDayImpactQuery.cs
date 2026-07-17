using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record PreviewNonWorkingDayImpactQuery(
    ActorContext Actor,
    DateOnly ProposedStartDate,
    DateOnly ProposedEndDate,
    string? ReasonCode,
    string? ReasonComment = null)
    : IBodyLifeQuery<PreviewNonWorkingDayImpactResult>;
