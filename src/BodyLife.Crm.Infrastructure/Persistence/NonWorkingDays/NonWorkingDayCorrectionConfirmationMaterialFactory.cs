using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal static class NonWorkingDayCorrectionConfirmationMaterialFactory
{
    internal static NonWorkingDayCorrectionConfirmationMaterial Create(
        NonWorkingDayCorrectionMode mode,
        NonWorkingDayCorrectionSource source,
        NonWorkingDayPreviewInput? replacementInput,
        MembershipNonWorkingDayReplacementImpactPreparation? replacementImpact)
    {
        ArgumentNullException.ThrowIfNull(source);

        return mode switch
        {
            NonWorkingDayCorrectionMode.ReplaceRange =>
                NonWorkingDayCorrectionConfirmationMaterial.ForReplaceRange(
                    source,
                    replacementInput
                        ?? throw new InvalidOperationException(
                            "Range replacement input is missing."),
                    replacementImpact
                        ?? throw new InvalidOperationException(
                            "Range replacement impact is missing.")),
            NonWorkingDayCorrectionMode.ReplaceReason =>
                NonWorkingDayCorrectionConfirmationMaterial.ForReplaceReason(
                    source,
                    new NonWorkingDayPreviewInput(
                        source.Period,
                        replacementInput?.ReasonCode
                            ?? throw new InvalidOperationException(
                                "Reason replacement input is missing."),
                        replacementInput.ReasonComment)),
            NonWorkingDayCorrectionMode.Cancel =>
                NonWorkingDayCorrectionConfirmationMaterial.ForCancel(source),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
