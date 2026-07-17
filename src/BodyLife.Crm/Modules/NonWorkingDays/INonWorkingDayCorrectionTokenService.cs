namespace BodyLife.Crm.Modules.NonWorkingDays;

public interface INonWorkingDayCorrectionTokenService
{
    NonWorkingDayCorrectionConfirmation Issue(
        NonWorkingDayCorrectionConfirmationMaterial material);

    NonWorkingDayCorrectionTokenValidation Validate(
        string? confirmationToken,
        NonWorkingDayCorrectionConfirmationMaterial currentMaterial);
}
