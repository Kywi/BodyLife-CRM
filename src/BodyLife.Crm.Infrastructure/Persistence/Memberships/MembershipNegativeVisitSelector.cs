using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipNegativeVisitSelector(BodyLifeDbContext dbContext)
{
    internal Task<MembershipNegativeVisitSelectionResult> SelectAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        return SelectCoreAsync(
            clientId,
            forUpdate: false,
            excludedNegativeClosureId: null,
            cancellationToken);
    }

    internal Task<MembershipNegativeVisitSelectionResult>
        SelectHypotheticallyWithoutClosureAsync(
            Guid clientId,
            Guid excludedNegativeClosureId,
            CancellationToken cancellationToken)
    {
        if (excludedNegativeClosureId == Guid.Empty)
        {
            throw new ArgumentException(
                "Excluded negative closure id is required.",
                nameof(excludedNegativeClosureId));
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Hypothetical negative Visit selection requires a caller-owned transaction.");
        }

        return SelectCoreAsync(
            clientId,
            forUpdate: false,
            excludedNegativeClosureId,
            cancellationToken);
    }

    internal async Task<MembershipNegativeVisitSelectionResult>
        SelectForUpdateAfterClientLockAsync(
            Guid clientId,
            CancellationToken cancellationToken)
    {
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Negative Visit selection requires a caller-owned transaction and a locked Client.");
        }

        return await SelectCoreAsync(
            clientId,
            forUpdate: true,
            excludedNegativeClosureId: null,
            cancellationToken);
    }

    private async Task<MembershipNegativeVisitSelectionResult> SelectCoreAsync(
        Guid clientId,
        bool forUpdate,
        Guid? excludedNegativeClosureId,
        CancellationToken cancellationToken)
    {
        IssuedMembershipRecord[] memberships;
        if (forUpdate)
        {
            memberships = await dbContext.Set<IssuedMembershipRecord>()
                .FromSqlInterpolated(
                    $"""
                    select *
                    from bodylife.issued_memberships
                    where client_id = {clientId}
                      and status = 'active'
                    order by id
                    for update
                    """)
                .AsNoTracking()
                .ToArrayAsync(cancellationToken);
        }
        else
        {
            memberships = await dbContext.Set<IssuedMembershipRecord>()
                .AsNoTracking()
                .Where(membership => membership.ClientId == clientId
                    && membership.Status == "active")
                .OrderBy(membership => membership.Id)
                .ToArrayAsync(cancellationToken);
        }

        if (memberships.Length == 0)
        {
            return MembershipNegativeVisitSelectionResult.Succeeded(
                new MembershipNegativeVisitSelection([], [], 0, 0, null));
        }

        var membershipIds = memberships.Select(membership => membership.Id).ToArray();
        var caches = await dbContext.Set<MembershipStateCacheRecord>()
            .AsNoTracking()
            .Where(cache => membershipIds.Contains(cache.MembershipId))
            .ToArrayAsync(cancellationToken);
        if (caches.Length != memberships.Length
            || caches.Any(cache => cache.RecalculationVersion
                != MembershipStateCacheRebuilder.CurrentRecalculationVersion))
        {
            return MembershipNegativeVisitSelectionResult.MissingCanonicalState();
        }

        VisitRecord[] lockedVisits;
        if (forUpdate)
        {
            lockedVisits = await dbContext.Set<VisitRecord>()
                .FromSqlInterpolated(
                    $"""
                    select visit.*
                    from bodylife.visits visit
                    join bodylife.visit_consumptions consumption
                      on consumption.visit_id = visit.id
                     and consumption.client_id = visit.client_id
                    where consumption.membership_id = any ({membershipIds})
                      and consumption.consumption_type = 'counted'
                      and consumption.status = 'active'
                      and visit.status = 'active'
                    order by visit.occurred_at, visit.recorded_at, visit.id
                    for update of visit
                    """)
                .AsNoTracking()
                .ToArrayAsync(cancellationToken);
        }
        else
        {
            lockedVisits = await (
                from visit in dbContext.Set<VisitRecord>().AsNoTracking()
                join consumption in dbContext.Set<VisitConsumptionRecord>()
                        .AsNoTracking()
                    on new { visit.Id, visit.ClientId }
                    equals new
                    {
                        Id = consumption.VisitId,
                        consumption.ClientId,
                    }
                where membershipIds.Contains(consumption.MembershipId)
                    && consumption.ConsumptionType == "counted"
                    && consumption.Status == "active"
                    && visit.Status == "active"
                orderby visit.OccurredAt, visit.RecordedAt, visit.Id
                select visit)
                .ToArrayAsync(cancellationToken);
        }

        var visitsById = lockedVisits.ToDictionary(visit => visit.Id);

        VisitConsumptionRecord[] originalConsumptions;
        if (forUpdate)
        {
            originalConsumptions = await dbContext.Set<VisitConsumptionRecord>()
                .FromSqlInterpolated(
                    $"""
                    select consumption.*
                    from bodylife.visit_consumptions consumption
                    where consumption.membership_id = any ({membershipIds})
                      and consumption.consumption_type = 'counted'
                      and consumption.status = 'active'
                    order by consumption.membership_id, consumption.visit_id, consumption.id
                    for update
                    """)
                .AsNoTracking()
                .ToArrayAsync(cancellationToken);
        }
        else
        {
            originalConsumptions = await dbContext.Set<VisitConsumptionRecord>()
                .AsNoTracking()
                .Where(consumption => membershipIds.Contains(consumption.MembershipId)
                    && consumption.ConsumptionType == "counted"
                    && consumption.Status == "active")
                .OrderBy(consumption => consumption.MembershipId)
                .ThenBy(consumption => consumption.VisitId)
                .ThenBy(consumption => consumption.Id)
                .ToArrayAsync(cancellationToken);
        }
        if (originalConsumptions.Any(consumption =>
                !visitsById.ContainsKey(consumption.VisitId)))
        {
            return MembershipNegativeVisitSelectionResult.InconsistentCanonicalState();
        }

        var coverageRows = await (
            from item in dbContext.Set<MembershipNegativeClosureItemRecord>()
                .AsNoTracking()
            join closure in dbContext.Set<MembershipNegativeClosureRecord>()
                .AsNoTracking()
                on item.NegativeClosureId equals closure.Id
            where item.ClientId == clientId
                && item.Status == "active"
                && closure.Status == "active"
                && (!excludedNegativeClosureId.HasValue
                    || closure.Id != excludedNegativeClosureId.Value)
            select new ActiveCoverageRow(
                item.Id,
                item.VisitId,
                item.SourceMembershipId,
                item.OldConsumptionId,
                item.CoveringMembershipId,
                item.NewConsumptionId))
            .ToArrayAsync(cancellationToken);
        var newConsumptionIds = coverageRows
            .Where(row => row.NewConsumptionId.HasValue)
            .Select(row => row.NewConsumptionId!.Value)
            .ToArray();
        var newConsumptions = newConsumptionIds.Length == 0
            ? []
            : await dbContext.Set<VisitConsumptionRecord>()
                .AsNoTracking()
                .Where(consumption => newConsumptionIds.Contains(consumption.Id))
                .ToArrayAsync(cancellationToken);
        var newConsumptionsById = newConsumptions.ToDictionary(
            consumption => consumption.Id);
        var outboundConsumptionIds = coverageRows
            .Select(row => row.OldConsumptionId)
            .ToHashSet();
        var cachesByMembershipId = caches.ToDictionary(cache => cache.MembershipId);
        var statesByMembershipId = new Dictionary<Guid, MembershipCalculatedState>(
            memberships.Length);
        var hypotheticalCalculator = excludedNegativeClosureId.HasValue
            ? new MembershipStateCacheRebuilder(dbContext, TimeProvider.System)
            : null;
        foreach (var membership in memberships)
        {
            var cache = cachesByMembershipId[membership.Id];
            try
            {
                var snapshot = new IssuedMembershipSnapshot(
                    membership.TypeNameSnapshot,
                    membership.DurationDaysSnapshot,
                    membership.VisitsLimitSnapshot,
                    new Money(
                        membership.PriceAmountSnapshot,
                        membership.PriceCurrencySnapshot));
                var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
                    membership.MembershipTypeId,
                    snapshot,
                    membership.StartDate,
                    membership.BaseEndDate);
                var storedState = MembershipCalculatedState.FromStoredCache(
                    issueTerms,
                    cache.CountedVisits,
                    cache.RemainingVisits,
                    cache.NegativeBalance,
                    cache.FirstNegativeVisitId,
                    cache.FirstNegativeVisitDate,
                    cache.ExtensionDays,
                    cache.EffectiveEndDate,
                    cache.LastCountedVisitAt);
                var selectedState = hypotheticalCalculator is null
                    ? storedState
                    : (await hypotheticalCalculator
                        .CalculateCanonicalStateForNegativeCoveragePreviewAsync(
                            membership,
                            excludedNegativeClosureId!.Value,
                            cancellationToken)).State;
                statesByMembershipId.Add(membership.Id, selectedState);
            }
            catch (Exception exception)
                when (exception is ArgumentException or InvalidOperationException)
            {
                return MembershipNegativeVisitSelectionResult
                    .InconsistentCanonicalState();
            }
        }

        var candidates = new List<MembershipNegativeVisitCoverageCandidate>();
        long totalNegativeBalance = 0;

        foreach (var membership in memberships)
        {
            var state = statesByMembershipId[membership.Id];
            totalNegativeBalance += state.NegativeBalance;
            if (totalNegativeBalance > int.MaxValue)
            {
                return MembershipNegativeVisitSelectionResult.InconsistentCanonicalState();
            }

            if (state.NegativeBalance == 0)
            {
                continue;
            }

            var effectiveEvents = new List<EffectiveVisitEvent>();
            foreach (var consumption in originalConsumptions.Where(
                         item => item.MembershipId == membership.Id
                             && !outboundConsumptionIds.Contains(item.Id)))
            {
                var visit = visitsById[consumption.VisitId];
                effectiveEvents.Add(new EffectiveVisitEvent(
                    visit.Id,
                    visit.OccurredAt,
                    consumption.RecordedAt,
                    new MembershipNegativeVisitCoverageCandidate(
                        visit.Id,
                        membership.Id,
                        consumption.Id,
                        visit.OccurredAt,
                        consumption.RecordedAt,
                        BusinessTimeZone.GetBusinessDate(visit.OccurredAt))));
            }

            foreach (var coverage in coverageRows.Where(
                         row => row.CoveringMembershipId == membership.Id))
            {
                if (coverage.NewConsumptionId is not { } newConsumptionId
                    || !newConsumptionsById.TryGetValue(
                        newConsumptionId,
                        out var newConsumption)
                    || newConsumption.Status != "active"
                    || newConsumption.ConsumptionType != "negative_coverage"
                    || newConsumption.SourceFactType != "negative_closure_item"
                    || newConsumption.SourceFactId != coverage.ItemId
                    || newConsumption.MembershipId != membership.Id
                    || !visitsById.TryGetValue(coverage.VisitId, out var visit))
                {
                    return MembershipNegativeVisitSelectionResult
                        .InconsistentCanonicalState();
                }

                effectiveEvents.Add(new EffectiveVisitEvent(
                    visit.Id,
                    visit.OccurredAt,
                    newConsumption.RecordedAt,
                    Candidate: null));
            }

            var orderedEvents = effectiveEvents
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.RecordedAt)
                .ThenBy(item => item.VisitId)
                .ToArray();
            var negativeTailCount = Math.Min(state.NegativeBalance, orderedEvents.Length);
            var negativeEvents = orderedEvents[^negativeTailCount..];
            if (state.FirstNegativeVisitId is { } firstNegativeVisitId
                && !negativeEvents.Any(item => item.VisitId == firstNegativeVisitId))
            {
                return MembershipNegativeVisitSelectionResult
                    .InconsistentCanonicalState();
            }

            candidates.AddRange(negativeEvents
                .Where(item => item.Candidate is not null)
                .Select(item => item.Candidate!));
        }

        var orderedCandidates = candidates
            .OrderBy(candidate => candidate.OccurredAt)
            .ThenBy(candidate => candidate.ConsumptionRecordedAt)
            .ThenBy(candidate => candidate.VisitId)
            .ThenBy(candidate => candidate.SourceMembershipId)
            .ToArray();
        var total = (int)totalNegativeBalance;
        var firstNegativeVisitDate = statesByMembershipId.Values
            .Where(state => state.NegativeBalance > 0
                && state.FirstNegativeVisitDate.HasValue)
            .Select(state => state.FirstNegativeVisitDate!.Value)
            .Order()
            .Cast<DateOnly?>()
            .FirstOrDefault();
        return MembershipNegativeVisitSelectionResult.Succeeded(
            new MembershipNegativeVisitSelection(
                memberships,
                orderedCandidates,
                total,
                total - orderedCandidates.Length,
                firstNegativeVisitDate));
    }

    private sealed record ActiveCoverageRow(
        Guid ItemId,
        Guid VisitId,
        Guid SourceMembershipId,
        Guid OldConsumptionId,
        Guid? CoveringMembershipId,
        Guid? NewConsumptionId);

    private sealed record EffectiveVisitEvent(
        Guid VisitId,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        MembershipNegativeVisitCoverageCandidate? Candidate);
}

