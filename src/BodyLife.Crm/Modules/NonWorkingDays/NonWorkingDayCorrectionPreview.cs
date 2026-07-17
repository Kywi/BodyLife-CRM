using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class NonWorkingDayCorrectionPreview
{
    public NonWorkingDayCorrectionPreview(
        NonWorkingDayCorrectionConfirmationMaterial material,
        IEnumerable<NonWorkingDayImpactMembershipPreview> replacementImpact,
        NonWorkingDayCorrectionConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(replacementImpact);
        ArgumentNullException.ThrowIfNull(confirmation);

        var impactItems = replacementImpact.ToArray();
        if (impactItems.Any(item => item is null))
        {
            throw new ArgumentException(
                "Correction preview cannot contain a missing replacement impact item.",
                nameof(replacementImpact));
        }

        if (material.Mode == NonWorkingDayCorrectionMode.ReplaceRange)
        {
            EnsureExactReplacementImpact(material.ReplacementScope, impactItems);
        }
        else if (impactItems.Length != 0)
        {
            throw new ArgumentException(
                "Only a range replacement can expose replacement impact estimates.",
                nameof(replacementImpact));
        }

        Material = material;
        ReplacementImpact = Array.AsReadOnly(impactItems);
        Confirmation = confirmation;
    }

    public NonWorkingDayCorrectionConfirmationMaterial Material { get; }

    public Guid PeriodId => Material.PeriodId;

    public NonWorkingDayCorrectionMode Mode => Material.Mode;

    public NonWorkingDayCorrectionScopeBehavior ScopeBehavior => Material.ScopeBehavior;

    public NonWorkingDayCorrectionSource OriginalSource => Material.OriginalSource;

    public int OriginalAffectedCount => OriginalSource.Applications.Count;

    public NonWorkingDayPreviewInput? ReplacementInput => Material.ReplacementInput;

    public MembershipNonWorkingDayAffectedScope? ConfirmedScope => Material.ConfirmedScope;

    public int ConfirmedAffectedCount => ConfirmedScope?.AffectedCount ?? 0;

    public IReadOnlyList<NonWorkingDayImpactMembershipPreview> ReplacementImpact { get; }

    public NonWorkingDayCorrectionConfirmation Confirmation { get; }

    private static void EnsureExactReplacementImpact(
        MembershipNonWorkingDayAffectedScope? replacementScope,
        IReadOnlyList<NonWorkingDayImpactMembershipPreview> impactItems)
    {
        if (replacementScope is null || replacementScope.AffectedCount != impactItems.Count)
        {
            throw new ArgumentException(
                "Range correction impact must match the exact confirmed replacement scope.",
                nameof(impactItems));
        }

        for (var index = 0; index < impactItems.Count; index++)
        {
            var scopeItem = replacementScope.AffectedMemberships[index];
            var impactItem = impactItems[index];
            if (impactItem.MembershipId != scopeItem.MembershipId
                || impactItem.ClientId != scopeItem.ClientId
                || impactItem.AppliedRange != scopeItem.AppliedRange)
            {
                throw new ArgumentException(
                    "Range correction impact must preserve confirmed scope order and identities.",
                    nameof(impactItems));
            }
        }
    }
}
