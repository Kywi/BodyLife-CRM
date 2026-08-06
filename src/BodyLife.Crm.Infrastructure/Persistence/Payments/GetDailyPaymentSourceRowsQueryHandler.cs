using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

public sealed class GetDailyPaymentSourceRowsQueryHandler(
    BodyLifeDbContext dbContext,
    IPaymentDayReconciliationStatusProvider dayReconciliationStatusProvider,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        GetDailyPaymentSourceRowsQuery,
        GetDailyPaymentSourceRowsResult>
{
    public async Task<GetDailyPaymentSourceRowsResult> ExecuteAsync(
        GetDailyPaymentSourceRowsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await PaymentQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetDailyPaymentSourceRowsResult.Denied();
        }

        if (query.BusinessDate == default || query.BusinessDate == DateOnly.MaxValue)
        {
            return GetDailyPaymentSourceRowsResult.Invalid(
                "Business date is outside the supported business date/report range.",
                "businessDate");
        }

        var dayStatus = await dayReconciliationStatusProvider.GetStatusAsync(
            query.BusinessDate,
            cancellationToken);
        if (!Enum.IsDefined(dayStatus))
        {
            return GetDailyPaymentSourceRowsResult.InconsistentSource();
        }

        var dayRange = BusinessTimeZone.GetUtcDayRange(query.BusinessDate);
        var sourceRows = await (
            from payment in dbContext.Set<PaymentRecord>().AsNoTracking()
            join client in dbContext.Set<ClientRecord>().AsNoTracking()
                on payment.ClientId equals client.Id
            join membership in dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
                on payment.MembershipId equals (Guid?)membership.Id
                into memberships
            from membership in memberships.DefaultIfEmpty()
            where payment.OccurredAt >= dayRange.FromInclusive
                && payment.OccurredAt < dayRange.ToExclusive
            orderby payment.OccurredAt descending,
                payment.RecordedAt descending,
                payment.Id descending
            select new DailyPaymentSourceRecord(
                payment.Id,
                payment.ClientId,
                client.Surname,
                client.Name,
                client.Patronymic,
                payment.MembershipId,
                membership == null ? null : membership.ClientId,
                membership == null ? null : membership.TypeNameSnapshot,
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
                payment.Status))
            .ToListAsync(cancellationToken);

        if (sourceRows.Count == 0)
        {
            return GetDailyPaymentSourceRowsResult.Succeeded(
                new DailyPaymentSourceSnapshot(
                    query.BusinessDate,
                    dayStatus,
                    Rows: []));
        }

        var paymentIds = sourceRows.Select(row => row.PaymentId).ToArray();
        var relations = await PaymentQuerySupport.ReadCanonicalRelationsAsync(
            dbContext,
            paymentIds,
            cancellationToken);
        if (relations is null)
        {
            return GetDailyPaymentSourceRowsResult.InconsistentSource();
        }

        var paperReferenceReader = new PaperFallbackEntryRowReferenceReader(dbContext);
        var paymentPaperReferences = await paperReferenceReader.LoadAsync(
            sourceRows.Select(source =>
            {
                relations.CorrectionsFromOriginalByPaymentId.TryGetValue(
                    source.PaymentId,
                    out var incomingCorrection);
                return new PaperFallbackEntryRowReferenceSource(
                    source.PaymentId,
                    source.EntryOrigin,
                    source.EntryBatchId,
                    incomingCorrection?.OccurredAt ?? source.OccurredAt,
                    source.RecordedByAccountId,
                    source.SessionId,
                    ExpectedPaymentEventType(source.PaymentContext, incomingCorrection));
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
            return GetDailyPaymentSourceRowsResult.InconsistentSource();
        }

        var resultRows = new List<DailyPaymentSourceRow>(sourceRows.Count);
        var activeCurrencies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sourceRows)
        {
            relations.CancellationsByPaymentId.TryGetValue(
                source.PaymentId,
                out var cancellationSource);
            relations.CorrectionsFromOriginalByPaymentId.TryGetValue(
                source.PaymentId,
                out var correctionFromOriginalSource);
            relations.CorrectionsToReplacementByPaymentId.TryGetValue(
                source.PaymentId,
                out var correctionToReplacementSource);
            var canonicalSource = source.ToCanonicalSource();
            if (!PaymentQuerySupport.TryMapSourceRow(
                    canonicalSource,
                    cancellationSource,
                    correctionFromOriginalSource,
                    correctionToReplacementSource,
                    out var projection)
                || projection is null)
            {
                return GetDailyPaymentSourceRowsResult.InconsistentSource();
            }

            if (projection.Status == ClientPaymentRowStatus.Active)
            {
                activeCurrencies.Add(projection.Amount.Currency);
                if (activeCurrencies.Count > 1)
                {
                    return GetDailyPaymentSourceRowsResult.InconsistentSource();
                }
            }

            var allowedActions = projection.Status == ClientPaymentRowStatus.Active
                ? PaymentQuerySupport.BuildCorrectionPermissions(
                    query.Actor,
                    projection.Status,
                    projection.PaymentContext,
                    dayStatus)
                : QueryPermissionSet.Empty;
            var payment = new ClientPaymentRow(
                canonicalSource.PaymentId,
                canonicalSource.ClientId,
                canonicalSource.MembershipId,
                canonicalSource.MembershipTypeNameSnapshot,
                projection.Amount,
                projection.Method,
                projection.PaymentContext,
                canonicalSource.OccurredAt,
                canonicalSource.RecordedAt,
                canonicalSource.RecordedByAccountId,
                canonicalSource.SessionId,
                projection.EntryOrigin,
                canonicalSource.EntryBatchId,
                canonicalSource.Comment,
                projection.Status,
                projection.Cancellation,
                projection.CorrectionFromOriginal,
                projection.CorrectionToReplacement,
                allowedActions);
            resultRows.Add(new DailyPaymentSourceRow(
                ClientQuerySupport.BuildDisplayName(
                    source.ClientSurname,
                    source.ClientName,
                    source.ClientPatronymic),
                payment));
        }

        return GetDailyPaymentSourceRowsResult.Succeeded(
            new DailyPaymentSourceSnapshot(
                query.BusinessDate,
                dayStatus,
                resultRows.AsReadOnly()));
    }

    private sealed record DailyPaymentSourceRecord(
        Guid PaymentId,
        Guid ClientId,
        string ClientSurname,
        string ClientName,
        string? ClientPatronymic,
        Guid? MembershipId,
        Guid? MembershipClientId,
        string? MembershipTypeNameSnapshot,
        decimal Amount,
        string Currency,
        string Method,
        string PaymentContext,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status)
    {
        internal PaymentQuerySupport.CanonicalPaymentSourceRow ToCanonicalSource()
        {
            return new PaymentQuerySupport.CanonicalPaymentSourceRow(
                PaymentId,
                ClientId,
                MembershipId,
                MembershipClientId,
                MembershipTypeNameSnapshot,
                Amount,
                Currency,
                Method,
                PaymentContext,
                OccurredAt,
                RecordedAt,
                RecordedByAccountId,
                SessionId,
                EntryOrigin,
                EntryBatchId,
                Comment,
                Status);
        }
    }

    private static PaperFallbackEventType ExpectedPaymentEventType(
        string paymentContext,
        PaymentQuerySupport.CanonicalPaymentCorrectionSourceRow? incomingCorrection) =>
        incomingCorrection is not null
            ? PaperFallbackEventType.CorrectionOrCancellation
            : paymentContext switch
            {
                "membership_sale" => PaperFallbackEventType.MembershipSale,
                "negative_closure" => PaperFallbackEventType.NegativeCoverage,
                _ => PaperFallbackEventType.Payment,
            };

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
            if (loaded is null || loaded.Keys.Any(references.ContainsKey))
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

    private sealed record PaperReferenceLookup(
        PaperFallbackEntryRowReferenceSource Source,
        string EntityType);
}
