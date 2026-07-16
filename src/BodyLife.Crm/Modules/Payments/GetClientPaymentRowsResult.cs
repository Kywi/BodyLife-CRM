namespace BodyLife.Crm.Modules.Payments;

public sealed record GetClientPaymentRowsResult(
    GetClientPaymentRowsStatus Status,
    ClientPaymentRowsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientPaymentRowsResult Succeeded(ClientPaymentRowsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetClientPaymentRowsResult(
            GetClientPaymentRowsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientPaymentRowsResult Denied()
    {
        return Failure(
            GetClientPaymentRowsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientPaymentRowsResult MissingClient()
    {
        return Failure(
            GetClientPaymentRowsStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetClientPaymentRowsResult Invalid(string message, string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetClientPaymentRowsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetClientPaymentRowsResult InconsistentSource()
    {
        return Failure(
            GetClientPaymentRowsStatus.SourceInconsistent,
            "source_inconsistent",
            "Payment rows are unavailable because canonical source records are inconsistent.",
            field: null);
    }

    private static GetClientPaymentRowsResult Failure(
        GetClientPaymentRowsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientPaymentRowsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
