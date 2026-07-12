using BodyLife.Crm.Application.Queries;

namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record SearchClientsResult(
    SearchClientsStatus Status,
    IReadOnlyList<ClientSearchResult> Items,
    Guid? AutoOpenClientId,
    string? NextPageCursor,
    QueryPermissionSet AllowedActions,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static SearchClientsResult Succeeded(
        IReadOnlyList<ClientSearchResult> items,
        Guid? autoOpenClientId,
        string? nextPageCursor,
        QueryPermissionSet allowedActions)
    {
        return new SearchClientsResult(
            SearchClientsStatus.Success,
            items,
            autoOpenClientId,
            nextPageCursor,
            allowedActions,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static SearchClientsResult Denied()
    {
        return Failure(
            SearchClientsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static SearchClientsResult Invalid(
        string message,
        string? field)
    {
        return Failure(
            SearchClientsStatus.ValidationFailed,
            "validation_failed",
            message,
            field);
    }

    private static SearchClientsResult Failure(
        SearchClientsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new SearchClientsResult(
            status,
            [],
            AutoOpenClientId: null,
            NextPageCursor: null,
            QueryPermissionSet.Empty,
            errorCode,
            errorMessage,
            field);
    }
}
