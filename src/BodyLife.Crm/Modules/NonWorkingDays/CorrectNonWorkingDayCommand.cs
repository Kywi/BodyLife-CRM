using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record CorrectNonWorkingDayCommand(
    CommandEnvelope Envelope,
    Guid PeriodId,
    NonWorkingDayCorrectionMode Mode,
    DateOnly? ReplacementStartDate,
    DateOnly? ReplacementEndDate,
    string? ReplacementReasonCode,
    string? ReplacementReasonComment,
    string? ConfirmationToken)
    : IBodyLifeCommand
{
    public const string PeriodEntityType = "non_working_period";
    public const string CancellationEntityType = "non_working_period_cancellation";
    public const string MembershipEntityType = "membership";
    public const string CanonicalRereadEntityType = "non_working_period";

    public EntityId SourcePeriodEntityId =>
        new(PeriodEntityType, PeriodId);

    public EntityId CanonicalRereadTargetId =>
        new(CanonicalRereadEntityType, PeriodId);
}
