using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class GetClientNonWorkingDayHistorySourceRowsQueryHandler(
    BodyLifeDbContext dbContext,
    IBodyLifeQueryHandler<GetClientAuditEntriesQuery, GetClientAuditEntriesResult>
        auditEntriesQueryHandler)
    : IBodyLifeQueryHandler<
        GetClientNonWorkingDayHistorySourceRowsQuery,
        GetClientNonWorkingDayHistorySourceRowsResult>
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(
        JsonSerializerDefaults.Web);

    private static readonly ClientAuditEntityFilter[] EntityFilters =
    [
        ClientAuditEntityFilter.NonWorkingPeriod,
    ];

    private static readonly string[] ActionTypes =
    [
        NonWorkingDayAuditActions.Added,
        NonWorkingDayAuditActions.Corrected,
        NonWorkingDayAuditActions.Canceled,
    ];

    public async Task<GetClientNonWorkingDayHistorySourceRowsResult> ExecuteAsync(
        GetClientNonWorkingDayHistorySourceRowsQuery query,
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
                item.EntityType != ClientAuditEntityFilter.NonWorkingPeriod)
            || auditPage.Items.Select(item => item.AuditEntryId).Distinct().Count()
                != auditPage.Items.Count
            || auditPage.Items
                .GroupBy(item => (item.ActionType, item.EntityId))
                .Any(group => group.Count() > 1))
        {
            return GetClientNonWorkingDayHistorySourceRowsResult
                .InconsistentSource();
        }

        if (auditPage.Items.Count == 0)
        {
            return GetClientNonWorkingDayHistorySourceRowsResult.Succeeded(
                ClientNonWorkingDayHistorySourceRowsPage.Create(
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    auditPage.Offset,
                    items: [],
                    auditPage.HasMore));
        }

        var parsedAuditRows = new List<ParsedAuditRow>(auditPage.Items.Count);
        foreach (var auditEntry in auditPage.Items)
        {
            if (!TryReadAuditLink(auditEntry, out var link) || link is null)
            {
                return GetClientNonWorkingDayHistorySourceRowsResult
                    .InconsistentSource();
            }

            parsedAuditRows.Add(new ParsedAuditRow(auditEntry, link));
        }

        var periodIds = parsedAuditRows
            .SelectMany(row => row.Link.ReplacementPeriodId is { } replacementId
                ? new[] { row.Link.OriginalPeriodId, replacementId }
                : [row.Link.OriginalPeriodId])
            .Distinct()
            .ToArray();
        var periodRows = await dbContext.Set<NonWorkingPeriodRecord>()
            .AsNoTracking()
            .Where(period => periodIds.Contains(period.Id))
            .ToArrayAsync(cancellationToken);
        if (periodRows.Length != periodIds.Length)
        {
            return GetClientNonWorkingDayHistorySourceRowsResult
                .InconsistentSource();
        }

        var expectedApplicationCount = await dbContext
            .Set<NonWorkingPeriodApplicationRecord>()
            .AsNoTracking()
            .CountAsync(
                application => periodIds.Contains(
                    application.NonWorkingPeriodId),
                cancellationToken);
        var applicationRows = await (
            from application in dbContext
                .Set<NonWorkingPeriodApplicationRecord>()
                .AsNoTracking()
            join membership in dbContext.Set<IssuedMembershipRecord>()
                .AsNoTracking()
                on new { application.MembershipId, application.ClientId }
                equals new
                {
                    MembershipId = membership.Id,
                    membership.ClientId,
                }
            where periodIds.Contains(application.NonWorkingPeriodId)
            select new ApplicationStorageRow(
                application,
                membership.ClientId,
                membership.TypeNameSnapshot))
            .ToArrayAsync(cancellationToken);
        if (applicationRows.Length != expectedApplicationCount)
        {
            return GetClientNonWorkingDayHistorySourceRowsResult
                .InconsistentSource();
        }

        var cancellationRows = await dbContext
            .Set<NonWorkingPeriodCancellationRecord>()
            .AsNoTracking()
            .Where(cancellation => periodIds.Contains(
                cancellation.NonWorkingPeriodId))
            .ToArrayAsync(cancellationToken);
        if (cancellationRows
            .GroupBy(cancellation => cancellation.NonWorkingPeriodId)
            .Any(group => group.Count() > 1))
        {
            return GetClientNonWorkingDayHistorySourceRowsResult
                .InconsistentSource();
        }

        var applicationsByPeriodId = applicationRows
            .GroupBy(row => row.Application.NonWorkingPeriodId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var cancellationsByPeriodId = cancellationRows.ToDictionary(
            cancellation => cancellation.NonWorkingPeriodId);
        var sourcesByPeriodId = new Dictionary<
            Guid,
            CanonicalPeriodHistorySource>(periodRows.Length);
        foreach (var periodRow in periodRows)
        {
            applicationsByPeriodId.TryGetValue(
                periodRow.Id,
                out var applications);
            cancellationsByPeriodId.TryGetValue(
                periodRow.Id,
                out var cancellation);
            if (!TryMapCanonicalPeriod(
                    periodRow,
                    applications ?? [],
                    cancellation,
                    query.ClientId,
                    out var source)
                || source is null)
            {
                return GetClientNonWorkingDayHistorySourceRowsResult
                    .InconsistentSource();
            }

            sourcesByPeriodId.Add(periodRow.Id, source);
        }

        try
        {
            var rows = new List<ClientNonWorkingDayHistorySourceRow>(
                parsedAuditRows.Count);
            foreach (var parsedRow in parsedAuditRows)
            {
                if (!sourcesByPeriodId.TryGetValue(
                        parsedRow.Link.OriginalPeriodId,
                        out var original))
                {
                    return GetClientNonWorkingDayHistorySourceRowsResult
                        .InconsistentSource();
                }

                CanonicalPeriodHistorySource? replacement = null;
                if (parsedRow.Link.ReplacementPeriodId is { } replacementId
                    && !sourcesByPeriodId.TryGetValue(
                        replacementId,
                        out replacement))
                {
                    return GetClientNonWorkingDayHistorySourceRowsResult
                        .InconsistentSource();
                }

                var row = parsedRow.AuditEntry.ActionType switch
                {
                    NonWorkingDayAuditActions.Added => MapAdded(
                        original,
                        parsedRow.AuditEntry,
                        parsedRow.Link),
                    NonWorkingDayAuditActions.Corrected => MapCorrected(
                        original,
                        replacement,
                        parsedRow.AuditEntry,
                        parsedRow.Link),
                    NonWorkingDayAuditActions.Canceled => MapCanceled(
                        original,
                        parsedRow.AuditEntry,
                        parsedRow.Link),
                    _ => null,
                };
                if (row is null)
                {
                    return GetClientNonWorkingDayHistorySourceRowsResult
                        .InconsistentSource();
                }

                rows.Add(row);
            }

            return GetClientNonWorkingDayHistorySourceRowsResult.Succeeded(
                ClientNonWorkingDayHistorySourceRowsPage.Create(
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    auditPage.Offset,
                    rows,
                    auditPage.HasMore));
        }
        catch (ArgumentException)
        {
            return GetClientNonWorkingDayHistorySourceRowsResult
                .InconsistentSource();
        }
        catch (InvalidOperationException)
        {
            return GetClientNonWorkingDayHistorySourceRowsResult
                .InconsistentSource();
        }
    }

    private static bool TryMapCanonicalPeriod(
        NonWorkingPeriodRecord period,
        IReadOnlyCollection<ApplicationStorageRow> applicationRows,
        NonWorkingPeriodCancellationRecord? cancellation,
        Guid requestedClientId,
        out CanonicalPeriodHistorySource? source)
    {
        source = null;
        if (period.Id == Guid.Empty
            || period.CreatedByAccountId == Guid.Empty
            || period.SessionId == Guid.Empty
            || string.IsNullOrWhiteSpace(period.ReasonCode)
            || period.ReasonCode != period.ReasonCode.Trim()
            || period.ReasonComment is not null
                && (string.IsNullOrWhiteSpace(period.ReasonComment)
                    || period.ReasonComment != period.ReasonComment.Trim())
            || !TryMapStatus(period.Status, out var status))
        {
            return false;
        }

        if (cancellation is not null
            && (cancellation.Id == Guid.Empty
                || cancellation.NonWorkingPeriodId != period.Id
                || cancellation.RecordedByAccountId == Guid.Empty
                || cancellation.SessionId == Guid.Empty
                || string.IsNullOrWhiteSpace(cancellation.Reason)
                || cancellation.Reason != cancellation.Reason.Trim()))
        {
            return false;
        }

        try
        {
            var periodRange = new DateRange(period.StartDate, period.EndDate);
            var applicationProjections = new List<CanonicalApplicationProjection>(
                applicationRows.Count);
            foreach (var row in applicationRows)
            {
                var application = row.Application;
                if (application.Id == Guid.Empty
                    || application.NonWorkingPeriodId != period.Id
                    || application.MembershipId == Guid.Empty
                    || application.ClientId == Guid.Empty
                    || row.MembershipClientId != application.ClientId
                    || string.IsNullOrWhiteSpace(
                        row.MembershipTypeNameSnapshot)
                    || row.MembershipTypeNameSnapshot
                        != row.MembershipTypeNameSnapshot.Trim()
                    || application.ConfirmedAt != period.CreatedAt
                    || !TryMapStatus(application.Status, out var applicationStatus)
                    || applicationStatus != status)
                {
                    return false;
                }

                var canonicalApplication =
                    new NonWorkingDayCorrectionApplicationSource(
                        application.Id,
                        application.MembershipId,
                        application.ClientId,
                        new DateRange(
                            application.AppliedStartDate,
                            application.AppliedEndDate),
                        application.PreviewedAt,
                        application.ConfirmedAt,
                        applicationStatus);
                applicationProjections.Add(
                    new CanonicalApplicationProjection(
                        canonicalApplication,
                        row.MembershipTypeNameSnapshot));
            }

            var orderedApplications = applicationProjections
                .OrderBy(item => item.Application.MembershipId)
                .ThenBy(item => item.Application.ApplicationId)
                .ToArray();
            var canonicalSource = new NonWorkingDayCorrectionSource(
                period.Id,
                periodRange,
                period.ReasonCode,
                period.ReasonComment,
                period.CreatedAt,
                period.CreatedByAccountId,
                period.SessionId,
                status,
                orderedApplications.Select(item => item.Application),
                cancellation?.Id);
            var clientApplications = orderedApplications
                .Where(item => item.Application.ClientId == requestedClientId)
                .Select(item => new NonWorkingDayHistoryApplicationSource(
                    item.Application.ApplicationId,
                    period.Id,
                    item.Application.MembershipId,
                    item.Application.ClientId,
                    item.MembershipTypeNameSnapshot,
                    item.Application.AppliedRange,
                    item.Application.PreviewedAt,
                    item.Application.ConfirmedAt,
                    item.Application.Status))
                .ToArray();
            var periodSource = new NonWorkingDayHistoryPeriodSource(
                canonicalSource.PeriodId,
                requestedClientId,
                canonicalSource.Period,
                canonicalSource.ReasonCode,
                canonicalSource.ReasonComment,
                canonicalSource.CreatedAt,
                new AccountId(canonicalSource.CreatedByAccountId),
                new SessionId(canonicalSource.SessionId),
                canonicalSource.Status,
                canonicalSource.ExistingCancellationId,
                canonicalSource.Applications.Count,
                canonicalSource.Applications
                    .Select(application => application.ClientId)
                    .Distinct()
                    .Count(),
                clientApplications);
            var cancellationSource = cancellation is null
                ? null
                : new NonWorkingDayCancellationHistorySource(
                    cancellation.Id,
                    cancellation.NonWorkingPeriodId,
                    cancellation.Reason,
                    cancellation.RecordedAt,
                    new AccountId(cancellation.RecordedByAccountId),
                    new SessionId(cancellation.SessionId));
            source = new CanonicalPeriodHistorySource(
                periodSource,
                orderedApplications,
                cancellationSource);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ClientNonWorkingDayHistorySourceRow? MapAdded(
        CanonicalPeriodHistorySource source,
        ClientAuditEntry auditEntry,
        HistoryAuditLink link)
    {
        var applicationMembershipIds = source.Applications
            .Select(item => item.Application.MembershipId)
            .ToArray();
        var applicationClientIds = source.Applications
            .Select(item => item.Application.ClientId)
            .ToArray();
        if (link.Mode is not null
            || link.OriginalPeriodId != source.Period.PeriodId
            || link.ReplacementPeriodId is not null
            || link.CancellationId is not null
            || !link.OldMembershipIds.SequenceEqual(applicationMembershipIds)
            || !link.AffectedClientIds.SequenceEqual(applicationClientIds)
            || source.Period.ClientApplications.Count == 0
            || auditEntry.ActionType != NonWorkingDayAuditActions.Added
            || auditEntry.EntityId != source.Period.PeriodId
            || auditEntry.RecordedAt != source.Period.CreatedAt
            || auditEntry.ActorAccountId != source.Period.CreatedByAccountId
            || auditEntry.SessionId != source.Period.RecordedSessionId)
        {
            return null;
        }

        return new ClientNonWorkingDayHistorySourceRow(
            ClientNonWorkingDayHistorySourceKind.Added,
            source.Period.ClientId,
            source.Period.PeriodId,
            auditEntry.OccurredAt,
            auditEntry.RecordedAt,
            auditEntry.EntryOrigin,
            source.Period,
            Correction: null,
            auditEntry);
    }

    private static ClientNonWorkingDayHistorySourceRow? MapCorrected(
        CanonicalPeriodHistorySource original,
        CanonicalPeriodHistorySource? replacement,
        ClientAuditEntry auditEntry,
        HistoryAuditLink link)
    {
        if (replacement is null
            || link.Mode is not (
                NonWorkingDayCorrectionMode.ReplaceRange
                or NonWorkingDayCorrectionMode.ReplaceReason)
            || original.Period.CurrentStatus
                != NonWorkingDayCorrectionSourceStatus.Corrected
            || !ValidateCorrectionScope(link, original, replacement)
            || !ValidateReplaceReasonScope(link.Mode.Value, original, replacement)
            || original.Period.ClientApplications.Count == 0
                && replacement.Period.ClientApplications.Count == 0
            || auditEntry.ActionType != NonWorkingDayAuditActions.Corrected
            || auditEntry.EntityId != original.Period.PeriodId
            || string.IsNullOrWhiteSpace(auditEntry.Reason)
            || auditEntry.Reason != auditEntry.Reason.Trim()
            || string.IsNullOrWhiteSpace(auditEntry.Comment)
            || auditEntry.Comment != auditEntry.Comment.Trim())
        {
            return null;
        }

        var correction = new NonWorkingDayCorrectionHistorySource(
            link.Mode.Value,
            original.Period,
            replacement.Period,
            cancellation: null,
            auditEntry.Reason,
            auditEntry.Comment,
            auditEntry.OccurredAt,
            auditEntry.RecordedAt,
            auditEntry.ActorAccountId,
            auditEntry.SessionId,
            auditEntry.EntryOrigin,
            link.AffectedMembershipIds);
        return new ClientNonWorkingDayHistorySourceRow(
            ClientNonWorkingDayHistorySourceKind.Corrected,
            original.Period.ClientId,
            original.Period.PeriodId,
            auditEntry.OccurredAt,
            auditEntry.RecordedAt,
            auditEntry.EntryOrigin,
            AddedPeriod: null,
            correction,
            auditEntry);
    }

    private static ClientNonWorkingDayHistorySourceRow? MapCanceled(
        CanonicalPeriodHistorySource original,
        ClientAuditEntry auditEntry,
        HistoryAuditLink link)
    {
        var cancellation = original.Cancellation;
        if (link.Mode != NonWorkingDayCorrectionMode.Cancel
            || link.ReplacementPeriodId is not null
            || cancellation is null
            || link.CancellationId != cancellation.CancellationId
            || original.Period.CurrentStatus
                != NonWorkingDayCorrectionSourceStatus.Canceled
            || !ValidateCorrectionScope(link, original, replacement: null)
            || original.Period.ClientApplications.Count == 0
            || auditEntry.ActionType != NonWorkingDayAuditActions.Canceled
            || auditEntry.EntityId != original.Period.PeriodId
            || auditEntry.RecordedAt != cancellation.RecordedAt
            || auditEntry.ActorAccountId != cancellation.RecordedByAccountId
            || auditEntry.SessionId != cancellation.RecordedSessionId
            || auditEntry.Reason != cancellation.Reason
            || string.IsNullOrWhiteSpace(auditEntry.Comment)
            || auditEntry.Comment != auditEntry.Comment.Trim())
        {
            return null;
        }

        var correction = new NonWorkingDayCorrectionHistorySource(
            NonWorkingDayCorrectionMode.Cancel,
            original.Period,
            replacementPeriod: null,
            cancellation,
            cancellation.Reason,
            auditEntry.Comment,
            auditEntry.OccurredAt,
            auditEntry.RecordedAt,
            auditEntry.ActorAccountId,
            auditEntry.SessionId,
            auditEntry.EntryOrigin,
            link.AffectedMembershipIds);
        return new ClientNonWorkingDayHistorySourceRow(
            ClientNonWorkingDayHistorySourceKind.Canceled,
            original.Period.ClientId,
            original.Period.PeriodId,
            auditEntry.OccurredAt,
            auditEntry.RecordedAt,
            auditEntry.EntryOrigin,
            AddedPeriod: null,
            correction,
            auditEntry);
    }

    private static bool ValidateCorrectionScope(
        HistoryAuditLink link,
        CanonicalPeriodHistorySource original,
        CanonicalPeriodHistorySource? replacement)
    {
        var oldMembershipIds = original.Applications
            .Select(item => item.Application.MembershipId)
            .ToArray();
        var newMembershipIds = replacement?.Applications
            .Select(item => item.Application.MembershipId)
            .ToArray() ?? [];
        var affectedMembershipIds = oldMembershipIds
            .Concat(newMembershipIds)
            .Distinct()
            .Order()
            .ToArray();
        var affectedClientIds = original.Applications
            .Select(item => item.Application.ClientId)
            .Concat(replacement?.Applications.Select(
                item => item.Application.ClientId) ?? [])
            .Distinct()
            .Order()
            .ToArray();

        return link.OriginalPeriodId == original.Period.PeriodId
            && link.ReplacementPeriodId == replacement?.Period.PeriodId
            && link.OldMembershipIds.SequenceEqual(oldMembershipIds)
            && link.NewMembershipIds.SequenceEqual(newMembershipIds)
            && link.AffectedMembershipIds.SequenceEqual(affectedMembershipIds)
            && link.AffectedClientIds.SequenceEqual(affectedClientIds)
            && link.OldAffectedCount == oldMembershipIds.Length
            && link.NewAffectedCount == newMembershipIds.Length
            && link.AffectedUnionCount == affectedMembershipIds.Length;
    }

    private static bool ValidateReplaceReasonScope(
        NonWorkingDayCorrectionMode mode,
        CanonicalPeriodHistorySource original,
        CanonicalPeriodHistorySource replacement)
    {
        if (mode != NonWorkingDayCorrectionMode.ReplaceReason)
        {
            return true;
        }

        return original.Period.Period == replacement.Period.Period
            && original.Applications
                .Select(item => (
                    item.Application.MembershipId,
                    item.Application.ClientId,
                    item.Application.AppliedRange))
                .SequenceEqual(replacement.Applications.Select(item => (
                    item.Application.MembershipId,
                    item.Application.ClientId,
                    item.Application.AppliedRange)));
    }

    private static bool TryReadAuditLink(
        ClientAuditEntry auditEntry,
        out HistoryAuditLink? link)
    {
        link = null;
        if (auditEntry.EntityType
                != ClientAuditEntityFilter.NonWorkingPeriod
            || auditEntry.EntityId == Guid.Empty
            || auditEntry.ActorAccountId.Value == Guid.Empty
            || auditEntry.SessionId.Value == Guid.Empty
            || auditEntry.OccurredAt.Offset != TimeSpan.Zero
            || auditEntry.RecordedAt.Offset != TimeSpan.Zero
            || auditEntry.OccurredAt > auditEntry.RecordedAt)
        {
            return false;
        }

        try
        {
            if (auditEntry.ActionType == NonWorkingDayAuditActions.Added)
            {
                var related = JsonSerializer.Deserialize<AddedAuditRelatedEntities>(
                    auditEntry.RelatedEntityRefsJson,
                    AuditJsonOptions);
                if (related?.AffectedMembershipIds is not { } membershipIds
                    || related.AffectedClientIds is not { } clientIds
                    || membershipIds.Length != clientIds.Length
                    || !HaveOrderedUniqueIds(membershipIds)
                    || clientIds.Any(id => id == Guid.Empty))
                {
                    return false;
                }

                link = new HistoryAuditLink(
                    Mode: null,
                    auditEntry.EntityId,
                    ReplacementPeriodId: null,
                    CancellationId: null,
                    membershipIds,
                    NewMembershipIds: [],
                    AffectedMembershipIds: membershipIds,
                    AffectedClientIds: clientIds,
                    OldAffectedCount: membershipIds.Length,
                    NewAffectedCount: 0,
                    AffectedUnionCount: membershipIds.Length);
                return true;
            }

            var correctionRelated = JsonSerializer
                .Deserialize<CorrectionAuditRelatedEntities>(
                    auditEntry.RelatedEntityRefsJson,
                    AuditJsonOptions);
            var afterSummary = JsonSerializer
                .Deserialize<CorrectionAuditAfterSummary>(
                    auditEntry.AfterSummaryJson,
                    AuditJsonOptions);
            if (correctionRelated?.OldMembershipIds is not { } oldIds
                || correctionRelated.NewMembershipIds is not { } newIds
                || correctionRelated.AffectedMembershipIds is not { } affectedIds
                || correctionRelated.AffectedClientIds is not { } affectedClientIds
                || afterSummary is null
                || correctionRelated.OriginalPeriodId != auditEntry.EntityId
                || !HaveOrderedUniqueIds(oldIds)
                || !HaveOrderedUniqueIds(newIds)
                || !HaveOrderedUniqueIds(affectedIds)
                || !HaveOrderedUniqueIds(affectedClientIds))
            {
                return false;
            }

            var mode = afterSummary.Mode switch
            {
                "replace_range" => NonWorkingDayCorrectionMode.ReplaceRange,
                "replace_reason" => NonWorkingDayCorrectionMode.ReplaceReason,
                "cancel" => NonWorkingDayCorrectionMode.Cancel,
                _ => default,
            };
            if (!Enum.IsDefined(mode)
                || (mode == NonWorkingDayCorrectionMode.Cancel
                    ? auditEntry.ActionType != NonWorkingDayAuditActions.Canceled
                        || correctionRelated.ReplacementPeriodId is not null
                        || correctionRelated.CancellationId is null
                    : auditEntry.ActionType != NonWorkingDayAuditActions.Corrected
                        || correctionRelated.ReplacementPeriodId is null
                        || correctionRelated.CancellationId is not null)
                || correctionRelated.ReplacementPeriodId == Guid.Empty
                || correctionRelated.CancellationId == Guid.Empty
                || !oldIds.Concat(newIds).Distinct().Order()
                    .SequenceEqual(affectedIds)
                || afterSummary.OldAffectedCount != oldIds.Length
                || afterSummary.NewAffectedCount != newIds.Length
                || afterSummary.AffectedUnionCount != affectedIds.Length)
            {
                return false;
            }

            link = new HistoryAuditLink(
                mode,
                correctionRelated.OriginalPeriodId,
                correctionRelated.ReplacementPeriodId,
                correctionRelated.CancellationId,
                oldIds,
                newIds,
                affectedIds,
                affectedClientIds,
                afterSummary.OldAffectedCount,
                afterSummary.NewAffectedCount,
                afterSummary.AffectedUnionCount);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HaveOrderedUniqueIds(IReadOnlyList<Guid> ids)
    {
        return ids.All(id => id != Guid.Empty)
            && ids.Distinct().Count() == ids.Count
            && ids.SequenceEqual(ids.Order());
    }

    private static bool TryMapStatus(
        string value,
        out NonWorkingDayCorrectionSourceStatus status)
    {
        status = value switch
        {
            "active" => NonWorkingDayCorrectionSourceStatus.Active,
            "canceled" => NonWorkingDayCorrectionSourceStatus.Canceled,
            "corrected" => NonWorkingDayCorrectionSourceStatus.Corrected,
            _ => default,
        };

        return status != default;
    }

    private static GetClientNonWorkingDayHistorySourceRowsResult MapAuditFailure(
        GetClientAuditEntriesResult auditResult)
    {
        return auditResult.Status switch
        {
            GetClientAuditEntriesStatus.PermissionDenied
                => GetClientNonWorkingDayHistorySourceRowsResult.Denied(),
            GetClientAuditEntriesStatus.ValidationFailed
                => GetClientNonWorkingDayHistorySourceRowsResult.Invalid(
                    auditResult.ErrorMessage
                        ?? "Client history selectors are invalid.",
                    auditResult.ErrorField),
            GetClientAuditEntriesStatus.NotFound
                => GetClientNonWorkingDayHistorySourceRowsResult.MissingClient(),
            _ => GetClientNonWorkingDayHistorySourceRowsResult
                .InconsistentSource(),
        };
    }

    private sealed record ApplicationStorageRow(
        NonWorkingPeriodApplicationRecord Application,
        Guid MembershipClientId,
        string MembershipTypeNameSnapshot);

    private sealed record CanonicalApplicationProjection(
        NonWorkingDayCorrectionApplicationSource Application,
        string MembershipTypeNameSnapshot);

    private sealed record CanonicalPeriodHistorySource(
        NonWorkingDayHistoryPeriodSource Period,
        IReadOnlyList<CanonicalApplicationProjection> Applications,
        NonWorkingDayCancellationHistorySource? Cancellation);

    private sealed record ParsedAuditRow(
        ClientAuditEntry AuditEntry,
        HistoryAuditLink Link);

    private sealed record HistoryAuditLink(
        NonWorkingDayCorrectionMode? Mode,
        Guid OriginalPeriodId,
        Guid? ReplacementPeriodId,
        Guid? CancellationId,
        IReadOnlyList<Guid> OldMembershipIds,
        IReadOnlyList<Guid> NewMembershipIds,
        IReadOnlyList<Guid> AffectedMembershipIds,
        IReadOnlyList<Guid> AffectedClientIds,
        int OldAffectedCount,
        int NewAffectedCount,
        int AffectedUnionCount);

    private sealed class AddedAuditRelatedEntities
    {
        public Guid[]? AffectedMembershipIds { get; init; }

        public Guid[]? AffectedClientIds { get; init; }
    }

    private sealed class CorrectionAuditRelatedEntities
    {
        public Guid OriginalPeriodId { get; init; }

        public Guid? ReplacementPeriodId { get; init; }

        public Guid? CancellationId { get; init; }

        public Guid[]? OldMembershipIds { get; init; }

        public Guid[]? NewMembershipIds { get; init; }

        public Guid[]? AffectedMembershipIds { get; init; }

        public Guid[]? AffectedClientIds { get; init; }
    }

    private sealed class CorrectionAuditAfterSummary
    {
        public string? Mode { get; init; }

        public int OldAffectedCount { get; init; }

        public int NewAffectedCount { get; init; }

        public int AffectedUnionCount { get; init; }
    }
}
