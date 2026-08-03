using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Audit;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

public sealed class CreatePaperFallbackBatchCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<CreatePaperFallbackBatchCommand>
{
    private const string CommandName = "CreatePaperFallbackBatch";

    public async Task<CommandResult> ExecuteAsync(
        CreatePaperFallbackBatchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Envelope?.Actor is null
            || !PaperFallbackCommandSupport.IsAllowedActorShape(
                command.Envelope.Actor))
        {
            return PaperFallbackCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to create a paper fallback batch.");
        }

        var validation = PaperFallbackCommandSupport.ValidateAndNormalize(
            command,
            out var normalized);
        if (validation is not null)
        {
            return validation;
        }

        var batch = normalized!;
        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = PaperFallbackCommandSupport.CreateFingerprint(batch);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await PaperFallbackCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    batch.Envelope.Actor,
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
                batch.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existing is not null)
            {
                return await RollBackAsync(
                    PaperFallbackCommandSupport.ReplayBatchOrRejectDuplicate(
                        existing,
                        batch,
                        fingerprint));
            }

            var batchId = Guid.NewGuid();
            var record = new EntryBatchRecord
            {
                Id = batchId,
                BatchType = "paper_fallback",
                PaperSheetNumber = batch.PaperSheetNumber,
                BusinessDateStart = batch.BusinessDateStart,
                BusinessDateEnd = batch.BusinessDateEnd,
                RecordedAt = recordedAt,
                RecordedByAccountId = batch.Envelope.Actor.AccountId.Value,
                Note = batch.Note,
            };
            dbContext.Set<EntryBatchRecord>().Add(record);

            var auditEntryId = auditAppender.Append(
                batch.Envelope,
                PaperFallbackAuditActions.BatchCreated,
                PaperFallbackAuditActions.BatchEntityType,
                batchId,
                recordedAt,
                afterSummary: new
                {
                    EntryBatch = new
                    {
                        record.Id,
                        record.BatchType,
                        record.PaperSheetNumber,
                        record.BusinessDateStart,
                        record.BusinessDateEnd,
                        record.RecordedAt,
                        record.RecordedByAccountId,
                        record.ReconciledAt,
                        record.ReconciledByAccountId,
                        record.Note,
                    },
                });

            dbContext.Set<CommandIdempotencyRecord>().Add(
                PaperFallbackCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    batch.Envelope,
                    recordedAt,
                    batchId,
                    batchId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PaperFallbackCommandSupport.BatchSuccess(batchId, auditEntryId);
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
                    batch.Envelope.IdempotencyKey!,
                    cancellationToken);
                if (concurrentWinner is not null)
                {
                    return PaperFallbackCommandSupport.ReplayBatchOrRejectDuplicate(
                        concurrentWinner,
                        batch,
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
}
