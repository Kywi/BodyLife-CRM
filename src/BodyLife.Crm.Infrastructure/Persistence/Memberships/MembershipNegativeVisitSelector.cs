using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipNegativeVisitSelector(BodyLifeDbContext dbContext)
{
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

        var memberships = await dbContext.Set<IssuedMembershipRecord>()
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
        if (memberships.Length == 0)
        {
            return MembershipNegativeVisitSelectionResult.Succeeded(
                new MembershipNegativeVisitSelection([], [], 0, 0));
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

        var lockedVisits = await dbContext.Set<VisitRecord>()
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
        var visitsById = lockedVisits.ToDictionary(visit => visit.Id);

        var originalConsumptions = await dbContext.Set<VisitConsumptionRecord>()
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
        var candidates = new List<MembershipNegativeVisitCandidate>();
        long totalNegativeBalance = 0;

        foreach (var membership in memberships)
        {
            var cache = cachesByMembershipId[membership.Id];
            totalNegativeBalance += cache.NegativeBalance;
            if (totalNegativeBalance > int.MaxValue)
            {
                return MembershipNegativeVisitSelectionResult.InconsistentCanonicalState();
            }

            if (cache.NegativeBalance == 0)
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
                    new MembershipNegativeVisitCandidate(
                        visit.Id,
                        clientId,
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
            var negativeTailCount = Math.Min(cache.NegativeBalance, orderedEvents.Length);
            var negativeEvents = orderedEvents[^negativeTailCount..];
            if (cache.FirstNegativeVisitId is { } firstNegativeVisitId
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
        return MembershipNegativeVisitSelectionResult.Succeeded(
            new MembershipNegativeVisitSelection(
                memberships,
                orderedCandidates,
                total,
                total - orderedCandidates.Length));
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
        MembershipNegativeVisitCandidate? Candidate);
}

internal sealed record MembershipNegativeVisitCandidate(
    Guid VisitId,
    Guid ClientId,
    Guid SourceMembershipId,
    Guid OldConsumptionId,
    DateTimeOffset OccurredAt,
    DateTimeOffset ConsumptionRecordedAt,
    DateOnly BusinessDate);

internal sealed record MembershipNegativeVisitSelection(
    IReadOnlyList<IssuedMembershipRecord> ActiveMemberships,
    IReadOnlyList<MembershipNegativeVisitCandidate> OpenConcreteVisits,
    int TotalNegativeBalance,
    int UnknownNegativeBalance)
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
