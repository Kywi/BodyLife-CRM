namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record PreviewCorrectNonWorkingDayResult(
    PreviewCorrectNonWorkingDayStatus Status,
    NonWorkingDayCorrectionPreview? Preview,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static PreviewCorrectNonWorkingDayResult Succeeded(
        NonWorkingDayCorrectionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new PreviewCorrectNonWorkingDayResult(
            PreviewCorrectNonWorkingDayStatus.Success,
            preview,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static PreviewCorrectNonWorkingDayResult Denied()
    {
        return Failure(
            PreviewCorrectNonWorkingDayStatus.PermissionDenied,
            "permission_denied",
            "An active Owner session is required to preview a NonWorkingDay correction.",
            field: null);
    }

    public static PreviewCorrectNonWorkingDayResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            PreviewCorrectNonWorkingDayStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static PreviewCorrectNonWorkingDayResult Missing()
    {
        return Failure(
            PreviewCorrectNonWorkingDayStatus.NotFound,
            "not_found",
            "NonWorkingDay period was not found.",
            "periodId");
    }

    public static PreviewCorrectNonWorkingDayResult AlreadyCanceled()
    {
        return Failure(
            PreviewCorrectNonWorkingDayStatus.AlreadyCanceled,
            "already_canceled",
            "NonWorkingDay period is already canceled.",
            "periodId");
    }

    public static PreviewCorrectNonWorkingDayResult Stale()
    {
        return Failure(
            PreviewCorrectNonWorkingDayStatus.StaleState,
            "stale_state",
            "NonWorkingDay period was already corrected. Refresh canonical state.",
            "periodId");
    }

    public static PreviewCorrectNonWorkingDayResult InconsistentSource()
    {
        return Failure(
            PreviewCorrectNonWorkingDayStatus.SourceInconsistent,
            "source_inconsistent",
            "NonWorkingDay correction preview is unavailable because canonical source records are inconsistent.",
            field: null);
    }

    public static PreviewCorrectNonWorkingDayResult RecalculationFailed()
    {
        return Failure(
            PreviewCorrectNonWorkingDayStatus.RecalculationFailed,
            "recalculation_failed",
            "Canonical Membership replacement impact could not be calculated.",
            field: null);
    }

    private static PreviewCorrectNonWorkingDayResult Failure(
        PreviewCorrectNonWorkingDayStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new PreviewCorrectNonWorkingDayResult(
            status,
            Preview: null,
            errorCode,
            errorMessage,
            field);
    }
}
