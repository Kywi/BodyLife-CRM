using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal static class NonWorkingDayImpactPreviewMapper
{
    internal static IReadOnlyList<NonWorkingDayImpactMembershipPreview> Map(
        MembershipNonWorkingDayImpactPreparation preparation,
        IReadOnlyDictionary<Guid, string> clientDisplayNames)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(clientDisplayNames);

        return preparation.AffectedMemberships
            .Select(item => MapMembership(item, clientDisplayNames))
            .ToArray();
    }

    private static NonWorkingDayImpactMembershipPreview MapMembership(
        MembershipNonWorkingDayImpactItem item,
        IReadOnlyDictionary<Guid, string> clientDisplayNames)
    {
        if (!clientDisplayNames.TryGetValue(item.ClientId, out var clientDisplayName))
        {
            throw new InvalidOperationException(
                $"Client {item.ClientId} is missing from the NonWorkingDay preview projection.");
        }

        return new NonWorkingDayImpactMembershipPreview(
            item.MembershipId,
            item.ClientId,
            clientDisplayName,
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
                    warning.OverlapDays)));
    }
}
