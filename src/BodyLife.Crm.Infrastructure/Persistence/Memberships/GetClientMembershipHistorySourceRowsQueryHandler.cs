using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
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
    private const string SaleCorrectionEntityType =
        "issued_membership_sale_correction";

    private static readonly ClientAuditEntityFilter[] EntityFilters =
    [
        ClientAuditEntityFilter.Membership,
        ClientAuditEntityFilter.MembershipOpeningState,
    ];

    private static readonly string[] ActionTypes =
    [
        MembershipAuditActions.Issued,
        MembershipAuditActions.Replaced,
        MembershipAuditActions.SaleCanceled,
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
            || auditPage.Items.Select(item => item.AuditEntryId).Distinct().Count()
                != auditPage.Items.Count
            || auditPage.Items
                .GroupBy(item => (item.EntityType, item.EntityId, item.ActionType))
                .Any(group => group.Count() > 1))
        {
            return GetClientMembershipHistorySourceRowsResult.InconsistentSource();
        }

        var membershipIds = auditPage.Items
            .Where(item => item.EntityType == ClientAuditEntityFilter.Membership)
            .Select(item => item.EntityId)
            .Distinct()
            .ToArray();
        var openingStateIds = auditPage.Items
            .Where(item =>
                item.EntityType == ClientAuditEntityFilter.MembershipOpeningState)
            .Select(item => item.EntityId)
            .ToArray();
        var correctionMembershipIds = auditPage.Items
            .Where(item => item.EntityType == ClientAuditEntityFilter.Membership
                && item.ActionType is MembershipAuditActions.Replaced
                    or MembershipAuditActions.SaleCanceled)
            .Select(item => item.EntityId)
            .Distinct()
            .ToArray();

        var membershipRows = membershipIds.Length == 0
            ? []
            : await dbContext.Set<IssuedMembershipRecord>()
                .AsNoTracking()
                .Where(membership =>
                    membershipIds.Contains(membership.Id)
                    && membership.ClientId == query.ClientId)
                .ToArrayAsync(cancellationToken);
        var correctionRows = correctionMembershipIds.Length == 0
            ? []
            : await dbContext.Set<IssuedMembershipSaleCorrectionRecord>()
                .AsNoTracking()
                .Where(correction =>
                    correctionMembershipIds.Contains(
                        correction.OriginalMembershipId)
                    && correction.ClientId == query.ClientId)
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
            || correctionRows.Length != correctionMembershipIds.Length
            || openingStateRows.Length != openingStateIds.Length)
        {
            return GetClientMembershipHistorySourceRowsResult.InconsistentSource();
        }

        var membershipsById = membershipRows.ToDictionary(membership => membership.Id);
        var correctionsByMembershipId = correctionRows.ToDictionary(
            correction => correction.OriginalMembershipId);
        var openingStatesById = openingStateRows.ToDictionary(row => row.OpeningState.Id);
        var paperReferenceReader = new PaperFallbackEntryRowReferenceReader(dbContext);
        var issuedPaperReferenceSources = auditPage.Items
            .Where(item => item.EntityType == ClientAuditEntityFilter.Membership
                && item.ActionType == MembershipAuditActions.Issued)
            .Select(item =>
            {
                var membership = membershipsById[item.EntityId];
                return new PaperFallbackEntryRowReferenceSource(
                    membership.Id,
                    membership.EntryOrigin,
                    membership.EntryBatchId,
                    item.OccurredAt,
                    item.ActorAccountId.Value,
                    item.SessionId.Value,
                    PaperFallbackEventType.MembershipSale);
            })
            .ToArray();
        var issuedPaperReferences = await paperReferenceReader.LoadAsync(
            issuedPaperReferenceSources,
            MembershipAuditActions.MembershipEntityType,
            PaperFallbackEventType.MembershipSale,
            cancellationToken);
        var correctionPaperReferences = await paperReferenceReader.LoadAsync(
            correctionRows.Select(correction =>
                new PaperFallbackEntryRowReferenceSource(
                    correction.Id,
                    correction.EntryOrigin,
                    correction.EntryBatchId,
                    correction.OccurredAt,
                    correction.RecordedByAccountId,
                    correction.SessionId,
                    PaperFallbackEventType.CorrectionOrCancellation))
                .ToArray(),
            SaleCorrectionEntityType,
            PaperFallbackEventType.CorrectionOrCancellation,
            cancellationToken);
        if (issuedPaperReferences is null
            || correctionPaperReferences is null)
        {
            return GetClientMembershipHistorySourceRowsResult.InconsistentSource();
        }

        var expectedCorrectionLinks = correctionRows
            .SelectMany(correction =>
            {
                var rowId = correctionPaperReferences
                    .GetValueOrDefault(correction.Id)?.EntryBatchRowId;
                var links = new List<PaperFallbackExpectedEntityLink>
                {
                    new(SaleCorrectionEntityType, correction.Id, rowId),
                };
                if (correction.ReplacementMembershipId is { } membershipId)
                {
                    links.Add(new(
                        MembershipAuditActions.MembershipEntityType,
                        membershipId,
                        rowId));
                }

                if (correction.ReplacementPaymentId is { } paymentId)
                {
                    links.Add(new(PaymentAuditActions.EntityType, paymentId, rowId));
                }

                return links;
            })
            .ToArray();
        if (!await paperReferenceReader.HasExpectedEntityLinksAsync(
                expectedCorrectionLinks,
                cancellationToken))
        {
            return GetClientMembershipHistorySourceRowsResult.InconsistentSource();
        }

        var correctionPaperReferencesByMembershipId = correctionRows.ToDictionary(
            correction => correction.OriginalMembershipId,
            correction => correctionPaperReferences.GetValueOrDefault(correction.Id));
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
                        => MapIssuedMembership(
                            membership,
                            auditEntry,
                            auditEntry.ActionType == MembershipAuditActions.Issued
                                ? issuedPaperReferences.GetValueOrDefault(membership.Id)
                                : correctionPaperReferencesByMembershipId
                                    .GetValueOrDefault(membership.Id),
                            auditEntry.ActionType == MembershipAuditActions.Issued
                                ? null
                                : correctionsByMembershipId.GetValueOrDefault(
                                    auditEntry.EntityId)),
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
        ClientAuditEntry auditEntry,
        PaperFallbackEntryRowReference? paperReference,
        IssuedMembershipSaleCorrectionRecord? correction)
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
            || auditEntry.EntityType != ClientAuditEntityFilter.Membership
            || auditEntry.EntityId != membership.Id)
        {
            return null;
        }

        var eventRecordedAt = membership.IssuedAt;
        var eventEntryOrigin = entryOrigin;
        if (auditEntry.ActionType == MembershipAuditActions.Issued)
        {
            if (correction is not null
                || auditEntry.RecordedAt != membership.IssuedAt
                || auditEntry.ActorAccountId.Value != membership.IssuedByAccountId
                || auditEntry.EntryOrigin != entryOrigin
                || auditEntry.Comment != membership.Comment
                || !PaperFallbackEntryRowReferenceReader.HasMatchingAuditReference(
                    auditEntry,
                    membership.EntryBatchId,
                    paperReference))
            {
                return null;
            }
        }
        else
        {
            if (correction is null
                || correction.Id == Guid.Empty
                || correction.ClientId != membership.ClientId
                || correction.OriginalMembershipId != membership.Id
                || correction.OriginalPaymentId == Guid.Empty
                || correction.RecordedByAccountId == Guid.Empty
                || correction.SessionId == Guid.Empty
                || correction.Status != "active"
                || string.IsNullOrWhiteSpace(correction.Reason)
                || correction.EntryOrigin is not ("normal" or "paper_fallback")
                || !TryMapEntryOrigin(
                    correction.EntryOrigin,
                    out eventEntryOrigin)
                || auditEntry.RecordedAt != correction.RecordedAt
                || auditEntry.OccurredAt != correction.OccurredAt
                || auditEntry.ActorAccountId.Value
                    != correction.RecordedByAccountId
                || auditEntry.SessionId.Value != correction.SessionId
                || auditEntry.EntryOrigin != eventEntryOrigin
                || auditEntry.Reason != correction.Reason
                || !PaperFallbackEntryRowReferenceReader.HasMatchingAuditReference(
                    auditEntry,
                    correction.EntryBatchId,
                    paperReference)
                || !IsMatchingSaleCorrectionLifecycle(
                    membership,
                    correction,
                    auditEntry.ActionType))
            {
                return null;
            }

            eventRecordedAt = correction.RecordedAt;
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
            correction is null
                ? membership.EntryBatchId
                : correction.EntryBatchId,
            membership.Comment,
            paperReference);
        return new ClientMembershipHistorySourceRow(
            ClientMembershipHistorySourceKind.IssuedMembership,
            membership.ClientId,
            membership.Id,
            auditEntry.OccurredAt,
            eventRecordedAt,
            eventEntryOrigin,
            source,
            OpeningState: null,
            auditEntry);
    }

    private static bool IsMatchingSaleCorrectionLifecycle(
        IssuedMembershipRecord membership,
        IssuedMembershipSaleCorrectionRecord correction,
        string actionType)
    {
        return actionType switch
        {
            MembershipAuditActions.Replaced =>
                correction.CorrectionMode == "replace"
                && membership.Status == "corrected"
                && correction.ReplacementMembershipId is { } replacementMembershipId
                && replacementMembershipId != Guid.Empty
                && replacementMembershipId != membership.Id
                && correction.ReplacementPaymentId is { } replacementPaymentId
                && replacementPaymentId != Guid.Empty
                && replacementPaymentId != correction.OriginalPaymentId,
            MembershipAuditActions.SaleCanceled =>
                correction.CorrectionMode == "cancel"
                && membership.Status == "canceled"
                && correction.ReplacementMembershipId is null
                && correction.ReplacementPaymentId is null,
            _ => false,
        };
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
