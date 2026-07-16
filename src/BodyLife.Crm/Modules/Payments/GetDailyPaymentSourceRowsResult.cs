namespace BodyLife.Crm.Modules.Payments;

public sealed record GetDailyPaymentSourceRowsResult(
    GetDailyPaymentSourceRowsStatus Status,
    DailyPaymentSourceSnapshot? Snapshot,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetDailyPaymentSourceRowsResult Succeeded(
        DailyPaymentSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new GetDailyPaymentSourceRowsResult(
            GetDailyPaymentSourceRowsStatus.Success,
            snapshot,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetDailyPaymentSourceRowsResult Denied()
    {
        return Failure(
            GetDailyPaymentSourceRowsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetDailyPaymentSourceRowsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetDailyPaymentSourceRowsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetDailyPaymentSourceRowsResult InconsistentSource()
    {
        return Failure(
            GetDailyPaymentSourceRowsStatus.SourceInconsistent,
            "source_inconsistent",
            "Daily Payment rows are unavailable because canonical source records are inconsistent.",
            field: null);
    }

    private static GetDailyPaymentSourceRowsResult Failure(
        GetDailyPaymentSourceRowsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetDailyPaymentSourceRowsResult(
            status,
            Snapshot: null,
            errorCode,
            errorMessage,
            field);
    }
}
