using System.Data;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class GetClientNegativeVisitCoverageQueryHandler(
    BodyLifeDbContext dbContext,
    MembershipNegativeVisitSelector negativeVisitSelector,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        GetClientNegativeVisitCoverageQuery,
        GetClientNegativeVisitCoverageResult>
{
    public async Task<GetClientNegativeVisitCoverageResult> ExecuteAsync(
        GetClientNegativeVisitCoverageQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(
                dbContext, query.Actor, timeProvider.GetUtcNow(), cancellationToken))
        {
            return GetClientNegativeVisitCoverageResult.Denied();
        }

        if (query.ClientId == Guid.Empty)
        {
            return GetClientNegativeVisitCoverageResult.Invalid("Client id is required.", "clientId");
        }

        if (!await dbContext.Set<ClientRecord>().AsNoTracking()
                .AnyAsync(client => client.Id == query.ClientId, cancellationToken))
        {
            return GetClientNegativeVisitCoverageResult.MissingClient();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "set transaction read only",
            cancellationToken);

        var selectionResult = await negativeVisitSelector.SelectAsync(query.ClientId, cancellationToken);
        if (selectionResult.Status == MembershipNegativeVisitSelectionStatus.MissingCanonicalState)
        {
            return await CompleteAsync(GetClientNegativeVisitCoverageResult.RecalculationFailed());
        }

        if (selectionResult.Status != MembershipNegativeVisitSelectionStatus.Succeeded)
        {
            return await CompleteAsync(GetClientNegativeVisitCoverageResult.CanonicalStateInvalid());
        }

        var closures = await dbContext.Set<MembershipNegativeClosureRecord>().AsNoTracking()
            .Where(closure => closure.ClientId == query.ClientId && closure.Status == "active")
            .OrderBy(closure => closure.OccurredAt)
            .ThenBy(closure => closure.RecordedAt)
            .ThenBy(closure => closure.Id)
            .ToArrayAsync(cancellationToken);
        var closureIds = closures.Select(closure => closure.Id).ToArray();
        var lines = closureIds.Length == 0 ? [] : await dbContext.Set<MembershipNegativeClosureLineRecord>()
            .AsNoTracking().Where(line => closureIds.Contains(line.NegativeClosureId))
            .ToArrayAsync(cancellationToken);
        var items = closureIds.Length == 0 ? [] : await dbContext.Set<MembershipNegativeClosureItemRecord>()
            .AsNoTracking().Where(item => closureIds.Contains(item.NegativeClosureId) && item.Status == "active")
            .ToArrayAsync(cancellationToken);
        var payments = closureIds.Length == 0 ? [] : await dbContext.Set<PaymentRecord>().AsNoTracking()
            .Where(payment => payment.NegativeClosureId.HasValue
                && closureIds.Contains(payment.NegativeClosureId.Value)
                && payment.Status == "active")
            .ToArrayAsync(cancellationToken);

        var allMembershipIds = closures.Where(closure => closure.CoveringMembershipId.HasValue)
            .Select(closure => closure.CoveringMembershipId!.Value)
            .Concat(items.Select(item => item.SourceMembershipId))
            .Concat(items.Where(item => item.CoveringMembershipId.HasValue).Select(item => item.CoveringMembershipId!.Value))
            .Distinct().ToArray();
        var memberships = allMembershipIds.Length == 0 ? [] : await dbContext.Set<IssuedMembershipRecord>()
            .AsNoTracking().Where(membership => allMembershipIds.Contains(membership.Id))
            .ToArrayAsync(cancellationToken);
        var membershipById = memberships.ToDictionary(membership => membership.Id);
        var visitIds = items.Select(item => item.VisitId).Distinct().ToArray();
        var visits = visitIds.Length == 0 ? [] : await dbContext.Set<VisitRecord>().AsNoTracking()
            .Where(visit => visitIds.Contains(visit.Id)).ToArrayAsync(cancellationToken);
        var visitsById = visits.ToDictionary(visit => visit.Id);
        var consumptionIds = items.Select(item => item.OldConsumptionId)
            .Concat(items.Where(item => item.NewConsumptionId.HasValue).Select(item => item.NewConsumptionId!.Value))
            .Distinct().ToArray();
        var consumptions = consumptionIds.Length == 0 ? [] : await dbContext.Set<VisitConsumptionRecord>()
            .AsNoTracking().Where(consumption => consumptionIds.Contains(consumption.Id)).ToArrayAsync(cancellationToken);
        var consumptionsById = consumptions.ToDictionary(consumption => consumption.Id);
        if (!TryProjectClosures(
                query.ClientId,
                closures,
                lines,
                items,
                payments,
                membershipById,
                visitsById,
                consumptionsById,
                out var projectedClosures))
        {
            return await CompleteAsync(GetClientNegativeVisitCoverageResult.CanonicalStateInvalid());
        }

        var activeOneOffTypeRecords = await dbContext.Set<MembershipTypeRecord>().AsNoTracking()
            .Where(type => type.IsActive && type.Kind == "one_off")
            .OrderBy(type => type.Name).ThenBy(type => type.Id)
            .ToArrayAsync(cancellationToken);
        if (activeOneOffTypeRecords.Any(type => !TryMoney(type.PriceAmount, type.PriceCurrency, out _)))
        {
            return await CompleteAsync(GetClientNegativeVisitCoverageResult.CanonicalStateInvalid());
        }

        var activeOneOffTypes = activeOneOffTypeRecords.Select(type => new OneOffMembershipTypeReadModel(
            type.Id, type.Name, type.DurationDays, type.VisitsLimit,
            new Money(type.PriceAmount, type.PriceCurrency), type.UpdatedAt)).ToArray();
        var selection = selectionResult.Selection!;
        var coverage = new ClientNegativeVisitCoverageReadModel(
            query.ClientId,
            selection.TotalNegativeBalance,
            selection.UnknownNegativeBalance,
            selection.FirstNegativeVisitDate,
            selection.OpenConcreteVisits.Select(candidate => new NegativeVisitCoverageCandidateReadModel(
                candidate.VisitId, candidate.SourceMembershipId, candidate.OldConsumptionId,
                candidate.OccurredAt, candidate.ConsumptionRecordedAt, candidate.BusinessDate)).ToArray(),
            activeOneOffTypes,
            projectedClosures);
        return await CompleteAsync(GetClientNegativeVisitCoverageResult.Succeeded(
            coverage,
            BuildActions(selection.OpenConcreteVisits.Count > 0 && activeOneOffTypes.Length > 0, projectedClosures.Count > 0)));

        async Task<GetClientNegativeVisitCoverageResult> CompleteAsync(
            GetClientNegativeVisitCoverageResult result)
        {
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
    }

    private static QueryPermissionSet BuildActions(bool canCloseOneOff, bool hasActiveCoverage) => new(
    [
        canCloseOneOff
            ? QueryPermissionResult.Allowed(MembershipActionKeys.CloseNegativeVisitsOneOff, MembershipActionKeys.AdminOrOwnerPolicy)
            : QueryPermissionResult.Denied(MembershipActionKeys.CloseNegativeVisitsOneOff, MembershipActionKeys.AdminOrOwnerPolicy, "negative_coverage_unavailable", "No open negative Visit and active one-off type are available."),
        hasActiveCoverage
            ? QueryPermissionResult.Allowed(MembershipActionKeys.CorrectNegativeVisitCoverage, MembershipActionKeys.AdminOrOwnerPolicy)
            : QueryPermissionResult.Denied(MembershipActionKeys.CorrectNegativeVisitCoverage, MembershipActionKeys.AdminOrOwnerPolicy, "negative_coverage_unavailable", "No active negative coverage is available."),
    ]);

    private static bool TryProjectClosures(
        Guid clientId,
        IReadOnlyList<MembershipNegativeClosureRecord> closures,
        IReadOnlyList<MembershipNegativeClosureLineRecord> allLines,
        IReadOnlyList<MembershipNegativeClosureItemRecord> allItems,
        IReadOnlyList<PaymentRecord> allPayments,
        IReadOnlyDictionary<Guid, IssuedMembershipRecord> memberships,
        IReadOnlyDictionary<Guid, VisitRecord> visits,
        IReadOnlyDictionary<Guid, VisitConsumptionRecord> consumptions,
        out IReadOnlyList<NegativeVisitCoverageClosureReadModel> result)
    {
        result = [];
        if (allItems.GroupBy(item => item.Id).Any(group => group.Count() != 1)
            || allItems.GroupBy(item => item.VisitId).Any(group => group.Count() != 1))
        {
            return false;
        }

        var output = new List<NegativeVisitCoverageClosureReadModel>(closures.Count);
        foreach (var closure in closures)
        {
            var lines = allLines.Where(line => line.NegativeClosureId == closure.Id)
                .OrderBy(line => line.Sequence).ThenBy(line => line.Id).ToArray();
            var items = allItems.Where(item => item.NegativeClosureId == closure.Id)
                .OrderBy(item => item.Sequence).ThenBy(item => item.Id).ToArray();
            var payments = allPayments.Where(payment => payment.NegativeClosureId == closure.Id).ToArray();
            if (closure.ClientId != clientId
                || closure.Status != "active"
                || closure.VisitsCount <= 0
                || items.Length != closure.VisitsCount
                || items.Length == 0
                || items.GroupBy(item => item.Sequence).Any(group => group.Count() != 1)
                || items[0].VisitId != closure.OldestOpenNegativeVisitId
                || items.Any(item => item.ClientId != clientId || item.Status != "active"))
            {
                return false;
            }

            if (closure.ClosureType == "one_off")
            {
                if (closure.CoveringMembershipId is not null || lines.Length == 0 || payments.Length != 1)
                {
                    return false;
                }

                if (lines.GroupBy(line => line.Sequence).Any(group => group.Count() != 1)
                    || lines.Any(line => line.Quantity <= 0
                        || string.IsNullOrWhiteSpace(line.TypeNameSnapshot)
                        || line.DurationDaysSnapshot <= 0
                        || line.VisitsLimitSnapshot != 1
                        || !TryMoney(line.UnitPriceAmountSnapshot, line.CurrencySnapshot, out _)
                        || !TryMoney(line.LineTotal, line.CurrencySnapshot, out _)
                        || line.LineTotal != line.UnitPriceAmountSnapshot * line.Quantity)
                    || lines.Sum(line => line.Quantity) != items.Length
                    || items.Any(item => item.ClosureLineId is null
                        || item.CoveringMembershipId is not null
                        || item.NewConsumptionId is not null)
                    || !ValidateOldFacts(clientId, items, memberships, visits, consumptions)
                    || !ValidateOneOffPayment(clientId, closure, payments[0], lines))
                {
                    return false;
                }
            }
            else if (closure.ClosureType == "new_membership")
            {
                if (closure.CoveringMembershipId is not { } coveringMembershipId
                    || lines.Length != 0
                    || payments.Length != 0
                    || !memberships.TryGetValue(coveringMembershipId, out var coveringMembership)
                    || coveringMembership.ClientId != clientId
                    || coveringMembership.Status != "active"
                    || items.Any(item => item.ClosureLineId is not null
                        || item.CoveringMembershipId != coveringMembershipId
                        || !ValidateNewConsumption(item, clientId, coveringMembershipId, consumptions))
                    || !ValidateOldFacts(clientId, items, memberships, visits, consumptions))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            var coveringSnapshot = closure.CoveringMembershipId is { } membershipId
                ? CreateMembershipSnapshot(memberships[membershipId])
                : null;
            var projectedLines = lines.Select(line => new NegativeVisitCoverageLineReadModel(
                line.Id, line.MembershipTypeId, line.TypeNameSnapshot, line.DurationDaysSnapshot,
                line.VisitsLimitSnapshot, line.Quantity,
                new Money(line.UnitPriceAmountSnapshot, line.CurrencySnapshot),
                new Money(line.LineTotal, line.CurrencySnapshot), line.Sequence)).ToArray();
            var projectedItems = items.Select(item =>
            {
                var visit = visits[item.VisitId];
                return new NegativeVisitCoverageItemReadModel(
                    item.Id, item.Sequence, item.VisitId, visit.OccurredAt,
                    BusinessTimeZone.GetBusinessDate(visit.OccurredAt), "visit",
                    item.SourceMembershipId, item.OldConsumptionId,
                    item.CoveringMembershipId, item.NewConsumptionId, item.Status);
            }).ToArray();
            var payment = payments.SingleOrDefault();
            output.Add(new NegativeVisitCoverageClosureReadModel(
                closure.Id, closure.ClosureType, closure.CoveringMembershipId, coveringSnapshot,
                closure.OldestOpenNegativeVisitId, closure.VisitsCount, closure.Comment,
                closure.OccurredAt, closure.RecordedAt, closure.RecordedByAccountId, closure.SessionId,
                closure.EntryOrigin, closure.Status, projectedLines, projectedItems,
                payment is null ? null : new NegativeVisitCoveragePaymentReadModel(
                    payment.Id, new Money(payment.Amount, payment.Currency), payment.OccurredAt,
                    payment.RecordedAt, payment.Status)));
        }

        result = output;
        return true;
    }

    private static bool ValidateOldFacts(
        Guid clientId,
        IEnumerable<MembershipNegativeClosureItemRecord> items,
        IReadOnlyDictionary<Guid, IssuedMembershipRecord> memberships,
        IReadOnlyDictionary<Guid, VisitRecord> visits,
        IReadOnlyDictionary<Guid, VisitConsumptionRecord> consumptions) => items.All(item =>
        memberships.TryGetValue(item.SourceMembershipId, out var sourceMembership)
        && sourceMembership.ClientId == clientId
        && visits.TryGetValue(item.VisitId, out var visit)
        && visit.ClientId == clientId && visit.Status == "active" && visit.VisitKind == "membership"
        && consumptions.TryGetValue(item.OldConsumptionId, out var oldConsumption)
        && oldConsumption.VisitId == item.VisitId && oldConsumption.ClientId == clientId
        && oldConsumption.MembershipId == item.SourceMembershipId && oldConsumption.VisitKind == "membership"
        && oldConsumption.ConsumptionType == "counted" && oldConsumption.SourceFactType == "visit"
        && oldConsumption.SourceFactId == item.VisitId && oldConsumption.Status == "active");

    private static bool ValidateNewConsumption(
        MembershipNegativeClosureItemRecord item,
        Guid clientId,
        Guid coveringMembershipId,
        IReadOnlyDictionary<Guid, VisitConsumptionRecord> consumptions) =>
        item.NewConsumptionId is { } newConsumptionId
        && consumptions.TryGetValue(newConsumptionId, out var newConsumption)
        && newConsumption.VisitId == item.VisitId && newConsumption.ClientId == clientId
        && newConsumption.MembershipId == coveringMembershipId && newConsumption.VisitKind == "membership"
        && newConsumption.ConsumptionType == "negative_coverage"
        && newConsumption.SourceFactType == "negative_closure_item"
        && newConsumption.SourceFactId == item.Id && newConsumption.Status == "active";

    private static bool ValidateOneOffPayment(
        Guid clientId,
        MembershipNegativeClosureRecord closure,
        PaymentRecord payment,
        IReadOnlyList<MembershipNegativeClosureLineRecord> lines)
    {
        if (payment.ClientId != clientId || payment.Status != "active" || payment.Method != "cash"
            || payment.PaymentContext != "negative_closure" || payment.NegativeClosureId != closure.Id
            || payment.MembershipId is not null || !TryMoney(payment.Amount, payment.Currency, out _))
        {
            return false;
        }

        var currency = lines[0].CurrencySnapshot;
        return lines.All(line => line.CurrencySnapshot == currency)
            && payment.Currency == currency
            && payment.Amount == lines.Sum(line => line.LineTotal);
    }

    private static IssuedMembershipCoverageSnapshotReadModel CreateMembershipSnapshot(IssuedMembershipRecord membership) => new(
        membership.Id, membership.MembershipTypeId, membership.TypeNameSnapshot,
        membership.DurationDaysSnapshot, membership.VisitsLimitSnapshot,
        new Money(membership.PriceAmountSnapshot, membership.PriceCurrencySnapshot),
        membership.StartDate, membership.BaseEndDate, membership.IssuedAt, membership.Status);

    private static bool TryMoney(decimal amount, string? currency, out Money money)
    {
        try
        {
            money = new Money(amount, currency!);
            return true;
        }
        catch (ArgumentException)
        {
            money = default;
            return false;
        }
    }
}
