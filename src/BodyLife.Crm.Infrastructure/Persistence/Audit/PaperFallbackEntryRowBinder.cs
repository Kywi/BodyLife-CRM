using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

/// <summary>
/// Locks one registered paper sheet row within the caller's existing
/// transaction and links every canonical source fact created by that command.
/// </summary>
public sealed class PaperFallbackEntryRowBinder(BodyLifeDbContext dbContext)
{
    internal async Task<PaperFallbackEntryRowBindingResult> PrepareAsync(
        CommandEnvelope envelope,
        PaperFallbackEventType expectedEventType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.EntryOrigin != EntryOrigin.PaperFallback)
        {
            return envelope.EntryBatchRowId is null
                ? PaperFallbackEntryRowBindingResult.NotRequired()
                : PaperFallbackEntryRowBindingResult.Failed(VisitError(
                    CommandErrorCode.ValidationFailed,
                    "Entry batch row id is only valid for paper fallback entry.",
                    "entryBatchRowId"));
        }

        if (envelope.EntryBatchRowId is not { } rowId || rowId == Guid.Empty)
        {
            return PaperFallbackEntryRowBindingResult.Failed(VisitError(
                CommandErrorCode.ValidationFailed,
                "Paper fallback entry requires an entry batch row id.",
                "entryBatchRowId"));
        }

        var candidateRow = await dbContext.Set<EntryBatchRowRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == rowId, cancellationToken);
        if (candidateRow is null)
        {
            return PaperFallbackEntryRowBindingResult.Failed(VisitError(
                CommandErrorCode.NotFound,
                "Paper fallback row was not found.",
                "entryBatchRowId"));
        }

        var batches = await dbContext.Set<EntryBatchRecord>()
            .FromSqlInterpolated($"""
                select *
                from bodylife.entry_batches
                where id = {candidateRow.EntryBatchId}
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var batch = batches.SingleOrDefault();
        if (batch is null)
        {
            return PaperFallbackEntryRowBindingResult.Failed(VisitError(
                CommandErrorCode.NotFound,
                "Paper fallback row parent batch was not found.",
                "entryBatchRowId"));
        }

        if (batch.ReconciledAt is not null)
        {
            return PaperFallbackEntryRowBindingResult.Failed(VisitError(
                CommandErrorCode.StaleState,
                "Reconciled paper fallback batch cannot accept a business entry.",
                "entryBatchRowId"));
        }

        var rows = await dbContext.Set<EntryBatchRowRecord>()
            .FromSqlInterpolated($"""
                select *
                from bodylife.entry_batch_rows
                where id = {rowId}
                  and entry_batch_id = {batch.Id}
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var row = rows.SingleOrDefault();
        if (row is null)
        {
            return PaperFallbackEntryRowBindingResult.Failed(VisitError(
                CommandErrorCode.NotFound,
                "Paper fallback row was not found in its locked parent batch.",
                "entryBatchRowId"));
        }

        if (batch.BatchType != "paper_fallback"
            || string.IsNullOrWhiteSpace(batch.PaperSheetNumber)
            || batch.PaperSheetNumber != batch.PaperSheetNumber.Trim().ToUpperInvariant()
            || row.LineNumber <= 0
            || string.IsNullOrWhiteSpace(row.Explanation)
            || row.Explanation != row.Explanation.Trim()
            || row.EventType != PaperFallbackCommandSupport.MapEventType(expectedEventType)
            || envelope.OccurredAt is null
            || !SamePostgreSqlInstant(row.OccurredAt, envelope.OccurredAt.Value)
            || BusinessTimeZone.GetBusinessDate(row.OccurredAt)
                < batch.BusinessDateStart
            || BusinessTimeZone.GetBusinessDate(row.OccurredAt)
                > batch.BusinessDateEnd
            || row.RecordedByAccountId != envelope.Actor.AccountId.Value
            || row.SessionId != envelope.Actor.SessionId.Value)
        {
            return PaperFallbackEntryRowBindingResult.Failed(VisitError(
                CommandErrorCode.ValidationFailed,
                "Paper fallback row does not match this command.",
                "entryBatchRowId"));
        }

        if (await dbContext.Set<EntryBatchRowEntityRecord>()
            .AsNoTracking()
            .AnyAsync(link => link.EntryBatchRowId == row.Id, cancellationToken))
        {
            return PaperFallbackEntryRowBindingResult.Failed(VisitError(
                CommandErrorCode.DuplicateSubmission,
                "Paper fallback row is already linked to a canonical entity.",
                "entryBatchRowId"),
                rowAlreadyLinked: true);
        }

        var reference = new PaperFallbackEntryRowReference(
            batch.Id,
            row.Id,
            batch.PaperSheetNumber,
            row.LineNumber,
            expectedEventType,
            row.OccurredAt,
            row.Explanation);

        return new PaperFallbackEntryRowBindingResult(reference, null);
    }

    internal void LinkEntity(
        PaperFallbackEntryRowReference reference,
        string entityType,
        Guid entityId)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var normalizedEntityType = entityType?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEntityType))
        {
            throw new ArgumentException(
                "Paper fallback entity type is required.",
                nameof(entityType));
        }

        if (entityId == Guid.Empty)
        {
            throw new ArgumentException(
                "Paper fallback entity id is required.",
                nameof(entityId));
        }

        dbContext.Set<EntryBatchRowEntityRecord>().Add(new EntryBatchRowEntityRecord
        {
            EntryBatchRowId = reference.EntryBatchRowId,
            EntityType = normalizedEntityType,
            EntityId = entityId,
        });
    }

    private static CommandResult VisitError(
        CommandErrorCode code,
        string message,
        string field) => CommandResult.Error([new CommandError(code, message, field)]);

    private static bool SamePostgreSqlInstant(DateTimeOffset left, DateTimeOffset right) =>
        left.UtcDateTime.Ticks / 10 == right.UtcDateTime.Ticks / 10;
}

internal sealed record PaperFallbackEntryRowBindingResult(
    PaperFallbackEntryRowReference? Reference,
    CommandResult? Error,
    bool RowAlreadyLinked = false)
{
    internal static PaperFallbackEntryRowBindingResult NotRequired() =>
        new(null, null);

    internal static PaperFallbackEntryRowBindingResult Failed(
        CommandResult error,
        bool rowAlreadyLinked = false) => new(null, error, rowAlreadyLinked);
}
