using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.NonWorkingDays;

namespace BodyLife.Crm.Web.Pages.Owner;

public sealed record NonWorkingDayCorrectionWorkspaceViewModel(
    IReadOnlyList<ActiveNonWorkingDayForCorrection> ActivePeriods,
    NonWorkingDayCorrectionPreviewFormInput Input,
    IReadOnlyList<NonWorkingDayCorrectionPreviewFormError> Errors,
    string? ErrorHeading,
    NonWorkingDayCorrectionPreview? Preview,
    NonWorkingDayCanonicalPeriod? CanonicalSource,
    string? IdempotencyKey,
    NonWorkingDayCanonicalCorrection? ConfirmedCorrection)
{
    public const int ReplacementReasonCodeMaxLength =
        NonWorkingDayPreviewInput.ReasonCodeMaxLength;
    public const int ReplacementReasonCommentMaxLength =
        NonWorkingDayPreviewInput.ReasonCommentMaxLength;
    public const int CorrectionReasonMaxLength = 1000;
    public const int CorrectionCommentMaxLength = 1000;

    public static NonWorkingDayCorrectionWorkspaceViewModel Empty { get; } = new(
        ActivePeriods: [],
        new NonWorkingDayCorrectionPreviewFormInput(),
        Errors: [],
        ErrorHeading: null,
        Preview: null,
        CanonicalSource: null,
        IdempotencyKey: null,
        ConfirmedCorrection: null);

    public bool HasActivePeriods => ActivePeriods.Count > 0;

    public static NonWorkingDayCorrectionWorkspaceViewModel FromList(
        GetActiveNonWorkingDaysForCorrectionResult result,
        Guid? selectedPeriodId = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status != GetActiveNonWorkingDaysForCorrectionStatus.Success)
        {
            return new NonWorkingDayCorrectionWorkspaceViewModel(
                ActivePeriods: [],
                new NonWorkingDayCorrectionPreviewFormInput(),
                [new NonWorkingDayCorrectionPreviewFormError(
                    Field: null,
                    result.ErrorMessage
                        ?? "Active non-working periods could not be loaded.")],
                ErrorHeading: "Correction periods unavailable",
                Preview: null,
                CanonicalSource: null,
                IdempotencyKey: null,
                ConfirmedCorrection: null);
        }

        var items = result.Items.ToArray();
        var selected = selectedPeriodId is { } requestedId
            ? items.FirstOrDefault(item => item.PeriodId == requestedId)
            : null;
        selected ??= items.FirstOrDefault();

        return new NonWorkingDayCorrectionWorkspaceViewModel(
            Array.AsReadOnly(items),
            InputFor(selected),
            Errors: [],
            ErrorHeading: null,
            Preview: null,
            CanonicalSource: null,
            IdempotencyKey: null,
            ConfirmedCorrection: null);
    }

    public static NonWorkingDayCorrectionWorkspaceViewModel FromValidationErrors(
        GetActiveNonWorkingDaysForCorrectionResult activePeriods,
        NonWorkingDayCorrectionPreviewFormInput submittedInput,
        IReadOnlyList<NonWorkingDayCorrectionPreviewFormError> errors)
    {
        ArgumentNullException.ThrowIfNull(activePeriods);
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(errors);

        return Failure(
            activePeriods,
            Copy(submittedInput),
            errors,
            "Correction preview not created");
    }

    public static NonWorkingDayCorrectionWorkspaceViewModel FromPreviewResult(
        GetActiveNonWorkingDaysForCorrectionResult activePeriods,
        NonWorkingDayCorrectionPreviewFormInput submittedInput,
        PreviewCorrectNonWorkingDayResult result,
        GetNonWorkingDayResult? canonicalResult)
    {
        ArgumentNullException.ThrowIfNull(activePeriods);
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(result);

        if (TryGetCanonicalPreview(
                result,
                canonicalResult,
                out var preview,
                out var canonicalSource))
        {
            return SuccessfulPreview(
                activePeriods,
                submittedInput,
                preview!,
                canonicalSource!,
                Errors: [],
                errorHeading: null);
        }

        return Failure(
            activePeriods,
            Copy(submittedInput),
            [PreviewOrCanonicalError(result, canonicalResult)],
            "Correction preview not created");
    }

    public static NonWorkingDayCorrectionWorkspaceViewModel
        FromConfirmationRefresh(
            GetActiveNonWorkingDaysForCorrectionResult activePeriods,
            NonWorkingDayCorrectionConfirmationFormInput submittedInput,
            PreviewCorrectNonWorkingDayResult refreshedPreview,
            GetNonWorkingDayResult? canonicalResult,
            IReadOnlyList<CommandError> commandErrors)
    {
        ArgumentNullException.ThrowIfNull(activePeriods);
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(refreshedPreview);
        ArgumentNullException.ThrowIfNull(commandErrors);

        var errors = commandErrors.Select(CommandError).ToList();
        if (TryGetCanonicalPreview(
                refreshedPreview,
                canonicalResult,
                out var preview,
                out var canonicalSource))
        {
            return SuccessfulPreview(
                activePeriods,
                Copy(submittedInput),
                preview!,
                canonicalSource!,
                errors.AsReadOnly(),
                "Correction not applied");
        }

        errors.Add(PreviewOrCanonicalError(refreshedPreview, canonicalResult));
        return Failure(
            activePeriods,
            Copy(submittedInput),
            errors.AsReadOnly(),
            "Correction not applied");
    }

    public static NonWorkingDayCorrectionWorkspaceViewModel
        FromConfirmationFailure(
            GetActiveNonWorkingDaysForCorrectionResult activePeriods,
            NonWorkingDayCorrectionConfirmationFormInput submittedInput,
            IReadOnlyList<CommandError> commandErrors)
    {
        ArgumentNullException.ThrowIfNull(activePeriods);
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(commandErrors);
        return Failure(
            activePeriods,
            Copy(submittedInput),
            commandErrors.Select(CommandError).ToArray(),
            "Correction not applied");
    }

    public static NonWorkingDayCorrectionWorkspaceViewModel FromCanonicalOutcome(
        GetActiveNonWorkingDaysForCorrectionResult activePeriods,
        GetNonWorkingDayCorrectionOutcomeResult result)
    {
        ArgumentNullException.ThrowIfNull(activePeriods);
        ArgumentNullException.ThrowIfNull(result);

        if (result is
            {
                Status: GetNonWorkingDayCorrectionOutcomeStatus.Success,
                Correction: { } correction,
            })
        {
            var items = SuccessfulItems(activePeriods);
            var selectedId = correction.ReplacementPeriod?.PeriodId;
            var selected = selectedId is { } replacementId
                ? items.FirstOrDefault(item => item.PeriodId == replacementId)
                : items.FirstOrDefault();
            return new NonWorkingDayCorrectionWorkspaceViewModel(
                items,
                InputFor(selected),
                Errors: [],
                ErrorHeading: null,
                Preview: null,
                CanonicalSource: null,
                IdempotencyKey: null,
                correction);
        }

        return new NonWorkingDayCorrectionWorkspaceViewModel(
            SuccessfulItems(activePeriods),
            InputFor(SuccessfulItems(activePeriods).FirstOrDefault()),
            [CorrectionOutcomeError(result)],
            ErrorHeading: "Correction result unavailable",
            Preview: null,
            CanonicalSource: null,
            IdempotencyKey: null,
            ConfirmedCorrection: null);
    }

    private static NonWorkingDayCorrectionWorkspaceViewModel SuccessfulPreview(
        GetActiveNonWorkingDaysForCorrectionResult activePeriods,
        NonWorkingDayCorrectionPreviewFormInput submittedInput,
        NonWorkingDayCorrectionPreview preview,
        NonWorkingDayCanonicalPeriod canonicalSource,
        IReadOnlyList<NonWorkingDayCorrectionPreviewFormError> Errors,
        string? errorHeading)
    {
        return new NonWorkingDayCorrectionWorkspaceViewModel(
            SuccessfulItems(activePeriods),
            CanonicalInput(submittedInput, preview),
            Errors,
            errorHeading,
            preview,
            canonicalSource,
            Guid.NewGuid().ToString("N"),
            ConfirmedCorrection: null);
    }

    private static NonWorkingDayCorrectionWorkspaceViewModel Failure(
        GetActiveNonWorkingDaysForCorrectionResult activePeriods,
        NonWorkingDayCorrectionPreviewFormInput input,
        IReadOnlyList<NonWorkingDayCorrectionPreviewFormError> errors,
        string errorHeading)
    {
        return new NonWorkingDayCorrectionWorkspaceViewModel(
            SuccessfulItems(activePeriods),
            input,
            errors,
            errorHeading,
            Preview: null,
            CanonicalSource: null,
            IdempotencyKey: null,
            ConfirmedCorrection: null);
    }

    private static bool TryGetCanonicalPreview(
        PreviewCorrectNonWorkingDayResult result,
        GetNonWorkingDayResult? canonicalResult,
        out NonWorkingDayCorrectionPreview? preview,
        out NonWorkingDayCanonicalPeriod? canonicalSource)
    {
        preview = result.Preview;
        canonicalSource = canonicalResult?.Period;
        return result.Status == PreviewCorrectNonWorkingDayStatus.Success
            && preview is not null
            && canonicalResult?.Status == GetNonWorkingDayStatus.Success
            && canonicalSource is not null
            && IsSameOriginalSource(preview.OriginalSource, canonicalSource);
    }

    private static IReadOnlyList<ActiveNonWorkingDayForCorrection> SuccessfulItems(
        GetActiveNonWorkingDaysForCorrectionResult result)
    {
        return result.Status == GetActiveNonWorkingDaysForCorrectionStatus.Success
            ? result.Items.ToArray()
            : [];
    }

    private static NonWorkingDayCorrectionPreviewFormInput InputFor(
        ActiveNonWorkingDayForCorrection? selected)
    {
        return new NonWorkingDayCorrectionPreviewFormInput
        {
            PeriodId = selected?.PeriodId,
            Mode = NonWorkingDayCorrectionMode.ReplaceRange,
            ReplacementStartDate = selected?.Period.StartDate,
            ReplacementEndDate = selected?.Period.EndDate,
            ReplacementReasonCode = selected?.ReasonCode,
            ReplacementReasonComment = selected?.ReasonComment,
        };
    }

    private static NonWorkingDayCorrectionPreviewFormInput CanonicalInput(
        NonWorkingDayCorrectionPreviewFormInput submittedInput,
        NonWorkingDayCorrectionPreview preview)
    {
        return new NonWorkingDayCorrectionPreviewFormInput
        {
            PeriodId = preview.PeriodId,
            Mode = preview.Mode,
            ReplacementStartDate = preview.Mode
                == NonWorkingDayCorrectionMode.ReplaceRange
                    ? preview.ReplacementInput?.Period.StartDate
                    : null,
            ReplacementEndDate = preview.Mode
                == NonWorkingDayCorrectionMode.ReplaceRange
                    ? preview.ReplacementInput?.Period.EndDate
                    : null,
            ReplacementReasonCode = preview.ReplacementInput?.ReasonCode,
            ReplacementReasonComment = preview.ReplacementInput?.ReasonComment,
            CorrectionReason = NormalizeOptional(submittedInput.CorrectionReason),
            CorrectionComment = NormalizeOptional(submittedInput.CorrectionComment),
        };
    }

    private static NonWorkingDayCorrectionPreviewFormInput Copy(
        NonWorkingDayCorrectionPreviewFormInput input)
    {
        return new NonWorkingDayCorrectionPreviewFormInput
        {
            PeriodId = input.PeriodId,
            Mode = input.Mode,
            ReplacementStartDate = input.ReplacementStartDate,
            ReplacementEndDate = input.ReplacementEndDate,
            ReplacementReasonCode = input.ReplacementReasonCode,
            ReplacementReasonComment = input.ReplacementReasonComment,
            CorrectionReason = input.CorrectionReason,
            CorrectionComment = input.CorrectionComment,
        };
    }

    private static NonWorkingDayCorrectionPreviewFormInput Copy(
        NonWorkingDayCorrectionConfirmationFormInput input)
    {
        return new NonWorkingDayCorrectionPreviewFormInput
        {
            PeriodId = input.PeriodId,
            Mode = input.Mode,
            ReplacementStartDate = input.ReplacementStartDate,
            ReplacementEndDate = input.ReplacementEndDate,
            ReplacementReasonCode = input.ReplacementReasonCode,
            ReplacementReasonComment = input.ReplacementReasonComment,
            CorrectionReason = input.CorrectionReason,
            CorrectionComment = input.CorrectionComment,
        };
    }

    private static NonWorkingDayCorrectionPreviewFormError PreviewOrCanonicalError(
        PreviewCorrectNonWorkingDayResult result,
        GetNonWorkingDayResult? canonicalResult)
    {
        return result.Status == PreviewCorrectNonWorkingDayStatus.Success
            ? new NonWorkingDayCorrectionPreviewFormError(
                Field: null,
                canonicalResult?.ErrorMessage
                    ?? "The original non-working period changed. Refresh and preview the correction again.")
            : PreviewError(result);
    }

    private static NonWorkingDayCorrectionPreviewFormError PreviewError(
        PreviewCorrectNonWorkingDayResult result)
    {
        var message = result.Status switch
        {
            PreviewCorrectNonWorkingDayStatus.NotFound =>
                "The selected non-working period no longer exists.",
            PreviewCorrectNonWorkingDayStatus.AlreadyCanceled =>
                "The selected non-working period is already canceled.",
            PreviewCorrectNonWorkingDayStatus.StaleState =>
                "The selected non-working period was already corrected. Refresh the active list.",
            PreviewCorrectNonWorkingDayStatus.SourceInconsistent =>
                "Canonical source records are inconsistent, so correction impact cannot be shown.",
            PreviewCorrectNonWorkingDayStatus.RecalculationFailed =>
                "Canonical Membership replacement impact could not be calculated.",
            PreviewCorrectNonWorkingDayStatus.ValidationFailed =>
                result.ErrorMessage ?? "Review the correction preview input.",
            PreviewCorrectNonWorkingDayStatus.PermissionDenied =>
                "An active Owner session is required to preview a correction.",
            _ => throw new InvalidOperationException(
                $"Unsupported correction preview status {result.Status}."),
        };
        return new NonWorkingDayCorrectionPreviewFormError(
            result.ErrorField,
            message);
    }

    private static NonWorkingDayCorrectionPreviewFormError CommandError(
        CommandError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var message = error.Code switch
        {
            CommandErrorCode.PreviewExpired =>
                "The correction preview expired. Review the refreshed exact old and new scope before confirming again.",
            CommandErrorCode.AffectedScopeChanged =>
                "The correction scope changed. Review the refreshed exact old and new scope before confirming again.",
            CommandErrorCode.ConcurrencyConflict or CommandErrorCode.StaleState =>
                "Canonical NonWorkingDay or Membership state changed. Review a fresh correction preview.",
            CommandErrorCode.AlreadyCanceled =>
                "The selected non-working period is already canceled.",
            CommandErrorCode.NotFound =>
                "The selected non-working period no longer exists.",
            CommandErrorCode.DuplicateSubmission =>
                "This correction was not applied. Review the refreshed preview and confirm it again.",
            CommandErrorCode.RecalculationFailed =>
                "Membership recalculation did not complete, so no correction was committed.",
            _ => error.Message,
        };
        return new NonWorkingDayCorrectionPreviewFormError(error.Field, message);
    }

    private static NonWorkingDayCorrectionPreviewFormError CorrectionOutcomeError(
        GetNonWorkingDayCorrectionOutcomeResult result)
    {
        return new NonWorkingDayCorrectionPreviewFormError(
            result.ErrorField,
            result.ErrorMessage
                ?? "The canonical non-working period correction could not be loaded.");
    }

    private static bool IsSameOriginalSource(
        NonWorkingDayCorrectionSource original,
        NonWorkingDayCanonicalPeriod canonical)
    {
        if (original.PeriodId != canonical.PeriodId
            || original.Period != canonical.Period
            || original.ReasonCode != canonical.ReasonCode
            || original.ReasonComment != canonical.ReasonComment
            || original.CreatedAt != canonical.CreatedAt
            || original.CreatedByAccountId != canonical.CreatedByAccountId
            || original.SessionId != canonical.SessionId
            || original.Status != NonWorkingDayCorrectionSourceStatus.Active
            || canonical.Status != NonWorkingDayCorrectionSourceStatus.Active
            || original.Applications.Count != canonical.Applications.Count)
        {
            return false;
        }

        for (var index = 0; index < original.Applications.Count; index++)
        {
            var sourceApplication = original.Applications[index];
            var canonicalApplication = canonical.Applications[index];
            if (sourceApplication.ApplicationId
                    != canonicalApplication.ApplicationId
                || sourceApplication.MembershipId
                    != canonicalApplication.MembershipId
                || sourceApplication.ClientId != canonicalApplication.ClientId
                || sourceApplication.AppliedRange
                    != canonicalApplication.AppliedRange
                || sourceApplication.PreviewedAt
                    != canonicalApplication.PreviewedAt
                || sourceApplication.ConfirmedAt
                    != canonicalApplication.ConfirmedAt
                || sourceApplication.Status != canonicalApplication.Status)
            {
                return false;
            }
        }

        return true;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

public class NonWorkingDayCorrectionPreviewFormInput
{
    public Guid? PeriodId { get; set; }

    public NonWorkingDayCorrectionMode Mode { get; set; }
        = NonWorkingDayCorrectionMode.ReplaceRange;

    public DateOnly? ReplacementStartDate { get; set; }

    public DateOnly? ReplacementEndDate { get; set; }

    public string? ReplacementReasonCode { get; set; }

    public string? ReplacementReasonComment { get; set; }

    public string? CorrectionReason { get; set; }

    public string? CorrectionComment { get; set; }
}

public sealed class NonWorkingDayCorrectionConfirmationFormInput
    : NonWorkingDayCorrectionPreviewFormInput
{
    public string? ConfirmationToken { get; set; }

    public string? ScopeFingerprint { get; set; }

    public string? IdempotencyKey { get; set; }

    public bool Confirmed { get; set; }
}

public sealed record NonWorkingDayCorrectionPreviewFormError(
    string? Field,
    string Message);
