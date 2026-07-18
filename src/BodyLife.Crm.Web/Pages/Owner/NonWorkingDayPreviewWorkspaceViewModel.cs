using BodyLife.Crm.Modules.NonWorkingDays;

namespace BodyLife.Crm.Web.Pages.Owner;

public sealed record NonWorkingDayPreviewWorkspaceViewModel(
    NonWorkingDayPreviewFormInput Input,
    IReadOnlyList<NonWorkingDayPreviewFormError> Errors,
    NonWorkingDayImpactPreview? Preview)
{
    public const int ReasonCodeMaxLength = NonWorkingDayPreviewInput.ReasonCodeMaxLength;
    public const int ReasonCommentMaxLength = NonWorkingDayPreviewInput.ReasonCommentMaxLength;

    public static NonWorkingDayPreviewWorkspaceViewModel Empty { get; } = new(
        new NonWorkingDayPreviewFormInput(),
        Errors: [],
        Preview: null);

    public static NonWorkingDayPreviewWorkspaceViewModel New(DateOnly currentDate)
    {
        return new NonWorkingDayPreviewWorkspaceViewModel(
            new NonWorkingDayPreviewFormInput
            {
                ProposedStartDate = currentDate,
                ProposedEndDate = currentDate,
            },
            Errors: [],
            Preview: null);
    }

    public static NonWorkingDayPreviewWorkspaceViewModel FromResult(
        NonWorkingDayPreviewFormInput submittedInput,
        PreviewNonWorkingDayImpactResult result)
    {
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status == PreviewNonWorkingDayImpactStatus.Success)
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
                Errors: [],
                preview);
        }

        return new NonWorkingDayPreviewWorkspaceViewModel(
            Copy(submittedInput),
            [new NonWorkingDayPreviewFormError(
                result.ErrorField,
                DisplayError(result))],
            Preview: null);
    }

    private static string DisplayError(PreviewNonWorkingDayImpactResult result)
    {
        return result.Status switch
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
}

public sealed class NonWorkingDayPreviewFormInput
{
    public DateOnly? ProposedStartDate { get; set; }

    public DateOnly? ProposedEndDate { get; set; }

    public string? ReasonCode { get; set; }

    public string? ReasonComment { get; set; }
}

public sealed record NonWorkingDayPreviewFormError(string? Field, string Message);
