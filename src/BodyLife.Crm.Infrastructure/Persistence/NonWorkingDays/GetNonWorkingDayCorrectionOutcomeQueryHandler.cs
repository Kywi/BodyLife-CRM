using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Modules.NonWorkingDays;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class GetNonWorkingDayCorrectionOutcomeQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        GetNonWorkingDayCorrectionOutcomeQuery,
        GetNonWorkingDayCorrectionOutcomeResult>
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(
        JsonSerializerDefaults.Web);

    public async Task<GetNonWorkingDayCorrectionOutcomeResult> ExecuteAsync(
        GetNonWorkingDayCorrectionOutcomeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await NonWorkingDayQuerySupport.IsOwnerAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetNonWorkingDayCorrectionOutcomeResult.Denied();
        }

        if (query.OriginalPeriodId == Guid.Empty)
        {
            return GetNonWorkingDayCorrectionOutcomeResult.Invalid(
                "Original non-working period id is required.",
                "originalPeriodId");
        }

        if (query.AuditEntryId == Guid.Empty)
        {
            return GetNonWorkingDayCorrectionOutcomeResult.Invalid(
                "Correction audit entry id is required.",
                "auditEntryId");
        }

        var audit = await dbContext.Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == query.AuditEntryId
                    && candidate.EntityId == query.OriginalPeriodId
                    && candidate.EntityType
                        == NonWorkingDayAuditActions.PeriodEntityType
                    && (candidate.ActionType
                            == NonWorkingDayAuditActions.Corrected
                        || candidate.ActionType
                            == NonWorkingDayAuditActions.Canceled),
                cancellationToken);
        if (audit is null)
        {
            return GetNonWorkingDayCorrectionOutcomeResult.Missing();
        }

        if (!TryReadAuditMaterial(
                audit,
                out var mode,
                out var related,
                out var afterSummary,
                out var entryOrigin))
        {
            return GetNonWorkingDayCorrectionOutcomeResult.InconsistentSource();
        }

        var originalRead = await NonWorkingDayCanonicalPeriodReader.ReadAsync(
            dbContext,
            query.OriginalPeriodId,
            audit.Id,
            cancellationToken);
        if (originalRead.Status
            != NonWorkingDayCanonicalPeriodReadStatus.Success)
        {
            return originalRead.Status
                == NonWorkingDayCanonicalPeriodReadStatus.NotFound
                    ? GetNonWorkingDayCorrectionOutcomeResult.Missing()
                    : GetNonWorkingDayCorrectionOutcomeResult.InconsistentSource();
        }

        NonWorkingDayCanonicalPeriod? replacementPeriod = null;
        NonWorkingDayCanonicalCancellation? cancellation = null;
        if (mode == NonWorkingDayCorrectionMode.Cancel)
        {
            if (related.ReplacementPeriodId is not null
                || related.CancellationId is not { } cancellationId)
            {
                return GetNonWorkingDayCorrectionOutcomeResult.InconsistentSource();
            }

            var cancellationRow = await dbContext
                .Set<NonWorkingPeriodCancellationRecord>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == cancellationId
                        && candidate.NonWorkingPeriodId
                            == query.OriginalPeriodId,
                    cancellationToken);
            if (cancellationRow is null)
            {
                return GetNonWorkingDayCorrectionOutcomeResult.InconsistentSource();
            }

            cancellation = new NonWorkingDayCanonicalCancellation(
                cancellationRow.Id,
                cancellationRow.NonWorkingPeriodId,
                cancellationRow.Reason,
                cancellationRow.RecordedAt,
                cancellationRow.RecordedByAccountId,
                cancellationRow.SessionId);
        }
        else
        {
            if (related.ReplacementPeriodId is not { } replacementPeriodId
                || related.CancellationId is not null)
            {
                return GetNonWorkingDayCorrectionOutcomeResult.InconsistentSource();
            }

            var replacementRead = await NonWorkingDayCanonicalPeriodReader
                .ReadAsync(
                    dbContext,
                    replacementPeriodId,
                    audit.Id,
                    cancellationToken);
            if (replacementRead.Status
                != NonWorkingDayCanonicalPeriodReadStatus.Success)
            {
                return GetNonWorkingDayCorrectionOutcomeResult
                    .InconsistentSource();
            }

            replacementPeriod = replacementRead.Period;
        }

        var originalPeriod = originalRead.Period!;
        var oldMembershipIds = originalPeriod.Applications
            .Select(application => application.MembershipId)
            .Order()
            .ToArray();
        var newMembershipIds = replacementPeriod?.Applications
            .Select(application => application.MembershipId)
            .Order()
            .ToArray() ?? [];
        var affectedMembershipIds = oldMembershipIds
            .Concat(newMembershipIds)
            .Distinct()
            .Order()
            .ToArray();
        var affectedClientIds = originalPeriod.Applications
            .Select(application => application.ClientId)
            .Concat(replacementPeriod?.Applications.Select(
                application => application.ClientId) ?? [])
            .Distinct()
            .Order()
            .ToArray();
        if (!SameOrderedIds(related.OldMembershipIds, oldMembershipIds)
            || !SameOrderedIds(related.NewMembershipIds, newMembershipIds)
            || !SameOrderedIds(
                related.AffectedMembershipIds,
                affectedMembershipIds)
            || !SameOrderedIds(related.AffectedClientIds, affectedClientIds)
            || afterSummary.OldAffectedCount != oldMembershipIds.Length
            || afterSummary.NewAffectedCount != newMembershipIds.Length
            || afterSummary.AffectedUnionCount != affectedMembershipIds.Length)
        {
            return GetNonWorkingDayCorrectionOutcomeResult.InconsistentSource();
        }

        try
        {
            return GetNonWorkingDayCorrectionOutcomeResult.Succeeded(
                new NonWorkingDayCanonicalCorrection(
                    mode,
                    originalPeriod,
                    replacementPeriod,
                    cancellation,
                    audit.Id,
                    audit.Reason!,
                    audit.Comment!,
                    audit.OccurredAt.ToUniversalTime(),
                    audit.RecordedAt.ToUniversalTime(),
                    audit.ActorAccountId,
                    audit.SessionId,
                    audit.DeviceLabel,
                    entryOrigin,
                    affectedMembershipIds));
        }
        catch (ArgumentException)
        {
            return GetNonWorkingDayCorrectionOutcomeResult.InconsistentSource();
        }
    }

    private static bool TryReadAuditMaterial(
        BusinessAuditEntryRecord audit,
        out NonWorkingDayCorrectionMode mode,
        out CorrectionAuditRelatedEntities related,
        out CorrectionAuditAfterSummary afterSummary,
        out EntryOrigin entryOrigin)
    {
        mode = default;
        related = null!;
        afterSummary = null!;
        entryOrigin = default;
        if (audit.ActorAccountId == Guid.Empty
            || audit.SessionId == Guid.Empty
            || audit.ActorRole != "owner"
            || audit.ActorAccountType != "owner"
            || string.IsNullOrWhiteSpace(audit.Reason)
            || audit.Reason != audit.Reason.Trim()
            || string.IsNullOrWhiteSpace(audit.Comment)
            || audit.Comment != audit.Comment.Trim()
            || string.IsNullOrWhiteSpace(audit.IdempotencyKey))
        {
            return false;
        }

        try
        {
            related = JsonSerializer.Deserialize<CorrectionAuditRelatedEntities>(
                    audit.RelatedEntityRefsJson,
                    AuditJsonOptions)
                ?? throw new JsonException();
            afterSummary = JsonSerializer.Deserialize<CorrectionAuditAfterSummary>(
                    audit.AfterSummaryJson,
                    AuditJsonOptions)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return false;
        }

        mode = afterSummary.Mode switch
        {
            "replace_range" => NonWorkingDayCorrectionMode.ReplaceRange,
            "replace_reason" => NonWorkingDayCorrectionMode.ReplaceReason,
            "cancel" => NonWorkingDayCorrectionMode.Cancel,
            _ => default,
        };
        if (!Enum.IsDefined(mode)
            || (mode == NonWorkingDayCorrectionMode.Cancel
                ? audit.ActionType != NonWorkingDayAuditActions.Canceled
                : audit.ActionType != NonWorkingDayAuditActions.Corrected)
            || related.OriginalPeriodId != audit.EntityId)
        {
            return false;
        }

        entryOrigin = audit.EntryOrigin switch
        {
            "normal" => EntryOrigin.Normal,
            "manual_backfill" => EntryOrigin.ManualBackfill,
            "paper_fallback" => EntryOrigin.PaperFallback,
            "future_import" => EntryOrigin.FutureImport,
            _ => default,
        };
        return Enum.IsDefined(entryOrigin);
    }

    private static bool SameOrderedIds(
        IReadOnlyList<Guid>? auditedIds,
        IReadOnlyList<Guid> canonicalIds)
    {
        return auditedIds is not null
            && auditedIds.All(id => id != Guid.Empty)
            && auditedIds.Distinct().Count() == auditedIds.Count
            && auditedIds.Order().SequenceEqual(canonicalIds);
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
