using System.Data;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipStatePersistenceCoordinator(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<MembershipStatePersistenceResult> PersistAsync(
        Guid membershipId,
        MembershipCalculatedState? calculatedState,
        MembershipExtensionCalculation? extensionCalculation,
        CancellationToken cancellationToken = default)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        ArgumentNullException.ThrowIfNull(calculatedState);
        ArgumentNullException.ThrowIfNull(extensionCalculation);

        if (calculatedState.ExtensionDays != extensionCalculation.ExtensionDays)
        {
            throw new ArgumentException(
                "Extension calculation must match the canonical state's extension days.",
                nameof(extensionCalculation));
        }

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
        var membership = membershipRows.SingleOrDefault();

        if (membership is null)
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }

            return MembershipStatePersistenceResult.MissingMembership(membershipId);
        }

        var snapshot = new IssuedMembershipSnapshot(
            membership.TypeNameSnapshot,
            membership.DurationDaysSnapshot,
            membership.VisitsLimitSnapshot,
            new Money(membership.PriceAmountSnapshot, membership.PriceCurrencySnapshot));
        var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
            membership.MembershipTypeId,
            snapshot,
            membership.StartDate,
            membership.BaseEndDate);
        MembershipCalculatedState canonicalState;

        try
        {
            canonicalState = MembershipCalculatedState.FromStoredCache(
                issueTerms,
                calculatedState.CountedVisits,
                calculatedState.RemainingVisits,
                calculatedState.NegativeBalance,
                calculatedState.FirstNegativeVisitId,
                calculatedState.FirstNegativeVisitDate,
                calculatedState.ExtensionDays,
                calculatedState.EffectiveEndDate,
                calculatedState.LastCountedVisitAt);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "Calculated state must match the selected issued membership.",
                nameof(calculatedState),
                exception);
        }

        var cache = await dbContext.Set<MembershipStateCacheRecord>()
            .SingleOrDefaultAsync(
                state => state.MembershipId == membershipId,
                cancellationToken);
        if (cache is null)
        {
            cache = new MembershipStateCacheRecord
            {
                MembershipId = membershipId,
            };
            dbContext.Set<MembershipStateCacheRecord>().Add(cache);
        }

        var recalculatedAt = timeProvider.GetUtcNow();
        MembershipStateCacheRebuilder.ApplyCalculatedState(
            cache,
            canonicalState,
            recalculatedAt);
        var persistedExtensionRowCount =
            await MembershipExtensionDayWriter.ReplaceAfterMembershipLockAsync(
                dbContext,
                membershipId,
                extensionCalculation,
                recalculatedAt,
                cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (ownedTransaction is not null)
        {
            await ownedTransaction.CommitAsync(cancellationToken);
        }

        return MembershipStatePersistenceResult.Persisted(
            membershipId,
            canonicalState,
            persistedExtensionRowCount,
            recalculatedAt);
    }
}
