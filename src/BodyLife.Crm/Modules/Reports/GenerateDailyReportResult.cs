namespace BodyLife.Crm.Modules.Reports;

public sealed record GenerateDailyReportResult(
    GenerateDailyReportStatus Status,
    DailyReportSnapshot? Report,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GenerateDailyReportResult Succeeded(DailyReportSnapshot report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new GenerateDailyReportResult(
            GenerateDailyReportStatus.Success,
            report,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GenerateDailyReportResult Denied()
    {
        return Failure(
            GenerateDailyReportStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GenerateDailyReportResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GenerateDailyReportStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GenerateDailyReportResult InconsistentSource()
    {
        return Failure(
            GenerateDailyReportStatus.SourceInconsistent,
            "source_inconsistent",
            "Daily report is unavailable because canonical Visit and Payment source records are inconsistent.",
            field: null);
    }

    private static GenerateDailyReportResult Failure(
        GenerateDailyReportStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GenerateDailyReportResult(
            status,
            Report: null,
            errorCode,
            errorMessage,
            field);
    }
}
