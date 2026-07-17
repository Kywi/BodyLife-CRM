using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class CorrectNonWorkingDayPreparation
{
    internal CorrectNonWorkingDayPreparation(
        CommandEnvelope envelope,
        Guid periodId,
        NonWorkingDayCorrectionMode mode,
        DateRange? replacementPeriod,
        string? replacementReasonCode,
        string? replacementReasonComment,
        string confirmationToken)
    {
        Envelope = envelope;
        PeriodId = periodId;
        Mode = mode;
        ReplacementPeriod = replacementPeriod;
        ReplacementReasonCode = replacementReasonCode;
        ReplacementReasonComment = replacementReasonComment;
        ConfirmationToken = confirmationToken;
    }

    public CommandEnvelope Envelope { get; }

    public Guid PeriodId { get; }

    public NonWorkingDayCorrectionMode Mode { get; }

    public DateRange? ReplacementPeriod { get; }

    public string? ReplacementReasonCode { get; }

    public string? ReplacementReasonComment { get; }

    public string ConfirmationToken { get; }

    public EntityId SourcePeriodEntityId =>
        new(CorrectNonWorkingDayCommand.PeriodEntityType, PeriodId);

    public EntityId CanonicalRereadTargetId =>
        new(CorrectNonWorkingDayCommand.CanonicalRereadEntityType, PeriodId);
}
