using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Visits;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

public sealed class GetDailyVisitSourceRowsQueryHandler(
    BodyLifeDbContext dbContext,
    IVisitDayReconciliationStatusProvider dayReconciliationStatusProvider,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        GetDailyVisitSourceRowsQuery,
        GetDailyVisitSourceRowsResult>
{
    public async Task<GetDailyVisitSourceRowsResult> ExecuteAsync(
        GetDailyVisitSourceRowsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await VisitQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetDailyVisitSourceRowsResult.Denied();
        }

        if (query.BusinessDate == default || query.BusinessDate == DateOnly.MaxValue)
        {
            return GetDailyVisitSourceRowsResult.Invalid(
                "Business date is outside the supported UTC report range.",
                "businessDate");
        }

        var dayStatus = await dayReconciliationStatusProvider.GetStatusAsync(
            query.BusinessDate,
            cancellationToken);
        if (!Enum.IsDefined(dayStatus))
        {
            return GetDailyVisitSourceRowsResult.InconsistentSource();
        }

        var dayStart = new DateTimeOffset(
            query.BusinessDate.ToDateTime(
                TimeOnly.MinValue,
                DateTimeKind.Utc));
        var nextDayStart = dayStart.AddDays(1);
        var sourceRows = await (
            from visit in dbContext.Set<VisitRecord>().AsNoTracking()
            join client in dbContext.Set<ClientRecord>().AsNoTracking()
                on visit.ClientId equals client.Id
            where visit.OccurredAt >= dayStart
                && visit.OccurredAt < nextDayStart
            orderby visit.OccurredAt descending,
                visit.RecordedAt descending,
                visit.Id descending
            select new DailyVisitSourceRecord(
                visit.Id,
                visit.ClientId,
                client.Surname,
                client.Name,
                client.Patronymic,
                visit.OccurredAt,
                visit.RecordedAt,
                visit.RecordedByAccountId,
                visit.SessionId,
                visit.VisitKind,
                visit.EntryOrigin,
                visit.EntryBatchId,
                visit.Comment,
                visit.Status))
            .ToListAsync(cancellationToken);

        if (sourceRows.Count == 0)
        {
            return GetDailyVisitSourceRowsResult.Succeeded(
                new DailyVisitSourceSnapshot(
                    query.BusinessDate,
                    dayStatus,
                    Rows: []));
        }

        var visitIds = sourceRows.Select(row => row.VisitId).ToArray();
        var consumptionRows = await (
            from consumption in dbContext.Set<VisitConsumptionRecord>().AsNoTracking()
            join membership in dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
                on consumption.MembershipId equals membership.Id
            where visitIds.Contains(consumption.VisitId)
            select new VisitQuerySupport.CanonicalVisitConsumptionSourceRow(
                consumption.Id,
                consumption.VisitId,
                consumption.ClientId,
                consumption.VisitKind,
                consumption.MembershipId,
                membership.ClientId,
                membership.TypeNameSnapshot,
                consumption.ConsumptionType,
                consumption.SourceFactType,
                consumption.SourceFactId,
                consumption.Status))
            .ToListAsync(cancellationToken);
        var cancellationRows = await dbContext.Set<VisitCancellationRecord>()
            .AsNoTracking()
            .Where(cancellation => visitIds.Contains(cancellation.VisitId))
            .Select(cancellation =>
                new VisitQuerySupport.CanonicalVisitCancellationSourceRow(
                    cancellation.Id,
                    cancellation.VisitId,
                    cancellation.Reason,
                    cancellation.OccurredAt,
                    cancellation.RecordedAt,
                    cancellation.RecordedByAccountId,
                    cancellation.SessionId,
                    cancellation.EntryOrigin,
                    cancellation.EntryBatchId))
            .ToListAsync(cancellationToken);

        if (consumptionRows.GroupBy(row => row.VisitId).Any(group => group.Count() > 1)
            || cancellationRows.GroupBy(row => row.VisitId).Any(group => group.Count() > 1))
        {
            return GetDailyVisitSourceRowsResult.InconsistentSource();
        }

        var consumptionByVisitId = consumptionRows.ToDictionary(row => row.VisitId);
        var cancellationByVisitId = cancellationRows.ToDictionary(row => row.VisitId);
        var resultRows = new List<DailyVisitSourceRow>(sourceRows.Count);

        foreach (var source in sourceRows)
        {
            consumptionByVisitId.TryGetValue(
                source.VisitId,
                out var consumptionSource);
            cancellationByVisitId.TryGetValue(
                source.VisitId,
                out var cancellationSource);
            var canonicalSource = source.ToCanonicalSource();
            if (!VisitQuerySupport.TryMapSourceRow(
                    canonicalSource,
                    consumptionSource,
                    cancellationSource,
                    out var projection)
                || projection is null)
            {
                return GetDailyVisitSourceRowsResult.InconsistentSource();
            }

            var allowedActions = projection.Status == ClientVisitRowStatus.Active
                ? VisitQuerySupport.BuildCancellationPermissions(
                    query.Actor,
                    projection.Status,
                    dayStatus)
                : QueryPermissionSet.Empty;
            var visit = new ClientVisitRow(
                canonicalSource.VisitId,
                canonicalSource.ClientId,
                canonicalSource.OccurredAt,
                canonicalSource.RecordedAt,
                canonicalSource.RecordedByAccountId,
                canonicalSource.SessionId,
                projection.VisitKind,
                projection.EntryOrigin,
                canonicalSource.EntryBatchId,
                canonicalSource.Comment,
                projection.Status,
                projection.Consumption,
                projection.Cancellation,
                allowedActions);
            resultRows.Add(new DailyVisitSourceRow(
                ClientQuerySupport.BuildDisplayName(
                    source.ClientSurname,
                    source.ClientName,
                    source.ClientPatronymic),
                visit));
        }

        return GetDailyVisitSourceRowsResult.Succeeded(
            new DailyVisitSourceSnapshot(
                query.BusinessDate,
                dayStatus,
                resultRows.AsReadOnly()));
    }

    private sealed record DailyVisitSourceRecord(
        Guid VisitId,
        Guid ClientId,
        string ClientSurname,
        string ClientName,
        string? ClientPatronymic,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string VisitKind,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status)
    {
        internal VisitQuerySupport.CanonicalVisitSourceRow ToCanonicalSource()
        {
            return new VisitQuerySupport.CanonicalVisitSourceRow(
                VisitId,
                ClientId,
                OccurredAt,
                RecordedAt,
                RecordedByAccountId,
                SessionId,
                VisitKind,
                EntryOrigin,
                EntryBatchId,
                Comment,
                Status);
        }
    }
}
