using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class GetClientMembershipHistorySourceRowsQueryHandler(
    BodyLifeDbContext dbContext,
    IBodyLifeQueryHandler<GetClientAuditEntriesQuery, GetClientAuditEntriesResult>
        auditEntriesQueryHandler)
    : IBodyLifeQueryHandler<
        GetClientMembershipHistorySourceRowsQuery,
        GetClientMembershipHistorySourceRowsResult>
{
    private static readonly ClientAuditEntityFilter[] EntityFilters =
    [
        ClientAuditEntityFilter.Membership,
        ClientAuditEntityFilter.MembershipOpeningState,
    ];

    private static readonly string[] ActionTypes =
    [
        MembershipAuditActions.Issued,
        MembershipAuditActions.OpeningStateCreated,
    ];

    public async Task<GetClientMembershipHistorySourceRowsResult> ExecuteAsync(
        GetClientMembershipHistorySourceRowsQuery query,
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
                .GroupBy(item => (item.EntityType, item.EntityId))
                .Any(group => group.Count() > 1))
        {
            return GetClientMembershipHistorySourceRowsResult.InconsistentSource();
        }

        var membershipIds = auditPage.Items
            .Where(item => item.EntityType == ClientAuditEntityFilter.Membership)
            .Select(item => item.EntityId)
            .ToArray();
        var openingStateIds = auditPage.Items
            .Where(item =>
                item.EntityType == ClientAuditEntityFilter.MembershipOpeningState)
            .Select(item => item.EntityId)
            .ToArray();

        var membershipRows = membershipIds.Length == 0
            ? []
            : await dbContext.Set<IssuedMembershipRecord>()
                .AsNoTracking()
                .Where(membership =>
                    membershipIds.Contains(membership.Id)
                    && membership.ClientId == query.ClientId)
                .ToArrayAsync(cancellationToken);
        var openingStateRows = openingStateIds.Length == 0
            ? []
            : await (
                from openingState in dbContext.Set<MembershipOpeningStateRecord>()
                    .AsNoTracking()
                join membership in dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
                    on openingState.MembershipId equals membership.Id
                where openingStateIds.Contains(openingState.Id)
                    && membership.ClientId == query.ClientId
                select new OpeningStateStorageRow(openingState, membership.ClientId))
                .ToArrayAsync(cancellationToken);

        if (membershipRows.Length != membershipIds.Length
            || openingStateRows.Length != openingStateIds.Length)
        {
            return GetClientMembershipHistorySourceRowsResult.InconsistentSource();
        }

        var membershipsById = membershipRows.ToDictionary(membership => membership.Id);
        var openingStatesById = openingStateRows.ToDictionary(row => row.OpeningState.Id);
        var rows = new List<ClientMembershipHistorySourceRow>(auditPage.Items.Count);

        try
        {
            foreach (var auditEntry in auditPage.Items)
            {
                ClientMembershipHistorySourceRow? row = auditEntry.EntityType switch
                {
                    ClientAuditEntityFilter.Membership
                        when membershipsById.TryGetValue(
                            auditEntry.EntityId,
                            out var membership)
                        => MapIssuedMembership(membership, auditEntry),
                    ClientAuditEntityFilter.MembershipOpeningState
                        when openingStatesById.TryGetValue(
                            auditEntry.EntityId,
                            out var openingState)
                        => MapOpeningState(openingState, auditEntry),
                    _ => null,
                };

                if (row is null)
                {
                    return GetClientMembershipHistorySourceRowsResult.InconsistentSource();
                }

                rows.Add(row);
            }

            return GetClientMembershipHistorySourceRowsResult.Succeeded(
                ClientMembershipHistorySourceRowsPage.Create(
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    auditPage.Offset,
                    rows,
                    auditPage.HasMore));
        }
        catch (ArgumentException)
        {
            return GetClientMembershipHistorySourceRowsResult.InconsistentSource();
        }
        catch (InvalidOperationException)
        {
            return GetClientMembershipHistorySourceRowsResult.InconsistentSource();
        }
    }

    private static ClientMembershipHistorySourceRow? MapIssuedMembership(
        IssuedMembershipRecord membership,
        ClientAuditEntry auditEntry)
    {
        if (membership.Id == Guid.Empty
            || membership.ClientId == Guid.Empty
            || membership.MembershipTypeId == Guid.Empty
            || membership.IssuedByAccountId == Guid.Empty
            || !MembershipQuerySupport.TryMapLifecycleStatus(
                membership.Status,
                out var status)
            || !TryMapEntryOrigin(membership.EntryOrigin, out var entryOrigin)
            || MembershipDateRules.CalculateBaseEndDate(
                membership.StartDate,
                membership.DurationDaysSnapshot) != membership.BaseEndDate
            || auditEntry.ActionType != MembershipAuditActions.Issued
            || auditEntry.EntityType != ClientAuditEntityFilter.Membership
            || auditEntry.EntityId != membership.Id
            || auditEntry.RecordedAt != membership.IssuedAt
            || auditEntry.ActorAccountId.Value != membership.IssuedByAccountId
            || auditEntry.EntryOrigin != entryOrigin)
        {
            return null;
        }

        var source = new IssuedMembershipHistorySource(
            membership.Id,
            membership.ClientId,
            membership.MembershipTypeId,
            new IssuedMembershipSnapshot(
                membership.TypeNameSnapshot,
                membership.DurationDaysSnapshot,
                membership.VisitsLimitSnapshot,
                new Money(
                    membership.PriceAmountSnapshot,
                    membership.PriceCurrencySnapshot)),
            membership.StartDate,
            membership.BaseEndDate,
            membership.IssuedAt,
            new AccountId(membership.IssuedByAccountId),
            status,
            membership.EntryBatchId,
            membership.Comment);
        return new ClientMembershipHistorySourceRow(
            ClientMembershipHistorySourceKind.IssuedMembership,
            membership.ClientId,
            membership.Id,
            auditEntry.OccurredAt,
            membership.IssuedAt,
            entryOrigin,
            source,
            OpeningState: null,
            auditEntry);
    }

    private static ClientMembershipHistorySourceRow? MapOpeningState(
        OpeningStateStorageRow row,
        ClientAuditEntry auditEntry)
    {
        var openingState = row.OpeningState;
        if (openingState.Id == Guid.Empty
            || row.ClientId == Guid.Empty
            || openingState.MembershipId == Guid.Empty
            || openingState.RecordedByAccountId == Guid.Empty
            || openingState.RecordedSessionId == Guid.Empty
            || string.IsNullOrWhiteSpace(openingState.SourceReference)
            || string.IsNullOrWhiteSpace(openingState.Reason)
            || !TryMapOpeningStateStatus(openingState.Status, out var status)
            || !TryMapEntryOrigin(openingState.EntryOrigin, out var entryOrigin)
            || auditEntry.ActionType != MembershipAuditActions.OpeningStateCreated
            || auditEntry.EntityType
                != ClientAuditEntityFilter.MembershipOpeningState
            || auditEntry.EntityId != openingState.Id
            || auditEntry.RecordedAt != openingState.RecordedAt
            || auditEntry.ActorAccountId.Value != openingState.RecordedByAccountId
            || auditEntry.SessionId.Value != openingState.RecordedSessionId
            || auditEntry.EntryOrigin != entryOrigin)
        {
            return null;
        }

        var source = new MembershipOpeningStateHistorySource(
            openingState.Id,
            row.ClientId,
            openingState.MembershipId,
            MembershipOpeningState.FromStoredSource(
                openingState.OpeningAsOfDate,
                openingState.DeclaredRemainingVisits,
                openingState.DeclaredNegativeBalance,
                openingState.KnownEffectiveEndDate,
                openingState.KnownExtensionDays),
            openingState.SourceReference,
            openingState.Reason,
            openingState.RecordedAt,
            new AccountId(openingState.RecordedByAccountId),
            new SessionId(openingState.RecordedSessionId),
            openingState.EntryBatchId,
            status);
        return new ClientMembershipHistorySourceRow(
            ClientMembershipHistorySourceKind.OpeningState,
            row.ClientId,
            openingState.MembershipId,
            auditEntry.OccurredAt,
            openingState.RecordedAt,
            entryOrigin,
            IssuedMembership: null,
            source,
            auditEntry);
    }

    private static GetClientMembershipHistorySourceRowsResult MapAuditFailure(
        GetClientAuditEntriesResult auditResult)
    {
        return auditResult.Status switch
        {
            GetClientAuditEntriesStatus.PermissionDenied
                => GetClientMembershipHistorySourceRowsResult.Denied(),
            GetClientAuditEntriesStatus.ValidationFailed
                => GetClientMembershipHistorySourceRowsResult.Invalid(
                    auditResult.ErrorMessage ?? "Client history selectors are invalid.",
                    auditResult.ErrorField),
            GetClientAuditEntriesStatus.NotFound
                => GetClientMembershipHistorySourceRowsResult.MissingClient(),
            _ => GetClientMembershipHistorySourceRowsResult.InconsistentSource(),
        };
    }

    private static bool TryMapEntryOrigin(string? value, out EntryOrigin entryOrigin)
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

    private static bool TryMapOpeningStateStatus(
        string? value,
        out MembershipOpeningStateSourceStatus status)
    {
        status = value switch
        {
            "active" => MembershipOpeningStateSourceStatus.Active,
            "canceled" => MembershipOpeningStateSourceStatus.Canceled,
            "corrected" => MembershipOpeningStateSourceStatus.Corrected,
            _ => default,
        };

        return status != default;
    }

    private sealed record OpeningStateStorageRow(
        MembershipOpeningStateRecord OpeningState,
        Guid ClientId);
}
