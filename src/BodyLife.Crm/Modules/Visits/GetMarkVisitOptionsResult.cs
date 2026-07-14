using BodyLife.Crm.Application.Queries;

namespace BodyLife.Crm.Modules.Visits;

public sealed record GetMarkVisitOptionsResult(
    GetMarkVisitOptionsStatus Status,
    MarkVisitOptions? Options,
    QueryPermissionSet AllowedActions,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetMarkVisitOptionsResult Succeeded(MarkVisitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new GetMarkVisitOptionsResult(
            GetMarkVisitOptionsStatus.Success,
            options,
            new QueryPermissionSet(
            [
                QueryPermissionResult.Allowed(
                    VisitActionKeys.Mark,
                    VisitActionKeys.AdminOrOwnerPolicy),
            ]),
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetMarkVisitOptionsResult Denied()
    {
        return Failure(
            GetMarkVisitOptionsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetMarkVisitOptionsResult MissingClient()
    {
        return Failure(
            GetMarkVisitOptionsStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetMarkVisitOptionsResult Invalid(string message, string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetMarkVisitOptionsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetMarkVisitOptionsResult RecalculationFailed()
    {
        return Failure(
            GetMarkVisitOptionsStatus.RecalculationFailed,
            "recalculation_failed",
            "Visit options are unavailable because Membership state could not be read canonically.",
            field: null);
    }

    private static GetMarkVisitOptionsResult Failure(
        GetMarkVisitOptionsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetMarkVisitOptionsResult(
            status,
            Options: null,
            QueryPermissionSet.Empty,
            errorCode,
            errorMessage,
            field);
    }
}
