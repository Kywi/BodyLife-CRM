using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

public sealed class CreatePaperFallbackBatchRowCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<CreatePaperFallbackBatchRowCommand>
{
    private const string CommandName = "CreatePaperFallbackBatchRow";

    public async Task<CommandResult> ExecuteAsync(
        CreatePaperFallbackBatchRowCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Envelope?.Actor is null
            || !PaperFallbackCommandSupport.IsAllowedActorShape(
                command.Envelope.Actor))
        {
            return PaperFallbackCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to create a paper fallback row.");
        }

        var validation = PaperFallbackCommandSupport.ValidateAndNormalize(
            command,
            out var normalized);
        if (validation is not null)
        {
            return validation;
        }

        var row = normalized!;
        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = PaperFallbackCommandSupport.CreateFingerprint(row);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await PaperFallbackCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    row.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return await RollBackAsync(PaperFallbackCommandSupport.Error(
                    CommandErrorCode.PermissionDenied,
                    "The Owner or Admin account or session is not active."));
            }

            var existing = await PaperFallbackCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                row.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existing is not null)
            {
                return await RollBackAsync(
                    PaperFallbackCommandSupport.ReplayRowOrRejectDuplicate(
                        existing,
                        row,
                        fingerprint));
            }

            var batches = await dbContext.Set<EntryBatchRecord>()
                .FromSqlInterpolated(
                    $"""
                    select *
                    from bodylife.entry_batches
                    where id = {row.EntryBatchId}
                    for update
                    """)
                .AsNoTracking()
                .ToArrayAsync(cancellationToken);
            var parent = batches.SingleOrDefault();
            if (parent is null)
            {
                return await RollBackAsync(PaperFallbackCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Paper fallback batch was not found.",
                    "entryBatchId"));
            }

            var concurrentWinner = await PaperFallbackCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                row.Envelope.IdempotencyKey!,
                cancellationToken);
            if (concurrentWinner is not null)
            {
                return await RollBackAsync(
                    PaperFallbackCommandSupport.ReplayRowOrRejectDuplicate(
                        concurrentWinner,
                        row,
                        fingerprint));
            }

            if (parent.BatchType != "paper_fallback")
            {
                return await RollBackAsync(PaperFallbackCommandSupport.ValidationError(
                    "Entry batch is not a paper fallback batch.",
                    "entryBatchId"));
            }

            if (parent.ReconciledAt is not null)
            {
                return await RollBackAsync(PaperFallbackCommandSupport.Error(
                    CommandErrorCode.StaleState,
                    "Reconciled paper fallback batch cannot accept another row.",
                    "entryBatchId"));
            }

            var occurredBusinessDate = BusinessTimeZone.GetBusinessDate(
                row.Envelope.OccurredAt!.Value);
            if (occurredBusinessDate < parent.BusinessDateStart
                || occurredBusinessDate > parent.BusinessDateEnd)
            {
                return await RollBackAsync(PaperFallbackCommandSupport.ValidationError(
                    "Paper row occurred_at must fall inside its batch business date range.",
                    "occurredAt"));
            }

            var lineNumber = row.RequestedLineNumber
                ?? await NextLineNumberAsync(row.EntryBatchId, cancellationToken);
            if (lineNumber is null)
            {
                return await RollBackAsync(PaperFallbackCommandSupport.ValidationError(
                    "Paper sheet has no available line number.",
                    "lineNumber"));
            }

            if (await dbContext.Set<EntryBatchRowRecord>()
                .AsNoTracking()
                .AnyAsync(
                    candidate => candidate.EntryBatchId == row.EntryBatchId
                        && candidate.LineNumber == lineNumber.Value,
                    cancellationToken))
            {
                return await RollBackAsync(PaperFallbackCommandSupport.Error(
                    CommandErrorCode.DuplicateSubmission,
                    "Paper sheet line number has already been registered.",
                    "lineNumber"));
            }

            var rowId = Guid.NewGuid();
            var record = new EntryBatchRowRecord
            {
                Id = rowId,
                EntryBatchId = row.EntryBatchId,
                LineNumber = lineNumber.Value,
                EventType = PaperFallbackCommandSupport.MapEventType(row.EventType),
                OccurredAt = row.Envelope.OccurredAt.Value,
                Explanation = row.Explanation,
                RecordedAt = recordedAt,
                RecordedByAccountId = row.Envelope.Actor.AccountId.Value,
                SessionId = row.Envelope.Actor.SessionId.Value,
            };
            dbContext.Set<EntryBatchRowRecord>().Add(record);

            var auditEntryId = auditAppender.Append(
                row.Envelope,
                PaperFallbackAuditActions.RowCreated,
                PaperFallbackAuditActions.RowEntityType,
                rowId,
                recordedAt,
                relatedEntityRefs: new { row.EntryBatchId },
                afterSummary: new
                {
                    EntryBatch = new
                    {
                        parent.Id,
                        parent.PaperSheetNumber,
                        parent.BusinessDateStart,
                        parent.BusinessDateEnd,
                    },
                    EntryBatchRow = new
                    {
                        record.Id,
                        record.EntryBatchId,
                        record.LineNumber,
                        record.EventType,
                        record.OccurredAt,
                        record.Explanation,
                        record.RecordedAt,
                        record.RecordedByAccountId,
                        record.SessionId,
                    },
                });

            dbContext.Set<CommandIdempotencyRecord>().Add(
                PaperFallbackCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    row.Envelope,
                    recordedAt,
                    rowId,
                    row.EntryBatchId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PaperFallbackCommandSupport.RowSuccess(
                rowId,
                row.EntryBatchId,
                auditEntryId);
        }
        catch (Exception exception)
        {
            var postgres = PaperFallbackCommandSupport.FindPostgresException(exception);
            if (postgres is null
                || !PaperFallbackCommandSupport.TryMapPostgresFailure(
                    postgres,
                    out var result))
            {
                await PaperFallbackCommandSupport.RollBackAndClearAsync(
                    dbContext,
                    transaction);
                throw;
            }

            await PaperFallbackCommandSupport.RollBackAndClearAsync(
                dbContext,
                transaction);
            if (PaperFallbackCommandSupport.IsUniqueViolation(postgres))
            {
                var concurrentWinner = await PaperFallbackCommandSupport.FindIdempotencyAsync(
                    dbContext,
                    CommandName,
                    row.Envelope.IdempotencyKey!,
                    cancellationToken);
                if (concurrentWinner is not null)
                {
                    return PaperFallbackCommandSupport.ReplayRowOrRejectDuplicate(
                        concurrentWinner,
                        row,
                        fingerprint);
                }
            }

            return result;
        }

        async Task<CommandResult> RollBackAsync(CommandResult result)
        {
            await PaperFallbackCommandSupport.RollBackAndClearAsync(
                dbContext,
                transaction);
            return result;
        }
    }

    private async Task<int?> NextLineNumberAsync(
        Guid entryBatchId,
        CancellationToken cancellationToken)
    {
        var maximum = await dbContext.Set<EntryBatchRowRecord>()
            .Where(row => row.EntryBatchId == entryBatchId)
            .MaxAsync(row => (int?)row.LineNumber, cancellationToken)
            ?? 0;
        return maximum == int.MaxValue ? null : maximum + 1;
    }
}
