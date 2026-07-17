namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record PreviewNonWorkingDayImpactResult(
    PreviewNonWorkingDayImpactStatus Status,
    NonWorkingDayImpactPreview? Preview,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static PreviewNonWorkingDayImpactResult Succeeded(
        NonWorkingDayImpactPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new PreviewNonWorkingDayImpactResult(
            PreviewNonWorkingDayImpactStatus.Success,
            preview,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static PreviewNonWorkingDayImpactResult Denied()
    {
        return Failure(
            PreviewNonWorkingDayImpactStatus.PermissionDenied,
            "permission_denied",
            "An active Owner session is required to preview NonWorkingDay impact.",
            field: null);
    }

    public static PreviewNonWorkingDayImpactResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            PreviewNonWorkingDayImpactStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static PreviewNonWorkingDayImpactResult RecalculationFailed()
    {
        return Failure(
            PreviewNonWorkingDayImpactStatus.RecalculationFailed,
            "recalculation_failed",
            "Canonical Membership impact could not be calculated.",
            field: null);
    }

    private static PreviewNonWorkingDayImpactResult Failure(
        PreviewNonWorkingDayImpactStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new PreviewNonWorkingDayImpactResult(
            status,
            Preview: null,
            errorCode,
            errorMessage,
            field);
    }
}
