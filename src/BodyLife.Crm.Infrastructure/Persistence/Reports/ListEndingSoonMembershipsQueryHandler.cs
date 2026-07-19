using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;

namespace BodyLife.Crm.Infrastructure.Persistence.Reports;

public sealed class ListEndingSoonMembershipsQueryHandler(
    IBodyLifeQueryHandler<
        GetEndingSoonMembershipStateRowsQuery,
        GetEndingSoonMembershipStateRowsResult> getEndingSoonMembershipStateRows)
    : IBodyLifeQueryHandler<
        ListEndingSoonMembershipsQuery,
        ListEndingSoonMembershipsResult>
{
    public async Task<ListEndingSoonMembershipsResult> ExecuteAsync(
        ListEndingSoonMembershipsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sourceResult = await getEndingSoonMembershipStateRows.ExecuteAsync(
            new GetEndingSoonMembershipStateRowsQuery(
                query.Actor,
                query.AsOfDate,
                query.DaysThreshold,
                query.Limit,
                query.Offset),
            cancellationToken);
        if (sourceResult.Status != GetEndingSoonMembershipStateRowsStatus.Success)
        {
            return MapFailure(sourceResult);
        }

        if (sourceResult.Page is null
            || !EndingSoonMembershipsPage.TryCreate(
                query,
                sourceResult.Page,
                out var page)
            || page is null)
        {
            return ListEndingSoonMembershipsResult.InconsistentSource();
        }

        return ListEndingSoonMembershipsResult.Succeeded(page);
    }

    private static ListEndingSoonMembershipsResult MapFailure(
        GetEndingSoonMembershipStateRowsResult result)
    {
        return result.Status switch
        {
            GetEndingSoonMembershipStateRowsStatus.PermissionDenied
                => ListEndingSoonMembershipsResult.Denied(),
            GetEndingSoonMembershipStateRowsStatus.ValidationFailed
                => ListEndingSoonMembershipsResult.Invalid(
                    result.ErrorMessage
                        ?? "Ending-soon Membership state request is invalid.",
                    result.ErrorField),
            GetEndingSoonMembershipStateRowsStatus.RecalculationFailed
                => ListEndingSoonMembershipsResult.RecalculationFailed(),
            _ => ListEndingSoonMembershipsResult.InconsistentSource(),
        };
    }
}
