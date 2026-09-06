using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class MembershipIssuePredecessorReader
{
    internal static async Task<MembershipIssuePredecessor?> ReadAsync(
        BodyLifeDbContext dbContext,
        IssuedMembershipRecord? predecessor,
        CancellationToken cancellationToken)
    {
        if (predecessor is null)
        {
            return null;
        }

        // xmin changes even when the recalculated signed balance stays the same.
        // RecalculationVersion describes the formula, not this source/cache state.
        var version = await dbContext.Database.SqlQuery<string>(
                $"""
                select membership.xmin::text || ':' || cache.xmin::text as "Value"
                from bodylife.issued_memberships membership
                join bodylife.membership_state_cache cache on cache.membership_id = membership.id
                where membership.id = {predecessor.Id}
                """)
            .SingleAsync(cancellationToken);
        var calculation = await new MembershipStateCacheRebuilder(dbContext, TimeProvider.System)
            .CalculateCanonicalStateAfterMembershipLockAsync(predecessor, cancellationToken);
        return new MembershipIssuePredecessor(
            predecessor.Id,
            IssuedMembershipLifecycleStatus.Active,
            version,
            calculation.State.RemainingVisits);
    }
}
