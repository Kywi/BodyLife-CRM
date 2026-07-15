namespace BodyLife.Crm.Modules.Visits;

public sealed record GetDailyVisitSourceRowsResult(
    GetDailyVisitSourceRowsStatus Status,
    DailyVisitSourceSnapshot? Snapshot,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetDailyVisitSourceRowsResult Succeeded(
        DailyVisitSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new GetDailyVisitSourceRowsResult(
            GetDailyVisitSourceRowsStatus.Success,
            snapshot,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetDailyVisitSourceRowsResult Denied()
    {
        return Failure(
            GetDailyVisitSourceRowsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetDailyVisitSourceRowsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetDailyVisitSourceRowsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetDailyVisitSourceRowsResult InconsistentSource()
    {
        return Failure(
            GetDailyVisitSourceRowsStatus.SourceInconsistent,
            "source_inconsistent",
            "Daily Visit rows are unavailable because canonical source records are inconsistent.",
            field: null);
    }

    private static GetDailyVisitSourceRowsResult Failure(
        GetDailyVisitSourceRowsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetDailyVisitSourceRowsResult(
            status,
            Snapshot: null,
            errorCode,
            errorMessage,
            field);
    }
}
