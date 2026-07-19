using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;

namespace BodyLife.Crm.Infrastructure.Persistence.Reports;

public sealed class ListNegativeClientsQueryHandler(
    IBodyLifeQueryHandler<
        GetNegativeMembershipStateRowsQuery,
        GetNegativeMembershipStateRowsResult> getNegativeMembershipStateRows)
    : IBodyLifeQueryHandler<ListNegativeClientsQuery, ListNegativeClientsResult>
{
    public async Task<ListNegativeClientsResult> ExecuteAsync(
        ListNegativeClientsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sourceResult = await getNegativeMembershipStateRows.ExecuteAsync(
            new GetNegativeMembershipStateRowsQuery(
                query.Actor,
                query.AsOfDate,
                query.Limit,
                query.Offset),
            cancellationToken);
        if (sourceResult.Status != GetNegativeMembershipStateRowsStatus.Success)
        {
            return MapFailure(sourceResult);
        }

        if (sourceResult.Page is null
            || !NegativeClientsPage.TryCreate(query, sourceResult.Page, out var page)
            || page is null)
        {
            return ListNegativeClientsResult.InconsistentSource();
        }

        return ListNegativeClientsResult.Succeeded(page);
    }

    private static ListNegativeClientsResult MapFailure(
        GetNegativeMembershipStateRowsResult result)
    {
        return result.Status switch
        {
            GetNegativeMembershipStateRowsStatus.PermissionDenied
                => ListNegativeClientsResult.Denied(),
            GetNegativeMembershipStateRowsStatus.ValidationFailed
                => ListNegativeClientsResult.Invalid(
                    result.ErrorMessage
                        ?? "Negative Membership state request is invalid.",
                    result.ErrorField),
            GetNegativeMembershipStateRowsStatus.RecalculationFailed
                => ListNegativeClientsResult.RecalculationFailed(),
            _ => ListNegativeClientsResult.InconsistentSource(),
        };
    }
}
