using System.Data;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Audit;
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
        var coveringMembershipIds = closures
            .Where(closure => closure.ClosureType == "new_membership"
                && closure.CoveringMembershipId.HasValue)
            .Select(closure => closure.CoveringMembershipId!.Value)
            .Distinct()
            .ToArray();
        var payments = closureIds.Length == 0 ? [] : await dbContext.Set<PaymentRecord>().AsNoTracking()
            .Where(payment => payment.Status == "active"
                && ((payment.NegativeClosureId.HasValue
                        && closureIds.Contains(payment.NegativeClosureId.Value))
                    || (payment.MembershipId.HasValue
                        && coveringMembershipIds.Contains(payment.MembershipId.Value)
                        && payment.PaymentContext == "membership_sale")))
            .ToArrayAsync(cancellationToken);
        var replacementCorrections = closureIds.Length == 0
            ? []
            : await dbContext.Set<MembershipNegativeClosureCorrectionRecord>()
                .AsNoTracking()
                .Where(correction => correction.ReplacementClosureId.HasValue
                    && closureIds.Contains(correction.ReplacementClosureId.Value))
                .ToArrayAsync(cancellationToken);
        var paperClosureIds = closures
            .Where(closure => closure.EntryOrigin == "paper_fallback")
            .Select(closure => closure.Id)
            .ToArray();
        var creationAudits = paperClosureIds.Length == 0
            ? []
            : await dbContext.Set<BusinessAuditEntryRecord>()
                .AsNoTracking()
                .Where(audit =>
                    audit.ActionType
                        == MembershipNegativeClosureAuditActions.Created
                    && audit.EntityType
                        == MembershipNegativeClosureAuditActions.EntityType
                    && paperClosureIds.Contains(audit.EntityId))
                .ToArrayAsync(cancellationToken);

        if (closures.Any(closure => closure.EntryOrigin is not ("normal" or "paper_fallback"))
            || replacementCorrections
                .GroupBy(correction => correction.ReplacementClosureId!.Value)
                .Any(group => group.Count() != 1)
            || creationAudits.Length != paperClosureIds.Length
            || creationAudits.GroupBy(audit => audit.EntityId)
                .Any(group => group.Count() != 1))
        {
            return await CompleteAsync(
                GetClientNegativeVisitCoverageResult.CanonicalStateInvalid());
        }

        var closuresById = closures.ToDictionary(closure => closure.Id);
        var correctionsByReplacementId = replacementCorrections.ToDictionary(
            correction => correction.ReplacementClosureId!.Value);
        var creationAuditsByClosureId = creationAudits.ToDictionary(
            audit => audit.EntityId);
        if (replacementCorrections.Any(correction =>
                correction.Mode != "replace"
                || !closuresById.TryGetValue(
                    correction.ReplacementClosureId!.Value,
                    out var replacement)
                || correction.EntryOrigin != replacement.EntryOrigin
                || correction.EntryBatchId != replacement.EntryBatchId
                || correction.OccurredAt != replacement.OccurredAt
                || correction.RecordedAt != replacement.RecordedAt
                || correction.RecordedByAccountId != replacement.RecordedByAccountId
                || correction.SessionId != replacement.SessionId))
        {
            return await CompleteAsync(
                GetClientNegativeVisitCoverageResult.CanonicalStateInvalid());
        }

        var lifecycleClosures = await dbContext.Set<MembershipLifecycleClosureRecord>().AsNoTracking()
            .Where(row => row.ClientId == query.ClientId).ToArrayAsync(cancellationToken);
        var paperReferenceReader = new PaperFallbackEntryRowReferenceReader(dbContext);
        var paperReferencesByClosureId = await paperReferenceReader.LoadAsync(
            closures.Select(closure => new PaperFallbackEntryRowReferenceSource(
                closure.Id,
                closure.EntryOrigin,
                closure.EntryBatchId,
                closure.OccurredAt,
                closure.RecordedByAccountId,
                closure.SessionId,
                ExpectedPaperEventType(
                    closure,
                    correctionsByReplacementId.ContainsKey(closure.Id))))
                .ToArray(),
            MembershipNegativeClosureAuditActions.EntityType,
            PaperFallbackEventType.NegativeCoverage,
            cancellationToken);
        if (paperReferencesByClosureId is null
            || !await paperReferenceReader.HasExpectedEntityLinksAsync(
                BuildExpectedPaperLinks(
                    closures,
                    lines,
                    items,
                    payments,
                    correctionsByReplacementId,
                    paperReferencesByClosureId, lifecycleClosures),
                cancellationToken)
            || closures.Any(closure =>
                closure.EntryOrigin == "paper_fallback"
                && !HasMatchingCreationAudit(
                    closure,
                    creationAuditsByClosureId[closure.Id],
                    paperReferencesByClosureId.GetValueOrDefault(closure.Id)))
            || !await HasPreservedNewMembershipSaleProvenanceAsync(
                dbContext,
                paperReferenceReader,
                closures,
                payments,
                correctionsByReplacementId,
                cancellationToken))
        {
            return await CompleteAsync(
                GetClientNegativeVisitCoverageResult.CanonicalStateInvalid());
        }

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
                correctionsByReplacementId.Keys.ToHashSet(),
                paperReferencesByClosureId,
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
        IReadOnlySet<Guid> replacementClosureIds,
        IReadOnlyDictionary<Guid, PaperFallbackEntryRowReference> paperReferences,
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
            var closurePayments = allPayments
                .Where(payment => payment.NegativeClosureId == closure.Id)
                .ToArray();
            var salePayments = closure.CoveringMembershipId is { } paymentMembershipId
                ? allPayments.Where(payment =>
                        payment.MembershipId == paymentMembershipId
                        && payment.PaymentContext == "membership_sale")
                    .ToArray()
                : [];
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
                if (closure.CoveringMembershipId is not null
                    || lines.Length == 0
                    || closurePayments.Length != 1
                    || salePayments.Length != 0)
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
                    || !ValidateOneOffPayment(clientId, closure, closurePayments[0], lines))
                {
                    return false;
                }
            }
            else if (closure.ClosureType == "new_membership")
            {
                if (closure.CoveringMembershipId is not { } coveringMembershipId
                    || lines.Length != 0
                    || closurePayments.Length != 0
                    || salePayments.Length != 1
                    || !memberships.TryGetValue(coveringMembershipId, out var coveringMembership)
                    || coveringMembership.ClientId != clientId
                    || coveringMembership.Status is not ("active" or "closed")
                    || !ValidateNewMembershipSalePayment(
                        clientId,
                        closure,
                        coveringMembership,
                        salePayments[0],
                        requireClosureEnvelope: !replacementClosureIds.Contains(
                            closure.Id))
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
            var payment = closurePayments.SingleOrDefault();
            output.Add(new NegativeVisitCoverageClosureReadModel(
                closure.Id, closure.ClosureType, closure.CoveringMembershipId, coveringSnapshot,
                closure.OldestOpenNegativeVisitId, closure.VisitsCount, closure.Comment,
                closure.OccurredAt, closure.RecordedAt, closure.RecordedByAccountId, closure.SessionId,
                closure.EntryOrigin, closure.Status, projectedLines, projectedItems,
                payment is null ? null : new NegativeVisitCoveragePaymentReadModel(
                    payment.Id, new Money(payment.Amount, payment.Currency), payment.OccurredAt,
                    payment.RecordedAt, payment.Status),
                closure.EntryBatchId,
                paperReferences.GetValueOrDefault(closure.Id)));
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
            || payment.MembershipId is not null
            || payment.OccurredAt != closure.OccurredAt
            || payment.RecordedAt != closure.RecordedAt
            || payment.RecordedByAccountId != closure.RecordedByAccountId
            || payment.SessionId != closure.SessionId
            || payment.EntryOrigin != closure.EntryOrigin
            || payment.EntryBatchId != closure.EntryBatchId
            || !TryMoney(payment.Amount, payment.Currency, out _))
        {
            return false;
        }

        var currency = lines[0].CurrencySnapshot;
        return lines.All(line => line.CurrencySnapshot == currency)
            && payment.Currency == currency
            && payment.Amount == lines.Sum(line => line.LineTotal);
    }

    private static bool HasMatchingCreationAudit(
        MembershipNegativeClosureRecord closure,
        BusinessAuditEntryRecord audit,
        PaperFallbackEntryRowReference? paperReference) =>
        audit.ActionType == MembershipNegativeClosureAuditActions.Created
        && audit.EntityType == MembershipNegativeClosureAuditActions.EntityType
        && audit.EntityId == closure.Id
        && audit.ActorAccountId == closure.RecordedByAccountId
        && audit.SessionId == closure.SessionId
        && SamePostgreSqlInstant(audit.OccurredAt, closure.OccurredAt)
        && SamePostgreSqlInstant(audit.RecordedAt, closure.RecordedAt)
        && audit.EntryOrigin == closure.EntryOrigin
        && audit.IdempotencyKey == closure.IdempotencyKey
        && audit.Comment == closure.Comment
        && PaperFallbackEntryRowReferenceReader.HasMatchingAuditReference(
            audit.RelatedEntityRefsJson,
            closure.EntryBatchId,
            paperReference);

    private static async Task<bool>
        HasPreservedNewMembershipSaleProvenanceAsync(
            BodyLifeDbContext dbContext,
            PaperFallbackEntryRowReferenceReader paperReferenceReader,
            IReadOnlyList<MembershipNegativeClosureRecord> closures,
            IReadOnlyList<PaymentRecord> allPayments,
            IReadOnlyDictionary<Guid, MembershipNegativeClosureCorrectionRecord>
                correctionsByReplacementId,
            CancellationToken cancellationToken)
    {
        var preservedMembershipIds = closures
            .Where(closure => closure.ClosureType == "new_membership"
                && closure.CoveringMembershipId.HasValue
                && correctionsByReplacementId.ContainsKey(closure.Id))
            .Select(closure => closure.CoveringMembershipId!.Value)
            .Distinct()
            .ToArray();
        if (preservedMembershipIds.Length == 0)
        {
            return true;
        }

        var salePayments = new List<PaymentRecord>(
            preservedMembershipIds.Length);
        foreach (var membershipId in preservedMembershipIds)
        {
            var matchingPayments = allPayments
                .Where(payment => payment.MembershipId == membershipId
                    && payment.PaymentContext == "membership_sale")
                .ToArray();
            if (matchingPayments.Length != 1
                || matchingPayments[0].EntryOrigin is not (
                    "normal" or "paper_fallback"))
            {
                return false;
            }

            salePayments.Add(matchingPayments[0]);
        }

        var paperReferences = await paperReferenceReader.LoadAsync(
            salePayments.Select(payment =>
                new PaperFallbackEntryRowReferenceSource(
                    payment.Id,
                    payment.EntryOrigin,
                    payment.EntryBatchId,
                    payment.OccurredAt,
                    payment.RecordedByAccountId,
                    payment.SessionId,
                    PaperFallbackEventType.MembershipSale))
                .ToArray(),
            PaymentAuditActions.EntityType,
            PaperFallbackEventType.MembershipSale,
            cancellationToken);
        if (paperReferences is null)
        {
            return false;
        }

        var paperSalePayments = salePayments
            .Where(payment => payment.EntryOrigin == "paper_fallback")
            .ToArray();
        if (!await HasMatchingPreservedSaleAuditsAsync(
                dbContext,
                paperSalePayments,
                paperReferences,
                cancellationToken))
        {
            return false;
        }

        var expectedLinks = salePayments
            .SelectMany(payment =>
            {
                var expectedRowId = paperReferences
                    .GetValueOrDefault(payment.Id)
                    ?.EntryBatchRowId;
                return new[]
                {
                    new PaperFallbackExpectedEntityLink(
                        PaymentAuditActions.EntityType,
                        payment.Id,
                        expectedRowId),
                    new PaperFallbackExpectedEntityLink(
                        MembershipAuditActions.MembershipEntityType,
                        payment.MembershipId!.Value,
                        expectedRowId),
                };
            })
            .ToArray();
        return await paperReferenceReader.HasRequiredEntityLinksAsync(
            expectedLinks,
            cancellationToken);
    }

    private static async Task<bool> HasMatchingPreservedSaleAuditsAsync(
        BodyLifeDbContext dbContext,
        IReadOnlyList<PaymentRecord> paperSalePayments,
        IReadOnlyDictionary<Guid, PaperFallbackEntryRowReference>
            paperReferences,
        CancellationToken cancellationToken)
    {
        if (paperSalePayments.Count == 0)
        {
            return true;
        }

        var paymentIds = paperSalePayments
            .Select(payment => payment.Id)
            .ToArray();
        var membershipIds = paperSalePayments
            .Select(payment => payment.MembershipId!.Value)
            .ToArray();
        var paymentAudits = await dbContext.Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .Where(audit => audit.ActionType == PaymentAuditActions.Created
                && audit.EntityType == PaymentAuditActions.EntityType
                && paymentIds.Contains(audit.EntityId))
            .ToArrayAsync(cancellationToken);
        var membershipAudits = await dbContext.Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .Where(audit => audit.ActionType == MembershipAuditActions.Issued
                && audit.EntityType == MembershipAuditActions.MembershipEntityType
                && membershipIds.Contains(audit.EntityId))
            .ToArrayAsync(cancellationToken);
        if (paymentAudits.Length != paperSalePayments.Count
            || membershipAudits.Length != paperSalePayments.Count
            || paymentAudits.GroupBy(audit => audit.EntityId)
                .Any(group => group.Count() != 1)
            || membershipAudits.GroupBy(audit => audit.EntityId)
                .Any(group => group.Count() != 1))
        {
            return false;
        }

        var paymentAuditsById = paymentAudits.ToDictionary(
            audit => audit.EntityId);
        var membershipAuditsById = membershipAudits.ToDictionary(
            audit => audit.EntityId);
        return paperSalePayments.All(payment =>
        {
            var paperReference = paperReferences.GetValueOrDefault(payment.Id);
            var paymentAudit = paymentAuditsById[payment.Id];
            var membershipAudit = membershipAuditsById[
                payment.MembershipId!.Value];
            return paperReference is not null
                && HasMatchingSaleAuditEnvelope(paymentAudit, payment)
                && HasMatchingSaleAuditEnvelope(membershipAudit, payment)
                && !string.IsNullOrWhiteSpace(paymentAudit.IdempotencyKey)
                && paymentAudit.IdempotencyKey
                    == membershipAudit.IdempotencyKey
                && PaperFallbackEntryRowReferenceReader
                    .HasMatchingAuditReference(
                        paymentAudit.RelatedEntityRefsJson,
                        payment.EntryBatchId,
                        paperReference)
                && PaperFallbackEntryRowReferenceReader
                    .HasMatchingAuditReference(
                        membershipAudit.RelatedEntityRefsJson,
                        payment.EntryBatchId,
                        paperReference);
        });
    }

    private static bool HasMatchingSaleAuditEnvelope(
        BusinessAuditEntryRecord audit,
        PaymentRecord payment) =>
        audit.ActorAccountId == payment.RecordedByAccountId
        && audit.SessionId == payment.SessionId
        && SamePostgreSqlInstant(audit.OccurredAt, payment.OccurredAt)
        && SamePostgreSqlInstant(audit.RecordedAt, payment.RecordedAt)
        && audit.EntryOrigin == payment.EntryOrigin
        && audit.Comment == payment.Comment;

    private static bool ValidateNewMembershipSalePayment(
        Guid clientId,
        MembershipNegativeClosureRecord closure,
        IssuedMembershipRecord membership,
        PaymentRecord payment,
        bool requireClosureEnvelope)
    {
        if (payment.ClientId != clientId
            || payment.MembershipId != membership.Id
            || payment.NegativeClosureId is not null
            || payment.Status != "active"
            || payment.Method != "cash"
            || payment.PaymentContext != "membership_sale"
            || payment.Amount != membership.PriceAmountSnapshot
            || payment.Currency != membership.PriceCurrencySnapshot
            || payment.RecordedAt != membership.IssuedAt
            || payment.RecordedByAccountId != membership.IssuedByAccountId
            || payment.EntryOrigin != membership.EntryOrigin
            || payment.EntryBatchId != membership.EntryBatchId
            || !TryMoney(payment.Amount, payment.Currency, out _))
        {
            return false;
        }

        return !requireClosureEnvelope
            || (payment.OccurredAt == closure.OccurredAt
                && payment.RecordedAt == closure.RecordedAt
                && payment.RecordedByAccountId == closure.RecordedByAccountId
                && payment.SessionId == closure.SessionId
                && payment.EntryOrigin == closure.EntryOrigin
                && payment.EntryBatchId == closure.EntryBatchId);
    }

    private static PaperFallbackEventType ExpectedPaperEventType(
        MembershipNegativeClosureRecord closure,
        bool isReplacement)
    {
        if (isReplacement)
        {
            return PaperFallbackEventType.CorrectionOrCancellation;
        }

        return closure.ClosureType == "new_membership"
            ? PaperFallbackEventType.MembershipSale
            : PaperFallbackEventType.NegativeCoverage;
    }

    private static IReadOnlyList<PaperFallbackExpectedEntityLink>
        BuildExpectedPaperLinks(
            IReadOnlyList<MembershipNegativeClosureRecord> closures,
            IReadOnlyList<MembershipNegativeClosureLineRecord> allLines,
            IReadOnlyList<MembershipNegativeClosureItemRecord> allItems,
            IReadOnlyList<PaymentRecord> allPayments,
            IReadOnlyDictionary<Guid, MembershipNegativeClosureCorrectionRecord>
                correctionsByReplacementId,
            IReadOnlyDictionary<Guid, PaperFallbackEntryRowReference>
                paperReferences,
            IReadOnlyList<MembershipLifecycleClosureRecord> lifecycleClosures)
    {
        var expected = new List<PaperFallbackExpectedEntityLink>();
        foreach (var closure in closures)
        {
            var expectedRowId = paperReferences.GetValueOrDefault(closure.Id)
                ?.EntryBatchRowId;
            var isReplacement = correctionsByReplacementId.ContainsKey(
                closure.Id);
            expected.Add(new PaperFallbackExpectedEntityLink(
                MembershipNegativeClosureAuditActions.EntityType,
                closure.Id,
                expectedRowId));
            if (closure.ClosureType == "new_membership"
                && !isReplacement
                && closure.CoveringMembershipId is { } coveringMembershipId)
            {
                expected.Add(new PaperFallbackExpectedEntityLink(
                    MembershipAuditActions.MembershipEntityType,
                    coveringMembershipId,
                    expectedRowId));
            }

            if (correctionsByReplacementId.TryGetValue(
                    closure.Id,
                    out var correction))
            {
                expected.Add(new PaperFallbackExpectedEntityLink(
                    CorrectNegativeVisitCoverageCommand.PrimaryEntityType,
                    correction.Id,
                    expectedRowId));
            }

            expected.AddRange(lifecycleClosures.Where(row => row.NegativeClosureId == closure.Id
                    || (!isReplacement && closure.CoveringMembershipId.HasValue
                        && row.SuccessorMembershipId == closure.CoveringMembershipId))
                .Select(row => new PaperFallbackExpectedEntityLink(
                    "membership_lifecycle_closure", row.Id, expectedRowId)));

            expected.AddRange(allLines
                .Where(line => line.NegativeClosureId == closure.Id)
                .Select(line => new PaperFallbackExpectedEntityLink(
                    "membership_negative_closure_line",
                    line.Id,
                    expectedRowId)));
            var items = allItems
                .Where(item => item.NegativeClosureId == closure.Id)
                .ToArray();
            expected.AddRange(items.Select(item =>
                new PaperFallbackExpectedEntityLink(
                    "membership_negative_closure_item",
                    item.Id,
                    expectedRowId)));
            expected.AddRange(items
                .Where(item => item.NewConsumptionId.HasValue)
                .Select(item => new PaperFallbackExpectedEntityLink(
                    "visit_consumption",
                    item.NewConsumptionId!.Value,
                    expectedRowId)));
            expected.AddRange(allPayments
                .Where(payment => payment.NegativeClosureId == closure.Id)
                .Select(payment => new PaperFallbackExpectedEntityLink(
                    PaymentAuditActions.EntityType,
                    payment.Id,
                    expectedRowId)));
            if (closure.ClosureType == "new_membership"
                && !isReplacement
                && closure.CoveringMembershipId is { } membershipId)
            {
                expected.AddRange(allPayments
                    .Where(payment => payment.MembershipId == membershipId
                        && payment.PaymentContext == "membership_sale")
                    .Select(payment => new PaperFallbackExpectedEntityLink(
                        PaymentAuditActions.EntityType,
                        payment.Id,
                        expectedRowId)));
            }
        }

        return expected;
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

    private static bool SamePostgreSqlInstant(
        DateTimeOffset left,
        DateTimeOffset right) =>
        left.UtcDateTime.Ticks / 10 == right.UtcDateTime.Ticks / 10;
}
