using BodyLife.Crm.Modules.NonWorkingDays;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class CorrectNonWorkingDaySourcePreparationResult
{
    private CorrectNonWorkingDaySourcePreparationResult(
        CorrectNonWorkingDaySourcePreparationStatus status,
        Guid periodId,
        NonWorkingDayCorrectionMode mode,
        NonWorkingDayCorrectionSource? source)
    {
        Status = status;
        PeriodId = periodId;
        Mode = mode;
        ScopeBehavior = NonWorkingDayCorrectionPolicy.GetScopeBehavior(mode);
        Source = source;
    }

    public CorrectNonWorkingDaySourcePreparationStatus Status { get; }

    public Guid PeriodId { get; }

    public NonWorkingDayCorrectionMode Mode { get; }

    public NonWorkingDayCorrectionScopeBehavior ScopeBehavior { get; }

    public NonWorkingDayCorrectionSource? Source { get; }

    public bool IsPrepared =>
        Status == CorrectNonWorkingDaySourcePreparationStatus.Prepared;

    internal static CorrectNonWorkingDaySourcePreparationResult Prepared(
        NonWorkingDayCorrectionMode mode,
        NonWorkingDayCorrectionSource source)
    {
        return new CorrectNonWorkingDaySourcePreparationResult(
            CorrectNonWorkingDaySourcePreparationStatus.Prepared,
            source.PeriodId,
            mode,
            source);
    }

    internal static CorrectNonWorkingDaySourcePreparationResult NotFound(
        Guid periodId,
        NonWorkingDayCorrectionMode mode)
    {
        return new CorrectNonWorkingDaySourcePreparationResult(
            CorrectNonWorkingDaySourcePreparationStatus.NotFound,
            periodId,
            mode,
            source: null);
    }

    internal static CorrectNonWorkingDaySourcePreparationResult AlreadyCanceled(
        NonWorkingDayCorrectionMode mode,
        NonWorkingDayCorrectionSource source)
    {
        return new CorrectNonWorkingDaySourcePreparationResult(
            CorrectNonWorkingDaySourcePreparationStatus.AlreadyCanceled,
            source.PeriodId,
            mode,
            source);
    }

    internal static CorrectNonWorkingDaySourcePreparationResult AlreadyCorrected(
        NonWorkingDayCorrectionMode mode,
        NonWorkingDayCorrectionSource source)
    {
        return new CorrectNonWorkingDaySourcePreparationResult(
            CorrectNonWorkingDaySourcePreparationStatus.AlreadyCorrected,
            source.PeriodId,
            mode,
            source);
    }

    internal static CorrectNonWorkingDaySourcePreparationResult InconsistentSource(
        Guid periodId,
        NonWorkingDayCorrectionMode mode)
    {
        return new CorrectNonWorkingDaySourcePreparationResult(
            CorrectNonWorkingDaySourcePreparationStatus.InconsistentSource,
            periodId,
            mode,
            source: null);
    }
}
