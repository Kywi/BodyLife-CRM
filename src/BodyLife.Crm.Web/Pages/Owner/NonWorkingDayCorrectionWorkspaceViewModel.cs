using System.Globalization;
using System.Resources;
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
    private static readonly ResourceManager OwnerResources = new(
        "BodyLife.Crm.Web.Resources.Localization.Owner",
        typeof(NonWorkingDayCorrectionWorkspaceViewModel).Assembly);
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
                    T("NonWorkingDays.Correction.Error.LoadActive"))],
                ErrorHeading: T("NonWorkingDays.Correction.Heading.Unavailable"),
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
            T("NonWorkingDays.Correction.Heading.PreviewFailed"));
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
            T("NonWorkingDays.Correction.Heading.PreviewFailed"));
    }

    public static NonWorkingDayCorrectionWorkspaceViewModel
        FromConfirmationRefresh(
            GetActiveNonWorkingDaysForCorrectionResult activePeriods,
            NonWorkingDayCorrectionConfirmationFormInput submittedInput,
            PreviewCorrectNonWorkingDayResult refreshedPreview,
            GetNonWorkingDayResult? canonicalResult,
            IReadOnlyList<CommandError> commandErrors,
            bool preserveLocalizedAdapterMessages)
    {
        ArgumentNullException.ThrowIfNull(activePeriods);
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(refreshedPreview);
        ArgumentNullException.ThrowIfNull(commandErrors);

        var errors = commandErrors
            .Select(error => preserveLocalizedAdapterMessages
                ? AdapterError(error)
                : CommandError(error))
            .ToList();
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
                T("NonWorkingDays.Correction.Heading.NotApplied"));
        }

        errors.Add(PreviewOrCanonicalError(refreshedPreview, canonicalResult));
        return Failure(
            activePeriods,
            Copy(submittedInput),
            errors.AsReadOnly(),
            T("NonWorkingDays.Correction.Heading.NotApplied"));
    }

    public static NonWorkingDayCorrectionWorkspaceViewModel
        FromConfirmationFailure(
            GetActiveNonWorkingDaysForCorrectionResult activePeriods,
            NonWorkingDayCorrectionConfirmationFormInput submittedInput,
            IReadOnlyList<CommandError> commandErrors,
            bool preserveLocalizedAdapterMessages)
    {
        ArgumentNullException.ThrowIfNull(activePeriods);
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(commandErrors);
        return Failure(
            activePeriods,
            Copy(submittedInput),
            commandErrors
                .Select(error => preserveLocalizedAdapterMessages
                    ? AdapterError(error)
                    : CommandError(error))
                .ToArray(),
            T("NonWorkingDays.Correction.Heading.NotApplied"));
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
            ErrorHeading: T("NonWorkingDays.Correction.Heading.OutcomeUnavailable"),
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
                T("NonWorkingDays.Correction.Error.OriginalChanged"))
            : PreviewError(result);
    }

    private static NonWorkingDayCorrectionPreviewFormError PreviewError(
        PreviewCorrectNonWorkingDayResult result)
    {
        var message = result.Status switch
        {
            PreviewCorrectNonWorkingDayStatus.NotFound =>
                T("NonWorkingDays.Correction.Error.NotFound"),
            PreviewCorrectNonWorkingDayStatus.AlreadyCanceled =>
                T("NonWorkingDays.Correction.Error.AlreadyCanceled"),
            PreviewCorrectNonWorkingDayStatus.StaleState =>
                T("NonWorkingDays.Correction.Error.Stale"),
            PreviewCorrectNonWorkingDayStatus.SourceInconsistent =>
                T("NonWorkingDays.Correction.Error.SourceInconsistent"),
            PreviewCorrectNonWorkingDayStatus.RecalculationFailed =>
                T("NonWorkingDays.Correction.Error.Recalculation"),
            PreviewCorrectNonWorkingDayStatus.ValidationFailed =>
                T("NonWorkingDays.Correction.Error.Validation"),
            PreviewCorrectNonWorkingDayStatus.PermissionDenied =>
                T("NonWorkingDays.Correction.Error.Permission"),
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
                T("NonWorkingDays.Correction.Error.PreviewExpired"),
            CommandErrorCode.AffectedScopeChanged =>
                T("NonWorkingDays.Correction.Error.ScopeChanged"),
            CommandErrorCode.ConcurrencyConflict or CommandErrorCode.StaleState =>
                T("NonWorkingDays.Correction.Error.StateChanged"),
            CommandErrorCode.AlreadyCanceled =>
                T("NonWorkingDays.Correction.Error.AlreadyCanceled"),
            CommandErrorCode.NotFound =>
                T("NonWorkingDays.Correction.Error.NotFound"),
            CommandErrorCode.DuplicateSubmission =>
                T("NonWorkingDays.Correction.Error.Duplicate"),
            CommandErrorCode.RecalculationFailed =>
                T("NonWorkingDays.Correction.Error.Recalculation"),
            _ => T("NonWorkingDays.Correction.Error.Generic"),
        };
        return new NonWorkingDayCorrectionPreviewFormError(error.Field, message);
    }

    private static NonWorkingDayCorrectionPreviewFormError AdapterError(
        CommandError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Code is not (
                CommandErrorCode.ValidationFailed
                or CommandErrorCode.ReasonRequired)
            || string.IsNullOrWhiteSpace(error.Message))
        {
            throw new InvalidOperationException(
                "Only localized Web adapter validation errors can be preserved.");
        }

        return new NonWorkingDayCorrectionPreviewFormError(
            error.Field,
            error.Message);
    }

    private static NonWorkingDayCorrectionPreviewFormError CorrectionOutcomeError(
        GetNonWorkingDayCorrectionOutcomeResult result)
    {
        return new NonWorkingDayCorrectionPreviewFormError(
            result.ErrorField,
            T("NonWorkingDays.Correction.Error.OutcomeLoad"));
    }

    private static string T(string key) =>
        OwnerResources.GetString(key, CultureInfo.CurrentUICulture)
        ?? OwnerResources.GetString(
            "NonWorkingDays.Correction.Error.Generic",
            CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException(
            "The Owner localization resources are unavailable.");

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
