using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Payments;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

public sealed class GetClientPaymentRowsQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<GetClientPaymentRowsQuery, GetClientPaymentRowsResult>
{
    public async Task<GetClientPaymentRowsResult> ExecuteAsync(
        GetClientPaymentRowsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await PaymentQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetClientPaymentRowsResult.Denied();
        }

        if (query.ClientId == Guid.Empty)
        {
            return GetClientPaymentRowsResult.Invalid(
                "Client id is required.",
                "clientId");
        }

        if (query.Limit is < 1 or > GetClientPaymentRowsQuery.MaxLimit)
        {
            return GetClientPaymentRowsResult.Invalid(
                $"Limit must be between 1 and {GetClientPaymentRowsQuery.MaxLimit}.",
                "limit");
        }

        var clientExists = await dbContext.Set<ClientRecord>()
            .AsNoTracking()
            .AnyAsync(client => client.Id == query.ClientId, cancellationToken);
        if (!clientExists)
        {
            return GetClientPaymentRowsResult.MissingClient();
        }

        var sourceRows = await (
            from payment in dbContext.Set<PaymentRecord>().AsNoTracking()
            join membership in dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
                on payment.MembershipId equals (Guid?)membership.Id
                into memberships
            from membership in memberships.DefaultIfEmpty()
            where payment.ClientId == query.ClientId
            orderby payment.OccurredAt descending,
                payment.RecordedAt descending,
                payment.Id descending
            select new PaymentQuerySupport.CanonicalPaymentSourceRow(
                payment.Id,
                payment.ClientId,
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
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);
        var hasMore = sourceRows.Count > query.Limit;
        var visibleRows = sourceRows.Take(query.Limit).ToArray();
        if (visibleRows.Length == 0)
        {
            return GetClientPaymentRowsResult.Succeeded(
                new ClientPaymentRowsPage(query.ClientId, [], HasMore: false));
        }

        var paymentIds = visibleRows.Select(row => row.PaymentId).ToArray();
        var cancellationRows = await dbContext.Set<PaymentCancellationRecord>()
            .AsNoTracking()
            .Where(cancellation => paymentIds.Contains(cancellation.PaymentId))
            .Select(cancellation =>
                new PaymentQuerySupport.CanonicalPaymentCancellationSourceRow(
                    cancellation.Id,
                    cancellation.PaymentId,
                    cancellation.Reason,
                    cancellation.OccurredAt,
                    cancellation.RecordedAt,
                    cancellation.RecordedByAccountId,
                    cancellation.SessionId,
                    cancellation.EntryOrigin,
                    cancellation.EntryBatchId))
            .ToListAsync(cancellationToken);
        var correctionRows = await dbContext.Set<PaymentCorrectionRecord>()
            .AsNoTracking()
            .Where(correction =>
                paymentIds.Contains(correction.OriginalPaymentId)
                || paymentIds.Contains(correction.ReplacementPaymentId))
            .Select(correction =>
                new PaymentQuerySupport.CanonicalPaymentCorrectionSourceRow(
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
                    correction.EntryBatchId))
            .ToListAsync(cancellationToken);

        if (cancellationRows
                .GroupBy(row => row.PaymentId)
                .Any(group => group.Count() > 1)
            || correctionRows
                .Where(row => paymentIds.Contains(row.OriginalPaymentId))
                .GroupBy(row => row.OriginalPaymentId)
                .Any(group => group.Count() > 1)
            || correctionRows
                .Where(row => paymentIds.Contains(row.ReplacementPaymentId))
                .GroupBy(row => row.ReplacementPaymentId)
                .Any(group => group.Count() > 1))
        {
            return GetClientPaymentRowsResult.InconsistentSource();
        }

        var cancellationByPaymentId = cancellationRows.ToDictionary(
            row => row.PaymentId);
        var correctionFromOriginalByPaymentId = correctionRows
            .Where(row => paymentIds.Contains(row.ReplacementPaymentId))
            .ToDictionary(row => row.ReplacementPaymentId);
        var correctionToReplacementByPaymentId = correctionRows
            .Where(row => paymentIds.Contains(row.OriginalPaymentId))
            .ToDictionary(row => row.OriginalPaymentId);
        var resultRows = new List<ClientPaymentRow>(visibleRows.Length);

        foreach (var source in visibleRows)
        {
            cancellationByPaymentId.TryGetValue(
                source.PaymentId,
                out var cancellationSource);
            correctionFromOriginalByPaymentId.TryGetValue(
                source.PaymentId,
                out var correctionFromOriginalSource);
            correctionToReplacementByPaymentId.TryGetValue(
                source.PaymentId,
                out var correctionToReplacementSource);
            if (!PaymentQuerySupport.TryMapSourceRow(
                    source,
                    cancellationSource,
                    correctionFromOriginalSource,
                    correctionToReplacementSource,
                    out var projection)
                || projection is null)
            {
                return GetClientPaymentRowsResult.InconsistentSource();
            }

            resultRows.Add(new ClientPaymentRow(
                source.PaymentId,
                source.ClientId,
                source.MembershipId,
                source.MembershipTypeNameSnapshot,
                projection.Amount,
                projection.Method,
                projection.PaymentContext,
                source.OccurredAt,
                source.RecordedAt,
                source.RecordedByAccountId,
                source.SessionId,
                projection.EntryOrigin,
                source.EntryBatchId,
                source.Comment,
                projection.Status,
                projection.Cancellation,
                projection.CorrectionFromOriginal,
                projection.CorrectionToReplacement));
        }

        return GetClientPaymentRowsResult.Succeeded(
            new ClientPaymentRowsPage(
                query.ClientId,
                resultRows.AsReadOnly(),
                hasMore));
    }
}
