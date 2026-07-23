using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class GetReceptionAttentionCountsQueryHandler(BodyLifeDbContext dbContext, TimeProvider timeProvider) : IBodyLifeQueryHandler<GetReceptionAttentionCountsQuery, GetReceptionAttentionCountsResult>
{
    public async Task<GetReceptionAttentionCountsResult> ExecuteAsync(GetReceptionAttentionCountsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(dbContext, query.Actor, timeProvider.GetUtcNow(), cancellationToken))
            return GetReceptionAttentionCountsResult.Failure(GetReceptionAttentionCountsStatus.PermissionDenied, "permission_denied", "An active Owner, named Admin or shared Reception/Admin session is required.");
        if (query.AsOfDate == default || query.AsOfDate == DateOnly.MaxValue)
            return GetReceptionAttentionCountsResult.Failure(GetReceptionAttentionCountsStatus.ValidationFailed, "validation_failed", "As-of date is required.", "asOfDate");
        if (query.EndingSoonDaysThreshold is < 0 or > GetEndingSoonMembershipStateRowsQuery.MaxDaysThreshold || query.AsOfDate.DayNumber > DateOnly.MaxValue.DayNumber - query.EndingSoonDaysThreshold)
            return GetReceptionAttentionCountsResult.Failure(GetReceptionAttentionCountsStatus.ValidationFailed, "validation_failed", "Ending-soon days threshold is outside the supported range.", "endingSoonDaysThreshold");
        var through = query.AsOfDate.AddDays(query.EndingSoonDaysThreshold);
        var activeMemberships = dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
            .Where(x => x.Status == MembershipQuerySupport.ActiveMembershipStatus);
        if (await activeMemberships.AnyAsync(m => !dbContext.Set<MembershipStateCacheRecord>().Any(c => c.MembershipId == m.Id && c.RecalculationVersion == MembershipStateCacheRebuilder.CurrentRecalculationVersion), cancellationToken))
            return GetReceptionAttentionCountsResult.Failure(GetReceptionAttentionCountsStatus.RecalculationFailed, "recalculation_failed", "Membership state is unavailable because recalculation has not completed successfully.");
        var cache = dbContext.Set<MembershipStateCacheRecord>().AsNoTracking();
        var endingSoon = await (from m in activeMemberships join c in cache on m.Id equals c.MembershipId where c.RecalculationVersion == MembershipStateCacheRebuilder.CurrentRecalculationVersion && c.EffectiveEndDate >= query.AsOfDate && c.EffectiveEndDate <= through select m.Id).CountAsync(cancellationToken);
        var negativeClients = await (from m in activeMemberships join c in cache on m.Id equals c.MembershipId where c.RecalculationVersion == MembershipStateCacheRebuilder.CurrentRecalculationVersion && c.NegativeBalance > 0 select m.ClientId).Distinct().CountAsync(cancellationToken);
        return GetReceptionAttentionCountsResult.Success(endingSoon, negativeClients);
    }
}
