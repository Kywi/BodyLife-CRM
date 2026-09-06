using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class NegativeVisitCoverageCorrectionSourceReader(
    BodyLifeDbContext dbContext)
{
    internal async Task<NegativeVisitCoverageCorrectionSourceResult> ReadAsync(
        Guid closureId,
        CancellationToken cancellationToken)
    {
        var closure = await dbContext.Set<MembershipNegativeClosureRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.Id == closureId, cancellationToken);
        if (closure is null)
        {
            return NegativeVisitCoverageCorrectionSourceResult.Failure(
                NegativeVisitCoverageCorrectionSourceStatus.NotFound);
        }

        var hasCorrection = await dbContext.Set<MembershipNegativeClosureCorrectionRecord>()
            .AsNoTracking()
            .AnyAsync(record => record.OriginalClosureId == closureId, cancellationToken);
        if (closure.Status == "canceled")
        {
            return NegativeVisitCoverageCorrectionSourceResult.Failure(
                NegativeVisitCoverageCorrectionSourceStatus.AlreadyCanceled,
                closure.ClientId);
        }

        if (closure.Status == "replaced" || hasCorrection)
        {
            return NegativeVisitCoverageCorrectionSourceResult.Failure(
                NegativeVisitCoverageCorrectionSourceStatus.Stale,
                closure.ClientId);
        }

        if (closure.Status != "active"
            || closure.ClientId == Guid.Empty
            || closure.VisitsCount <= 0
            || closure.ClosureType is not ("one_off" or "new_membership"))
        {
            return NegativeVisitCoverageCorrectionSourceResult.Failure(
                NegativeVisitCoverageCorrectionSourceStatus.Inconsistent,
                closure.ClientId);
        }

        var lines = await dbContext.Set<MembershipNegativeClosureLineRecord>()
            .AsNoTracking()
            .Where(record => record.NegativeClosureId == closureId)
            .OrderBy(record => record.Sequence)
            .ThenBy(record => record.Id)
            .ToArrayAsync(cancellationToken);
        var items = await dbContext.Set<MembershipNegativeClosureItemRecord>()
            .AsNoTracking()
            .Where(record => record.NegativeClosureId == closureId)
            .OrderBy(record => record.Sequence)
            .ThenBy(record => record.Id)
            .ToArrayAsync(cancellationToken);
        var payments = await dbContext.Set<PaymentRecord>()
            .AsNoTracking()
            .Where(record => record.NegativeClosureId == closureId
                && record.PaymentContext == "negative_closure")
            .ToArrayAsync(cancellationToken);
        if (items.Length != closure.VisitsCount
            || items.Length == 0
            || items.Any(item => item.Status != "active"
                || item.ClientId != closure.ClientId)
            || !HasContiguousSequence(items, item => item.Sequence)
            || items.Select(item => item.VisitId).Distinct().Count() != items.Length
            || items[0].VisitId != closure.OldestOpenNegativeVisitId)
        {
            return NegativeVisitCoverageCorrectionSourceResult.Failure(
                NegativeVisitCoverageCorrectionSourceStatus.Inconsistent,
                closure.ClientId);
        }

        var membershipIds = items.Select(item => item.SourceMembershipId)
            .Concat(closure.CoveringMembershipId is { } coveringId ? [coveringId] : [])
            .Distinct()
            .ToArray();
        var memberships = await dbContext.Set<IssuedMembershipRecord>()
            .AsNoTracking()
            .Where(record => membershipIds.Contains(record.Id))
            .ToArrayAsync(cancellationToken);
        var membershipsById = memberships.ToDictionary(record => record.Id);
        var visitIds = items.Select(item => item.VisitId).ToArray();
        var visits = await dbContext.Set<VisitRecord>()
            .AsNoTracking()
            .Where(record => visitIds.Contains(record.Id))
            .ToArrayAsync(cancellationToken);
        var visitsById = visits.ToDictionary(record => record.Id);
        var consumptionIds = items.Select(item => item.OldConsumptionId)
            .Concat(items.Where(item => item.NewConsumptionId.HasValue)
                .Select(item => item.NewConsumptionId!.Value))
            .Distinct()
            .ToArray();
        var consumptions = await dbContext.Set<VisitConsumptionRecord>()
            .AsNoTracking()
            .Where(record => consumptionIds.Contains(record.Id))
            .ToArrayAsync(cancellationToken);
        var consumptionsById = consumptions.ToDictionary(record => record.Id);

        if (memberships.Length != membershipIds.Length
            || visits.Length != visitIds.Length
            || consumptions.Length != consumptionIds.Length
            || !ValidateOldFacts(
                closure.ClientId,
                items,
                membershipsById,
                visitsById,
                consumptionsById))
        {
            return NegativeVisitCoverageCorrectionSourceResult.Failure(
                NegativeVisitCoverageCorrectionSourceStatus.Inconsistent,
                closure.ClientId);
        }

        IssuedMembershipRecord? coveringMembership = null;
        MembershipStateCacheRecord? coveringCache = null;
        if (closure.ClosureType == "one_off")
        {
            if (closure.CoveringMembershipId is not null
                || payments.Length != 1
                || !ValidateOneOffLinesAndPayment(
                    closure,
                    lines,
                    items,
                    payments[0]))
            {
                return NegativeVisitCoverageCorrectionSourceResult.Failure(
                    NegativeVisitCoverageCorrectionSourceStatus.Inconsistent,
                    closure.ClientId);
            }
        }
        else
        {
            if (closure.CoveringMembershipId is not { } coveringMembershipId
                || lines.Length != 0
                || payments.Length != 0
                || !membershipsById.TryGetValue(
                    coveringMembershipId,
                    out coveringMembership)
                || coveringMembership.ClientId != closure.ClientId
                || coveringMembership.Status is not ("active" or "closed")
                || items.Any(item => item.ClosureLineId is not null
                    || item.CoveringMembershipId != coveringMembershipId
                    || !ValidateNewConsumption(
                        item,
                        closure.ClientId,
                        coveringMembershipId,
                        consumptionsById)))
            {
                return NegativeVisitCoverageCorrectionSourceResult.Failure(
                    NegativeVisitCoverageCorrectionSourceStatus.Inconsistent,
                    closure.ClientId);
            }

            coveringCache = await dbContext.Set<MembershipStateCacheRecord>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    record => record.MembershipId == coveringMembershipId,
                    cancellationToken);
            if (coveringCache is null
                || coveringCache.RecalculationVersion
                    != MembershipStateCacheRebuilder.CurrentRecalculationVersion)
            {
                return NegativeVisitCoverageCorrectionSourceResult.Failure(
                    NegativeVisitCoverageCorrectionSourceStatus.MissingCanonicalState,
                    closure.ClientId);
            }
        }

        var restoredVisits = items.Select(item =>
        {
            var visit = visitsById[item.VisitId];
            var oldConsumption = consumptionsById[item.OldConsumptionId];
            return new NegativeVisitCoverageCandidateReadModel(
                visit.Id,
                item.SourceMembershipId,
                item.OldConsumptionId,
                visit.OccurredAt,
                oldConsumption.RecordedAt,
                BusinessTimeZone.GetBusinessDate(visit.OccurredAt));
        }).ToArray();
        var originalLines = lines.Select(line => new NegativeVisitCoverageLineReadModel(
            line.Id,
            line.MembershipTypeId,
            line.TypeNameSnapshot,
            line.DurationDaysSnapshot,
            line.VisitsLimitSnapshot,
            line.Quantity,
            new Money(line.UnitPriceAmountSnapshot, line.CurrencySnapshot),
            new Money(line.LineTotal, line.CurrencySnapshot),
            line.Sequence)).ToArray();
        var payment = payments.SingleOrDefault();
        var source = new NegativeVisitCoverageCorrectionSource(
            closure,
            originalLines,
            restoredVisits,
            payment,
            coveringMembership,
            coveringCache);
        return NegativeVisitCoverageCorrectionSourceResult.Completed(source);
    }

    private static bool ValidateOldFacts(
        Guid clientId,
        IReadOnlyCollection<MembershipNegativeClosureItemRecord> items,
        IReadOnlyDictionary<Guid, IssuedMembershipRecord> memberships,
        IReadOnlyDictionary<Guid, VisitRecord> visits,
        IReadOnlyDictionary<Guid, VisitConsumptionRecord> consumptions) => items.All(item =>
        memberships.TryGetValue(item.SourceMembershipId, out var membership)
        && membership.ClientId == clientId
        && membership.Status is "active" or "closed"
        && visits.TryGetValue(item.VisitId, out var visit)
        && visit.ClientId == clientId
        && visit.Status == "active"
        && visit.VisitKind == "membership"
        && consumptions.TryGetValue(item.OldConsumptionId, out var consumption)
        && consumption.VisitId == item.VisitId
        && consumption.ClientId == clientId
        && consumption.MembershipId == item.SourceMembershipId
        && consumption.VisitKind == "membership"
        && consumption.ConsumptionType == "counted"
        && consumption.SourceFactType == "visit"
        && consumption.SourceFactId == item.VisitId
        && consumption.Status == "active");

    private static bool ValidateNewConsumption(
        MembershipNegativeClosureItemRecord item,
        Guid clientId,
        Guid coveringMembershipId,
        IReadOnlyDictionary<Guid, VisitConsumptionRecord> consumptions) =>
        item.NewConsumptionId is { } consumptionId
        && consumptions.TryGetValue(consumptionId, out var consumption)
        && consumption.VisitId == item.VisitId
        && consumption.ClientId == clientId
        && consumption.MembershipId == coveringMembershipId
        && consumption.VisitKind == "membership"
        && consumption.ConsumptionType == "negative_coverage"
        && consumption.SourceFactType == "negative_closure_item"
        && consumption.SourceFactId == item.Id
        && consumption.Status == "active";

    private static bool ValidateOneOffLinesAndPayment(
        MembershipNegativeClosureRecord closure,
        IReadOnlyList<MembershipNegativeClosureLineRecord> lines,
        IReadOnlyCollection<MembershipNegativeClosureItemRecord> items,
        PaymentRecord payment)
    {
        if (lines.Count == 0
            || !HasContiguousSequence(lines, line => line.Sequence)
            || lines.Any(line => line.Quantity <= 0
                || string.IsNullOrWhiteSpace(line.TypeNameSnapshot)
                || line.DurationDaysSnapshot <= 0
                || line.VisitsLimitSnapshot != 1
                || !NegativeVisitCoveragePreviewSupport.TryMoney(
                    line.UnitPriceAmountSnapshot,
                    line.CurrencySnapshot,
                    out _)
                || !NegativeVisitCoveragePreviewSupport.TryMoney(
                    line.LineTotal,
                    line.CurrencySnapshot,
                    out _)
                || line.LineTotal != line.UnitPriceAmountSnapshot * line.Quantity)
            || lines.Sum(line => line.Quantity) != items.Count
            || items.Any(item => item.ClosureLineId is null
                || item.CoveringMembershipId is not null
                || item.NewConsumptionId is not null)
            || lines.Any(line => items.Count(item => item.ClosureLineId == line.Id)
                != line.Quantity)
            || payment.ClientId != closure.ClientId
            || payment.Status != "active"
            || payment.Method != "cash"
            || payment.PaymentContext != "negative_closure"
            || payment.NegativeClosureId != closure.Id
            || payment.MembershipId is not null
            || payment.OccurredAt != closure.OccurredAt
            || payment.RecordedAt != closure.RecordedAt
            || payment.RecordedByAccountId != closure.RecordedByAccountId
            || payment.SessionId != closure.SessionId
            || payment.EntryOrigin != closure.EntryOrigin
            || payment.EntryBatchId != closure.EntryBatchId
            || !NegativeVisitCoveragePreviewSupport.TryMoney(
                payment.Amount,
                payment.Currency,
                out _))
        {
            return false;
        }

        var currency = lines[0].CurrencySnapshot;
        return lines.All(line => line.CurrencySnapshot == currency)
            && payment.Currency == currency
            && payment.Amount == lines.Sum(line => line.LineTotal);
    }

    private static bool HasContiguousSequence<T>(
        IReadOnlyList<T> records,
        Func<T, int> sequenceSelector) => records
        .Select(sequenceSelector)
        .SequenceEqual(Enumerable.Range(1, records.Count));
}

