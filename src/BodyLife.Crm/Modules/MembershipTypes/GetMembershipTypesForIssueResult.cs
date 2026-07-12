using BodyLife.Crm.Application.Queries;

namespace BodyLife.Crm.Modules.MembershipTypes;

public sealed record GetMembershipTypesForIssueResult(
    GetMembershipTypesForIssueStatus Status,
    IReadOnlyList<MembershipTypeCatalogItem> Items,
    QueryPermissionSet AllowedActions,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static GetMembershipTypesForIssueResult Succeeded(
        IReadOnlyList<MembershipTypeCatalogItem> items,
        bool includeInactive,
        QueryPermissionSet allowedActions)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(allowedActions);

        if (!includeInactive && items.Any(item => !item.IsAvailableForOrdinaryIssue))
        {
            throw new ArgumentException(
                "Ordinary issue results cannot contain inactive membership types.",
                nameof(items));
        }

        return new GetMembershipTypesForIssueResult(
            GetMembershipTypesForIssueStatus.Success,
            items.ToArray(),
            allowedActions,
            ErrorCode: null,
            ErrorMessage: null);
    }

    public static GetMembershipTypesForIssueResult Denied(string message)
    {
        var normalizedMessage = message?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Permission denial message is required.", nameof(message));
        }

        return new GetMembershipTypesForIssueResult(
            GetMembershipTypesForIssueStatus.PermissionDenied,
            Items: [],
            QueryPermissionSet.Empty,
            ErrorCode: "permission_denied",
            normalizedMessage);
    }
}
