using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record GetNonWorkingDayCorrectionOutcomeQuery(
    ActorContext Actor,
    Guid OriginalPeriodId,
    Guid AuditEntryId)
    : IBodyLifeQuery<GetNonWorkingDayCorrectionOutcomeResult>;