internal enum NegativeVisitCoverageCorrectionSourceStatus
{
    Prepared = 1,
    NotFound,
    AlreadyCanceled,
    Stale,
    MissingCanonicalState,
    Inconsistent,
}

internal sealed record NegativeVisitCoverageCorrectionSourceResult(
    NegativeVisitCoverageCorrectionSourceStatus Status,
    NegativeVisitCoverageCorrectionSource? Source,
    Guid? ClientId)
{
    internal static NegativeVisitCoverageCorrectionSourceResult Completed(
        NegativeVisitCoverageCorrectionSource source) =>
        new(NegativeVisitCoverageCorrectionSourceStatus.Prepared, source, source.Closure.ClientId);

    internal static NegativeVisitCoverageCorrectionSourceResult Failure(
        NegativeVisitCoverageCorrectionSourceStatus status,
        Guid? clientId = null) => new(status, null, clientId);
}

internal sealed record NegativeVisitCoverageCorrectionSource(
    MembershipNegativeClosureRecord Closure,
    IReadOnlyList<NegativeVisitCoverageLineReadModel> OriginalLines,
    IReadOnlyList<NegativeVisitCoverageCandidateReadModel> RestoredVisits,
    PaymentRecord? OriginalPayment,
    IssuedMembershipRecord? CoveringMembership,
    MembershipStateCacheRecord? CoveringCache);
