using BodyLife.Crm.Application.Queries;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record PreviewIssueMembershipResult(
    PreviewIssueMembershipStatus Status,
    MembershipIssuePreview? Preview,
    QueryPermissionSet AllowedActions,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static PreviewIssueMembershipResult Succeeded(
        MembershipIssuePreview preview,
        QueryPermissionSet allowedActions)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(allowedActions);

        return new PreviewIssueMembershipResult(
            PreviewIssueMembershipStatus.Success,
            preview,
            allowedActions,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static PreviewIssueMembershipResult Denied()
    {
        return Failure(
            PreviewIssueMembershipStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static PreviewIssueMembershipResult MissingClient()
    {
        return Failure(
            PreviewIssueMembershipStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static PreviewIssueMembershipResult MissingMembershipType()
    {
        return Failure(
            PreviewIssueMembershipStatus.NotFound,
            "not_found",
            "Membership type was not found.",
            "membershipTypeId");
    }

    public static PreviewIssueMembershipResult InactiveMembershipType()
    {
        return Failure(
            PreviewIssueMembershipStatus.MembershipTypeInactive,
            "membership_type_inactive",
            "Inactive membership types cannot be used for ordinary issue.",
            "membershipTypeId");
    }

    public static PreviewIssueMembershipResult Invalid(string message, string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            PreviewIssueMembershipStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static PreviewIssueMembershipResult RecalculationFailed()
    {
        return Failure(
            PreviewIssueMembershipStatus.RecalculationFailed,
            "recalculation_failed",
            "Existing membership state is unavailable because recalculation has not completed successfully.",
            field: null);
    }

    private static PreviewIssueMembershipResult Failure(
        PreviewIssueMembershipStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new PreviewIssueMembershipResult(
            status,
            Preview: null,
            QueryPermissionSet.Empty,
            errorCode,
            errorMessage,
            field);
    }
}
