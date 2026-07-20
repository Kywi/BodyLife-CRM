namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record GetClientNonWorkingDayHistorySourceRowsResult(
    GetClientNonWorkingDayHistorySourceRowsStatus Status,
    ClientNonWorkingDayHistorySourceRowsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientNonWorkingDayHistorySourceRowsResult Succeeded(
        ClientNonWorkingDayHistorySourceRowsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetClientNonWorkingDayHistorySourceRowsResult(
            GetClientNonWorkingDayHistorySourceRowsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientNonWorkingDayHistorySourceRowsResult Denied()
    {
        return Failure(
            GetClientNonWorkingDayHistorySourceRowsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientNonWorkingDayHistorySourceRowsResult MissingClient()
    {
        return Failure(
            GetClientNonWorkingDayHistorySourceRowsStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetClientNonWorkingDayHistorySourceRowsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException(
                "Validation message is required.",
                nameof(message));
        }

        return Failure(
            GetClientNonWorkingDayHistorySourceRowsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetClientNonWorkingDayHistorySourceRowsResult InconsistentSource()
    {
        return Failure(
            GetClientNonWorkingDayHistorySourceRowsStatus.SourceInconsistent,
            "source_inconsistent",
            "NonWorkingDay history is unavailable because canonical source or audit records are inconsistent.",
            field: null);
    }

    private static GetClientNonWorkingDayHistorySourceRowsResult Failure(
        GetClientNonWorkingDayHistorySourceRowsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientNonWorkingDayHistorySourceRowsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
