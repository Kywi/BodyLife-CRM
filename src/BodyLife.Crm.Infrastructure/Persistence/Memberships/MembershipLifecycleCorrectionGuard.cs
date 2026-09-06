using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class MembershipLifecycleCorrectionGuard
{
    internal static Task<Guid?> FindBlockedVisitCancellationAsync(
        BodyLifeDbContext dbContext,
        Guid membershipId,
        Guid visitId,
        CancellationToken cancellationToken)
    {
        return FindBlockedMembershipAsync(
            dbContext, [membershipId], visitId, null, [], cancellationToken);
    }

    // Caller holds Client and affected Membership locks, or a read-only preview snapshot.
    internal static async Task<Guid?> FindBlockedMembershipAsync(
        BodyLifeDbContext dbContext,
        IReadOnlyCollection<Guid> affectedMembershipIds,
        Guid? excludedVisitId,
        Guid? excludedNegativeClosureId,
        IReadOnlyList<MembershipNegativeCoverageSourceFact> projectedCoverage,
        CancellationToken cancellationToken)
    {
        var memberships = await dbContext.Set<IssuedMembershipRecord>()
            .AsNoTracking()
            .Where(row => affectedMembershipIds.Contains(row.Id) && row.Status == "closed")
            .OrderBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var calculator = new MembershipStateCacheRebuilder(dbContext, TimeProvider.System);
        foreach (var membership in memberships)
        {
            var final = await calculator.CalculateCanonicalStateForCorrectionAsync(
                membership, excludedVisitId, excludedNegativeClosureId,
                projectedCoverage, cancellationToken);
            if (final.State.RemainingVisits > 0)
            {
                return membership.Id;
            }
        }

        return null;
    }
}
