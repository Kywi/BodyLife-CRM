using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class CorrectNonWorkingDayCommandRevalidationResult
{
    private CorrectNonWorkingDayCommandRevalidationResult(
        CorrectNonWorkingDayPreparation? commandPreparation,
        NonWorkingDayCorrectionConfirmationMaterial? confirmationMaterial,
        MembershipNonWorkingDayReplacementImpactPreparation? replacementImpact,
        NonWorkingDayCorrectionTokenValidation? tokenValidation,
        IReadOnlyList<CommandError> errors)
    {
        CommandPreparation = commandPreparation;
        ConfirmationMaterial = confirmationMaterial;
        ReplacementImpact = replacementImpact;
        TokenValidation = tokenValidation;
        Errors = errors;
    }

    public CorrectNonWorkingDayPreparation? CommandPreparation { get; }

    public NonWorkingDayCorrectionConfirmationMaterial? ConfirmationMaterial { get; }

    public MembershipNonWorkingDayReplacementImpactPreparation? ReplacementImpact
    {
        get;
    }

    public NonWorkingDayCorrectionTokenValidation? TokenValidation { get; }

    public IReadOnlyList<CommandError> Errors { get; }

    public bool IsPrepared => ConfirmationMaterial is not null;

    internal static CorrectNonWorkingDayCommandRevalidationResult Prepared(
        CorrectNonWorkingDayPreparation commandPreparation,
        NonWorkingDayCorrectionConfirmationMaterial confirmationMaterial,
        MembershipNonWorkingDayReplacementImpactPreparation? replacementImpact,
        NonWorkingDayCorrectionTokenValidation tokenValidation)
    {
        ArgumentNullException.ThrowIfNull(commandPreparation);
        ArgumentNullException.ThrowIfNull(confirmationMaterial);
        ArgumentNullException.ThrowIfNull(tokenValidation);

        if (!tokenValidation.IsValid)
        {
            throw new ArgumentException(
                "Only a valid correction confirmation can be prepared.",
                nameof(tokenValidation));
        }

        if (confirmationMaterial.PeriodId != commandPreparation.PeriodId
            || confirmationMaterial.Mode != commandPreparation.Mode)
        {
            throw new ArgumentException(
                "Correction confirmation material must match the prepared command.",
                nameof(confirmationMaterial));
        }

        if ((commandPreparation.Mode == NonWorkingDayCorrectionMode.ReplaceRange)
            != (replacementImpact is not null))
        {
            throw new ArgumentException(
                "Only range replacement revalidation can carry replacement impact.",
                nameof(replacementImpact));
        }

        return new CorrectNonWorkingDayCommandRevalidationResult(
            commandPreparation,
            confirmationMaterial,
            replacementImpact,
            tokenValidation,
            errors: []);
    }

    internal static CorrectNonWorkingDayCommandRevalidationResult Rejected(
        CommandErrorCode code,
        string message,
        string? field = null)
    {
        return new CorrectNonWorkingDayCommandRevalidationResult(
            commandPreparation: null,
            confirmationMaterial: null,
            replacementImpact: null,
            tokenValidation: null,
            Array.AsReadOnly([new CommandError(code, message, field)]));
    }
}
