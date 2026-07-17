using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class NonWorkingDayCorrectionConfirmationMaterial
{
    private NonWorkingDayCorrectionConfirmationMaterial(
        NonWorkingDayCorrectionMode mode,
        NonWorkingDayCorrectionSource originalSource,
        NonWorkingDayPreviewInput? replacementInput,
        MembershipNonWorkingDayAffectedScope? confirmedScope)
    {
        if (originalSource.Status != NonWorkingDayCorrectionSourceStatus.Active)
        {
            throw new ArgumentException(
                "Only an active NonWorkingDay source can be confirmed for correction.",
                nameof(originalSource));
        }

        var originalApplicationIds = originalSource.Applications
            .Select(application => application.ApplicationId)
            .Order()
            .ToArray();

        Mode = mode;
        ScopeBehavior = NonWorkingDayCorrectionPolicy.GetScopeBehavior(mode);
        OriginalSource = originalSource;
        OriginalApplicationIds = Array.AsReadOnly(originalApplicationIds);
        ReplacementInput = replacementInput;
        ConfirmedScope = confirmedScope;
    }

    public NonWorkingDayCorrectionMode Mode { get; }

    public NonWorkingDayCorrectionScopeBehavior ScopeBehavior { get; }

    public Guid PeriodId => OriginalSource.PeriodId;

    public NonWorkingDayCorrectionSource OriginalSource { get; }

    public IReadOnlyList<Guid> OriginalApplicationIds { get; }

    public NonWorkingDayPreviewInput? ReplacementInput { get; }

    public MembershipNonWorkingDayAffectedScope? ConfirmedScope { get; }

    public MembershipNonWorkingDayAffectedScope? ReplacementScope =>
        Mode == NonWorkingDayCorrectionMode.ReplaceRange ? ConfirmedScope : null;

    public MembershipNonWorkingDayAffectedScope? PreservedScope =>
        Mode == NonWorkingDayCorrectionMode.ReplaceReason ? ConfirmedScope : null;

    public static NonWorkingDayCorrectionConfirmationMaterial ForReplaceRange(
        NonWorkingDayCorrectionSource originalSource,
        NonWorkingDayPreviewInput replacementInput,
        MembershipNonWorkingDayReplacementImpactPreparation replacementImpact)
    {
        ArgumentNullException.ThrowIfNull(originalSource);
        ArgumentNullException.ThrowIfNull(replacementInput);
        ArgumentNullException.ThrowIfNull(replacementImpact);

        if (replacementImpact.ReplacedPeriodId != originalSource.PeriodId)
        {
            throw new ArgumentException(
                "Replacement impact must target the original NonWorkingDay period.",
                nameof(replacementImpact));
        }

        if (replacementImpact.ReplacementPeriod != replacementInput.Period)
        {
            throw new ArgumentException(
                "Replacement impact period must match replacement input.",
                nameof(replacementImpact));
        }

        var originalApplicationIds = originalSource.Applications
            .Select(application => application.ApplicationId)
            .Order();
        if (!replacementImpact.ExcludedApplicationIds.SequenceEqual(
                originalApplicationIds))
        {
            throw new ArgumentException(
                "Replacement impact must exclude the exact original application set.",
                nameof(replacementImpact));
        }

        return new NonWorkingDayCorrectionConfirmationMaterial(
            NonWorkingDayCorrectionMode.ReplaceRange,
            originalSource,
            replacementInput,
            replacementImpact.AffectedScope);
    }

    public static NonWorkingDayCorrectionConfirmationMaterial ForReplaceReason(
        NonWorkingDayCorrectionSource originalSource,
        NonWorkingDayPreviewInput replacementInput)
    {
        ArgumentNullException.ThrowIfNull(originalSource);
        ArgumentNullException.ThrowIfNull(replacementInput);

        if (replacementInput.Period != originalSource.Period)
        {
            throw new ArgumentException(
                "Reason-only replacement must preserve the original period.",
                nameof(replacementInput));
        }

        var preservedScope = new MembershipNonWorkingDayAffectedScope(
            originalSource.Period,
            originalSource.Applications.Select(application =>
                new MembershipNonWorkingDayAffectedScopeItem(
                    application.MembershipId,
                    application.ClientId,
                    application.AppliedRange)));

        return new NonWorkingDayCorrectionConfirmationMaterial(
            NonWorkingDayCorrectionMode.ReplaceReason,
            originalSource,
            replacementInput,
            preservedScope);
    }

    public static NonWorkingDayCorrectionConfirmationMaterial ForCancel(
        NonWorkingDayCorrectionSource originalSource)
    {
        ArgumentNullException.ThrowIfNull(originalSource);

        return new NonWorkingDayCorrectionConfirmationMaterial(
            NonWorkingDayCorrectionMode.Cancel,
            originalSource,
            replacementInput: null,
            confirmedScope: null);
    }
}
