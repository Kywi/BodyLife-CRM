using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;

namespace BodyLife.Crm.Infrastructure.Persistence.Reports;

public sealed class ListLowRemainingMembershipsQueryHandler(
    IBodyLifeQueryHandler<
        GetLowRemainingMembershipStateRowsQuery,
        GetLowRemainingMembershipStateRowsResult> getLowRemainingMembershipStateRows)
    : IBodyLifeQueryHandler<
        ListLowRemainingMembershipsQuery,
        ListLowRemainingMembershipsResult>
{
    public async Task<ListLowRemainingMembershipsResult> ExecuteAsync(
        ListLowRemainingMembershipsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sourceResult = await getLowRemainingMembershipStateRows.ExecuteAsync(
            new GetLowRemainingMembershipStateRowsQuery(
                query.Actor,
                query.AsOfDate,
                query.RemainingVisitsThreshold,
                query.Limit,
                query.Offset),
            cancellationToken);
        if (sourceResult.Status != GetLowRemainingMembershipStateRowsStatus.Success)
        {
            return MapFailure(sourceResult);
        }

        if (sourceResult.Page is null
            || !LowRemainingMembershipsPage.TryCreate(
                query,
                sourceResult.Page,
                out var page)
            || page is null)
        {
            return ListLowRemainingMembershipsResult.InconsistentSource();
        }

        return ListLowRemainingMembershipsResult.Succeeded(page);
    }

    private static ListLowRemainingMembershipsResult MapFailure(
        GetLowRemainingMembershipStateRowsResult result)
    {
        return result.Status switch
        {
            GetLowRemainingMembershipStateRowsStatus.PermissionDenied
                => ListLowRemainingMembershipsResult.Denied(),
            GetLowRemainingMembershipStateRowsStatus.ValidationFailed
                => ListLowRemainingMembershipsResult.Invalid(
                    result.ErrorMessage
                        ?? "Low-remaining Membership state request is invalid.",
                    result.ErrorField),
            GetLowRemainingMembershipStateRowsStatus.RecalculationFailed
                => ListLowRemainingMembershipsResult.RecalculationFailed(),
            _ => ListLowRemainingMembershipsResult.InconsistentSource(),
        };
    }
}
