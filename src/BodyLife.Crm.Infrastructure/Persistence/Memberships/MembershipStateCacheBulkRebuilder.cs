using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

/// <summary>
/// Rebuilds every issued Membership cache as an operational derived-state repair.
/// Each Membership keeps the canonical rebuilder's own transaction boundary.
/// </summary>
public sealed class MembershipStateCacheBulkRebuilder(
    BodyLifeDbContext dbContext,
    MembershipStateCacheRebuilder rebuilder)
{
    public async Task<MembershipStateCacheBulkRebuildResult> RebuildAllAsync(
        CancellationToken cancellationToken = default)
    {
        var membershipIds = await dbContext.Set<IssuedMembershipRecord>()
            .AsNoTracking()
            .OrderBy(membership => membership.Id)
            .Select(membership => membership.Id)
            .ToArrayAsync(cancellationToken);

        var created = 0;
        var repaired = 0;
        var verified = 0;

        foreach (var membershipId in membershipIds)
        {
            var result = await rebuilder.RebuildAsync(membershipId, cancellationToken);
            if (!result.Succeeded)
            {
                dbContext.ChangeTracker.Clear();
                return MembershipStateCacheBulkRebuildResult.MissingSource(
                    membershipId,
                    membershipIds.Length,
                    created,
                    repaired,
                    verified);
            }

            switch (result.Status)
            {
                case MembershipStateCacheRebuildStatus.Created:
                    created++;
                    break;
                case MembershipStateCacheRebuildStatus.Repaired:
                    repaired++;
                    break;
                case MembershipStateCacheRebuildStatus.Verified:
                    verified++;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported Membership cache rebuild status: {result.Status}.");
            }

            dbContext.ChangeTracker.Clear();
        }

        return MembershipStateCacheBulkRebuildResult.Completed(
            membershipIds.Length,
            created,
            repaired,
            verified);
    }
}
