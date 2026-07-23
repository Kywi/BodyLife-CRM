using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;

namespace BodyLife.Crm.Infrastructure.Persistence.Reports;

public sealed class GetReceptionAttentionSummaryQueryHandler(IBodyLifeQueryHandler<GetReceptionAttentionCountsQuery, GetReceptionAttentionCountsResult> counts) : IBodyLifeQueryHandler<GetReceptionAttentionSummaryQuery, GetReceptionAttentionSummaryResult>
{
    public async Task<GetReceptionAttentionSummaryResult> ExecuteAsync(GetReceptionAttentionSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = await counts.ExecuteAsync(new GetReceptionAttentionCountsQuery(query.Actor, query.AsOfDate, query.EndingSoonDaysThreshold), cancellationToken);
        if (result.Status == GetReceptionAttentionCountsStatus.Success
            && result.EndingSoonMembershipCount is { } endingSoon
            && result.NegativeClientCount is { } negativeClients)
        {
            return GetReceptionAttentionSummaryResult.Success(
                ReceptionAttentionSummary.Create(
                    endingSoon,
                    negativeClients,
                    ReceptionAttentionDestination.EndingSoon,
                    ReceptionAttentionDestination.NegativeClients));
        }

        var message = result.ErrorMessage
            ?? "Reception attention is unavailable because canonical source records are inconsistent.";
        return result.Status switch
        {
            GetReceptionAttentionCountsStatus.PermissionDenied
                => GetReceptionAttentionSummaryResult.PermissionDenied(message),
            GetReceptionAttentionCountsStatus.ValidationFailed
                => GetReceptionAttentionSummaryResult.ValidationFailed(message, result.ErrorField),
            GetReceptionAttentionCountsStatus.RecalculationFailed
                => GetReceptionAttentionSummaryResult.RecalculationFailed(message),
            _ => GetReceptionAttentionSummaryResult.SourceInconsistent(message, result.ErrorField),
        };
    }
}
