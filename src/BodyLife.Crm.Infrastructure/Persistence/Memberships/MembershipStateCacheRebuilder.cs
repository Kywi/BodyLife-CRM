using System.Data;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipStateCacheRebuilder
{
    private readonly BodyLifeDbContext dbContext;
    private readonly IReadOnlyList<IMembershipExtensionSourceProvider>
        extensionSourceProviders;
    private readonly TimeProvider timeProvider;

    public MembershipStateCacheRebuilder(
        BodyLifeDbContext dbContext,
        TimeProvider timeProvider,
        IEnumerable<IMembershipExtensionSourceProvider>? extensionSourceProviders = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var providers = extensionSourceProviders?.ToArray() ?? [];
        if (providers.Any(provider => provider is null))
        {
            throw new ArgumentException(
                "Membership extension source providers cannot contain a missing item.",
                nameof(extensionSourceProviders));
        }

        this.dbContext = dbContext;
        this.timeProvider = timeProvider;
        this.extensionSourceProviders = providers;
    }

    public const int CurrentRecalculationVersion = 7;

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

        var canonical = await CalculateCanonicalStateAfterMembershipLockAsync(
            source,
            cancellationToken);
        var calculatedState = canonical.State;
        var extensionCalculation = canonical.ExtensionCalculation;
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
        if (extensionCalculation is not null)
        {
            await MembershipExtensionDayWriter.ReplaceAfterMembershipLockAsync(
                dbContext,
                membershipId,
                extensionCalculation,
                recalculatedAt,
                cancellationToken);
        }

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

    internal Task<MembershipCanonicalStateCalculation>
        CalculateCanonicalStateAfterMembershipLockAsync(
            IssuedMembershipRecord source,
            CancellationToken cancellationToken = default)
    {
        return CalculateCanonicalStateAfterMembershipLockCoreAsync(
            source,
            excludedNonWorkingDayApplicationIds: null,
            cancellationToken);
    }

    internal Task<MembershipCanonicalStateCalculation>
        CalculateCanonicalStateForNonWorkingDayReplacementAfterMembershipLockAsync(
            IssuedMembershipRecord source,
            IReadOnlySet<Guid> excludedApplicationIds,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(excludedApplicationIds);
        if (excludedApplicationIds.Any(applicationId => applicationId == Guid.Empty))
        {
            throw new ArgumentException(
                "Excluded NonWorkingDay application ids must be non-empty.",
                nameof(excludedApplicationIds));
        }

        return CalculateCanonicalStateAfterMembershipLockCoreAsync(
            source,
            excludedApplicationIds,
            cancellationToken);
    }

    private async Task<MembershipCanonicalStateCalculation>
        CalculateCanonicalStateAfterMembershipLockCoreAsync(
            IssuedMembershipRecord source,
            IReadOnlySet<Guid>? excludedNonWorkingDayApplicationIds,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Canonical Membership state calculation requires a caller-owned "
                + "database transaction and an already locked Membership source.");
        }

        var membershipId = source.Id;
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
        var adjustmentQuery = dbContext.Set<MembershipAdjustmentRecord>()
            .AsNoTracking()
            .Where(adjustment => adjustment.MembershipId == membershipId);
        if (openingStateSource is not null)
        {
            var openingRecordedAt = openingStateSource.RecordedAt;
            adjustmentQuery = adjustmentQuery.Where(
                adjustment => adjustment.RecordedAt > openingRecordedAt);
        }

        var adjustmentSources = await adjustmentQuery
            .OrderBy(adjustment => adjustment.RecordedAt)
            .ThenBy(adjustment => adjustment.Id)
            .ToArrayAsync(cancellationToken);
        var adjustmentFacts = adjustmentSources
            .Select(MapAdjustmentSource)
            .ToArray();
        var visitSourceRows = await (
            from consumption in dbContext.Set<VisitConsumptionRecord>().AsNoTracking()
            join visit in dbContext.Set<VisitRecord>().AsNoTracking()
                on consumption.VisitId equals visit.Id
            where consumption.MembershipId == membershipId
            select new MembershipVisitSourceRow(
                consumption.Id,
                consumption.VisitId,
                visit.OccurredAt,
                visit.Status,
                consumption.RecordedAt,
                consumption.Status))
            .ToArrayAsync(cancellationToken);
        var visitFacts = visitSourceRows
            .GroupBy(sourceRow => sourceRow.VisitId)
            .Select(sourceRows => MembershipVisitSourceMapper.Map(
                membershipId,
                sourceRows))
            .Where(visitFact => openingStateSource is null
                || visitFact.RecordedAt > openingStateSource.RecordedAt)
            .ToArray();
        var sourceBaseline = openingStateSource is null
            ? MembershipStateCalculator.CalculateFromVisitAndAdjustmentFacts(
                membershipId,
                issueTerms,
                visitFacts,
                adjustmentFacts)
            : MembershipStateCalculator
                .CalculateFromOpeningStateVisitAndAdjustmentFacts(
                    membershipId,
                    issueTerms,
                    MembershipOpeningState.FromStoredSource(
                        openingStateSource.OpeningAsOfDate,
                        openingStateSource.DeclaredRemainingVisits,
                        openingStateSource.DeclaredNegativeBalance,
                        openingStateSource.KnownEffectiveEndDate,
                        openingStateSource.KnownExtensionDays),
                    visitFacts,
                    adjustmentFacts);
        MembershipExtensionCalculation? extensionCalculation = null;
        var calculatedState = sourceBaseline;
        if (extensionSourceProviders.Count > 0)
        {
            var extensionSources = new List<MembershipExtensionSourceRange>();
            foreach (var provider in extensionSourceProviders)
            {
                var providerSources = await provider.GetForMembershipAsync(
                    membershipId,
                    cancellationToken);
                if (providerSources is null)
                {
                    throw new InvalidOperationException(
                        "Membership extension source provider returned no collection.");
                }

                extensionSources.AddRange(providerSources.Where(providerSource =>
                    providerSource is null
                    || excludedNonWorkingDayApplicationIds is null
                    || !string.Equals(
                        providerSource.SourceType,
                        MembershipExtensionSourceRange.NonWorkingPeriodSourceType,
                        StringComparison.Ordinal)
                    || !excludedNonWorkingDayApplicationIds.Contains(
                        providerSource.SourceId)));
            }

            extensionCalculation = MembershipExtensionCalculator.Calculate(
                extensionSources);
            calculatedState = MembershipStateCalculator
                .ApplyDateRangeExtensionCalculation(
                    issueTerms,
                    sourceBaseline,
                    extensionCalculation);
        }

        return new MembershipCanonicalStateCalculation(
            issueTerms,
            calculatedState,
            extensionCalculation);
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

    private static MembershipAdjustmentSourceFact MapAdjustmentSource(
        MembershipAdjustmentRecord source)
    {
        var status = source.Status switch
        {
            "active" => MembershipAdjustmentSourceStatus.Active,
            "canceled" => MembershipAdjustmentSourceStatus.Canceled,
            "corrected" => MembershipAdjustmentSourceStatus.Corrected,
            _ => throw new InvalidOperationException(
                $"Membership adjustment status '{source.Status}' is not supported."),
        };

        return new MembershipAdjustmentSourceFact(
            source.MembershipId,
            source.Id,
            source.AdjustmentType,
            source.DaysDelta,
            source.VisitsDelta,
            source.MoneyDelta,
            source.EffectiveDate,
            status);
    }

}
