using BodyLife.Crm.Application.Queries;
using Microsoft.AspNetCore.Authorization;

namespace BodyLife.Crm.Web.Operations;

public sealed record QueryPermissionRequest(
    string ActionKey,
    string RequiredPolicy,
    object? Resource = null);

public interface IQueryPermissionResolver
{
    Task<QueryPermissionSet> ResolveAsync(
        IEnumerable<QueryPermissionRequest> requests,
        CancellationToken cancellationToken = default);
}

public sealed class QueryPermissionResolver(
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorizationService) : IQueryPermissionResolver
{
    public async Task<QueryPermissionSet> ResolveAsync(
        IEnumerable<QueryPermissionRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var httpContext = httpContextAccessor.HttpContext;
        var user = httpContext?.User;
        var results = new List<QueryPermissionResult>();

        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (user?.Identity?.IsAuthenticated != true)
            {
                results.Add(QueryPermissionResult.Denied(
                    request.ActionKey,
                    request.RequiredPolicy,
                    QueryPermissionDeniedReasonCodes.NotAuthenticated,
                    "Authentication is required."));

                continue;
            }

            var authorizationResult = await authorizationService.AuthorizeAsync(
                user,
                request.Resource,
                request.RequiredPolicy);

            results.Add(authorizationResult.Succeeded
                ? QueryPermissionResult.Allowed(request.ActionKey, request.RequiredPolicy)
                : QueryPermissionResult.Denied(
                    request.ActionKey,
                    request.RequiredPolicy,
                    QueryPermissionDeniedReasonCodes.PermissionDenied,
                    "Current account is not allowed to perform this action."));
        }

        return new QueryPermissionSet(results);
    }
}
