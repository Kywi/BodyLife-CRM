using System.Data;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipStateCacheRebuilder(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
{
    public const int CurrentRecalculationVersion = 2;

    public async Task<MembershipStateCacheRebuildResult> RebuildAsync(
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        await using var ownedTransaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken)
            : null;
        var sourceRows = await dbContext.Set<IssuedMembershipRecord>()
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
            .ToArrayAsync(cancellationToken);
        var source = sourceRows.SingleOrDefault();

        if (source is null)
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }

            return MembershipStateCacheRebuildResult.MissingSource(membershipId);
        }

        var snapshot = new IssuedMembershipSnapshot(
            source.TypeNameSnapshot,
            source.DurationDaysSnapshot,
            source.VisitsLimitSnapshot,
            new Money(source.PriceAmountSnapshot, source.PriceCurrencySnapshot));
        var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
            source.MembershipTypeId,
            snapshot,
            source.StartDate,
            source.BaseEndDate);
        var openingStateSource = await dbContext.Set<MembershipOpeningStateRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                openingState => openingState.MembershipId == membershipId
                    && openingState.Status == "active",
                cancellationToken);
        var calculatedState = openingStateSource is null
            ? MembershipStateCalculator.CalculateInitial(issueTerms)
            : MembershipStateCalculator.CalculateFromOpeningState(
                issueTerms,
                MembershipOpeningState.FromStoredSource(
                    openingStateSource.OpeningAsOfDate,
                    openingStateSource.DeclaredRemainingVisits,
                    openingStateSource.DeclaredNegativeBalance,
                    openingStateSource.KnownEffectiveEndDate,
                    openingStateSource.KnownExtensionDays));
        var cache = await dbContext.Set<MembershipStateCacheRecord>()
            .SingleOrDefaultAsync(
                state => state.MembershipId == membershipId,
                cancellationToken);
        var rebuildStatus = cache switch
        {
            null => MembershipStateCacheRebuildStatus.Created,
            _ when Matches(cache, calculatedState) => MembershipStateCacheRebuildStatus.Verified,
            _ => MembershipStateCacheRebuildStatus.Repaired,
        };

        if (cache is null)
        {
            cache = new MembershipStateCacheRecord
            {
                MembershipId = membershipId,
            };
            dbContext.Set<MembershipStateCacheRecord>().Add(cache);
        }

        var recalculatedAt = timeProvider.GetUtcNow();
        ApplyCalculatedState(cache, calculatedState, recalculatedAt);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (ownedTransaction is not null)
        {
            await ownedTransaction.CommitAsync(cancellationToken);
        }

        return MembershipStateCacheRebuildResult.Completed(
            rebuildStatus,
            membershipId,
            calculatedState,
            recalculatedAt,
            CurrentRecalculationVersion);
    }

    private static bool Matches(
        MembershipStateCacheRecord cache,
        MembershipCalculatedState calculatedState)
    {
        return cache.CountedVisits == calculatedState.CountedVisits
            && cache.RemainingVisits == calculatedState.RemainingVisits
            && cache.NegativeBalance == calculatedState.NegativeBalance
            && cache.FirstNegativeVisitId == calculatedState.FirstNegativeVisitId
            && cache.FirstNegativeVisitDate == calculatedState.FirstNegativeVisitDate
            && cache.ExtensionDays == calculatedState.ExtensionDays
            && cache.EffectiveEndDate == calculatedState.EffectiveEndDate
            && cache.LastCountedVisitAt == calculatedState.LastCountedVisitAt
            && cache.RecalculationVersion == CurrentRecalculationVersion;
    }

    internal static void ApplyCalculatedState(
        MembershipStateCacheRecord cache,
        MembershipCalculatedState calculatedState,
        DateTimeOffset recalculatedAt)
    {
        cache.CountedVisits = calculatedState.CountedVisits;
        cache.RemainingVisits = calculatedState.RemainingVisits;
        cache.NegativeBalance = calculatedState.NegativeBalance;
        cache.FirstNegativeVisitId = calculatedState.FirstNegativeVisitId;
        cache.FirstNegativeVisitDate = calculatedState.FirstNegativeVisitDate;
        cache.ExtensionDays = calculatedState.ExtensionDays;
        cache.EffectiveEndDate = calculatedState.EffectiveEndDate;
        cache.LastCountedVisitAt = calculatedState.LastCountedVisitAt;
        cache.RecalculatedAt = recalculatedAt;
        cache.RecalculationVersion = CurrentRecalculationVersion;
    }
}
