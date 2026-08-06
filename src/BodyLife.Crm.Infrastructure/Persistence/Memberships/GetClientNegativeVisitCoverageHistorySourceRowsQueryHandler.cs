using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class GetClientNegativeVisitCoverageHistorySourceRowsQueryHandler(
    BodyLifeDbContext dbContext,
    IBodyLifeQueryHandler<GetClientAuditEntriesQuery, GetClientAuditEntriesResult>
        auditEntriesQueryHandler)
    : IBodyLifeQueryHandler<
        GetClientNegativeVisitCoverageHistorySourceRowsQuery,
        GetClientNegativeVisitCoverageHistorySourceRowsResult>
{
    private static readonly ClientAuditEntityFilter[] EntityFilters =
        [ClientAuditEntityFilter.MembershipNegativeClosure];

    private static readonly string[] ActionTypes =
    [
        MembershipNegativeClosureAuditActions.Created,
        MembershipNegativeClosureAuditActions.Canceled,
        MembershipNegativeClosureAuditActions.Replaced,
    ];

    public async Task<GetClientNegativeVisitCoverageHistorySourceRowsResult> ExecuteAsync(
        GetClientNegativeVisitCoverageHistorySourceRowsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var auditResult = await auditEntriesQueryHandler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                query.Actor, query.ClientId, query.OccurredFromInclusive,
                query.OccurredBeforeExclusive, EntityFilters, ActionTypes, query.Limit,
                query.Offset, query.AuditEntryIds), cancellationToken);
        if (auditResult.Status != GetClientAuditEntriesStatus.Success)
        {
            return MapAuditFailure(auditResult);
        }

        var auditPage = auditResult.Page;
        if (auditPage is null
            || auditPage.ClientId != query.ClientId
            || auditPage.Offset != query.Offset
            || !auditPage.EntityFilters.SequenceEqual(EntityFilters)
            || !auditPage.ActionTypes.SequenceEqual(ActionTypes)
            || auditPage.Items.Count > query.Limit
            || auditPage.Items.Select(entry => entry.AuditEntryId).Distinct().Count()
                != auditPage.Items.Count
            || auditPage.Items.GroupBy(entry => (entry.ActionType, entry.EntityId))
                .Any(group => group.Count() != 1)
            || auditPage.Items.Any(entry => entry.EntityType
                != ClientAuditEntityFilter.MembershipNegativeClosure
                || !ActionTypes.Contains(entry.ActionType)))
        {
            return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
        }

        var selectedAuditEntityIds = auditPage.Items
            .Select(entry => entry.EntityId)
            .Distinct()
            .ToArray();
        var auditWitnesses = selectedAuditEntityIds.Length == 0
            ? []
            : await dbContext.Set<BusinessAuditEntryRecord>()
                .AsNoTracking()
                .Where(audit => selectedAuditEntityIds.Contains(audit.EntityId)
                    && audit.EntityType
                        == MembershipNegativeClosureAuditActions.EntityType
                    && ActionTypes.Contains(audit.ActionType))
                .ToArrayAsync(cancellationToken);
        if (auditPage.Items.Any(selected => auditWitnesses.Count(witness =>
                witness.Id == selected.AuditEntryId.Value
                && witness.EntityId == selected.EntityId
                && witness.ActionType == selected.ActionType) != 1)
            || auditPage.Items.Any(selected => auditWitnesses.Count(witness =>
                witness.EntityId == selected.EntityId
                && witness.ActionType == selected.ActionType) != 1))
        {
            return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
        }

        var allClosures = await dbContext.Set<MembershipNegativeClosureRecord>()
            .AsNoTracking()
            .Where(closure => closure.ClientId == query.ClientId)
            .ToArrayAsync(cancellationToken);
        var selectedClosureIds = auditPage.Items.Select(entry => entry.EntityId)
            .Distinct().ToArray();
        var closureIds = allClosures.Select(closure => closure.Id).ToArray();
        if (!selectedClosureIds.All(closureIds.Contains)
            || allClosures.Select(closure => closure.Id).Distinct().Count()
                != allClosures.Length
            || allClosures.Any(closure => !IsValidClosure(closure, query.ClientId)))
        {
            return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
        }

        var corrections = closureIds.Length == 0 ? [] : await dbContext
            .Set<MembershipNegativeClosureCorrectionRecord>().AsNoTracking()
            .Where(correction => closureIds.Contains(correction.OriginalClosureId)
                || (correction.ReplacementClosureId.HasValue
                    && closureIds.Contains(correction.ReplacementClosureId.Value)))
            .ToArrayAsync(cancellationToken);
        var closuresById = allClosures.ToDictionary(closure => closure.Id);
        if (!TryBuildCorrectionGraph(
                allClosures,
                corrections,
                closuresById,
                out var outgoingByClosureId,
                out var incomingByClosureId))
        {
            return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
        }

        var lines = closureIds.Length == 0 ? [] : await dbContext
            .Set<MembershipNegativeClosureLineRecord>().AsNoTracking()
            .Where(line => closureIds.Contains(line.NegativeClosureId)).ToArrayAsync(cancellationToken);
        var items = closureIds.Length == 0 ? [] : await dbContext
            .Set<MembershipNegativeClosureItemRecord>().AsNoTracking()
            .Where(item => closureIds.Contains(item.NegativeClosureId)).ToArrayAsync(cancellationToken);
        var coveringMembershipIds = allClosures
            .Where(closure => closure.CoveringMembershipId.HasValue)
            .Select(closure => closure.CoveringMembershipId!.Value)
            .Distinct().ToArray();
        var membershipIds = coveringMembershipIds
            .Concat(items.Select(item => item.SourceMembershipId))
            .Distinct().ToArray();
        var memberships = membershipIds.Length == 0 ? [] : await dbContext
            .Set<IssuedMembershipRecord>().AsNoTracking()
            .Where(membership => membershipIds.Contains(membership.Id))
            .ToArrayAsync(cancellationToken);
        if (memberships.Length != membershipIds.Length
            || memberships.Any(membership => membership.ClientId != query.ClientId))
        {
            return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
        }

        var membershipById = memberships.ToDictionary(membership => membership.Id);
        var visitIds = items.Select(item => item.VisitId).Distinct().ToArray();
        var visits = visitIds.Length == 0 ? [] : await dbContext.Set<VisitRecord>().AsNoTracking()
            .Where(visit => visitIds.Contains(visit.Id)).ToArrayAsync(cancellationToken);
        var consumptionIds = items.Select(item => item.OldConsumptionId)
            .Concat(items.Where(item => item.NewConsumptionId.HasValue)
                .Select(item => item.NewConsumptionId!.Value)).Distinct().ToArray();
        var consumptions = consumptionIds.Length == 0 ? [] : await dbContext
            .Set<VisitConsumptionRecord>().AsNoTracking()
            .Where(consumption => consumptionIds.Contains(consumption.Id)).ToArrayAsync(cancellationToken);
        if (visits.Length != visitIds.Length || consumptions.Length != consumptionIds.Length)
        {
            return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
        }

        var payments = closureIds.Length == 0 ? [] : await dbContext.Set<PaymentRecord>()
            .AsNoTracking().Where(payment =>
                (payment.NegativeClosureId.HasValue
                    && closureIds.Contains(payment.NegativeClosureId.Value))
                || (payment.MembershipId.HasValue
                    && coveringMembershipIds.Contains(payment.MembershipId.Value)
                    && payment.PaymentContext == "membership_sale"))
            .ToArrayAsync(cancellationToken);
        var paymentIds = payments.Select(payment => payment.Id).Distinct().ToArray();
        var auditMembershipIds = memberships.Select(membership => membership.Id).Distinct().ToArray();
        var lifecycleAudits = closureIds.Length == 0 ? [] : await dbContext
            .Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .Where(audit =>
                (audit.EntityType == MembershipNegativeClosureAuditActions.EntityType
                    && closureIds.Contains(audit.EntityId))
                || (audit.EntityType == PaymentAuditActions.EntityType
                    && paymentIds.Contains(audit.EntityId))
                || (audit.EntityType == MembershipAuditActions.MembershipEntityType
                    && auditMembershipIds.Contains(audit.EntityId)))
            .ToArrayAsync(cancellationToken);

        var paperReader = new PaperFallbackEntryRowReferenceReader(dbContext);
        var paperReferences = await paperReader.LoadAsync(
            allClosures.Select(closure => new PaperFallbackEntryRowReferenceSource(
                closure.Id, closure.EntryOrigin, closure.EntryBatchId, closure.OccurredAt,
                closure.RecordedByAccountId, closure.SessionId,
                ExpectedPaperEventType(closure, incomingByClosureId.ContainsKey(closure.Id))))
                .ToArray(),
            MembershipNegativeClosureAuditActions.EntityType,
            PaperFallbackEventType.NegativeCoverage,
            cancellationToken);
        if (paperReferences is null)
        {
            return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
        }
        var correctionPaperReferences = await paperReader.LoadAsync(
            corrections.Select(correction => new PaperFallbackEntryRowReferenceSource(
                correction.Id, correction.EntryOrigin, correction.EntryBatchId,
                correction.OccurredAt, correction.RecordedByAccountId, correction.SessionId,
                PaperFallbackEventType.CorrectionOrCancellation)).ToArray(),
            CorrectNegativeVisitCoverageCommand.PrimaryEntityType,
            PaperFallbackEventType.CorrectionOrCancellation,
            cancellationToken);
        if (correctionPaperReferences is null)
        {
            return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
        }

        var expectedPaperLinks = BuildExpectedPaperLinks(
            allClosures,
            lines,
            items,
            payments,
            corrections,
            incomingByClosureId,
            paperReferences,
            correctionPaperReferences);
        if (expectedPaperLinks is null
            || !HasMatchingCorrectionPaperReferences(
                corrections,
                paperReferences,
                correctionPaperReferences)
            || !await paperReader.HasExpectedEntityLinksAsync(
                expectedPaperLinks,
                cancellationToken))
        {
            return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
        }

        var visitById = visits.ToDictionary(visit => visit.Id);
        var consumptionById = consumptions.ToDictionary(consumption => consumption.Id);
        var rows = new List<ClientNegativeVisitCoverageHistorySourceRow>(auditPage.Items.Count);
        try
        {
            foreach (var auditEntry in auditPage.Items)
            {
                if (!closuresById.TryGetValue(auditEntry.EntityId, out var closure)
                    || !TryMapHistoryKind(auditEntry.ActionType, out var kind)
                    || !TryProjectClosure(closure, lines, items, payments, membershipById,
                        visitById, consumptionById, incomingByClosureId.ContainsKey(closure.Id),
                        out var projectedClosure))
                {
                    return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
                }

                incomingByClosureId.TryGetValue(closure.Id, out var incoming);
                outgoingByClosureId.TryGetValue(closure.Id, out var outgoing);
                var correction = kind == ClientNegativeVisitCoverageHistorySourceKind.Created
                    ? incoming : outgoing;
                if (!MatchesAudit(kind, auditEntry, closure, correction,
                        kind == ClientNegativeVisitCoverageHistorySourceKind.Created
                            ? paperReferences.GetValueOrDefault(closure.Id)
                            : correction is null
                                ? null
                                : correctionPaperReferences.GetValueOrDefault(correction.Id),
                        lines, items, payments, lifecycleAudits))
                {
                    return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
                }

                NegativeVisitCoverageClosureHistorySnapshot? replacement = null;
                if (kind == ClientNegativeVisitCoverageHistorySourceKind.Replaced)
                {
                    if (outgoing?.ReplacementClosureId is not { } replacementId
                        || !closuresById.TryGetValue(replacementId, out var replacementClosure)
                        || !TryProjectClosure(replacementClosure, lines, items, payments,
                            membershipById, visitById, consumptionById,
                            true, out replacement))
                    {
                        return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
                    }
                }

                rows.Add(new ClientNegativeVisitCoverageHistorySourceRow(
                    kind, query.ClientId, auditEntry.OccurredAt, auditEntry.RecordedAt,
                    auditEntry.EntryOrigin, projectedClosure, replacement,
                    correction is null ? null : ProjectCorrection(correction),
                    kind == ClientNegativeVisitCoverageHistorySourceKind.Created
                        ? paperReferences.GetValueOrDefault(closure.Id)
                        : correction is null
                            ? null
                            : correctionPaperReferences.GetValueOrDefault(correction.Id),
                    auditEntry));
            }

            return GetClientNegativeVisitCoverageHistorySourceRowsResult.Succeeded(
                ClientNegativeVisitCoverageHistorySourceRowsPage.Create(
                    auditPage.ClientId, auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive, auditPage.Offset, rows,
                    auditPage.HasMore));
        }
        catch (ArgumentException)
        {
            return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
        }
        catch (InvalidOperationException)
        {
            return GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource();
        }
    }

    private static GetClientNegativeVisitCoverageHistorySourceRowsResult MapAuditFailure(
        GetClientAuditEntriesResult result) => result.Status switch
        {
            GetClientAuditEntriesStatus.PermissionDenied =>
                GetClientNegativeVisitCoverageHistorySourceRowsResult.Denied(),
            GetClientAuditEntriesStatus.NotFound =>
                GetClientNegativeVisitCoverageHistorySourceRowsResult.MissingClient(),
            GetClientAuditEntriesStatus.ValidationFailed =>
                GetClientNegativeVisitCoverageHistorySourceRowsResult.Invalid(
                    result.ErrorMessage ?? "Audit history query is invalid.", result.ErrorField),
            _ => GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource(),
        };

    private static bool IsValidClosure(
        MembershipNegativeClosureRecord closure,
        Guid clientId) => closure.Id != Guid.Empty
            && closure.ClientId == clientId
            && closure.VisitsCount > 0
            && closure.OldestOpenNegativeVisitId != Guid.Empty
            && closure.RecordedByAccountId != Guid.Empty
            && closure.SessionId != Guid.Empty
            && !string.IsNullOrWhiteSpace(closure.IdempotencyKey)
            && closure.IdempotencyKey == closure.IdempotencyKey.Trim()
            && TryMapMethod(closure.ClosureType, out _)
            && TryMapClosureStatus(closure.Status, out _)
            && TryMapEntryOrigin(closure.EntryOrigin, out _)
            && HasExpectedBatchShape(closure.EntryOrigin, closure.EntryBatchId);

    private static bool TryBuildCorrectionGraph(
        IReadOnlyCollection<MembershipNegativeClosureRecord> closures,
        IReadOnlyCollection<MembershipNegativeClosureCorrectionRecord> corrections,
        IReadOnlyDictionary<Guid, MembershipNegativeClosureRecord> closuresById,
        out Dictionary<Guid, MembershipNegativeClosureCorrectionRecord> outgoing,
        out Dictionary<Guid, MembershipNegativeClosureCorrectionRecord> incoming)
    {
        outgoing = [];
        incoming = [];
        if (corrections.Select(correction => correction.Id).Distinct().Count()
            != corrections.Count)
        {
            return false;
        }

        foreach (var correction in corrections)
        {
            if (!IsValidCorrection(correction)
                || !closuresById.TryGetValue(correction.OriginalClosureId, out var original)
                || !outgoing.TryAdd(original.Id, correction)
                || !TryMapCorrectionMode(correction.Mode, out var mode))
            {
                return false;
            }

            if (mode == NegativeVisitCoverageCorrectionHistoryMode.Cancel)
            {
                if (correction.ReplacementClosureId is not null
                    || original.Status != "canceled")
                {
                    return false;
                }

                continue;
            }

            if (correction.ReplacementClosureId is not { } replacementId
                || replacementId == original.Id
                || original.Status != "replaced"
                || !closuresById.TryGetValue(replacementId, out var replacement)
                || !incoming.TryAdd(replacementId, correction)
                || original.ClosureType != replacement.ClosureType
                || original.CoveringMembershipId != replacement.CoveringMembershipId
                || replacement.IdempotencyKey != correction.IdempotencyKey
                || replacement.EntryOrigin != correction.EntryOrigin
                || replacement.EntryBatchId != correction.EntryBatchId
                || !SamePostgreSqlInstant(replacement.OccurredAt, correction.OccurredAt)
                || !SamePostgreSqlInstant(replacement.RecordedAt, correction.RecordedAt)
                || replacement.RecordedByAccountId != correction.RecordedByAccountId
                || replacement.SessionId != correction.SessionId)
            {
                return false;
            }
        }

        foreach (var closure in closures)
        {
            outgoing.TryGetValue(closure.Id, out var correction);
            if (closure.Status == "active" && correction is not null
                || closure.Status == "canceled" && correction?.Mode != "cancel"
                || closure.Status == "replaced" && correction?.Mode != "replace")
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidCorrection(
        MembershipNegativeClosureCorrectionRecord correction) =>
        correction.Id != Guid.Empty
        && correction.OriginalClosureId != Guid.Empty
        && !string.IsNullOrWhiteSpace(correction.Reason)
        && correction.Reason == correction.Reason.Trim()
        && correction.RecordedByAccountId != Guid.Empty
        && correction.SessionId != Guid.Empty
        && !string.IsNullOrWhiteSpace(correction.IdempotencyKey)
        && correction.IdempotencyKey == correction.IdempotencyKey.Trim()
        && TryMapCorrectionMode(correction.Mode, out _)
        && TryMapEntryOrigin(correction.EntryOrigin, out _)
        && HasExpectedBatchShape(correction.EntryOrigin, correction.EntryBatchId);

    private static bool HasExpectedBatchShape(string entryOrigin, Guid? entryBatchId) =>
        entryOrigin == "normal"
            ? entryBatchId is null
            : entryOrigin == "paper_fallback" && entryBatchId.HasValue;

    private static bool HasMatchingCorrectionPaperReferences(
        IReadOnlyCollection<MembershipNegativeClosureCorrectionRecord> corrections,
        IReadOnlyDictionary<Guid, PaperFallbackEntryRowReference> closureReferences,
        IReadOnlyDictionary<Guid, PaperFallbackEntryRowReference> correctionReferences) =>
        corrections.All(correction =>
        {
            if (correction.ReplacementClosureId is not { } replacementId)
            {
                return true;
            }

            var closureReference = closureReferences.GetValueOrDefault(replacementId);
            var correctionReference = correctionReferences.GetValueOrDefault(correction.Id);
            return closureReference is null
                ? correctionReference is null
                : correctionReference is not null
                    && closureReference.EntryBatchId == correctionReference.EntryBatchId
                    && closureReference.EntryBatchRowId == correctionReference.EntryBatchRowId
                    && closureReference.EventType == PaperFallbackEventType.CorrectionOrCancellation
                    && correctionReference.EventType
                        == PaperFallbackEventType.CorrectionOrCancellation;
        });

    private static IReadOnlyList<PaperFallbackExpectedEntityLink>? BuildExpectedPaperLinks(
        IReadOnlyCollection<MembershipNegativeClosureRecord> closures,
        IReadOnlyCollection<MembershipNegativeClosureLineRecord> lines,
        IReadOnlyCollection<MembershipNegativeClosureItemRecord> items,
        IReadOnlyCollection<PaymentRecord> payments,
        IReadOnlyCollection<MembershipNegativeClosureCorrectionRecord> corrections,
        IReadOnlyDictionary<Guid, MembershipNegativeClosureCorrectionRecord> incoming,
        IReadOnlyDictionary<Guid, PaperFallbackEntryRowReference> closureReferences,
        IReadOnlyDictionary<Guid, PaperFallbackEntryRowReference> correctionReferences)
    {
        var expected = new Dictionary<(string EntityType, Guid EntityId), Guid?>();
        foreach (var closure in closures)
        {
            var rowId = closureReferences.GetValueOrDefault(closure.Id)?.EntryBatchRowId;
            if (!TryAddExpected(
                    expected,
                    MembershipNegativeClosureAuditActions.EntityType,
                    closure.Id,
                    rowId))
            {
                return null;
            }

            foreach (var line in lines.Where(line => line.NegativeClosureId == closure.Id))
            {
                if (!TryAddExpected(
                        expected,
                        "membership_negative_closure_line",
                        line.Id,
                        rowId))
                {
                    return null;
                }
            }

            var closureItems = items
                .Where(item => item.NegativeClosureId == closure.Id)
                .ToArray();
            foreach (var item in closureItems)
            {
                if (!TryAddExpected(
                        expected,
                        "membership_negative_closure_item",
                        item.Id,
                        rowId)
                    || item.NewConsumptionId is { } consumptionId
                        && !TryAddExpected(
                            expected,
                            "visit_consumption",
                            consumptionId,
                            rowId))
                {
                    return null;
                }
            }

            foreach (var payment in payments.Where(payment =>
                         payment.NegativeClosureId == closure.Id))
            {
                if (!TryAddExpected(
                        expected,
                        PaymentAuditActions.EntityType,
                        payment.Id,
                        rowId))
                {
                    return null;
                }
            }

            if (closure.ClosureType == "new_membership"
                && !incoming.ContainsKey(closure.Id)
                && closure.CoveringMembershipId is { } membershipId)
            {
                if (!TryAddExpected(
                        expected,
                        MembershipAuditActions.MembershipEntityType,
                        membershipId,
                        rowId))
                {
                    return null;
                }

                foreach (var payment in payments.Where(payment =>
                             payment.MembershipId == membershipId
                             && payment.PaymentContext == "membership_sale"))
                {
                    if (!TryAddExpected(
                            expected,
                            PaymentAuditActions.EntityType,
                            payment.Id,
                            rowId))
                    {
                        return null;
                    }
                }
            }
        }

        foreach (var correction in corrections)
        {
            var rowId = correctionReferences.GetValueOrDefault(correction.Id)?.EntryBatchRowId;
            if (!TryAddExpected(
                    expected,
                    CorrectNegativeVisitCoverageCommand.PrimaryEntityType,
                    correction.Id,
                    rowId))
            {
                return null;
            }
        }

        return expected.Select(item => new PaperFallbackExpectedEntityLink(
                item.Key.EntityType,
                item.Key.EntityId,
                item.Value))
            .ToArray();
    }

    private static bool TryAddExpected(
        IDictionary<(string EntityType, Guid EntityId), Guid?> expected,
        string entityType,
        Guid entityId,
        Guid? rowId)
    {
        if (string.IsNullOrWhiteSpace(entityType) || entityId == Guid.Empty)
        {
            return false;
        }

        var key = (entityType, entityId);
        return expected.TryGetValue(key, out var existing)
            ? existing == rowId
            : expected.TryAdd(key, rowId);
    }

    private static bool TryProjectClosure(
        MembershipNegativeClosureRecord closure,
        IReadOnlyCollection<MembershipNegativeClosureLineRecord> allLines,
        IReadOnlyCollection<MembershipNegativeClosureItemRecord> allItems,
        IReadOnlyCollection<PaymentRecord> allPayments,
        IReadOnlyDictionary<Guid, IssuedMembershipRecord> memberships,
        IReadOnlyDictionary<Guid, VisitRecord> visits,
        IReadOnlyDictionary<Guid, VisitConsumptionRecord> consumptions,
        bool correctionCreated,
        out NegativeVisitCoverageClosureHistorySnapshot snapshot)
    {
        snapshot = null!;
        if (!TryMapMethod(closure.ClosureType, out var method)
            || !TryMapClosureStatus(closure.Status, out var status)
            || !TryMapEntryOrigin(closure.EntryOrigin, out var entryOrigin))
        {
            return false;
        }

        var lines = allLines.Where(line => line.NegativeClosureId == closure.Id)
            .OrderBy(line => line.Sequence).ThenBy(line => line.Id).ToArray();
        var items = allItems.Where(item => item.NegativeClosureId == closure.Id)
            .OrderBy(item => item.Sequence).ThenBy(item => item.Id).ToArray();
        var closurePayments = allPayments.Where(payment => payment.NegativeClosureId == closure.Id)
            .ToArray();
        if (items.Length != closure.VisitsCount || items.Length == 0
            || !HasContiguousSequence(items.Select(item => item.Sequence))
            || items.Select(item => item.Id).Distinct().Count() != items.Length
            || items.Select(item => item.VisitId).Distinct().Count() != items.Length
            || items[0].VisitId != closure.OldestOpenNegativeVisitId
            || items.Any(item => item.ClientId != closure.ClientId
                || !TryMapClosureStatus(item.Status, out var itemStatus)
                || itemStatus != status)
            || !ValidateVisitOrder(items, visits, consumptions)
            || !ValidateOldFacts(
                closure.ClientId,
                items,
                memberships,
                visits,
                consumptions))
        {
            return false;
        }

        NegativeVisitCoveragePaymentHistorySnapshot? payment = null;
        NegativeVisitCoverageCoveringMembershipHistorySnapshot? membership = null;
        if (method == NegativeVisitCoverageClosureMethod.OneOff)
        {
            if (closure.CoveringMembershipId is not null || lines.Length == 0
                || closurePayments.Length != 1
                || !HasContiguousSequence(lines.Select(line => line.Sequence))
                || lines.Select(line => line.Id).Distinct().Count() != lines.Length
                || lines.Any(line => line.Quantity <= 0 || line.DurationDaysSnapshot <= 0
                    || line.VisitsLimitSnapshot != 1 || string.IsNullOrWhiteSpace(line.TypeNameSnapshot)
                    || !TryMoney(line.UnitPriceAmountSnapshot, line.CurrencySnapshot, out _)
                    || !TryMoney(line.LineTotal, line.CurrencySnapshot, out _)
                    || line.LineTotal != line.UnitPriceAmountSnapshot * line.Quantity)
                || lines.Sum(line => line.Quantity) != items.Length
                || lines.Any(line => items.Count(item => item.ClosureLineId == line.Id)
                    != line.Quantity)
                || items.Any(item => item.ClosureLineId is null
                    || item.CoveringMembershipId is not null || item.NewConsumptionId is not null)
                || !TryProjectOneOffPayment(closure, closurePayments.Single(), lines, status,
                    out payment))
            {
                return false;
            }
        }
        else
        {
            if (closure.CoveringMembershipId is not { } membershipId || lines.Length != 0
                || closurePayments.Length != 0 || !memberships.TryGetValue(membershipId, out var record)
                || !TryProjectCoveringMembership(record, closure.ClientId, out membership)
                || items.Any(item => item.ClosureLineId is not null
                    || item.CoveringMembershipId != membershipId
                    || !ValidateNewConsumption(
                        item,
                        closure,
                        membershipId,
                        consumptions))
                || !ValidateMembershipSalePayment(
                    closure, record, allPayments, correctionCreated))
            {
                return false;
            }
        }

        snapshot = new NegativeVisitCoverageClosureHistorySnapshot(
            closure.Id, closure.ClientId, method, closure.OldestOpenNegativeVisitId,
            closure.VisitsCount, closure.Comment, closure.OccurredAt, closure.RecordedAt,
            new AccountId(closure.RecordedByAccountId), new SessionId(closure.SessionId),
            entryOrigin, closure.EntryBatchId, status,
            lines.Select(line => new NegativeVisitCoverageLineHistorySnapshot(line.Id,
                line.MembershipTypeId, line.TypeNameSnapshot, line.DurationDaysSnapshot,
                line.VisitsLimitSnapshot, line.Quantity,
                new Money(line.UnitPriceAmountSnapshot, line.CurrencySnapshot),
                new Money(line.LineTotal, line.CurrencySnapshot), line.Sequence)).ToArray(),
            items.Select(item => new NegativeVisitCoverageItemHistorySnapshot(item.Id, item.Sequence,
                item.VisitId, visits[item.VisitId].OccurredAt,
                BusinessTimeZone.GetBusinessDate(visits[item.VisitId].OccurredAt),
                item.SourceMembershipId, item.OldConsumptionId, item.CoveringMembershipId,
                item.NewConsumptionId, status)).ToArray(), payment, membership);
        return true;
    }

    private static bool TryProjectOneOffPayment(
        MembershipNegativeClosureRecord closure,
        PaymentRecord payment,
        IReadOnlyCollection<MembershipNegativeClosureLineRecord> lines,
        NegativeVisitCoverageClosureHistoryStatus expectedStatus,
        out NegativeVisitCoveragePaymentHistorySnapshot snapshot)
    {
        snapshot = null!;
        if (!TryMapClosureStatus(payment.Status, out var status)
            || status != expectedStatus || payment.ClientId != closure.ClientId
            || payment.Method != "cash" || payment.PaymentContext != "negative_closure"
            || payment.NegativeClosureId != closure.Id || payment.MembershipId is not null
            || payment.OccurredAt != closure.OccurredAt || payment.RecordedAt != closure.RecordedAt
            || payment.RecordedByAccountId != closure.RecordedByAccountId
            || payment.SessionId != closure.SessionId || payment.EntryOrigin != closure.EntryOrigin
            || payment.EntryBatchId != closure.EntryBatchId
            || !TryMapEntryOrigin(payment.EntryOrigin, out var entryOrigin)
            || !TryMoney(payment.Amount, payment.Currency, out _))
        {
            return false;
        }

        var currency = lines.First().CurrencySnapshot;
        if (lines.Any(line => line.CurrencySnapshot != currency)
            || payment.Currency != currency || payment.Amount != lines.Sum(line => line.LineTotal))
        {
            return false;
        }

        snapshot = new NegativeVisitCoveragePaymentHistorySnapshot(payment.Id,
            new Money(payment.Amount, payment.Currency), payment.OccurredAt, payment.RecordedAt,
            new AccountId(payment.RecordedByAccountId), new SessionId(payment.SessionId),
            entryOrigin, payment.EntryBatchId, status);
        return true;
    }

    private static bool TryProjectCoveringMembership(
        IssuedMembershipRecord membership,
        Guid clientId,
        out NegativeVisitCoverageCoveringMembershipHistorySnapshot snapshot)
    {
        snapshot = null!;
        if (membership.ClientId != clientId || membership.Id == Guid.Empty
            || membership.MembershipTypeId == Guid.Empty || membership.IssuedByAccountId == Guid.Empty
            || !MembershipQuerySupport.TryMapLifecycleStatus(membership.Status, out var status)
            || !TryMapEntryOrigin(membership.EntryOrigin, out var entryOrigin)
            || MembershipDateRules.CalculateBaseEndDate(membership.StartDate,
                membership.DurationDaysSnapshot) != membership.BaseEndDate
            || !TryMoney(membership.PriceAmountSnapshot, membership.PriceCurrencySnapshot, out var price))
        {
            return false;
        }

        snapshot = new NegativeVisitCoverageCoveringMembershipHistorySnapshot(membership.Id,
            membership.MembershipTypeId, new IssuedMembershipSnapshot(membership.TypeNameSnapshot,
                membership.DurationDaysSnapshot, membership.VisitsLimitSnapshot, price),
            membership.StartDate, membership.BaseEndDate, membership.IssuedAt,
            new AccountId(membership.IssuedByAccountId), entryOrigin, membership.EntryBatchId, status);
        return true;
    }

    private static bool ValidateMembershipSalePayment(
        MembershipNegativeClosureRecord closure,
        IssuedMembershipRecord membership,
        IReadOnlyCollection<PaymentRecord> payments,
        bool correctionCreated)
    {
        var salePayments = payments.Where(payment => payment.MembershipId == membership.Id
            && payment.PaymentContext == "membership_sale").ToArray();
        if (salePayments.Length != 1)
        {
            return false;
        }

        var payment = salePayments[0];
        var expectedPaymentStatus = membership.Status switch
        {
            "active" => "active",
            "canceled" => "canceled",
            "corrected" => "replaced",
            _ => null,
        };
        if (payment.ClientId != closure.ClientId || payment.NegativeClosureId is not null
            || expectedPaymentStatus is null || payment.Status != expectedPaymentStatus
            || payment.Method != "cash"
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

        return correctionCreated
            || payment.OccurredAt == closure.OccurredAt
                && payment.RecordedAt == closure.RecordedAt
                && payment.RecordedByAccountId == closure.RecordedByAccountId
                && payment.SessionId == closure.SessionId
                && payment.EntryOrigin == closure.EntryOrigin
                && payment.EntryBatchId == closure.EntryBatchId;
    }

    private static bool MatchesAudit(
        ClientNegativeVisitCoverageHistorySourceKind kind,
        ClientAuditEntry audit,
        MembershipNegativeClosureRecord closure,
        MembershipNegativeClosureCorrectionRecord? correction,
        PaperFallbackEntryRowReference? paperReference,
        IReadOnlyCollection<MembershipNegativeClosureLineRecord> lines,
        IReadOnlyCollection<MembershipNegativeClosureItemRecord> items,
        IReadOnlyCollection<PaymentRecord> payments,
        IReadOnlyCollection<BusinessAuditEntryRecord> lifecycleAudits)
    {
        if (audit.EntityId != closure.Id || audit.EntityType
            != ClientAuditEntityFilter.MembershipNegativeClosure
            || !PaperFallbackEntryRowReferenceReader.HasMatchingAuditReference(
                audit.RelatedEntityRefsJson,
                correction?.EntryBatchId ?? closure.EntryBatchId,
                paperReference))
        {
            return false;
        }

        var witness = lifecycleAudits.SingleOrDefault(candidate => candidate.Id
            == audit.AuditEntryId.Value);
        if (witness is null || witness.EntityId != closure.Id
            || witness.EntityType != MembershipNegativeClosureAuditActions.EntityType
            || witness.ActionType != audit.ActionType
            || !HasExpectedRelatedIds(kind, audit, closure, correction, lines, items,
                payments, lifecycleAudits)
            || !HasExpectedAfterSummary(kind, witness, closure, correction, lines, items,
                payments))
        {
            return false;
        }

        if (kind == ClientNegativeVisitCoverageHistorySourceKind.Created && correction is null)
        {
            return audit.ActionType == MembershipNegativeClosureAuditActions.Created
                && SamePostgreSqlInstant(audit.RecordedAt, closure.RecordedAt)
                && SamePostgreSqlInstant(audit.OccurredAt, closure.OccurredAt)
                && audit.ActorAccountId.Value == closure.RecordedByAccountId
                && audit.SessionId.Value == closure.SessionId
                && audit.Comment == closure.Comment
                && audit.IdempotencyKey == closure.IdempotencyKey
                && TryMapEntryOrigin(closure.EntryOrigin, out var closureOrigin)
                && audit.EntryOrigin == closureOrigin;
        }

        return correction is not null
            && audit.ActionType == (kind == ClientNegativeVisitCoverageHistorySourceKind.Created
                ? MembershipNegativeClosureAuditActions.Created
                : kind == ClientNegativeVisitCoverageHistorySourceKind.Canceled
                    ? MembershipNegativeClosureAuditActions.Canceled
                    : MembershipNegativeClosureAuditActions.Replaced)
            && IsExpectedCorrectionForKind(kind, closure, correction)
            && SamePostgreSqlInstant(audit.OccurredAt, correction.OccurredAt)
            && SamePostgreSqlInstant(audit.RecordedAt, correction.RecordedAt)
            && audit.ActorAccountId.Value == correction.RecordedByAccountId
            && audit.SessionId.Value == correction.SessionId
            && TryMapEntryOrigin(correction.EntryOrigin, out var correctionOrigin)
            && audit.EntryOrigin == correctionOrigin
            && audit.Reason == correction.Reason
            && audit.IdempotencyKey == correction.IdempotencyKey
            && (kind != ClientNegativeVisitCoverageHistorySourceKind.Created
                || audit.Comment == closure.Comment);
    }

    private static NegativeVisitCoverageCorrectionHistorySnapshot ProjectCorrection(
        MembershipNegativeClosureCorrectionRecord correction)
    {
        _ = TryMapCorrectionMode(correction.Mode, out var mode);
        _ = TryMapEntryOrigin(correction.EntryOrigin, out var origin);
        return new NegativeVisitCoverageCorrectionHistorySnapshot(correction.Id,
            correction.OriginalClosureId, correction.ReplacementClosureId, mode, correction.Reason,
            correction.OccurredAt, correction.RecordedAt,
            new AccountId(correction.RecordedByAccountId), new SessionId(correction.SessionId),
            origin, correction.EntryBatchId);
    }

    private static bool ValidateOldFacts(Guid clientId,
        IReadOnlyCollection<MembershipNegativeClosureItemRecord> items,
        IReadOnlyDictionary<Guid, IssuedMembershipRecord> memberships,
        IReadOnlyDictionary<Guid, VisitRecord> visits,
        IReadOnlyDictionary<Guid, VisitConsumptionRecord> consumptions) => items.All(item =>
        memberships.TryGetValue(item.SourceMembershipId, out var sourceMembership)
        && sourceMembership.ClientId == clientId
        && visits.TryGetValue(item.VisitId, out var visit) && visit.ClientId == clientId
        && visit.Status == "active" && visit.VisitKind == "membership"
        && consumptions.TryGetValue(item.OldConsumptionId, out var consumption)
        && consumption.VisitId == item.VisitId && consumption.ClientId == clientId
        && consumption.MembershipId == item.SourceMembershipId
        && consumption.VisitKind == "membership" && consumption.ConsumptionType == "counted"
        && consumption.SourceFactType == "visit" && consumption.SourceFactId == item.VisitId
        && consumption.Status == "active");

    private static bool ValidateNewConsumption(MembershipNegativeClosureItemRecord item,
        MembershipNegativeClosureRecord closure,
        Guid membershipId,
        IReadOnlyDictionary<Guid, VisitConsumptionRecord> consumptions) =>
        item.NewConsumptionId is { } consumptionId
        && consumptions.TryGetValue(consumptionId, out var consumption)
        && consumption.VisitId == item.VisitId && consumption.ClientId == closure.ClientId
        && consumption.MembershipId == membershipId && consumption.VisitKind == "membership"
        && consumption.ConsumptionType == "negative_coverage"
        && consumption.SourceFactType == "negative_closure_item"
        && consumption.SourceFactId == item.Id
        && SamePostgreSqlInstant(consumption.RecordedAt, closure.RecordedAt)
        && consumption.RecordedByAccountId == closure.RecordedByAccountId
        && consumption.RecordedSessionId == closure.SessionId
        && consumption.Status == (closure.Status == "active" ? "active" : "canceled");

    private static bool ValidateVisitOrder(
        IReadOnlyList<MembershipNegativeClosureItemRecord> items,
        IReadOnlyDictionary<Guid, VisitRecord> visits,
        IReadOnlyDictionary<Guid, VisitConsumptionRecord> consumptions)
    {
        if (items.Any(item => !visits.ContainsKey(item.VisitId)
                || !consumptions.ContainsKey(item.OldConsumptionId)))
        {
            return false;
        }

        var expectedOrder = items
            .OrderBy(item => visits[item.VisitId].OccurredAt)
            .ThenBy(item => consumptions[item.OldConsumptionId].RecordedAt)
            .ThenBy(item => item.VisitId)
            .ThenBy(item => item.SourceMembershipId)
            .Select(item => item.VisitId);
        return items.Select(item => item.VisitId).SequenceEqual(expectedOrder);
    }

    private static bool IsExpectedCorrectionForKind(
        ClientNegativeVisitCoverageHistorySourceKind kind,
        MembershipNegativeClosureRecord closure,
        MembershipNegativeClosureCorrectionRecord correction) => kind switch
        {
            ClientNegativeVisitCoverageHistorySourceKind.Created =>
                correction.Mode == "replace"
                && correction.ReplacementClosureId == closure.Id,
            ClientNegativeVisitCoverageHistorySourceKind.Canceled =>
                correction.Mode == "cancel"
                && correction.OriginalClosureId == closure.Id
                && correction.ReplacementClosureId is null
                && closure.Status == "canceled",
            ClientNegativeVisitCoverageHistorySourceKind.Replaced =>
                correction.Mode == "replace"
                && correction.OriginalClosureId == closure.Id
                && correction.ReplacementClosureId.HasValue
                && closure.Status == "replaced",
            _ => false,
        };

    private static bool HasExpectedRelatedIds(
        ClientNegativeVisitCoverageHistorySourceKind kind,
        ClientAuditEntry audit,
        MembershipNegativeClosureRecord closure,
        MembershipNegativeClosureCorrectionRecord? correction,
        IReadOnlyCollection<MembershipNegativeClosureLineRecord> lines,
        IReadOnlyCollection<MembershipNegativeClosureItemRecord> items,
        IReadOnlyCollection<PaymentRecord> payments,
        IReadOnlyCollection<BusinessAuditEntryRecord> lifecycleAudits)
    {
        try
        {
            using var document = JsonDocument.Parse(audit.RelatedEntityRefsJson);
            var related = document.RootElement;
            if (related.GetProperty("clientId").GetGuid() != closure.ClientId)
            {
                return false;
            }

            if (correction is null)
            {
                return !related.TryGetProperty("correctionId", out _)
                    && HasExactGuidSet(related, "sourceMembershipIds", items
                        .Where(item => item.NegativeClosureId == closure.Id)
                        .Select(item => item.SourceMembershipId))
                    && HasExactGuidSet(related, "visitIds", items
                        .Where(item => item.NegativeClosureId == closure.Id)
                        .Select(item => item.VisitId))
                    && HasExpectedCreationPaymentWitness(
                        related, closure, payments, lifecycleAudits);
            }

            if (related.GetProperty("correctionId").GetGuid() != correction.Id)
            {
                return false;
            }

            if (kind == ClientNegativeVisitCoverageHistorySourceKind.Created)
            {
                return related.GetProperty("originalNegativeClosureId").GetGuid()
                    == correction.OriginalClosureId
                    && ReadNullableGuid(related, "coveringMembershipId")
                        == closure.CoveringMembershipId
                    && HasExactGuidSet(
                        related,
                        "sourceMembershipIds",
                        GetCorrectionMembershipIds(closure, correction, items))
                    && HasExactGuidSet(related, "visitIds", items
                        .Where(item => item.NegativeClosureId == closure.Id)
                        .Select(item => item.VisitId))
                    && HasExpectedReplacementPaymentWitness(
                        related, closure, payments, lifecycleAudits);
            }

            return ReadNullableGuid(related, "replacementNegativeClosureId")
                == correction.ReplacementClosureId
                && HasExpectedLifecycleWitness(
                    related, kind, closure, correction, payments, lifecycleAudits)
                && HasExactGuidSet(
                    related,
                    "membershipIds",
                    GetCorrectionMembershipIds(closure, correction, items));
        }
        catch (JsonException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool HasExpectedCreationPaymentWitness(
        JsonElement related,
        MembershipNegativeClosureRecord closure,
        IReadOnlyCollection<PaymentRecord> payments,
        IReadOnlyCollection<BusinessAuditEntryRecord> audits)
    {
        var expectedPayment = closure.ClosureType == "one_off"
            ? payments.SingleOrDefault(payment => payment.NegativeClosureId == closure.Id)
            : closure.CoveringMembershipId is { } membershipId
                ? payments.SingleOrDefault(payment => payment.MembershipId == membershipId
                    && payment.PaymentContext == "membership_sale")
                : null;
        var paymentIdProperty = closure.ClosureType == "one_off" ? "paymentId" : "salePaymentId";
        var auditIdProperty = closure.ClosureType == "one_off"
            ? "paymentAuditEntryId"
            : "salePaymentAuditEntryId";
        return expectedPayment is not null
            && (closure.ClosureType != "new_membership"
                || ReadRequiredGuid(related, "coveringMembershipId")
                    == closure.CoveringMembershipId)
            && ReadRequiredGuid(related, paymentIdProperty) == expectedPayment.Id
            && HasExpectedPaymentCreatedAudit(
                ReadRequiredGuid(related, auditIdProperty), expectedPayment, audits);
    }

    private static bool HasExpectedReplacementPaymentWitness(
        JsonElement related,
        MembershipNegativeClosureRecord closure,
        IReadOnlyCollection<PaymentRecord> payments,
        IReadOnlyCollection<BusinessAuditEntryRecord> audits)
    {
        var payment = payments.SingleOrDefault(candidate => candidate.NegativeClosureId == closure.Id);
        return closure.ClosureType == "one_off"
            ? payment is not null
                && ReadRequiredGuid(related, "replacementPaymentId") == payment.Id
                && HasExpectedPaymentCreatedAudit(
                    ReadRequiredGuid(related, "replacementPaymentAuditEntryId"), payment, audits)
            : payment is null
                && ReadNullableGuid(related, "replacementPaymentId") is null
                && ReadNullableGuid(related, "replacementPaymentAuditEntryId") is null;
    }

    private static bool HasExpectedLifecycleWitness(
        JsonElement related,
        ClientNegativeVisitCoverageHistorySourceKind kind,
        MembershipNegativeClosureRecord closure,
        MembershipNegativeClosureCorrectionRecord correction,
        IReadOnlyCollection<PaymentRecord> payments,
        IReadOnlyCollection<BusinessAuditEntryRecord> audits)
    {
        var originalPayment = payments.SingleOrDefault(payment => payment.NegativeClosureId == closure.Id);
        var replacementPayment = correction.ReplacementClosureId is { } replacementId
            ? payments.SingleOrDefault(payment => payment.NegativeClosureId == replacementId)
            : null;
        if (ReadNullableGuid(related, "originalPaymentId") != originalPayment?.Id
            || ReadNullableGuid(related, "replacementPaymentId") != replacementPayment?.Id)
        {
            return false;
        }

        if (kind == ClientNegativeVisitCoverageHistorySourceKind.Replaced)
        {
            if (correction.ReplacementClosureId is not { } replacementClosureId
                || !HasExpectedAudit(
                    ReadRequiredGuid(related, "replacementClosureAuditId"),
                    MembershipNegativeClosureAuditActions.Created,
                    MembershipNegativeClosureAuditActions.EntityType,
                    replacementClosureId,
                    correction,
                    audits))
            {
                return false;
            }
        }
        else if (ReadNullableGuid(related, "replacementClosureAuditId") is not null)
        {
            return false;
        }

        return originalPayment is null
            ? ReadNullableGuid(related, "paymentLifecycleAuditId") is null
            : HasExpectedAudit(
                ReadRequiredGuid(related, "paymentLifecycleAuditId"),
                kind == ClientNegativeVisitCoverageHistorySourceKind.Canceled
                    ? PaymentAuditActions.Canceled
                    : PaymentAuditActions.Corrected,
                PaymentAuditActions.EntityType,
                originalPayment.Id,
                correction,
                audits);
    }

    private static bool HasExpectedPaymentCreatedAudit(
        Guid auditId,
        PaymentRecord payment,
        IReadOnlyCollection<BusinessAuditEntryRecord> audits) =>
        auditId != Guid.Empty
        && audits.Count(audit => audit.Id == auditId
            && audit.ActionType == PaymentAuditActions.Created
            && audit.EntityType == PaymentAuditActions.EntityType
            && audit.EntityId == payment.Id
            && SamePostgreSqlInstant(audit.OccurredAt, payment.OccurredAt)
            && SamePostgreSqlInstant(audit.RecordedAt, payment.RecordedAt)
            && audit.ActorAccountId == payment.RecordedByAccountId
            && audit.SessionId == payment.SessionId
            && audit.EntryOrigin == payment.EntryOrigin
            && audit.Comment == payment.Comment) == 1;

    private static bool HasExpectedAudit(
        Guid auditId,
        string actionType,
        string entityType,
        Guid entityId,
        MembershipNegativeClosureCorrectionRecord correction,
        IReadOnlyCollection<BusinessAuditEntryRecord> audits) =>
        auditId != Guid.Empty
        && audits.Count(audit => audit.Id == auditId
            && audit.ActionType == actionType
            && audit.EntityType == entityType
            && audit.EntityId == entityId
            && SamePostgreSqlInstant(audit.OccurredAt, correction.OccurredAt)
            && SamePostgreSqlInstant(audit.RecordedAt, correction.RecordedAt)
            && audit.ActorAccountId == correction.RecordedByAccountId
            && audit.SessionId == correction.SessionId
            && audit.EntryOrigin == correction.EntryOrigin
            && audit.Reason == correction.Reason
            && audit.IdempotencyKey == correction.IdempotencyKey) == 1;

    private static bool HasExpectedAfterSummary(
        ClientNegativeVisitCoverageHistorySourceKind kind,
        BusinessAuditEntryRecord audit,
        MembershipNegativeClosureRecord closure,
        MembershipNegativeClosureCorrectionRecord? correction,
        IReadOnlyCollection<MembershipNegativeClosureLineRecord> lines,
        IReadOnlyCollection<MembershipNegativeClosureItemRecord> items,
        IReadOnlyCollection<PaymentRecord> payments)
    {
        try
        {
            using var document = JsonDocument.Parse(audit.AfterSummaryJson);
            var after = document.RootElement;
            var closureItems = items.Where(item => item.NegativeClosureId == closure.Id).ToArray();
            if (kind == ClientNegativeVisitCoverageHistorySourceKind.Created)
            {
                return ReadRequiredGuid(after, "negativeClosureId") == closure.Id
                    && after.GetProperty("closureType").GetString() == closure.ClosureType
                    && ReadCreatedVisitsCount(after) == closure.VisitsCount
                    && HasExactGuidSet(after, "coveredVisitIds", closureItems.Select(item => item.VisitId))
                    && SamePostgreSqlInstant(after.GetProperty("occurredAt").GetDateTimeOffset(), closure.OccurredAt)
                    && SamePostgreSqlInstant(after.GetProperty("recordedAt").GetDateTimeOffset(), closure.RecordedAt)
                    && after.GetProperty("entryOrigin").GetString() == closure.EntryOrigin
                    && ReadNullableGuid(after, "entryBatchId") == closure.EntryBatchId
                    && after.GetProperty("status").GetString() == "active"
                    && (closure.ClosureType != "new_membership"
                        || ReadRequiredGuid(after, "coveringMembershipId")
                            == closure.CoveringMembershipId)
                    && (closure.ClosureType != "one_off"
                        || HasExpectedLineSummaries(after, closure, lines))
                    && (correction is null
                        || HasExpectedReplacementAuditSummary(after, audit))
                    && (closure.ClosureType != "one_off"
                        || (correction is null
                            ? HasExpectedOneOffAfterPayment(
                                after,
                                closure,
                                lines,
                                payments)
                            : HasExpectedReplacementOneOffAfterPayment(
                                after,
                                closure,
                                lines,
                                payments)));
            }

            if (correction is null || !after.TryGetProperty("correction", out var correctionAfter)
                || ReadRequiredGuid(correctionAfter, "correctionId") != correction.Id
                || correctionAfter.GetProperty("mode").GetString() != correction.Mode
                || correctionAfter.GetProperty("reason").GetString() != correction.Reason
                || SamePostgreSqlInstant(
                    correctionAfter.GetProperty("occurredAt").GetDateTimeOffset(), correction.OccurredAt) == false
                || SamePostgreSqlInstant(
                    correctionAfter.GetProperty("recordedAt").GetDateTimeOffset(), correction.RecordedAt) == false
                || correctionAfter.GetProperty("entryOrigin").GetString() != correction.EntryOrigin
                || ReadNullableGuid(correctionAfter, "entryBatchId") != correction.EntryBatchId)
            {
                return false;
            }

            return after.TryGetProperty("originalClosure", out var originalAfter)
                && ReadRequiredGuid(originalAfter, "id") == closure.Id
                && originalAfter.GetProperty("closureType").GetString() == closure.ClosureType
                && originalAfter.GetProperty("status").GetString() == closure.Status
                && correctionAfter.GetProperty("changedAfterClose").GetBoolean()
                    == audit.ChangedAfterClose
                && HasExpectedLifecycleReplacementSummary(
                    after,
                    correction,
                    items,
                    payments);
        }
        catch (JsonException) { return false; }
        catch (KeyNotFoundException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    private static bool HasExpectedOneOffAfterPayment(
        JsonElement after,
        MembershipNegativeClosureRecord closure,
        IReadOnlyCollection<MembershipNegativeClosureLineRecord> lines,
        IReadOnlyCollection<PaymentRecord> payments)
    {
        var payment = payments.SingleOrDefault(candidate => candidate.NegativeClosureId == closure.Id);
        return payment is not null && after.TryGetProperty("payment", out var paymentAfter)
            && ReadRequiredGuid(paymentAfter, "paymentId") == payment.Id
            && paymentAfter.GetProperty("amount").GetDecimal() == payment.Amount
            && paymentAfter.GetProperty("currency").GetString() == payment.Currency
            && paymentAfter.GetProperty("method").GetString() == "cash"
            && paymentAfter.GetProperty("context").GetString() == "negative_closure"
            && lines.Where(line => line.NegativeClosureId == closure.Id)
                .Sum(line => line.LineTotal) == payment.Amount;
    }

    private static bool HasExpectedReplacementOneOffAfterPayment(
        JsonElement after,
        MembershipNegativeClosureRecord closure,
        IReadOnlyCollection<MembershipNegativeClosureLineRecord> lines,
        IReadOnlyCollection<PaymentRecord> payments)
    {
        var payment = payments.SingleOrDefault(
            candidate => candidate.NegativeClosureId == closure.Id);
        return payment is not null
            && ReadRequiredGuid(after, "replacementPaymentId") == payment.Id
            && after.TryGetProperty("replacementPayment", out var paymentAfter)
            && paymentAfter.ValueKind == JsonValueKind.Object
            && ReadRequiredGuid(paymentAfter, "paymentId") == payment.Id
            && ReadNullableGuid(paymentAfter, "negativeClosureId") == closure.Id
            && paymentAfter.GetProperty("amount").GetDecimal() == payment.Amount
            && paymentAfter.GetProperty("currency").GetString() == payment.Currency
            && paymentAfter.GetProperty("method").GetString() == "cash"
            && paymentAfter.GetProperty("paymentContext").GetString()
                == "negative_closure"
            && SamePostgreSqlInstant(
                paymentAfter.GetProperty("occurredAt").GetDateTimeOffset(),
                payment.OccurredAt)
            && SamePostgreSqlInstant(
                paymentAfter.GetProperty("recordedAt").GetDateTimeOffset(),
                payment.RecordedAt)
            && paymentAfter.GetProperty("entryOrigin").GetString()
                == payment.EntryOrigin
            && ReadNullableGuid(paymentAfter, "entryBatchId")
                == payment.EntryBatchId
            && paymentAfter.GetProperty("status").GetString() == "active"
            && lines.Where(line => line.NegativeClosureId == closure.Id)
                .Sum(line => line.LineTotal) == payment.Amount;
    }

    private static bool HasExpectedLineSummaries(
        JsonElement after,
        MembershipNegativeClosureRecord closure,
        IEnumerable<MembershipNegativeClosureLineRecord> lines)
    {
        var expected = lines
            .Where(line => line.NegativeClosureId == closure.Id)
            .OrderBy(line => line.Sequence)
            .ToArray();
        var actual = after.GetProperty("lines").EnumerateArray().ToArray();
        if (actual.Length != expected.Length)
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            var source = expected[index];
            var witness = actual[index];
            if (ReadRequiredGuid(witness, "lineId") != source.Id
                || witness.GetProperty("sequence").GetInt32() != source.Sequence
                || ReadRequiredGuid(witness, "membershipTypeId")
                    != source.MembershipTypeId
                || witness.GetProperty("typeName").GetString()
                    != source.TypeNameSnapshot
                || witness.GetProperty("quantity").GetInt32() != source.Quantity
                || witness.GetProperty("unitPriceAmount").GetDecimal()
                    != source.UnitPriceAmountSnapshot
                || witness.GetProperty("currency").GetString()
                    != source.CurrencySnapshot
                || witness.GetProperty("lineTotal").GetDecimal()
                    != source.LineTotal
                || witness.TryGetProperty("durationDays", out var durationDays)
                    && durationDays.GetInt32() != source.DurationDaysSnapshot
                || witness.TryGetProperty("visitsLimit", out var visitsLimit)
                    && visitsLimit.GetInt32() != source.VisitsLimitSnapshot)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasExpectedReplacementAuditSummary(
        JsonElement after,
        BusinessAuditEntryRecord audit)
    {
        using var relatedDocument = JsonDocument.Parse(audit.RelatedEntityRefsJson);
        var related = relatedDocument.RootElement;
        return ReadNullableGuid(after, "replacementPaymentId")
                == ReadNullableGuid(related, "replacementPaymentId")
            && ReadNullableGuid(after, "replacementPaymentAuditEntryId")
                == ReadNullableGuid(related, "replacementPaymentAuditEntryId")
            && after.GetProperty("changedAfterClose").GetBoolean()
                == audit.ChangedAfterClose;
    }

    private static bool HasExpectedLifecycleReplacementSummary(
        JsonElement after,
        MembershipNegativeClosureCorrectionRecord correction,
        IEnumerable<MembershipNegativeClosureItemRecord> items,
        IEnumerable<PaymentRecord> payments)
    {
        var replacement = after.GetProperty("replacement");
        if (correction.ReplacementClosureId is not { } replacementId)
        {
            return replacement.ValueKind == JsonValueKind.Null;
        }

        var replacementItems = items
            .Where(item => item.NegativeClosureId == replacementId)
            .ToArray();
        var replacementPaymentId = payments.SingleOrDefault(
            payment => payment.NegativeClosureId == replacementId)?.Id;
        return replacement.ValueKind == JsonValueKind.Object
            && ReadRequiredGuid(replacement, "negativeClosureId") == replacementId
            && replacement.GetProperty("visitsCount").GetInt32()
                == replacementItems.Length
            && HasExactGuidSet(
                replacement,
                "visitIds",
                replacementItems.Select(item => item.VisitId))
            && ReadNullableGuid(replacement, "paymentId") == replacementPaymentId;
    }

    private static IEnumerable<Guid> GetCorrectionMembershipIds(
        MembershipNegativeClosureRecord closure,
        MembershipNegativeClosureCorrectionRecord correction,
        IEnumerable<MembershipNegativeClosureItemRecord> items)
    {
        var closureIds = new[]
        {
            closure.Id,
            correction.OriginalClosureId,
            correction.ReplacementClosureId,
        };
        return items
            .Where(item => closureIds.Contains(item.NegativeClosureId))
            .Select(item => item.SourceMembershipId)
            .Concat(closure.CoveringMembershipId is { } membershipId
                ? [membershipId]
                : []);
    }

    private static int ReadCreatedVisitsCount(JsonElement after) =>
        after.TryGetProperty("visitsCount", out var visitsCount)
            ? visitsCount.GetInt32()
            : after.GetProperty("coveredVisitCount").GetInt32();

    private static bool HasExactGuidSet(
        JsonElement source,
        string propertyName,
        IEnumerable<Guid> expected)
    {
        var expectedSet = expected.Distinct().Order().ToArray();
        var actual = source.GetProperty(propertyName).EnumerateArray()
            .Select(value => value.GetGuid()).Order().ToArray();
        return actual.Length == actual.Distinct().Count()
            && actual.SequenceEqual(expectedSet);
    }

    private static Guid ReadRequiredGuid(JsonElement source, string propertyName) =>
        source.GetProperty(propertyName).GetGuid();

    private static Guid? ReadNullableGuid(JsonElement source, string propertyName)
    {
        var value = source.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetGuid();
    }

    private static bool HasContiguousSequence(IEnumerable<int> values)
    {
        var snapshot = values.ToArray();
        return snapshot.SequenceEqual(Enumerable.Range(1, snapshot.Length));
    }

    private static bool TryMapHistoryKind(string action, out ClientNegativeVisitCoverageHistorySourceKind kind)
    {
        kind = action switch
        {
            MembershipNegativeClosureAuditActions.Created => ClientNegativeVisitCoverageHistorySourceKind.Created,
            MembershipNegativeClosureAuditActions.Canceled => ClientNegativeVisitCoverageHistorySourceKind.Canceled,
            MembershipNegativeClosureAuditActions.Replaced => ClientNegativeVisitCoverageHistorySourceKind.Replaced,
            _ => default,
        };
        return action is MembershipNegativeClosureAuditActions.Created
            or MembershipNegativeClosureAuditActions.Canceled
            or MembershipNegativeClosureAuditActions.Replaced;
    }

    private static bool TryMapMethod(string value, out NegativeVisitCoverageClosureMethod method)
    {
        method = value == "one_off" ? NegativeVisitCoverageClosureMethod.OneOff
            : value == "new_membership" ? NegativeVisitCoverageClosureMethod.NewMembership : default;
        return value is "one_off" or "new_membership";
    }

    private static bool TryMapClosureStatus(string value,
        out NegativeVisitCoverageClosureHistoryStatus status)
    {
        status = value == "active" ? NegativeVisitCoverageClosureHistoryStatus.Active
            : value == "canceled" ? NegativeVisitCoverageClosureHistoryStatus.Canceled
            : value == "replaced" ? NegativeVisitCoverageClosureHistoryStatus.Replaced : default;
        return value is "active" or "canceled" or "replaced";
    }

    private static bool TryMapCorrectionMode(string value,
        out NegativeVisitCoverageCorrectionHistoryMode mode)
    {
        mode = value == "cancel" ? NegativeVisitCoverageCorrectionHistoryMode.Cancel
            : value == "replace" ? NegativeVisitCoverageCorrectionHistoryMode.Replace : default;
        return value is "cancel" or "replace";
    }

    private static bool TryMapEntryOrigin(string value, out EntryOrigin origin)
    {
        origin = value switch
        {
            "normal" => EntryOrigin.Normal,
            "paper_fallback" => EntryOrigin.PaperFallback,
            _ => default,
        };
        return value is "normal" or "paper_fallback";
    }

    private static bool TryMoney(decimal amount, string? currency, out Money money)
    {
        try { money = new Money(amount, currency!); return true; }
        catch (ArgumentException) { money = default; return false; }
    }

    private static PaperFallbackEventType ExpectedPaperEventType(
        MembershipNegativeClosureRecord closure, bool correctionCreated) => correctionCreated
        ? PaperFallbackEventType.CorrectionOrCancellation
        : closure.ClosureType == "new_membership"
            ? PaperFallbackEventType.MembershipSale
            : PaperFallbackEventType.NegativeCoverage;

    private static bool SamePostgreSqlInstant(
        DateTimeOffset left,
        DateTimeOffset right) =>
        left.UtcDateTime.Ticks / 10 == right.UtcDateTime.Ticks / 10;
}
