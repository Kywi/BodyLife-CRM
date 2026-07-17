using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipNonWorkingDayReplacementImpactPreparer(
    MembershipNonWorkingDayAffectedScopePreparer affectedScopePreparer,
    IMembershipNonWorkingDayApplicationSourceProvider applicationSourceProvider)
    : IMembershipNonWorkingDayReplacementImpactPreparer
{
    public async Task<MembershipNonWorkingDayReplacementImpactPreparation>
        PrepareReplacementImpactAsync(
            Guid replacedPeriodId,
            DateRange replacementPeriod,
            CancellationToken cancellationToken = default)
    {
        if (replacedPeriodId == Guid.Empty)
        {
            throw new ArgumentException(
                "Replaced NonWorkingDay period id is required.",
                nameof(replacedPeriodId));
        }

        if (replacementPeriod.StartDate == default
            || replacementPeriod.EndDate == default)
        {
            throw new ArgumentException(
                "Replacement NonWorkingDay period dates are required.",
                nameof(replacementPeriod));
        }

        affectedScopePreparer.EnsureConsistentTransaction();

        var applicationIds = await applicationSourceProvider
            .GetApplicationIdsForPeriodAsync(
                replacedPeriodId,
                cancellationToken);
        if (applicationIds is null)
        {
            throw new InvalidOperationException(
                "NonWorkingDay application source provider returned no collection.");
        }

        var excludedApplicationIds = applicationIds.ToArray();
        if (excludedApplicationIds.Any(applicationId => applicationId == Guid.Empty)
            || excludedApplicationIds.Distinct().Count()
                != excludedApplicationIds.Length
            || !excludedApplicationIds.SequenceEqual(
                excludedApplicationIds.Order()))
        {
            throw new InvalidOperationException(
                "NonWorkingDay application source provider returned invalid or "
                + "non-deterministic identities.");
        }

        // The read above takes no row lock. The shared preparer now locks every
        // active candidate before canonical source rows are locked and filtered.
        var replacementImpact = await affectedScopePreparer
            .PrepareReplacementImpactAsync(
                replacementPeriod,
                excludedApplicationIds.ToHashSet(),
                cancellationToken);

        return new MembershipNonWorkingDayReplacementImpactPreparation(
            replacedPeriodId,
            excludedApplicationIds,
            replacementImpact);
    }
}
