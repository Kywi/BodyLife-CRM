namespace BodyLife.Crm.Modules.Payments;

public sealed record GetClientPaymentHistorySourceRowsResult(
    GetClientPaymentHistorySourceRowsStatus Status,
    ClientPaymentHistorySourceRowsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientPaymentHistorySourceRowsResult Succeeded(
        ClientPaymentHistorySourceRowsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetClientPaymentHistorySourceRowsResult(
            GetClientPaymentHistorySourceRowsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientPaymentHistorySourceRowsResult Denied()
    {
        return Failure(
            GetClientPaymentHistorySourceRowsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientPaymentHistorySourceRowsResult MissingClient()
    {
        return Failure(
            GetClientPaymentHistorySourceRowsStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetClientPaymentHistorySourceRowsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetClientPaymentHistorySourceRowsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetClientPaymentHistorySourceRowsResult InconsistentSource()
    {
        return Failure(
            GetClientPaymentHistorySourceRowsStatus.SourceInconsistent,
            "source_inconsistent",
            "Payment history is unavailable because canonical source or audit records are inconsistent.",
            field: null);
    }

    private static GetClientPaymentHistorySourceRowsResult Failure(
        GetClientPaymentHistorySourceRowsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientPaymentHistorySourceRowsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
