using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using Microsoft.EntityFrameworkCore.Storage;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class CorrectNonWorkingDayCommandRevalidationPreparer(
    BodyLifeDbContext dbContext,
    IMembershipNonWorkingDayReplacementImpactPreparer replacementImpactPreparer,
    CorrectNonWorkingDaySourcePreparer sourcePreparer,
    INonWorkingDayCorrectionTokenService correctionTokenService,
    TimeProvider timeProvider)
{
    public async Task<CorrectNonWorkingDayCommandRevalidationResult> PrepareAsync(
        CorrectNonWorkingDayPreparation commandPreparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandPreparation);
        EnsureConsistentTransaction();

        if (!await NonWorkingDayQuerySupport.IsOwnerAuthorizedAsync(
                dbContext,
                commandPreparation.Envelope.Actor,
                timeProvider.GetUtcNow().ToUniversalTime(),
                cancellationToken))
        {
            return CorrectNonWorkingDayCommandRevalidationResult.Rejected(
                CommandErrorCode.PermissionDenied,
                "The Owner account or session is not active.");
        }

        try
        {
            NonWorkingDayPreviewInput? replacementInput = null;
            MembershipNonWorkingDayReplacementImpactPreparation? replacementImpact =
                null;
            if (commandPreparation.Mode == NonWorkingDayCorrectionMode.ReplaceRange)
            {
                var replacementPeriod = commandPreparation.ReplacementPeriod
                    ?? throw new InvalidOperationException(
                        "Prepared range replacement period is missing.");
                replacementInput = CreateReplacementInput(
                    replacementPeriod,
                    commandPreparation);

                // ADR-016: lock every active replacement candidate before old source rows.
                replacementImpact = await replacementImpactPreparer
                    .PrepareReplacementImpactAsync(
                        commandPreparation.PeriodId,
                        replacementPeriod,
                        cancellationToken);
            }

            var sourcePreparation = await sourcePreparer.PrepareAsync(
                commandPreparation.PeriodId,
                commandPreparation.Mode,
                cancellationToken);
            var sourceError = MapSourceError(sourcePreparation.Status);
            if (sourceError is not null)
            {
                return sourceError;
            }

            var source = sourcePreparation.Source
                ?? throw new InvalidOperationException(
                    "Prepared NonWorkingDay correction source is missing.");
            if (commandPreparation.Mode == NonWorkingDayCorrectionMode.ReplaceReason)
            {
                replacementInput = CreateReplacementInput(
                    source.Period,
                    commandPreparation);
            }

            var material = NonWorkingDayCorrectionConfirmationMaterialFactory.Create(
                commandPreparation.Mode,
                source,
                replacementInput,
                replacementImpact);
            var tokenValidation = correctionTokenService.Validate(
                commandPreparation.ConfirmationToken,
                material);
            var tokenError = MapTokenError(tokenValidation);
            if (tokenError is not null)
            {
                return tokenError;
            }

            return CorrectNonWorkingDayCommandRevalidationResult.Prepared(
                commandPreparation,
                material,
                replacementImpact,
                tokenValidation);
        }
        catch (ArgumentException exception)
            when (NonWorkingDayCommandSupport.FindPostgresException(exception) is null)
        {
            return RecalculationFailed();
        }
        catch (InvalidOperationException exception)
            when (NonWorkingDayCommandSupport.FindPostgresException(exception) is null)
        {
            return RecalculationFailed();
        }
    }

    private void EnsureConsistentTransaction()
    {
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "CorrectNonWorkingDay command revalidation requires a caller-owned "
                + "consistent database transaction.");
        var isolationLevel = transaction.GetDbTransaction().IsolationLevel;
        if (isolationLevel is not IsolationLevel.RepeatableRead
            and not IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "CorrectNonWorkingDay command revalidation requires RepeatableRead "
                + "or Serializable transaction isolation.");
        }
    }

    private static NonWorkingDayPreviewInput CreateReplacementInput(
        SharedKernel.DateRange period,
        CorrectNonWorkingDayPreparation commandPreparation)
    {
        return new NonWorkingDayPreviewInput(
            period,
            commandPreparation.ReplacementReasonCode
                ?? throw new InvalidOperationException(
                    "Prepared replacement reason code is missing."),
            commandPreparation.ReplacementReasonComment);
    }

    private static CorrectNonWorkingDayCommandRevalidationResult? MapSourceError(
        CorrectNonWorkingDaySourcePreparationStatus status)
    {
        return status switch
        {
            CorrectNonWorkingDaySourcePreparationStatus.Prepared => null,
            CorrectNonWorkingDaySourcePreparationStatus.NotFound =>
                CorrectNonWorkingDayCommandRevalidationResult.Rejected(
                    CommandErrorCode.NotFound,
                    "NonWorkingDay period was not found.",
                    "periodId"),
            CorrectNonWorkingDaySourcePreparationStatus.AlreadyCanceled =>
                CorrectNonWorkingDayCommandRevalidationResult.Rejected(
                    CommandErrorCode.AlreadyCanceled,
                    "NonWorkingDay period is already canceled.",
                    "periodId"),
            CorrectNonWorkingDaySourcePreparationStatus.AlreadyCorrected =>
                CorrectNonWorkingDayCommandRevalidationResult.Rejected(
                    CommandErrorCode.StaleState,
                    "NonWorkingDay period was already corrected. Refresh canonical state.",
                    "periodId"),
            CorrectNonWorkingDaySourcePreparationStatus.InconsistentSource =>
                RecalculationFailed(),
            _ => RecalculationFailed(),
        };
    }

    private static CorrectNonWorkingDayCommandRevalidationResult? MapTokenError(
        NonWorkingDayCorrectionTokenValidation validation)
    {
        return validation.Status switch
        {
            NonWorkingDayCorrectionTokenValidationStatus.Valid => null,
            NonWorkingDayCorrectionTokenValidationStatus.Expired =>
                CorrectNonWorkingDayCommandRevalidationResult.Rejected(
                    CommandErrorCode.PreviewExpired,
                    "The NonWorkingDay correction preview has expired. Create a new preview before confirming.",
                    "confirmationToken"),
            NonWorkingDayCorrectionTokenValidationStatus
                .ConfirmationMaterialMismatch =>
                CorrectNonWorkingDayCommandRevalidationResult.Rejected(
                    CommandErrorCode.AffectedScopeChanged,
                    "The NonWorkingDay correction source or affected Membership scope changed after preview. Review and confirm a new preview.",
                    "confirmationToken"),
            NonWorkingDayCorrectionTokenValidationStatus.InvalidToken =>
                CorrectNonWorkingDayCommandRevalidationResult.Rejected(
                    CommandErrorCode.ValidationFailed,
                    "The correction confirmation token is invalid.",
                    "confirmationToken"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(validation),
                validation.Status,
                "Correction token validation status is not supported."),
        };
    }

    private static CorrectNonWorkingDayCommandRevalidationResult
        RecalculationFailed()
    {
        return CorrectNonWorkingDayCommandRevalidationResult.Rejected(
            CommandErrorCode.RecalculationFailed,
            "Canonical NonWorkingDay correction scope could not be prepared.");
    }
}
