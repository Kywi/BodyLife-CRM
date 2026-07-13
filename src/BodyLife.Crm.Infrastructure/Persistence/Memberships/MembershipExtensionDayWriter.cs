using System.Data;
using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipExtensionDayWriter(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<MembershipExtensionDayWriteResult> ReplaceAsync(
        Guid membershipId,
        MembershipExtensionCalculation? calculation,
        CancellationToken cancellationToken = default)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        ArgumentNullException.ThrowIfNull(calculation);

        await using var ownedTransaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken)
            : null;
        var membershipRows = await dbContext.Set<IssuedMembershipRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    id,
                    client_id,
                    membership_type_id,
                    type_name_snapshot,
                    duration_days_snapshot,
                    visits_limit_snapshot,
                    price_amount_snapshot,
                    price_currency_snapshot,
                    start_date,
                    base_end_date,
                    issued_at,
                    issued_by_account_id,
                    status,
                    entry_origin,
                    entry_batch_id,
                    comment
                from bodylife.issued_memberships
                where id = {membershipId}
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        if (membershipRows.Length == 0)
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }

            return MembershipExtensionDayWriteResult.MissingMembership(membershipId);
        }

        var recalculatedAt = timeProvider.GetUtcNow();
        var persistedRowCount = await ReplaceAfterMembershipLockAsync(
            dbContext,
            membershipId,
            calculation,
            recalculatedAt,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (ownedTransaction is not null)
        {
            await ownedTransaction.CommitAsync(cancellationToken);
        }

        return MembershipExtensionDayWriteResult.Replaced(
            membershipId,
            calculation.ExtensionDays,
            persistedRowCount,
            recalculatedAt);
    }

    internal static async Task<int> ReplaceAfterMembershipLockAsync(
        BodyLifeDbContext dbContext,
        Guid membershipId,
        MembershipExtensionCalculation calculation,
        DateTimeOffset recalculatedAt,
        CancellationToken cancellationToken)
    {
        foreach (var entry in dbContext.ChangeTracker
                     .Entries<MembershipExtensionDayRecord>()
                     .Where(entry => entry.Entity.MembershipId == membershipId)
                     .ToArray())
        {
            entry.State = EntityState.Detached;
        }

        await dbContext.Set<MembershipExtensionDayRecord>()
            .Where(extensionDay => extensionDay.MembershipId == membershipId)
            .ExecuteDeleteAsync(cancellationToken);

        var replacementRows = calculation.ExplanationDays
            .Select(extensionDay => new MembershipExtensionDayRecord
            {
                Id = Guid.NewGuid(),
                MembershipId = membershipId,
                ExtensionDate = extensionDay.ExtensionDate,
                SourceType = extensionDay.SourceType,
                SourceId = extensionDay.SourceId,
                SourceLabel = extensionDay.SourceLabel,
                IsActive = extensionDay.IsActive,
                RecalculatedAt = recalculatedAt,
            })
            .ToArray();
        dbContext.Set<MembershipExtensionDayRecord>().AddRange(replacementRows);

        return replacementRows.Length;
    }
}
