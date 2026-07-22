using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
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
            return GetDailyPaymentSourceRowsResult.InconsistentSource();
        }

        var cancellationByPaymentId = cancellationRows.ToDictionary(
            row => row.PaymentId);
        var correctionFromOriginalByPaymentId = correctionRows
            .Where(row => paymentIds.Contains(row.ReplacementPaymentId))
            .ToDictionary(row => row.ReplacementPaymentId);
        var correctionToReplacementByPaymentId = correctionRows
            .Where(row => paymentIds.Contains(row.OriginalPaymentId))
            .ToDictionary(row => row.OriginalPaymentId);
        var resultRows = new List<DailyPaymentSourceRow>(sourceRows.Count);
        var activeCurrencies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sourceRows)
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
}
