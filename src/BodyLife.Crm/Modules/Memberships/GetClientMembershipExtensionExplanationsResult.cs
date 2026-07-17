namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetClientMembershipExtensionExplanationsResult(
    GetClientMembershipExtensionExplanationsStatus Status,
    ClientMembershipExtensionExplanations? Explanations,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientMembershipExtensionExplanationsResult Succeeded(
        ClientMembershipExtensionExplanations explanations)
    {
        ArgumentNullException.ThrowIfNull(explanations);

        return new GetClientMembershipExtensionExplanationsResult(
            GetClientMembershipExtensionExplanationsStatus.Success,
            explanations,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientMembershipExtensionExplanationsResult Denied()
    {
        return Failure(
            GetClientMembershipExtensionExplanationsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientMembershipExtensionExplanationsResult MissingClient()
    {
        return Failure(
            GetClientMembershipExtensionExplanationsStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetClientMembershipExtensionExplanationsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetClientMembershipExtensionExplanationsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetClientMembershipExtensionExplanationsResult InconsistentSource()
    {
        return Failure(
            GetClientMembershipExtensionExplanationsStatus.SourceInconsistent,
            "source_inconsistent",
            "Membership extension source records are inconsistent.",
            field: null);
    }

    private static GetClientMembershipExtensionExplanationsResult Failure(
        GetClientMembershipExtensionExplanationsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientMembershipExtensionExplanationsResult(
            status,
            Explanations: null,
            errorCode,
            errorMessage,
            field);
    }
}
