using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record PreviewCorrectNonWorkingDayQuery(
    ActorContext Actor,
    Guid PeriodId,
    NonWorkingDayCorrectionMode Mode,
    DateOnly? ReplacementStartDate = null,
    DateOnly? ReplacementEndDate = null,
    string? ReplacementReasonCode = null,
    string? ReplacementReasonComment = null)
    : IBodyLifeQuery<PreviewCorrectNonWorkingDayResult>;
