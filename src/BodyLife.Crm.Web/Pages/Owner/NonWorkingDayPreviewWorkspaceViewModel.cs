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
            [PreviewError(result)],
            "Impact preview not created",
            Preview: null,
            IdempotencyKey: null,
            ConfirmedPeriod: null);
    }

    public static NonWorkingDayPreviewWorkspaceViewModel FromConfirmationRefresh(
        NonWorkingDayConfirmationFormInput submittedInput,
        PreviewNonWorkingDayImpactResult refreshedPreview,
        IReadOnlyList<CommandError> commandErrors)
    {
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(refreshedPreview);
        ArgumentNullException.ThrowIfNull(commandErrors);

        var errors = commandErrors
            .Select(CommandError)
            .ToList();
        if (refreshedPreview.Status == PreviewNonWorkingDayImpactStatus.Success)
        {
            return FromSuccessfulPreview(
                refreshedPreview,
                errors.AsReadOnly(),
                "Non-working period not added");
        }

        errors.Add(PreviewError(refreshedPreview));
        return new NonWorkingDayPreviewWorkspaceViewModel(
            Copy(submittedInput),
            errors.AsReadOnly(),
            "Non-working period not added",
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
                    result.ErrorMessage
                        ?? "The confirmed non-working period could not be loaded."),
            ],
            ErrorHeading = "Confirmed period unavailable",
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
        PreviewNonWorkingDayImpactResult result)
    {
        var message = result.Status switch
        {
            PreviewNonWorkingDayImpactStatus.PermissionDenied =>
                "An active Owner session is required to preview NonWorkingDay impact.",
            PreviewNonWorkingDayImpactStatus.RecalculationFailed =>
                "Canonical Membership impact could not be calculated. No NonWorkingDay data was changed.",
            PreviewNonWorkingDayImpactStatus.ValidationFailed =>
                result.ErrorMessage ?? "Review the proposed period and reason.",
            _ => throw new InvalidOperationException(
                $"Unsupported NonWorkingDay preview status {result.Status}."),
        };
        return new NonWorkingDayPreviewFormError(result.ErrorField, message);
    }

    private static NonWorkingDayPreviewFormError CommandError(CommandError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var message = error.Code switch
        {
            CommandErrorCode.PreviewExpired =>
                "The preview expired. Review the refreshed exact scope before confirming again.",
            CommandErrorCode.AffectedScopeChanged =>
                "The affected Membership scope changed. Review the refreshed exact scope before confirming again.",
            CommandErrorCode.ConcurrencyConflict or CommandErrorCode.StaleState =>
                "Canonical Membership state changed. Review the refreshed exact scope before confirming again.",
            CommandErrorCode.DuplicateSubmission =>
                "This confirmation was not applied. Review the refreshed preview and confirm it again.",
            CommandErrorCode.RecalculationFailed =>
                "Membership recalculation did not complete, so no NonWorkingDay data was committed.",
            _ => error.Message,
        };
        return new NonWorkingDayPreviewFormError(error.Field, message);
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
