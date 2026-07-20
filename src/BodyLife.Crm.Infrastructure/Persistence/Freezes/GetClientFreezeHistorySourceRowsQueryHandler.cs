using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

public sealed class GetClientFreezeHistorySourceRowsQueryHandler(
    BodyLifeDbContext dbContext,
    IBodyLifeQueryHandler<GetClientAuditEntriesQuery, GetClientAuditEntriesResult>
        auditEntriesQueryHandler)
    : IBodyLifeQueryHandler<
        GetClientFreezeHistorySourceRowsQuery,
        GetClientFreezeHistorySourceRowsResult>
{
    private static readonly ClientAuditEntityFilter[] EntityFilters =
    [
        ClientAuditEntityFilter.Freeze,
    ];

    private static readonly string[] ActionTypes =
    [
        FreezeAuditActions.Added,
        FreezeAuditActions.Canceled,
    ];

    public async Task<GetClientFreezeHistorySourceRowsResult> ExecuteAsync(
        GetClientFreezeHistorySourceRowsQuery query,
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
            || auditPage.Items.Any(item =>
                item.EntityType != ClientAuditEntityFilter.Freeze)
            || auditPage.Items.Select(item => item.AuditEntryId).Distinct().Count()
                != auditPage.Items.Count
            || auditPage.Items
                .GroupBy(item => (item.ActionType, item.EntityId))
                .Any(group => group.Count() > 1))
        {
            return GetClientFreezeHistorySourceRowsResult.InconsistentSource();
        }

        var freezeIds = auditPage.Items
            .Select(item => item.EntityId)
            .Distinct()
            .ToArray();
        if (freezeIds.Length == 0)
        {
            return GetClientFreezeHistorySourceRowsResult.Succeeded(
                ClientFreezeHistorySourceRowsPage.Create(
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    auditPage.Offset,
                    items: [],
                    auditPage.HasMore));
        }

        var freezeRows = await (
            from freeze in dbContext.Set<FreezeRecord>().AsNoTracking()
            join membership in dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
                on new { freeze.MembershipId, freeze.ClientId }
                equals new
                {
                    MembershipId = membership.Id,
                    membership.ClientId,
                }
            where freezeIds.Contains(freeze.Id)
                && freeze.ClientId == query.ClientId
            select new FreezeStorageRow(
                freeze,
                membership.ClientId,
                membership.TypeNameSnapshot))
            .ToArrayAsync(cancellationToken);
        var cancellationRows = await dbContext.Set<FreezeCancellationRecord>()
            .AsNoTracking()
            .Where(cancellation => freezeIds.Contains(cancellation.FreezeId))
            .ToArrayAsync(cancellationToken);
        if (freezeRows.Length != freezeIds.Length
            || cancellationRows
                .GroupBy(cancellation => cancellation.FreezeId)
                .Any(group => group.Count() > 1))
        {
            return GetClientFreezeHistorySourceRowsResult.InconsistentSource();
        }

        var cancellationsByFreezeId = cancellationRows.ToDictionary(
            cancellation => cancellation.FreezeId);
        var sourcesByFreezeId = new Dictionary<Guid, CanonicalFreezeHistorySource>(
            freezeRows.Length);
        foreach (var storageRow in freezeRows)
        {
            cancellationsByFreezeId.TryGetValue(
                storageRow.Freeze.Id,
                out var cancellation);
            if (!TryMapCanonicalSource(
                    storageRow,
                    cancellation,
                    out var source)
                || source is null)
            {
                return GetClientFreezeHistorySourceRowsResult.InconsistentSource();
            }

            sourcesByFreezeId.Add(storageRow.Freeze.Id, source);
        }

        var rows = new List<ClientFreezeHistorySourceRow>(auditPage.Items.Count);
        foreach (var auditEntry in auditPage.Items)
        {
            if (!sourcesByFreezeId.TryGetValue(
                    auditEntry.EntityId,
                    out var source))
            {
                return GetClientFreezeHistorySourceRowsResult.InconsistentSource();
            }

            var row = auditEntry.ActionType switch
            {
                FreezeAuditActions.Added => MapAddedFreeze(source, auditEntry),
                FreezeAuditActions.Canceled => MapCanceledFreeze(source, auditEntry),
                _ => null,
            };
            if (row is null)
            {
                return GetClientFreezeHistorySourceRowsResult.InconsistentSource();
            }

            rows.Add(row);
        }

        try
        {
            return GetClientFreezeHistorySourceRowsResult.Succeeded(
                ClientFreezeHistorySourceRowsPage.Create(
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    auditPage.Offset,
                    rows,
                    auditPage.HasMore));
        }
        catch (ArgumentException)
        {
            return GetClientFreezeHistorySourceRowsResult.InconsistentSource();
        }
    }

    private static bool TryMapCanonicalSource(
        FreezeStorageRow storageRow,
        FreezeCancellationRecord? cancellation,
        out CanonicalFreezeHistorySource? source)
    {
        source = null;
        var freeze = storageRow.Freeze;
        if (freeze.Id == Guid.Empty
            || freeze.ClientId == Guid.Empty
            || freeze.MembershipId == Guid.Empty
            || freeze.RecordedByAccountId == Guid.Empty
            || freeze.SessionId == Guid.Empty
            || storageRow.MembershipClientId != freeze.ClientId
            || string.IsNullOrWhiteSpace(storageRow.MembershipTypeNameSnapshot)
            || storageRow.MembershipTypeNameSnapshot
                != storageRow.MembershipTypeNameSnapshot.Trim()
            || string.IsNullOrWhiteSpace(freeze.Reason)
            || freeze.Reason != freeze.Reason.Trim()
            || !TryMapEntryOrigin(freeze.EntryOrigin, out var freezeEntryOrigin)
            || !TryMapStatus(freeze.Status, out var status))
        {
            return false;
        }

        EntryOrigin? cancellationEntryOrigin = null;
        if (cancellation is not null)
        {
            if (cancellation.Id == Guid.Empty
                || cancellation.FreezeId != freeze.Id
                || cancellation.RecordedByAccountId == Guid.Empty
                || cancellation.SessionId == Guid.Empty
                || string.IsNullOrWhiteSpace(cancellation.Reason)
                || cancellation.Reason != cancellation.Reason.Trim()
                || !TryMapEntryOrigin(
                    cancellation.EntryOrigin,
                    out var mappedCancellationEntryOrigin))
            {
                return false;
            }

            cancellationEntryOrigin = mappedCancellationEntryOrigin;
        }

        if (status == FreezeCancellationSourceStatus.Active
                && cancellation is not null
            || status == FreezeCancellationSourceStatus.Canceled
                && cancellation is null)
        {
            return false;
        }

        try
        {
            var canonicalSource = new FreezeCancellationSource(
                freeze.Id,
                freeze.ClientId,
                freeze.MembershipId,
                new DateRange(freeze.StartDate, freeze.EndDate),
                freeze.Reason,
                freeze.OccurredAt,
                freeze.RecordedAt,
                freeze.RecordedByAccountId,
                freeze.SessionId,
                freezeEntryOrigin,
                freeze.EntryBatchId,
                status,
                cancellation?.Id);
            var freezeSource = new FreezeHistorySource(
                canonicalSource.FreezeId,
                canonicalSource.ClientId,
                canonicalSource.MembershipId,
                storageRow.MembershipTypeNameSnapshot,
                canonicalSource.Range,
                canonicalSource.Reason,
                canonicalSource.OccurredAt,
                canonicalSource.RecordedAt,
                new AccountId(canonicalSource.RecordedByAccountId),
                new SessionId(canonicalSource.SessionId),
                canonicalSource.EntryOrigin,
                canonicalSource.EntryBatchId,
                canonicalSource.Status,
                canonicalSource.ExistingCancellationId);
            var cancellationSource = cancellation is null
                ? null
                : new FreezeCancellationHistorySource(
                    cancellation.Id,
                    freeze.Id,
                    freeze.ClientId,
                    freeze.MembershipId,
                    cancellation.Reason,
                    cancellation.OccurredAt,
                    cancellation.RecordedAt,
                    new AccountId(cancellation.RecordedByAccountId),
                    new SessionId(cancellation.SessionId),
                    cancellationEntryOrigin!.Value,
                    cancellation.EntryBatchId,
                    freezeSource);
            source = new CanonicalFreezeHistorySource(
                freezeSource,
                cancellationSource);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ClientFreezeHistorySourceRow? MapAddedFreeze(
        CanonicalFreezeHistorySource source,
        ClientAuditEntry auditEntry)
    {
        var freeze = source.Freeze;
        if (auditEntry.ActionType != FreezeAuditActions.Added
            || auditEntry.EntityType != ClientAuditEntityFilter.Freeze
            || auditEntry.EntityId != freeze.FreezeId
            || auditEntry.OccurredAt != freeze.OccurredAt
            || auditEntry.RecordedAt != freeze.RecordedAt
            || auditEntry.ActorAccountId != freeze.RecordedByAccountId
            || auditEntry.SessionId != freeze.RecordedSessionId
            || auditEntry.EntryOrigin != freeze.EntryOrigin
            || auditEntry.Reason != freeze.Reason)
        {
            return null;
        }

        return new ClientFreezeHistorySourceRow(
            ClientFreezeHistorySourceKind.AddedFreeze,
            freeze.ClientId,
            freeze.FreezeId,
            freeze.OccurredAt,
            freeze.RecordedAt,
            freeze.EntryOrigin,
            freeze,
            Cancellation: null,
            auditEntry);
    }

    private static ClientFreezeHistorySourceRow? MapCanceledFreeze(
        CanonicalFreezeHistorySource source,
        ClientAuditEntry auditEntry)
    {
        var cancellation = source.Cancellation;
        if (cancellation is null
            || source.Freeze.CurrentStatus
                != FreezeCancellationSourceStatus.Canceled
            || auditEntry.ActionType != FreezeAuditActions.Canceled
            || auditEntry.EntityType != ClientAuditEntityFilter.Freeze
            || auditEntry.EntityId != source.Freeze.FreezeId
            || auditEntry.OccurredAt != cancellation.OccurredAt
            || auditEntry.RecordedAt != cancellation.RecordedAt
            || auditEntry.ActorAccountId != cancellation.RecordedByAccountId
            || auditEntry.SessionId != cancellation.RecordedSessionId
            || auditEntry.EntryOrigin != cancellation.EntryOrigin
            || auditEntry.Reason != cancellation.Reason)
        {
            return null;
        }

        return new ClientFreezeHistorySourceRow(
            ClientFreezeHistorySourceKind.CanceledFreeze,
            cancellation.ClientId,
            cancellation.FreezeId,
            cancellation.OccurredAt,
            cancellation.RecordedAt,
            cancellation.EntryOrigin,
            AddedFreeze: null,
            cancellation,
            auditEntry);
    }

    private static bool TryMapEntryOrigin(
        string value,
        out EntryOrigin entryOrigin)
    {
        entryOrigin = value switch
        {
            "normal" => EntryOrigin.Normal,
            "manual_backfill" => EntryOrigin.ManualBackfill,
            "paper_fallback" => EntryOrigin.PaperFallback,
            "future_import" => EntryOrigin.FutureImport,
            _ => default,
        };

        return entryOrigin != default;
    }

    private static bool TryMapStatus(
        string value,
        out FreezeCancellationSourceStatus status)
    {
        status = value switch
        {
            "active" => FreezeCancellationSourceStatus.Active,
            "canceled" => FreezeCancellationSourceStatus.Canceled,
            _ => default,
        };

        return status != default;
    }

    private static GetClientFreezeHistorySourceRowsResult MapAuditFailure(
        GetClientAuditEntriesResult auditResult)
    {
        return auditResult.Status switch
        {
            GetClientAuditEntriesStatus.PermissionDenied
                => GetClientFreezeHistorySourceRowsResult.Denied(),
            GetClientAuditEntriesStatus.ValidationFailed
                => GetClientFreezeHistorySourceRowsResult.Invalid(
                    auditResult.ErrorMessage ?? "Client history selectors are invalid.",
                    auditResult.ErrorField),
            GetClientAuditEntriesStatus.NotFound
                => GetClientFreezeHistorySourceRowsResult.MissingClient(),
            _ => GetClientFreezeHistorySourceRowsResult.InconsistentSource(),
        };
    }

    private sealed record FreezeStorageRow(
        FreezeRecord Freeze,
        Guid MembershipClientId,
        string MembershipTypeNameSnapshot);

    private sealed record CanonicalFreezeHistorySource(
        FreezeHistorySource Freeze,
        FreezeCancellationHistorySource? Cancellation);
}
