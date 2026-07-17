using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal static class NonWorkingDayImpactPreviewMapper
{
    internal static IReadOnlyList<NonWorkingDayImpactMembershipPreview> Map(
        MembershipNonWorkingDayImpactPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        return preparation.AffectedMemberships
            .Select(item => new NonWorkingDayImpactMembershipPreview(
                item.MembershipId,
                item.ClientId,
                item.AppliedRange,
                item.Estimate.BeforeExtensionDays,
                item.Estimate.BeforeEffectiveEndDate,
                item.Estimate.EstimatedAfterExtensionDays,
                item.Estimate.EstimatedAfterEffectiveEndDate,
                item.Estimate.AddedUniqueExtensionDays,
                item.Estimate.ExistingOverlapDays,
                item.Estimate.OverlapWarnings.Select(warning =>
                    new NonWorkingDayImpactOverlapWarning(
                        warning.SourceType,
                        warning.SourceId,
                        warning.SourceLabel,
                        warning.OverlapRange,
                        warning.OverlapDays))))
            .ToArray();
    }
}
