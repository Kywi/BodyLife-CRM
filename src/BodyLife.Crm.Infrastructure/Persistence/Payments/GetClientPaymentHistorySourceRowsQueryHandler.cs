using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
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

        var outgoingCorrections = await dbContext.Set<PaymentCorrectionRecord>()
            .AsNoTracking()
            .Where(correction =>
                auditPaymentIds.Contains(correction.OriginalPaymentId))
            .ToArrayAsync(cancellationToken);
        if (outgoingCorrections
            .GroupBy(correction => correction.OriginalPaymentId)
            .Any(group => group.Count() > 1))
        {
            return GetClientPaymentHistorySourceRowsResult.InconsistentSource();
        }

        var relevantPaymentIds = auditPaymentIds
            .Concat(outgoingCorrections.Select(correction =>
                correction.ReplacementPaymentId))
            .Distinct()
            .ToArray();
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

        var correctionRows = await dbContext.Set<PaymentCorrectionRecord>()
            .AsNoTracking()
            .Where(correction =>
                relevantPaymentIds.Contains(correction.OriginalPaymentId)
                || relevantPaymentIds.Contains(correction.ReplacementPaymentId))
            .ToArrayAsync(cancellationToken);
        var cancellationRows = await dbContext.Set<PaymentCancellationRecord>()
            .AsNoTracking()
            .Where(cancellation =>
                relevantPaymentIds.Contains(cancellation.PaymentId))
            .ToArrayAsync(cancellationToken);
        if (!HaveCanonicalRelationKeys(correctionRows, cancellationRows)
            || correctionRows
                .Where(correction =>
                    relevantPaymentIds.Contains(correction.OriginalPaymentId))
                .GroupBy(correction => correction.OriginalPaymentId)
                .Any(group => group.Count() > 1)
            || correctionRows
                .Where(correction =>
                    relevantPaymentIds.Contains(correction.ReplacementPaymentId))
                .GroupBy(correction => correction.ReplacementPaymentId)
                .Any(group => group.Count() > 1)
            || cancellationRows
                .GroupBy(cancellation => cancellation.PaymentId)
                .Any(group => group.Count() > 1))
        {
            return GetClientPaymentHistorySourceRowsResult.InconsistentSource();
        }

        var correctionsFromOriginal = correctionRows
            .Where(correction =>
                relevantPaymentIds.Contains(correction.ReplacementPaymentId))
            .ToDictionary(correction => correction.ReplacementPaymentId);
        var correctionsToReplacement = correctionRows
            .Where(correction =>
                relevantPaymentIds.Contains(correction.OriginalPaymentId))
            .ToDictionary(correction => correction.OriginalPaymentId);
        var cancellationsByPaymentId = cancellationRows.ToDictionary(
            cancellation => cancellation.PaymentId);
        var sourcesByPaymentId = new Dictionary<Guid, CanonicalPaymentHistorySource>(
            paymentRows.Length);

        foreach (var storageRow in paymentRows)
        {
            var paymentId = storageRow.Payment.Id;
            correctionsFromOriginal.TryGetValue(
                paymentId,
                out var correctionFromOriginal);
            correctionsToReplacement.TryGetValue(
                paymentId,
                out var correctionToReplacement);
            cancellationsByPaymentId.TryGetValue(paymentId, out var cancellation);
            if (!TryMapCanonicalSource(
                    storageRow,
                    cancellation,
                    correctionFromOriginal,
                    correctionToReplacement,
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

    private static bool HaveCanonicalRelationKeys(
        IReadOnlyCollection<PaymentCorrectionRecord> corrections,
        IReadOnlyCollection<PaymentCancellationRecord> cancellations)
    {
        return corrections.All(correction =>
                correction.Id != Guid.Empty
                && correction.ClientId != Guid.Empty
                && correction.OriginalPaymentId != Guid.Empty
                && correction.ReplacementPaymentId != Guid.Empty
                && correction.RecordedByAccountId != Guid.Empty
                && correction.SessionId != Guid.Empty)
            && cancellations.All(cancellation =>
                cancellation.Id != Guid.Empty
                && cancellation.PaymentId != Guid.Empty
                && cancellation.RecordedByAccountId != Guid.Empty
                && cancellation.SessionId != Guid.Empty);
    }

    private static bool TryMapCanonicalSource(
        PaymentStorageRow storageRow,
        PaymentCancellationRecord? cancellation,
        PaymentCorrectionRecord? correctionFromOriginal,
        PaymentCorrectionRecord? correctionToReplacement,
        out CanonicalPaymentHistorySource? source)
    {
        source = null;
        var payment = storageRow.Payment;
        if (payment.Id == Guid.Empty
            || payment.ClientId == Guid.Empty
            || payment.RecordedByAccountId == Guid.Empty
            || payment.SessionId == Guid.Empty)
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
        var canonicalCancellation = cancellation is null
            ? null
            : MapCancellation(cancellation);
        var canonicalCorrectionFromOriginal = correctionFromOriginal is null
            ? null
            : MapCorrection(correctionFromOriginal);
        var canonicalCorrectionToReplacement = correctionToReplacement is null
            ? null
            : MapCorrection(correctionToReplacement);
        if (!PaymentQuerySupport.TryMapSourceRow(
                canonicalPayment,
                canonicalCancellation,
                canonicalCorrectionFromOriginal,
                canonicalCorrectionToReplacement,
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
            projection.CorrectionToReplacement?.CorrectionId);
        source = new CanonicalPaymentHistorySource(
            payment,
            paymentSource,
            projection);
        return true;
    }

    private static PaymentQuerySupport.CanonicalPaymentCancellationSourceRow
        MapCancellation(PaymentCancellationRecord cancellation)
    {
        return new PaymentQuerySupport.CanonicalPaymentCancellationSourceRow(
            cancellation.Id,
            cancellation.PaymentId,
            cancellation.Reason,
            cancellation.OccurredAt,
            cancellation.RecordedAt,
            cancellation.RecordedByAccountId,
            cancellation.SessionId,
            cancellation.EntryOrigin,
            cancellation.EntryBatchId);
    }

    private static PaymentQuerySupport.CanonicalPaymentCorrectionSourceRow
        MapCorrection(PaymentCorrectionRecord correction)
    {
        return new PaymentQuerySupport.CanonicalPaymentCorrectionSourceRow(
            correction.Id,
            correction.ClientId,
            correction.OriginalPaymentId,
            correction.ReplacementPaymentId,
            correction.ChangedFieldsJson,
            correction.Reason,
            correction.OccurredAt,
            correction.RecordedAt,
            correction.RecordedByAccountId,
            correction.SessionId,
            correction.EntryOrigin,
            correction.EntryBatchId);
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
            || auditEntry.Comment != payment.Comment)
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
            || original.Source.CurrentStatus != ClientPaymentRowStatus.Replaced
            || !sourcesByPaymentId.TryGetValue(
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
            || auditEntry.Reason != correction.Reason)
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
            replacement.Source);
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
            || source.Source.CurrentStatus != ClientPaymentRowStatus.Canceled
            || auditEntry.ActionType != PaymentAuditActions.Canceled
            || auditEntry.EntityType != ClientAuditEntityFilter.Payment
            || auditEntry.EntityId != source.Payment.Id
            || auditEntry.OccurredAt != cancellation.OccurredAt
            || auditEntry.RecordedAt != cancellation.RecordedAt
            || auditEntry.ActorAccountId.Value
                != cancellation.RecordedByAccountId
            || auditEntry.SessionId.Value != cancellation.SessionId
            || auditEntry.EntryOrigin != cancellation.EntryOrigin
            || auditEntry.Reason != cancellation.Reason)
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
            source.Source);
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

    private sealed record PaymentStorageRow(
        PaymentRecord Payment,
        Guid? MembershipClientId,
        string? MembershipTypeNameSnapshot);

    private sealed record CanonicalPaymentHistorySource(
        PaymentRecord Payment,
        PaymentHistorySource Source,
        PaymentQuerySupport.CanonicalPaymentProjection Projection);
}
