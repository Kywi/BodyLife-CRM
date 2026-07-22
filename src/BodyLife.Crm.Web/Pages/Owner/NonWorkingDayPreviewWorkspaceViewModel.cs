using System.Globalization;
using System.Resources;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.NonWorkingDays;

namespace BodyLife.Crm.Web.Pages.Owner;

public sealed record NonWorkingDayPreviewWorkspaceViewModel(
    NonWorkingDayPreviewFormInput Input,
    IReadOnlyList<NonWorkingDayPreviewFormError> Errors,
    string? ErrorHeading,
    NonWorkingDayImpactPreview? Preview,
    string? IdempotencyKey,
    NonWorkingDayCanonicalPeriod? ConfirmedPeriod)
{
    private static readonly ResourceManager OwnerResources = new(
        "BodyLife.Crm.Web.Resources.Localization.Owner",
        typeof(NonWorkingDayPreviewWorkspaceViewModel).Assembly);
    public const int ReasonCodeMaxLength = NonWorkingDayPreviewInput.ReasonCodeMaxLength;
    public const int ReasonCommentMaxLength = NonWorkingDayPreviewInput.ReasonCommentMaxLength;

    public static NonWorkingDayPreviewWorkspaceViewModel Empty { get; } = new(
        new NonWorkingDayPreviewFormInput(),
        Errors: [],
        ErrorHeading: null,
        Preview: null,
        IdempotencyKey: null,
        ConfirmedPeriod: null);

    public static NonWorkingDayPreviewWorkspaceViewModel New(DateOnly currentDate)
    {
        return new NonWorkingDayPreviewWorkspaceViewModel(
            new NonWorkingDayPreviewFormInput
            {
                ProposedStartDate = currentDate,
                ProposedEndDate = currentDate,
            },
            Errors: [],
            ErrorHeading: null,
            Preview: null,
            IdempotencyKey: null,
            ConfirmedPeriod: null);
    }

    public static NonWorkingDayPreviewWorkspaceViewModel FromPreviewResult(
        NonWorkingDayPreviewFormInput submittedInput,
        PreviewNonWorkingDayImpactResult result)
    {
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status == PreviewNonWorkingDayImpactStatus.Success)
        {
            return FromSuccessfulPreview(result, Errors: [], errorHeading: null);
        }

        return new NonWorkingDayPreviewWorkspaceViewModel(
            Copy(submittedInput),
            [PreviewError(
                result,
                submittedInput.ProposedStartDate,
                submittedInput.ProposedEndDate,
                submittedInput.ReasonCode,
                submittedInput.ReasonComment)],
            T("NonWorkingDays.PreviewFailed"),
            Preview: null,
            IdempotencyKey: null,
            ConfirmedPeriod: null);
    }

    public static NonWorkingDayPreviewWorkspaceViewModel FromConfirmationRefresh(
        NonWorkingDayConfirmationFormInput submittedInput,
        PreviewNonWorkingDayImpactResult refreshedPreview,
        IReadOnlyList<CommandError> commandErrors,
        bool preserveLocalizedAdapterMessages)
    {
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(refreshedPreview);
        ArgumentNullException.ThrowIfNull(commandErrors);

        var errors = commandErrors
            .Select(error => preserveLocalizedAdapterMessages
                ? AdapterError(error)
                : CommandError(error))
            .ToList();
        if (refreshedPreview.Status == PreviewNonWorkingDayImpactStatus.Success)
        {
            return FromSuccessfulPreview(
                refreshedPreview,
                errors.AsReadOnly(),
                T("NonWorkingDays.NotAdded"));
        }

        errors.Add(PreviewError(
            refreshedPreview,
            submittedInput.ProposedStartDate,
            submittedInput.ProposedEndDate,
            submittedInput.ReasonCode,
            submittedInput.ReasonComment));
        return new NonWorkingDayPreviewWorkspaceViewModel(
            Copy(submittedInput),
            errors.AsReadOnly(),
            T("NonWorkingDays.NotAdded"),
            Preview: null,
            IdempotencyKey: null,
            ConfirmedPeriod: null);
    }

    public static NonWorkingDayPreviewWorkspaceViewModel FromCanonicalPeriod(
        NonWorkingDayCanonicalPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);

        return new NonWorkingDayPreviewWorkspaceViewModel(
            new NonWorkingDayPreviewFormInput
            {
                ProposedStartDate = period.Period.StartDate,
                ProposedEndDate = period.Period.EndDate,
                ReasonCode = period.ReasonCode,
                ReasonComment = period.ReasonComment,
            },
            Errors: [],
            ErrorHeading: null,
            Preview: null,
            IdempotencyKey: null,
            ConfirmedPeriod: period);
    }

    public static NonWorkingDayPreviewWorkspaceViewModel FromCanonicalFailure(
        DateOnly currentDate,
        GetNonWorkingDayResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var workspace = New(currentDate);
        return workspace with
        {
            Errors =
            [
                new NonWorkingDayPreviewFormError(
                    result.ErrorField,
                    T("NonWorkingDays.Error.Load")),
            ],
            ErrorHeading = T("NonWorkingDays.ConfirmedUnavailable"),
        };
    }

    private static NonWorkingDayPreviewWorkspaceViewModel FromSuccessfulPreview(
        PreviewNonWorkingDayImpactResult result,
        IReadOnlyList<NonWorkingDayPreviewFormError> Errors,
        string? errorHeading)
    {
        var preview = result.Preview
            ?? throw new InvalidOperationException(
                "Successful NonWorkingDay preview result is missing its payload.");
        return new NonWorkingDayPreviewWorkspaceViewModel(
            new NonWorkingDayPreviewFormInput
            {
                ProposedStartDate = preview.Period.StartDate,
                ProposedEndDate = preview.Period.EndDate,
                ReasonCode = preview.ReasonCode,
                ReasonComment = preview.ReasonComment,
            },
            Errors,
            errorHeading,
            preview,
            Guid.NewGuid().ToString("N"),
            ConfirmedPeriod: null);
    }

    private static NonWorkingDayPreviewFormError PreviewError(
        PreviewNonWorkingDayImpactResult result,
        DateOnly? proposedStartDate,
        DateOnly? proposedEndDate,
        string? reasonCode,
        string? reasonComment)
    {
        var message = result.Status switch
        {
            PreviewNonWorkingDayImpactStatus.PermissionDenied =>
                T("NonWorkingDays.Error.Permission"),
            PreviewNonWorkingDayImpactStatus.RecalculationFailed =>
                T("NonWorkingDays.Error.Recalculation"),
            PreviewNonWorkingDayImpactStatus.ValidationFailed =>
                ValidationMessage(
                    result.ErrorField,
                    proposedStartDate,
                    proposedEndDate,
                    reasonCode,
                    reasonComment),
            _ => throw new InvalidOperationException(
                $"Unsupported NonWorkingDay preview status {result.Status}."),
        };
        return new NonWorkingDayPreviewFormError(result.ErrorField, message);
    }

    private static string ValidationMessage(
        string? field,
        DateOnly? proposedStartDate,
        DateOnly? proposedEndDate,
        string? reasonCode,
        string? reasonComment)
    {
        return field switch
        {
            "proposedStartDate" => T("NonWorkingDays.Error.StartRequired"),
            "proposedEndDate" when proposedStartDate is { } startDate
                && proposedEndDate is { } endDate
                && endDate < startDate => T("NonWorkingDays.Error.EndBeforeStart"),
            "proposedEndDate" => T("NonWorkingDays.Error.EndRequired"),
            "reasonCode" when string.IsNullOrWhiteSpace(reasonCode) =>
                T("NonWorkingDays.Error.ReasonCodeRequired"),
            "reasonCode" => T(
                "NonWorkingDays.Error.ReasonCodeTooLong",
                ReasonCodeMaxLength),
            "reasonComment" when reasonComment?.Length > ReasonCommentMaxLength =>
                T(
                    "NonWorkingDays.Error.ReasonCommentTooLong",
                    ReasonCommentMaxLength),
            _ => T("NonWorkingDays.Error.Validation"),
        };
    }

    private static NonWorkingDayPreviewFormError AdapterError(CommandError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Code != CommandErrorCode.ValidationFailed
            || string.IsNullOrWhiteSpace(error.Message))
        {
            throw new InvalidOperationException(
                "Only localized Web adapter validation errors can be preserved.");
        }

        return new NonWorkingDayPreviewFormError(error.Field, error.Message);
    }

    private static NonWorkingDayPreviewFormError CommandError(CommandError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var message = error.Code switch
        {
            CommandErrorCode.PreviewExpired =>
                T("NonWorkingDays.Error.PreviewExpired"),
            CommandErrorCode.AffectedScopeChanged =>
                T("NonWorkingDays.Error.ScopeChanged"),
            CommandErrorCode.ConcurrencyConflict or CommandErrorCode.StaleState =>
                T("NonWorkingDays.Error.StateChanged"),
            CommandErrorCode.DuplicateSubmission =>
                T("NonWorkingDays.Error.Duplicate"),
            CommandErrorCode.RecalculationFailed =>
                T("NonWorkingDays.Error.Recalculation"),
            _ => T("NonWorkingDays.Error.Generic"),
        };
        return new NonWorkingDayPreviewFormError(error.Field, message);
    }

    private static string T(string key, params object[] arguments)
    {
        var value = OwnerResources.GetString(key, CultureInfo.CurrentUICulture)
            ?? OwnerResources.GetString(
                "NonWorkingDays.Error.Generic",
                CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException(
                "The Owner localization resources are unavailable.");
        return arguments.Length == 0
            ? value
            : string.Format(CultureInfo.CurrentCulture, value, arguments);
    }

    private static NonWorkingDayPreviewFormInput Copy(
        NonWorkingDayPreviewFormInput input)
    {
        return new NonWorkingDayPreviewFormInput
        {
            ProposedStartDate = input.ProposedStartDate,
            ProposedEndDate = input.ProposedEndDate,
            ReasonCode = input.ReasonCode,
            ReasonComment = input.ReasonComment,
        };
    }

    private static NonWorkingDayPreviewFormInput Copy(
        NonWorkingDayConfirmationFormInput input)
    {
        return new NonWorkingDayPreviewFormInput
        {
            ProposedStartDate = input.ProposedStartDate,
            ProposedEndDate = input.ProposedEndDate,
            ReasonCode = input.ReasonCode,
            ReasonComment = input.ReasonComment,
        };
    }
}

public sealed class NonWorkingDayPreviewFormInput
{
    public DateOnly? ProposedStartDate { get; set; }

    public DateOnly? ProposedEndDate { get; set; }

    public string? ReasonCode { get; set; }

    public string? ReasonComment { get; set; }
}

public sealed class NonWorkingDayConfirmationFormInput
{
    public DateOnly? ProposedStartDate { get; set; }

    public DateOnly? ProposedEndDate { get; set; }

    public string? ReasonCode { get; set; }

    public string? ReasonComment { get; set; }

    public string? ConfirmationToken { get; set; }

    public string? ScopeFingerprint { get; set; }

    public string? IdempotencyKey { get; set; }

    public bool Confirmed { get; set; }
}

public sealed record NonWorkingDayPreviewFormError(string? Field, string Message);
