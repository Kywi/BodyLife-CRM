using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

public sealed class GetClientPaymentHistorySourceRowsQueryHandler(
    BodyLifeDbContext dbContext,
    IBodyLifeQueryHandler<GetClientAuditEntriesQuery, GetClientAuditEntriesResult>
        auditEntriesQueryHandler)
    : IBodyLifeQueryHandler<
        GetClientPaymentHistorySourceRowsQuery,
        GetClientPaymentHistorySourceRowsResult>
{
    private static readonly ClientAuditEntityFilter[] EntityFilters =
    [
        ClientAuditEntityFilter.Payment,
    ];

    private static readonly string[] ActionTypes =
    [
        PaymentAuditActions.Created,
        PaymentAuditActions.Corrected,
        PaymentAuditActions.Canceled,
    ];

    public async Task<GetClientPaymentHistorySourceRowsResult> ExecuteAsync(
        GetClientPaymentHistorySourceRowsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var auditResult = await auditEntriesQueryHandler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                query.Actor,
                query.ClientId,
                query.OccurredFromInclusive,
                query.OccurredBeforeExclusive,
                EntityFilters,
                ActionTypes,
                query.Limit,
                query.Offset,
                query.AuditEntryIds),
            cancellationToken);
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
            || auditPage.Items.Any(item =>
                item.EntityType != ClientAuditEntityFilter.Payment)
            || auditPage.Items.Select(item => item.AuditEntryId).Distinct().Count()
                != auditPage.Items.Count
            || auditPage.Items
                .GroupBy(item => (item.ActionType, item.EntityId))
                .Any(group => group.Count() > 1))
        {
            return GetClientPaymentHistorySourceRowsResult.InconsistentSource();
        }

        var auditPaymentIds = auditPage.Items
            .Select(item => item.EntityId)
            .Distinct()
            .ToArray();
        if (auditPaymentIds.Length == 0)
        {
            return GetClientPaymentHistorySourceRowsResult.Succeeded(
                ClientPaymentHistorySourceRowsPage.Create(
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    auditPage.Offset,
                    items: [],
                    auditPage.HasMore));
        }

        var relations = await PaymentQuerySupport.ReadCanonicalRelationsAsync(
            dbContext,
            auditPaymentIds,
            cancellationToken);
        if (relations is null)
        {
            return GetClientPaymentHistorySourceRowsResult.InconsistentSource();
        }

        var relevantPaymentIds = relations.PaymentIds.ToArray();
        var paymentRows = await (
            from payment in dbContext.Set<PaymentRecord>().AsNoTracking()
            join membership in dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
                on payment.MembershipId equals (Guid?)membership.Id
                into memberships
            from membership in memberships.DefaultIfEmpty()
            where relevantPaymentIds.Contains(payment.Id)
                && payment.ClientId == query.ClientId
            select new PaymentStorageRow(
                payment,
                membership == null ? null : membership.ClientId,
                membership == null ? null : membership.TypeNameSnapshot))
            .ToArrayAsync(cancellationToken);
        if (paymentRows.Length != relevantPaymentIds.Length)
        {
            return GetClientPaymentHistorySourceRowsResult.InconsistentSource();
        }

        var paperReferenceReader = new PaperFallbackEntryRowReferenceReader(dbContext);
        var paymentPaperReferences = await paperReferenceReader.LoadAsync(
            paymentRows.Select(row =>
            {
                relations.CorrectionsFromOriginalByPaymentId.TryGetValue(
                    row.Payment.Id,
                    out var incomingCorrection);
                return new PaperFallbackEntryRowReferenceSource(
                    row.Payment.Id,
                    row.Payment.EntryOrigin,
                    row.Payment.EntryBatchId,
                    incomingCorrection?.OccurredAt ?? row.Payment.OccurredAt,
                    row.Payment.RecordedByAccountId,
                    row.Payment.SessionId,
                    ExpectedPaymentEventType(row.Payment, incomingCorrection));
            }).ToArray(),
            CorrectPaymentCommand.PaymentEntityType,
            PaperFallbackEventType.Payment,
            cancellationToken);
        var correctionPaperReferences = await LoadPaperReferencesAsync(
            relations.CorrectionsToReplacementByPaymentId.Values
                .Select(correction => new PaperReferenceLookup(
                    new PaperFallbackEntryRowReferenceSource(
                        correction.CorrectionId,
                        correction.EntryOrigin,
                        correction.EntryBatchId,
                        correction.OccurredAt,
                        correction.RecordedByAccountId,
                        correction.SessionId),
                    correction.PaperEntityType))
                .ToArray(),
            PaperFallbackEventType.CorrectionOrCancellation,
            cancellationToken);
        var cancellationPaperReferences = await LoadPaperReferencesAsync(
            relations.CancellationsByPaymentId.Values
                .Select(cancellation => new PaperReferenceLookup(
                    new PaperFallbackEntryRowReferenceSource(
                        cancellation.CancellationId,
                        cancellation.EntryOrigin,
                        cancellation.EntryBatchId,
                        cancellation.OccurredAt,
                        cancellation.RecordedByAccountId,
                        cancellation.SessionId),
                    cancellation.PaperEntityType))
                .ToArray(),
            PaperFallbackEventType.CorrectionOrCancellation,
            cancellationToken);
        if (paymentPaperReferences is null
            || correctionPaperReferences is null
            || cancellationPaperReferences is null)
        {
            return GetClientPaymentHistorySourceRowsResult.InconsistentSource();
        }

        var sourcesByPaymentId = new Dictionary<Guid, CanonicalPaymentHistorySource>(
            paymentRows.Length);

        foreach (var storageRow in paymentRows)
        {
            var paymentId = storageRow.Payment.Id;
            relations.CorrectionsFromOriginalByPaymentId.TryGetValue(
                paymentId,
                out var correctionFromOriginal);
            relations.CorrectionsToReplacementByPaymentId.TryGetValue(
                paymentId,
                out var correctionToReplacement);
            relations.CancellationsByPaymentId.TryGetValue(
                paymentId,
                out var cancellation);
            if (!TryMapCanonicalSource(
                    storageRow,
                    cancellation,
                    correctionFromOriginal,
                    correctionToReplacement,
                    paymentPaperReferences.GetValueOrDefault(paymentId),
                    correctionFromOriginal is null
                        ? null
                        : correctionPaperReferences.GetValueOrDefault(
                            correctionFromOriginal.CorrectionId),
                    correctionToReplacement is null
                        ? null
                        : correctionPaperReferences.GetValueOrDefault(
                            correctionToReplacement.CorrectionId),
                    cancellation is null
                        ? null
                        : cancellationPaperReferences.GetValueOrDefault(
                            cancellation.CancellationId),
                    out var source)
                || source is null)
            {
                return GetClientPaymentHistorySourceRowsResult.InconsistentSource();
            }

            sourcesByPaymentId.Add(paymentId, source);
        }

        var rows = new List<ClientPaymentHistorySourceRow>(auditPage.Items.Count);
        try
        {
            foreach (var auditEntry in auditPage.Items)
            {
                if (!sourcesByPaymentId.TryGetValue(
                        auditEntry.EntityId,
                        out var source))
                {
                    return GetClientPaymentHistorySourceRowsResult.InconsistentSource();
                }

                var row = auditEntry.ActionType switch
                {
                    PaymentAuditActions.Created => MapCreatedPayment(
                        source,
                        auditEntry),
                    PaymentAuditActions.Corrected => MapCorrectedPayment(
                        source,
                        sourcesByPaymentId,
                        auditEntry),
                    PaymentAuditActions.Canceled => MapCanceledPayment(
                        source,
                        auditEntry),
                    _ => null,
                };
                if (row is null)
                {
                    return GetClientPaymentHistorySourceRowsResult.InconsistentSource();
                }

                rows.Add(row);
            }

            return GetClientPaymentHistorySourceRowsResult.Succeeded(
                ClientPaymentHistorySourceRowsPage.Create(
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    auditPage.Offset,
                    rows,
                    auditPage.HasMore));
        }
        catch (ArgumentException)
        {
            return GetClientPaymentHistorySourceRowsResult.InconsistentSource();
        }
        catch (InvalidOperationException)
        {
            return GetClientPaymentHistorySourceRowsResult.InconsistentSource();
        }
    }

    private static bool TryMapCanonicalSource(
        PaymentStorageRow storageRow,
        PaymentQuerySupport.CanonicalPaymentCancellationSourceRow? cancellation,
        PaymentQuerySupport.CanonicalPaymentCorrectionSourceRow?
            correctionFromOriginal,
        PaymentQuerySupport.CanonicalPaymentCorrectionSourceRow?
            correctionToReplacement,
        PaperFallbackEntryRowReference? paperReference,
        PaperFallbackEntryRowReference? correctionFromPaperReference,
        PaperFallbackEntryRowReference? correctionToPaperReference,
        PaperFallbackEntryRowReference? cancellationPaperReference,
        out CanonicalPaymentHistorySource? source)
    {
        source = null;
        var payment = storageRow.Payment;
        if (payment.Id == Guid.Empty
            || payment.ClientId == Guid.Empty
            || payment.RecordedByAccountId == Guid.Empty
            || payment.SessionId == Guid.Empty
            || payment.EntryOrigin == "paper_fallback" && paperReference is null
            || payment.EntryOrigin != "paper_fallback" && paperReference is not null
            || correctionFromOriginal is not null
                && correctionFromOriginal.EntryOrigin == "paper_fallback"
                && correctionFromPaperReference is null
            || correctionFromOriginal is not null
                && correctionFromOriginal.EntryOrigin != "paper_fallback"
                && correctionFromPaperReference is not null
            || correctionToReplacement is not null
                && correctionToReplacement.EntryOrigin == "paper_fallback"
                && correctionToPaperReference is null
            || correctionToReplacement is not null
                && correctionToReplacement.EntryOrigin != "paper_fallback"
                && correctionToPaperReference is not null
            || cancellation is not null
                && cancellation.EntryOrigin == "paper_fallback"
                && cancellationPaperReference is null
            || cancellation is not null
                && cancellation.EntryOrigin != "paper_fallback"
                && cancellationPaperReference is not null)
        {
            return false;
        }

        var canonicalPayment = new PaymentQuerySupport.CanonicalPaymentSourceRow(
            payment.Id,
            payment.ClientId,
            payment.MembershipId,
            storageRow.MembershipClientId,
            storageRow.MembershipTypeNameSnapshot,
            payment.Amount,
            payment.Currency,
            payment.Method,
            payment.PaymentContext,
            payment.OccurredAt,
            payment.RecordedAt,
            payment.RecordedByAccountId,
            payment.SessionId,
            payment.EntryOrigin,
            payment.EntryBatchId,
            payment.Comment,
            payment.Status);
        if (!PaymentQuerySupport.TryMapSourceRow(
                canonicalPayment,
                cancellation,
                correctionFromOriginal,
                correctionToReplacement,
                out var projection)
            || projection is null)
        {
            return false;
        }

        var paymentSource = new PaymentHistorySource(
            payment.Id,
            payment.ClientId,
            payment.MembershipId,
            storageRow.MembershipTypeNameSnapshot,
            projection.Amount,
            projection.Method,
            projection.PaymentContext,
            payment.OccurredAt,
            payment.RecordedAt,
            new AccountId(payment.RecordedByAccountId),
            new SessionId(payment.SessionId),
            projection.EntryOrigin,
            payment.EntryBatchId,
            payment.Comment,
            projection.Status,
            projection.Cancellation?.CancellationId,
            projection.CorrectionFromOriginal?.CorrectionId,
            projection.CorrectionToReplacement?.CorrectionId,
            paperReference);
        source = new CanonicalPaymentHistorySource(
            payment,
            paymentSource,
            projection,
            correctionFromPaperReference,
            correctionToPaperReference,
            cancellationPaperReference);
        return true;
    }

    private static ClientPaymentHistorySourceRow? MapCreatedPayment(
        CanonicalPaymentHistorySource source,
        ClientAuditEntry auditEntry)
    {
        var payment = source.Payment;
        if (auditEntry.ActionType != PaymentAuditActions.Created
            || auditEntry.EntityType != ClientAuditEntityFilter.Payment
            || auditEntry.EntityId != payment.Id
            || auditEntry.OccurredAt != payment.OccurredAt
            || auditEntry.RecordedAt != payment.RecordedAt
            || auditEntry.ActorAccountId.Value != payment.RecordedByAccountId
            || auditEntry.SessionId.Value != payment.SessionId
            || auditEntry.EntryOrigin != source.Source.EntryOrigin
            || auditEntry.Comment != payment.Comment
            || !PaperFallbackEntryRowReferenceReader.HasMatchingAuditReference(
                auditEntry,
                payment.EntryBatchId,
                source.Source.PaperReference))
        {
            return null;
        }

        return new ClientPaymentHistorySourceRow(
            ClientPaymentHistorySourceKind.CreatedPayment,
            payment.ClientId,
            payment.Id,
            payment.OccurredAt,
            payment.RecordedAt,
            source.Source.EntryOrigin,
            source.Source,
            Correction: null,
            Cancellation: null,
            auditEntry);
    }

    private static ClientPaymentHistorySourceRow? MapCorrectedPayment(
        CanonicalPaymentHistorySource original,
        IReadOnlyDictionary<Guid, CanonicalPaymentHistorySource> sourcesByPaymentId,
        ClientAuditEntry auditEntry)
    {
        var correction = original.Projection.CorrectionToReplacement;
        if (correction is null
            || original.Source.CurrentStatus != ClientPaymentRowStatus.Replaced)
        {
            return null;
        }

        if (!sourcesByPaymentId.TryGetValue(
                correction.ReplacementPaymentId,
                out var replacement)
            || replacement.Source.IncomingCorrectionId != correction.CorrectionId
            || replacement.Payment.RecordedAt != correction.RecordedAt
            || replacement.Payment.RecordedByAccountId
                != correction.RecordedByAccountId
            || replacement.Payment.SessionId != correction.SessionId
            || replacement.Source.EntryOrigin != correction.EntryOrigin
            || replacement.Payment.EntryBatchId != correction.EntryBatchId
            || auditEntry.ActionType != PaymentAuditActions.Corrected
            || auditEntry.EntityType != ClientAuditEntityFilter.Payment
            || auditEntry.EntityId != original.Payment.Id
            || auditEntry.OccurredAt != correction.OccurredAt
            || auditEntry.RecordedAt != correction.RecordedAt
            || auditEntry.ActorAccountId.Value != correction.RecordedByAccountId
            || auditEntry.SessionId.Value != correction.SessionId
            || auditEntry.EntryOrigin != correction.EntryOrigin
            || auditEntry.Reason != correction.Reason
            || !PaperFallbackEntryRowReferenceReader.HasMatchingAuditReference(
                auditEntry,
                correction.EntryBatchId,
                original.CorrectionToPaperReference))
        {
            return null;
        }

        var correctionSource = new PaymentCorrectionHistorySource(
            correction.CorrectionId,
            original.Payment.ClientId,
            correction.OriginalPaymentId,
            correction.ReplacementPaymentId,
            correction.ChangedFields,
            correction.Reason,
            correction.OccurredAt,
            correction.RecordedAt,
            new AccountId(correction.RecordedByAccountId),
            new SessionId(correction.SessionId),
            correction.EntryOrigin,
            correction.EntryBatchId,
            original.Source,
            replacement.Source,
            original.CorrectionToPaperReference);
        return new ClientPaymentHistorySourceRow(
            ClientPaymentHistorySourceKind.CorrectedPayment,
            original.Payment.ClientId,
            original.Payment.Id,
            correction.OccurredAt,
            correction.RecordedAt,
            correction.EntryOrigin,
            CreatedPayment: null,
            correctionSource,
            Cancellation: null,
            auditEntry);
    }

    private static ClientPaymentHistorySourceRow? MapCanceledPayment(
        CanonicalPaymentHistorySource source,
        ClientAuditEntry auditEntry)
    {
        var cancellation = source.Projection.Cancellation;
        if (cancellation is null
            || source.Source.CurrentStatus != ClientPaymentRowStatus.Canceled)
        {
            return null;
        }

        if (auditEntry.ActionType != PaymentAuditActions.Canceled
            || auditEntry.EntityType != ClientAuditEntityFilter.Payment
            || auditEntry.EntityId != source.Payment.Id
            || auditEntry.OccurredAt != cancellation.OccurredAt
            || auditEntry.RecordedAt != cancellation.RecordedAt
            || auditEntry.ActorAccountId.Value
                != cancellation.RecordedByAccountId
            || auditEntry.SessionId.Value != cancellation.SessionId
            || auditEntry.EntryOrigin != cancellation.EntryOrigin
            || auditEntry.Reason != cancellation.Reason
            || !PaperFallbackEntryRowReferenceReader.HasMatchingAuditReference(
                auditEntry,
                cancellation.EntryBatchId,
                source.CancellationPaperReference))
        {
            return null;
        }

        var cancellationSource = new PaymentCancellationHistorySource(
            cancellation.CancellationId,
            source.Payment.ClientId,
            source.Payment.Id,
            cancellation.Reason,
            cancellation.OccurredAt,
            cancellation.RecordedAt,
            new AccountId(cancellation.RecordedByAccountId),
            new SessionId(cancellation.SessionId),
            cancellation.EntryOrigin,
            cancellation.EntryBatchId,
            source.Source,
            source.CancellationPaperReference);
        return new ClientPaymentHistorySourceRow(
            ClientPaymentHistorySourceKind.CanceledPayment,
            source.Payment.ClientId,
            source.Payment.Id,
            cancellation.OccurredAt,
            cancellation.RecordedAt,
            cancellation.EntryOrigin,
            CreatedPayment: null,
            Correction: null,
            cancellationSource,
            auditEntry);
    }

    private static GetClientPaymentHistorySourceRowsResult MapAuditFailure(
        GetClientAuditEntriesResult auditResult)
    {
        return auditResult.Status switch
        {
            GetClientAuditEntriesStatus.PermissionDenied
                => GetClientPaymentHistorySourceRowsResult.Denied(),
            GetClientAuditEntriesStatus.ValidationFailed
                => GetClientPaymentHistorySourceRowsResult.Invalid(
                    auditResult.ErrorMessage ?? "Client history selectors are invalid.",
                    auditResult.ErrorField),
            GetClientAuditEntriesStatus.NotFound
                => GetClientPaymentHistorySourceRowsResult.MissingClient(),
            _ => GetClientPaymentHistorySourceRowsResult.InconsistentSource(),
        };
    }

    private async Task<Dictionary<Guid, PaperFallbackEntryRowReference>?>
        LoadPaperReferencesAsync(
            IReadOnlyList<PaperReferenceLookup> lookups,
            PaperFallbackEventType expectedEventType,
            CancellationToken cancellationToken)
    {
        var references = new Dictionary<Guid, PaperFallbackEntryRowReference>();
        foreach (var group in lookups.GroupBy(lookup => lookup.EntityType))
        {
            var loaded = await new PaperFallbackEntryRowReferenceReader(dbContext)
                .LoadAsync(
                    group.Select(lookup => lookup.Source).ToArray(),
                    group.Key,
                    expectedEventType,
                    cancellationToken);
            if (loaded is null
                || loaded.Keys.Any(references.ContainsKey))
            {
                return null;
            }

            foreach (var (entityId, reference) in loaded)
            {
                references.Add(entityId, reference);
            }
        }

        return references;
    }

    private static PaperFallbackEventType ExpectedPaymentEventType(
        PaymentRecord payment,
        PaymentQuerySupport.CanonicalPaymentCorrectionSourceRow? incomingCorrection) =>
        incomingCorrection is not null
            ? PaperFallbackEventType.CorrectionOrCancellation
            : payment.PaymentContext switch
            {
                "membership_sale" => PaperFallbackEventType.MembershipSale,
                "negative_closure" => PaperFallbackEventType.NegativeCoverage,
                _ => PaperFallbackEventType.Payment,
            };

    private sealed record PaymentStorageRow(
        PaymentRecord Payment,
        Guid? MembershipClientId,
        string? MembershipTypeNameSnapshot);

    private sealed record CanonicalPaymentHistorySource(
        PaymentRecord Payment,
        PaymentHistorySource Source,
        PaymentQuerySupport.CanonicalPaymentProjection Projection,
        PaperFallbackEntryRowReference? CorrectionFromPaperReference,
        PaperFallbackEntryRowReference? CorrectionToPaperReference,
        PaperFallbackEntryRowReference? CancellationPaperReference);

    private sealed record PaperReferenceLookup(
        PaperFallbackEntryRowReferenceSource Source,
        string EntityType);
}
