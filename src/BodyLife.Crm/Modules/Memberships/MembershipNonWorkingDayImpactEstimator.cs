using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipNonWorkingDayImpactEstimator
{
    public static MembershipNonWorkingDayImpactEstimate Estimate(
        MembershipCalculatedState currentState,
        MembershipExtensionCalculation? currentDateRangeExtensions,
        DateRange proposedPeriod)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        if (proposedPeriod.StartDate == default || proposedPeriod.EndDate == default)
        {
            throw new ArgumentException(
                "Proposed NonWorkingDay period dates are required.",
                nameof(proposedPeriod));
        }

        if (currentDateRangeExtensions is not null
            && currentState.ExtensionDays < currentDateRangeExtensions.ExtensionDays)
        {
            throw new ArgumentException(
                "Current Membership state cannot contain fewer extension days than "
                + "its active date-range source union.",
                nameof(currentDateRangeExtensions));
        }

        var overlappingActiveDays = currentDateRangeExtensions?.ExplanationDays
            .Where(day => day.IsActive && proposedPeriod.Contains(day.ExtensionDate))
            .ToArray()
            ?? [];
        var existingOverlapDays = overlappingActiveDays
            .Select(day => day.ExtensionDate)
            .Distinct()
            .Count();
        var addedUniqueExtensionDays = proposedPeriod.InclusiveDays - existingOverlapDays;
        var estimatedExtensionDays = (long)currentState.ExtensionDays
            + addedUniqueExtensionDays;
        var estimatedEffectiveEndDayNumber = (long)currentState.EffectiveEndDate.DayNumber
            + addedUniqueExtensionDays;
        if (estimatedExtensionDays > int.MaxValue
            || estimatedEffectiveEndDayNumber > DateOnly.MaxValue.DayNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(proposedPeriod),
                proposedPeriod,
                "Proposed NonWorkingDay impact exceeds the supported calendar range.");
        }

        var warnings = overlappingActiveDays
            .GroupBy(day => new
            {
                day.SourceType,
                day.SourceId,
                day.SourceLabel,
            })
            .Select(group =>
            {
                var dates = group
                    .Select(day => day.ExtensionDate)
                    .Distinct()
                    .Order()
                    .ToArray();
                return new MembershipNonWorkingDayOverlapWarning(
                    group.Key.SourceType,
                    group.Key.SourceId,
                    group.Key.SourceLabel,
                    new DateRange(dates[0], dates[^1]),
                    dates.Length);
            })
            .OrderBy(warning => warning.SourceType, StringComparer.Ordinal)
            .ThenBy(warning => warning.SourceId)
            .ThenBy(warning => warning.SourceLabel, StringComparer.Ordinal)
            .ToArray();

        return new MembershipNonWorkingDayImpactEstimate(
            currentState.ExtensionDays,
            currentState.EffectiveEndDate,
            (int)estimatedExtensionDays,
            DateOnly.FromDayNumber((int)estimatedEffectiveEndDayNumber),
            addedUniqueExtensionDays,
            existingOverlapDays,
            warnings);
    }
}

public sealed class MembershipNonWorkingDayImpactEstimate
{
    internal MembershipNonWorkingDayImpactEstimate(
        int beforeExtensionDays,
        DateOnly beforeEffectiveEndDate,
        int estimatedAfterExtensionDays,
        DateOnly estimatedAfterEffectiveEndDate,
        int addedUniqueExtensionDays,
        int existingOverlapDays,
        IEnumerable<MembershipNonWorkingDayOverlapWarning> overlapWarnings)
    {
        BeforeExtensionDays = beforeExtensionDays;
        BeforeEffectiveEndDate = beforeEffectiveEndDate;
        EstimatedAfterExtensionDays = estimatedAfterExtensionDays;
        EstimatedAfterEffectiveEndDate = estimatedAfterEffectiveEndDate;
        AddedUniqueExtensionDays = addedUniqueExtensionDays;
        ExistingOverlapDays = existingOverlapDays;
        OverlapWarnings = Array.AsReadOnly(overlapWarnings.ToArray());
    }

    public int BeforeExtensionDays { get; }

    public DateOnly BeforeEffectiveEndDate { get; }

    public int EstimatedAfterExtensionDays { get; }

    public DateOnly EstimatedAfterEffectiveEndDate { get; }

    public int AddedUniqueExtensionDays { get; }

    public int ExistingOverlapDays { get; }

    public IReadOnlyList<MembershipNonWorkingDayOverlapWarning> OverlapWarnings { get; }

    public bool HasOverlapWarnings => OverlapWarnings.Count > 0;
}

public sealed class MembershipNonWorkingDayOverlapWarning
{
    internal MembershipNonWorkingDayOverlapWarning(
        string sourceType,
        Guid sourceId,
        string sourceLabel,
        DateRange overlapRange,
        int overlapDays)
    {
        SourceType = sourceType;
        SourceId = sourceId;
        SourceLabel = sourceLabel;
        OverlapRange = overlapRange;
        OverlapDays = overlapDays;
    }

    public string SourceType { get; }

    public Guid SourceId { get; }

    public string SourceLabel { get; }

    public DateRange OverlapRange { get; }

    public int OverlapDays { get; }
}
