using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

public sealed class GetClientVisitHistorySourceRowsQueryHandler(
    BodyLifeDbContext dbContext,
    IBodyLifeQueryHandler<GetClientAuditEntriesQuery, GetClientAuditEntriesResult>
        auditEntriesQueryHandler)
    : IBodyLifeQueryHandler<
        GetClientVisitHistorySourceRowsQuery,
        GetClientVisitHistorySourceRowsResult>
{
    private static readonly ClientAuditEntityFilter[] EntityFilters =
    [
        ClientAuditEntityFilter.Visit,
    ];

    private static readonly string[] ActionTypes =
    [
        VisitAuditActions.Marked,
        VisitAuditActions.Canceled,
    ];

    public async Task<GetClientVisitHistorySourceRowsResult> ExecuteAsync(
        GetClientVisitHistorySourceRowsQuery query,
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
                query.Offset),
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
            || auditPage.Items.Select(item => item.AuditEntryId).Distinct().Count()
                != auditPage.Items.Count
            || auditPage.Items
                .GroupBy(item => (item.ActionType, item.EntityType, item.EntityId))
                .Any(group => group.Count() > 1))
        {
            return GetClientVisitHistorySourceRowsResult.InconsistentSource();
        }

        var visitIds = auditPage.Items
            .Select(item => item.EntityId)
            .Distinct()
            .ToArray();
        if (visitIds.Length == 0)
        {
            return GetClientVisitHistorySourceRowsResult.Succeeded(
                ClientVisitHistorySourceRowsPage.Create(
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    auditPage.Offset,
                    items: [],
                    auditPage.HasMore));
        }

        var visitRows = await dbContext.Set<VisitRecord>()
            .AsNoTracking()
            .Where(visit =>
                visitIds.Contains(visit.Id)
                && visit.ClientId == query.ClientId)
            .ToArrayAsync(cancellationToken);
        if (visitRows.Length != visitIds.Length)
        {
            return GetClientVisitHistorySourceRowsResult.InconsistentSource();
        }

        var consumptionRows = await (
            from consumption in dbContext.Set<VisitConsumptionRecord>().AsNoTracking()
            join membership in dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
                on consumption.MembershipId equals membership.Id
            where visitIds.Contains(consumption.VisitId)
            select new ConsumptionStorageRow(
                consumption,
                membership.ClientId,
                membership.TypeNameSnapshot))
            .ToArrayAsync(cancellationToken);
        var cancellationRows = await dbContext.Set<VisitCancellationRecord>()
            .AsNoTracking()
            .Where(cancellation => visitIds.Contains(cancellation.VisitId))
            .ToArrayAsync(cancellationToken);
        if (consumptionRows.GroupBy(row => row.Consumption.VisitId)
                .Any(group => group.Count() > 1)
            || cancellationRows.GroupBy(row => row.VisitId)
                .Any(group => group.Count() > 1))
        {
            return GetClientVisitHistorySourceRowsResult.InconsistentSource();
        }

        var consumptionByVisitId = consumptionRows.ToDictionary(
            row => row.Consumption.VisitId);
        var cancellationByVisitId = cancellationRows.ToDictionary(
            row => row.VisitId);
        var sourcesByVisitId = new Dictionary<Guid, CanonicalVisitHistorySource>(
            visitRows.Length);

        foreach (var visit in visitRows)
        {
            consumptionByVisitId.TryGetValue(visit.Id, out var consumption);
            cancellationByVisitId.TryGetValue(visit.Id, out var cancellation);
            if (!TryMapCanonicalSource(
                    visit,
                    consumption,
                    cancellation,
                    out var source)
                || source is null)
            {
                return GetClientVisitHistorySourceRowsResult.InconsistentSource();
            }

            sourcesByVisitId.Add(visit.Id, source);
        }

        var rows = new List<ClientVisitHistorySourceRow>(auditPage.Items.Count);
        try
        {
            foreach (var auditEntry in auditPage.Items)
            {
                if (!sourcesByVisitId.TryGetValue(auditEntry.EntityId, out var source))
                {
                    return GetClientVisitHistorySourceRowsResult.InconsistentSource();
                }

                var row = auditEntry.ActionType switch
                {
                    VisitAuditActions.Marked => MapMarkedVisit(source, auditEntry),
                    VisitAuditActions.Canceled => MapCanceledVisit(source, auditEntry),
                    _ => null,
                };
                if (row is null)
                {
                    return GetClientVisitHistorySourceRowsResult.InconsistentSource();
                }

                rows.Add(row);
            }

            return GetClientVisitHistorySourceRowsResult.Succeeded(
                ClientVisitHistorySourceRowsPage.Create(
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    auditPage.Offset,
                    rows,
                    auditPage.HasMore));
        }
        catch (ArgumentException)
        {
            return GetClientVisitHistorySourceRowsResult.InconsistentSource();
        }
        catch (InvalidOperationException)
        {
            return GetClientVisitHistorySourceRowsResult.InconsistentSource();
        }
    }

    private static bool TryMapCanonicalSource(
        VisitRecord visit,
        ConsumptionStorageRow? consumption,
        VisitCancellationRecord? cancellation,
        out CanonicalVisitHistorySource? source)
    {
        source = null;
        if (visit.Id == Guid.Empty
            || visit.ClientId == Guid.Empty
            || visit.RecordedByAccountId == Guid.Empty
            || visit.SessionId == Guid.Empty
            || consumption is not null
                && (consumption.Consumption.VisitId != visit.Id
                    || consumption.Consumption.RecordedAt != visit.RecordedAt
                    || consumption.Consumption.RecordedByAccountId
                        != visit.RecordedByAccountId
                    || consumption.Consumption.RecordedSessionId != visit.SessionId)
            || cancellation is not null && cancellation.VisitId != visit.Id)
        {
            return false;
        }

        var canonicalVisit = new VisitQuerySupport.CanonicalVisitSourceRow(
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
            visit.Status);
        var canonicalConsumption = consumption is null
            ? null
            : new VisitQuerySupport.CanonicalVisitConsumptionSourceRow(
                consumption.Consumption.Id,
                consumption.Consumption.VisitId,
                consumption.Consumption.ClientId,
                consumption.Consumption.VisitKind,
                consumption.Consumption.MembershipId,
                consumption.MembershipClientId,
                consumption.MembershipTypeNameSnapshot,
                consumption.Consumption.ConsumptionType,
                consumption.Consumption.SourceFactType,
                consumption.Consumption.SourceFactId,
                consumption.Consumption.Status);
        var canonicalCancellation = cancellation is null
            ? null
            : new VisitQuerySupport.CanonicalVisitCancellationSourceRow(
                cancellation.Id,
                cancellation.VisitId,
                cancellation.Reason,
                cancellation.OccurredAt,
                cancellation.RecordedAt,
                cancellation.RecordedByAccountId,
                cancellation.SessionId,
                cancellation.EntryOrigin,
                cancellation.EntryBatchId);
        if (!VisitQuerySupport.TryMapSourceRow(
                canonicalVisit,
                canonicalConsumption,
                canonicalCancellation,
                out var projection)
            || projection is null)
        {
            return false;
        }

        source = new CanonicalVisitHistorySource(
            visit,
            cancellation,
            projection);
        return true;
    }

    private static ClientVisitHistorySourceRow? MapMarkedVisit(
        CanonicalVisitHistorySource source,
        ClientAuditEntry auditEntry)
    {
        var visit = source.Visit;
        var projection = source.Projection;
        if (auditEntry.ActionType != VisitAuditActions.Marked
            || auditEntry.EntityType != ClientAuditEntityFilter.Visit
            || auditEntry.EntityId != visit.Id
            || auditEntry.OccurredAt != visit.OccurredAt
            || auditEntry.RecordedAt != visit.RecordedAt
            || auditEntry.ActorAccountId.Value != visit.RecordedByAccountId
            || auditEntry.SessionId.Value != visit.SessionId
            || auditEntry.EntryOrigin != projection.EntryOrigin
            || auditEntry.Comment != visit.Comment)
        {
            return null;
        }

        var markedVisit = new MarkedVisitHistorySource(
            visit.Id,
            visit.ClientId,
            visit.OccurredAt,
            visit.RecordedAt,
            new AccountId(visit.RecordedByAccountId),
            new SessionId(visit.SessionId),
            projection.VisitKind,
            visit.EntryBatchId,
            visit.Comment,
            projection.Status,
            projection.Consumption,
            source.Cancellation?.Id);
        return new ClientVisitHistorySourceRow(
            ClientVisitHistorySourceKind.MarkedVisit,
            visit.ClientId,
            visit.Id,
            visit.OccurredAt,
            visit.RecordedAt,
            projection.EntryOrigin,
            markedVisit,
            Cancellation: null,
            auditEntry);
    }

    private static ClientVisitHistorySourceRow? MapCanceledVisit(
        CanonicalVisitHistorySource source,
        ClientAuditEntry auditEntry)
    {
        var visit = source.Visit;
        var cancellation = source.Cancellation;
        if (cancellation is null
            || source.Projection.Status != ClientVisitRowStatus.Canceled
            || !VisitQuerySupport.TryMapEntryOrigin(
                cancellation.EntryOrigin,
                out var entryOrigin)
            || auditEntry.ActionType != VisitAuditActions.Canceled
            || auditEntry.EntityType != ClientAuditEntityFilter.Visit
            || auditEntry.EntityId != visit.Id
            || auditEntry.OccurredAt != cancellation.OccurredAt
            || auditEntry.RecordedAt != cancellation.RecordedAt
            || auditEntry.ActorAccountId.Value != cancellation.RecordedByAccountId
            || auditEntry.SessionId.Value != cancellation.SessionId
            || auditEntry.EntryOrigin != entryOrigin
            || auditEntry.Reason != cancellation.Reason)
        {
            return null;
        }

        var cancellationSource = new VisitCancellationHistorySource(
            cancellation.Id,
            cancellation.VisitId,
            visit.ClientId,
            cancellation.Reason,
            cancellation.OccurredAt,
            cancellation.RecordedAt,
            new AccountId(cancellation.RecordedByAccountId),
            new SessionId(cancellation.SessionId),
            cancellation.EntryBatchId);
        return new ClientVisitHistorySourceRow(
            ClientVisitHistorySourceKind.CanceledVisit,
            visit.ClientId,
            visit.Id,
            cancellation.OccurredAt,
            cancellation.RecordedAt,
            entryOrigin,
            MarkedVisit: null,
            cancellationSource,
            auditEntry);
    }

    private static GetClientVisitHistorySourceRowsResult MapAuditFailure(
        GetClientAuditEntriesResult auditResult)
    {
        return auditResult.Status switch
        {
            GetClientAuditEntriesStatus.PermissionDenied
                => GetClientVisitHistorySourceRowsResult.Denied(),
            GetClientAuditEntriesStatus.ValidationFailed
                => GetClientVisitHistorySourceRowsResult.Invalid(
                    auditResult.ErrorMessage ?? "Client history selectors are invalid.",
                    auditResult.ErrorField),
            GetClientAuditEntriesStatus.NotFound
                => GetClientVisitHistorySourceRowsResult.MissingClient(),
            _ => GetClientVisitHistorySourceRowsResult.InconsistentSource(),
        };
    }

    private sealed record ConsumptionStorageRow(
        VisitConsumptionRecord Consumption,
        Guid MembershipClientId,
        string MembershipTypeNameSnapshot);

    private sealed record CanonicalVisitHistorySource(
        VisitRecord Visit,
        VisitCancellationRecord? Cancellation,
        VisitQuerySupport.CanonicalVisitProjection Projection);
}
