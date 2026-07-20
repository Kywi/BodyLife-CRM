namespace BodyLife.Crm.Modules.Freezes;

public sealed record GetClientFreezeHistorySourceRowsResult(
    GetClientFreezeHistorySourceRowsStatus Status,
    ClientFreezeHistorySourceRowsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientFreezeHistorySourceRowsResult Succeeded(
        ClientFreezeHistorySourceRowsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetClientFreezeHistorySourceRowsResult(
            GetClientFreezeHistorySourceRowsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientFreezeHistorySourceRowsResult Denied()
    {
        return Failure(
            GetClientFreezeHistorySourceRowsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientFreezeHistorySourceRowsResult MissingClient()
    {
        return Failure(
            GetClientFreezeHistorySourceRowsStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetClientFreezeHistorySourceRowsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetClientFreezeHistorySourceRowsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetClientFreezeHistorySourceRowsResult InconsistentSource()
    {
        return Failure(
            GetClientFreezeHistorySourceRowsStatus.SourceInconsistent,
            "source_inconsistent",
            "Freeze history is unavailable because canonical source or audit records are inconsistent.",
            field: null);
    }

    private static GetClientFreezeHistorySourceRowsResult Failure(
        GetClientFreezeHistorySourceRowsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientFreezeHistorySourceRowsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
