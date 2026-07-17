using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipNonWorkingDayReplacementImpactPreparation
{
    public MembershipNonWorkingDayReplacementImpactPreparation(
        Guid replacedPeriodId,
        IEnumerable<Guid> excludedApplicationIds,
        MembershipNonWorkingDayImpactPreparation replacementImpact)
    {
        if (replacedPeriodId == Guid.Empty)
        {
            throw new ArgumentException(
                "Replaced NonWorkingDay period id is required.",
                nameof(replacedPeriodId));
        }

        ArgumentNullException.ThrowIfNull(excludedApplicationIds);
        ArgumentNullException.ThrowIfNull(replacementImpact);

        var applicationIds = excludedApplicationIds.ToArray();
        if (applicationIds.Any(applicationId => applicationId == Guid.Empty))
        {
            throw new ArgumentException(
                "Excluded NonWorkingDay application ids must be non-empty.",
                nameof(excludedApplicationIds));
        }

        if (applicationIds.Distinct().Count() != applicationIds.Length)
        {
            throw new ArgumentException(
                "Excluded NonWorkingDay application ids must be unique.",
                nameof(excludedApplicationIds));
        }

        if (!applicationIds.SequenceEqual(applicationIds.Order()))
        {
            throw new ArgumentException(
                "Excluded NonWorkingDay application ids must use deterministic order.",
                nameof(excludedApplicationIds));
        }

        ReplacedPeriodId = replacedPeriodId;
        ExcludedApplicationIds = Array.AsReadOnly(applicationIds);
        ReplacementImpact = replacementImpact;
    }

    public Guid ReplacedPeriodId { get; }

    public IReadOnlyList<Guid> ExcludedApplicationIds { get; }

    public MembershipNonWorkingDayImpactPreparation ReplacementImpact { get; }

    public DateRange ReplacementPeriod => ReplacementImpact.Period;

    public MembershipNonWorkingDayAffectedScope AffectedScope =>
        ReplacementImpact.AffectedScope;

    public IReadOnlyList<MembershipNonWorkingDayImpactItem> AffectedMemberships =>
        ReplacementImpact.AffectedMemberships;

    public int AffectedCount => ReplacementImpact.AffectedCount;
}
