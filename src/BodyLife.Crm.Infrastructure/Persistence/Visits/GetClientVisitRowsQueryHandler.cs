using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Visits;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

public sealed class GetClientVisitRowsQueryHandler(
    BodyLifeDbContext dbContext,
    IVisitDayReconciliationStatusProvider dayReconciliationStatusProvider,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<GetClientVisitRowsQuery, GetClientVisitRowsResult>
{
    public async Task<GetClientVisitRowsResult> ExecuteAsync(
        GetClientVisitRowsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await VisitQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetClientVisitRowsResult.Denied();
        }

        if (query.ClientId == Guid.Empty)
        {
            return GetClientVisitRowsResult.Invalid(
                "Client id is required.",
                "clientId");
        }

        if (query.Limit is < 1 or > GetClientVisitRowsQuery.MaxLimit)
        {
            return GetClientVisitRowsResult.Invalid(
                $"Limit must be between 1 and {GetClientVisitRowsQuery.MaxLimit}.",
                "limit");
        }

        var clientExists = await dbContext.Set<ClientRecord>()
            .AsNoTracking()
            .AnyAsync(client => client.Id == query.ClientId, cancellationToken);
        if (!clientExists)
        {
            return GetClientVisitRowsResult.MissingClient();
        }

        var sourceRows = await dbContext.Set<VisitRecord>()
            .AsNoTracking()
            .Where(visit => visit.ClientId == query.ClientId)
            .OrderByDescending(visit => visit.OccurredAt)
            .ThenByDescending(visit => visit.RecordedAt)
            .ThenByDescending(visit => visit.Id)
            .Take(query.Limit + 1)
            .Select(visit => new VisitQuerySupport.CanonicalVisitSourceRow(
                visit.Id,
                visit.ClientId,
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
        var hasMore = sourceRows.Count > query.Limit;
        var visibleRows = sourceRows.Take(query.Limit).ToArray();
        if (visibleRows.Length == 0)
        {
            return GetClientVisitRowsResult.Succeeded(
                new ClientVisitRowsPage(query.ClientId, [], HasMore: false));
        }

        var visitIds = visibleRows.Select(row => row.VisitId).ToArray();
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
            .Select(cancellation => new VisitQuerySupport.CanonicalVisitCancellationSourceRow(
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
            return GetClientVisitRowsResult.InconsistentSource();
        }

        var consumptionByVisitId = consumptionRows.ToDictionary(row => row.VisitId);
        var cancellationByVisitId = cancellationRows.ToDictionary(row => row.VisitId);
        var dayStatuses = new Dictionary<DateOnly, VisitDayReconciliationStatus>();
        var resultRows = new List<ClientVisitRow>(visibleRows.Length);

        foreach (var source in visibleRows)
        {
            consumptionByVisitId.TryGetValue(source.VisitId, out var consumptionSource);
            cancellationByVisitId.TryGetValue(source.VisitId, out var cancellationSource);
            if (!VisitQuerySupport.TryMapSourceRow(
                    source,
                    consumptionSource,
                    cancellationSource,
                    out var projection)
                || projection is null)
            {
                return GetClientVisitRowsResult.InconsistentSource();
            }

            var allowedActions = QueryPermissionSet.Empty;
            if (projection.Status == ClientVisitRowStatus.Active)
            {
                var businessDate = DateOnly.FromDateTime(source.OccurredAt.DateTime);
                if (!dayStatuses.TryGetValue(businessDate, out var dayStatus))
                {
                    dayStatus = await dayReconciliationStatusProvider.GetStatusAsync(
                        businessDate,
                        cancellationToken);
                    if (!Enum.IsDefined(dayStatus))
                    {
                        return GetClientVisitRowsResult.InconsistentSource();
                    }

                    dayStatuses.Add(businessDate, dayStatus);
                }

                allowedActions = VisitQuerySupport.BuildCancellationPermissions(
                    query.Actor,
                    projection.Status,
                    dayStatus);
            }

            resultRows.Add(new ClientVisitRow(
                source.VisitId,
                source.ClientId,
                source.OccurredAt,
                source.RecordedAt,
                source.RecordedByAccountId,
                source.SessionId,
                projection.VisitKind,
                projection.EntryOrigin,
                source.EntryBatchId,
                source.Comment,
                projection.Status,
                projection.Consumption,
                projection.Cancellation,
                allowedActions));
        }

        return GetClientVisitRowsResult.Succeeded(
            new ClientVisitRowsPage(
                query.ClientId,
                resultRows.AsReadOnly(),
                hasMore));
    }

}