internal sealed record MembershipNegativeVisitSelection(
    IReadOnlyList<IssuedMembershipRecord> ActiveMemberships,
    IReadOnlyList<MembershipNegativeVisitCoverageCandidate> OpenConcreteVisits,
    int TotalNegativeBalance,
    int UnknownNegativeBalance,
    DateOnly? FirstNegativeVisitDate)
{
    public Guid? OldestOpenConcreteVisitId => OpenConcreteVisits.Count == 0
        ? null
        : OpenConcreteVisits[0].VisitId;
}

internal enum MembershipNegativeVisitSelectionStatus
{
    Succeeded = 1,
    MissingCanonicalState,
    InconsistentCanonicalState,
}

internal sealed record MembershipNegativeVisitSelectionResult(
    MembershipNegativeVisitSelectionStatus Status,
    MembershipNegativeVisitSelection? Selection)
{
    internal static MembershipNegativeVisitSelectionResult Succeeded(
        MembershipNegativeVisitSelection selection)
    {
        return new MembershipNegativeVisitSelectionResult(
            MembershipNegativeVisitSelectionStatus.Succeeded,
            selection);
    }

    internal static MembershipNegativeVisitSelectionResult MissingCanonicalState()
    {
        return new MembershipNegativeVisitSelectionResult(
            MembershipNegativeVisitSelectionStatus.MissingCanonicalState,
            Selection: null);
    }

    internal static MembershipNegativeVisitSelectionResult InconsistentCanonicalState()
    {
        return new MembershipNegativeVisitSelectionResult(
            MembershipNegativeVisitSelectionStatus.InconsistentCanonicalState,
            Selection: null);
    }
}
