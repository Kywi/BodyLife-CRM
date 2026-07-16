using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

public sealed class CancelFreezeCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    CancelFreezeSourcePreparer sourcePreparer,
    IMembershipStateRecalculator membershipStateRecalculator,
    IBodyLifeQueryHandler<GetMembershipStateQuery, GetMembershipStateResult>
        membershipStateQueryHandler,
    IFreezeDayReconciliationStatusProvider dayReconciliationStatusProvider,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<CancelFreezeCommand>
{
    private const string CommandName = "CancelFreeze";

    public async Task<CommandResult> ExecuteAsync(
        CancelFreezeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !FreezeCommandSupport.IsAllowedActorShape(command.Envelope.Actor))
        {
            return FreezeCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to cancel a Freeze.");
        }

        var validation = FreezeCommandSupport.ValidateAndNormalize(
            command,
            out var normalizedCancellation);
        if (validation is not null)
        {
            return validation;
        }

        var cancellation = normalizedCancellation!;
        var recordedAt = timeProvider.GetUtcNow();
        var currentDate = DateOnly.FromDateTime(recordedAt.UtcDateTime);
        var fingerprint = FreezeCommandSupport.CreateFingerprint(cancellation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await FreezeCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    cancellation.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return await RollBackAsync(FreezeCommandSupport.Error(
                    CommandErrorCode.PermissionDenied,
                    "The Owner or Admin account or session is not active."));
            }

            var existingIdempotency = await FreezeCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                cancellation.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(await ReplayOrRejectDuplicateAsync(
                    existingIdempotency,
                    cancellation,
                    fingerprint,
                    cancellationToken));
            }

            var sourceResult = await sourcePreparer.PrepareAsync(
                cancellation.FreezeId,
                cancellationToken);

            existingIdempotency = await FreezeCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                cancellation.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(await ReplayOrRejectDuplicateAsync(
                    existingIdempotency,
                    cancellation,
                    fingerprint,
                    cancellationToken));
            }

            if (!sourceResult.IsPrepared || sourceResult.Source is null)
            {
                return await RollBackAsync(MapSourcePreparationFailure(sourceResult));
            }

            var source = sourceResult.Source;
            var businessDate = DateOnly.FromDateTime(source.OccurredAt.UtcDateTime);
            var dayStatus = await dayReconciliationStatusProvider.GetStatusAsync(
                businessDate,
                cancellationToken);
            if (!Enum.IsDefined(dayStatus))
            {
                throw new InvalidOperationException(
                    $"Freeze day reconciliation status '{dayStatus}' is not supported.");
            }

            var changedAfterClose = dayStatus
                == FreezeDayReconciliationStatus.Reconciled;
            if (changedAfterClose
                && cancellation.Envelope.Actor.Role != ActorRole.Owner)
            {
                return await RollBackAsync(FreezeCommandSupport.Error(
                    CommandErrorCode.DayClosedRequiresOwner,
                    "Only the Owner can cancel a Freeze from a reconciled day.",
                    "freezeId"));
            }

            var beforeRecalculation = await membershipStateRecalculator
                .RecalculateAsync(source.MembershipId, cancellationToken);
            if (!beforeRecalculation.Succeeded)
            {
                return await RollBackAsync(RecalculationFailed());
            }

            var beforeStateResult = await ReadMembershipStateAsync(
                cancellation.Envelope.Actor,
                source.MembershipId,
                currentDate,
                cancellationToken);
            if (beforeStateResult.State is null)
            {
                return await RollBackAsync(MapMembershipStateReadFailure(
                    beforeStateResult));
            }

            var beforeState = beforeStateResult.State;
            var updatedFreezeCount = await dbContext.Set<FreezeRecord>()
                .Where(freeze => freeze.Id == source.FreezeId
                    && freeze.ClientId == source.ClientId
                    && freeze.MembershipId == source.MembershipId
                    && freeze.Status == "active")
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        freeze => freeze.Status,
                        "canceled"),
                    cancellationToken);
            if (updatedFreezeCount != 1)
            {
                return await RollBackAsync(SourceChangedConcurrently());
            }

            var cancellationId = Guid.NewGuid();
            var cancellationRecord = new FreezeCancellationRecord
            {
                Id = cancellationId,
                FreezeId = source.FreezeId,
                Reason = cancellation.Envelope.Reason!,
                OccurredAt = cancellation.Envelope.OccurredAt!.Value,
                RecordedAt = recordedAt,
                RecordedByAccountId = cancellation.Envelope.Actor.AccountId.Value,
                SessionId = cancellation.Envelope.Actor.SessionId.Value,
                EntryOrigin = FreezeCommandSupport.MapEntryOrigin(
                    cancellation.Envelope.EntryOrigin),
                EntryBatchId = cancellation.EntryBatchId,
            };
            dbContext.Set<FreezeCancellationRecord>().Add(cancellationRecord);
            await dbContext.SaveChangesAsync(cancellationToken);

            var afterRecalculation = await membershipStateRecalculator
                .RecalculateAsync(source.MembershipId, cancellationToken);
            if (!afterRecalculation.Succeeded)
            {
                return await RollBackAsync(RecalculationFailed());
            }

            var afterStateResult = await ReadMembershipStateAsync(
                cancellation.Envelope.Actor,
                source.MembershipId,
                currentDate,
                cancellationToken);
            if (afterStateResult.State is null)
            {
                return await RollBackAsync(MapMembershipStateReadFailure(
                    afterStateResult));
            }

            var afterState = afterStateResult.State;
            var auditEntryId = auditAppender.Append(
                cancellation.Envelope,
                FreezeAuditActions.Canceled,
                FreezeAuditActions.FreezeEntityType,
                source.FreezeId,
                recordedAt,
                relatedEntityRefs: new
                {
                    source.ClientId,
                    source.MembershipId,
                    CancellationId = cancellationId,
                },
                beforeSummary: new
                {
                    Freeze = SummarizeSource(source),
                    MembershipState = Summarize(beforeState),
                },
                afterSummary: new
                {
                    Cancellation = new
                    {
                        CancellationId = cancellationId,
                        source.FreezeId,
                        cancellationRecord.Reason,
                        cancellationRecord.OccurredAt,
                        cancellationRecord.RecordedAt,
                        cancellationRecord.EntryOrigin,
                        cancellationRecord.EntryBatchId,
                        ChangedAfterClose = changedAfterClose,
                    },
                    Freeze = new
                    {
                        source.FreezeId,
                        source.ClientId,
                        source.MembershipId,
                        source.Range.StartDate,
                        source.Range.EndDate,
                        source.Range.InclusiveDays,
                        source.Reason,
                        Status = "canceled",
                    },
                    MembershipState = Summarize(afterState),
                },
                changedAfterClose);

            dbContext.Set<CommandIdempotencyRecord>().Add(
                FreezeCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    cancellation,
                    recordedAt,
                    cancellationId,
                    source.ClientId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return FreezeCommandSupport.CancelFreezeSuccess(
                cancellationId,
                source.FreezeId,
                source.ClientId,
                auditEntryId,
                changedAfterClose);
        }
        catch (Exception exception)
        {
            var postgresException = FreezeCommandSupport.FindPostgresException(exception);
            if (postgresException is not null
                && FreezeCommandSupport.TryMapCancelFreezePostgresFailure(
                    postgresException,
                    out var errorResult))
            {
                return await RollBackAsync(errorResult);
            }

            await FreezeCommandSupport.RollBackAndClearAsync(dbContext, transaction);
            throw;
        }

        async Task<CommandResult> RollBackAsync(CommandResult result)
        {
            return await FreezeCommandSupport.RollBackAndReturnAsync(
                dbContext,
                transaction,
                result);
        }
    }

    private Task<GetMembershipStateResult> ReadMembershipStateAsync(
        ActorContext actor,
        Guid membershipId,
        DateOnly currentDate,
        CancellationToken cancellationToken)
    {
        return membershipStateQueryHandler.ExecuteAsync(
            new GetMembershipStateQuery(actor, membershipId, currentDate),
            cancellationToken);
    }

    private async Task<CommandResult> ReplayOrRejectDuplicateAsync(
        CommandIdempotencyRecord record,
        NormalizedCancelFreeze cancellation,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (!FreezeCommandSupport.TryGetSuccessfulReplay(
                record,
                cancellation,
                fingerprint,
                out var cancellationId,
                out var clientId,
                out var auditEntryId))
        {
            return FreezeCommandSupport.CancelFreezeDuplicateSubmission();
        }

        var audit = await dbContext.Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entry => entry.Id == auditEntryId.Value
                    && entry.ActionType == FreezeAuditActions.Canceled
                    && entry.EntityType == FreezeAuditActions.FreezeEntityType
                    && entry.EntityId == cancellation.FreezeId,
                cancellationToken);
        if (audit is null)
        {
            return FreezeCommandSupport.CancelFreezeDuplicateSubmission();
        }

        return FreezeCommandSupport.CancelFreezeSuccess(
            cancellationId,
            cancellation.FreezeId,
            clientId,
            auditEntryId,
            audit.ChangedAfterClose);
    }

    private static CommandResult MapSourcePreparationFailure(
        CancelFreezeSourcePreparationResult result)
    {
        return result.Status switch
        {
            CancelFreezeSourcePreparationStatus.NotFound => FreezeCommandSupport.Error(
                CommandErrorCode.NotFound,
                "Freeze was not found.",
                "freezeId"),
            CancelFreezeSourcePreparationStatus.AlreadyCanceled =>
                FreezeCommandSupport.AlreadyCanceled(),
            CancelFreezeSourcePreparationStatus.InconsistentSource =>
                SourceChangedConcurrently(),
            _ => SourceChangedConcurrently(),
        };
    }

    private static CommandResult MapMembershipStateReadFailure(
        GetMembershipStateResult result)
    {
        return result.Status switch
        {
            GetMembershipStateStatus.PermissionDenied => FreezeCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                result.ErrorMessage ?? "Membership state access was denied."),
            GetMembershipStateStatus.NotFound => FreezeCommandSupport.Error(
                CommandErrorCode.NotFound,
                result.ErrorMessage ?? "Membership was not found.",
                result.ErrorField),
            _ => RecalculationFailed(),
        };
    }

    private static CommandResult RecalculationFailed()
    {
        return FreezeCommandSupport.Error(
            CommandErrorCode.RecalculationFailed,
            "Membership state could not be rebuilt after Freeze cancellation.");
    }

    private static CommandResult SourceChangedConcurrently()
    {
        return FreezeCommandSupport.Error(
            CommandErrorCode.ConcurrencyConflict,
            "Freeze cancellation source changed. Refresh canonical state and try again.");
    }

    private static object SummarizeSource(FreezeCancellationSource source)
    {
        return new
        {
            source.FreezeId,
            source.ClientId,
            source.MembershipId,
            source.Range.StartDate,
            source.Range.EndDate,
            source.Range.InclusiveDays,
            source.Reason,
            source.OccurredAt,
            source.RecordedAt,
            EntryOrigin = FreezeCommandSupport.MapEntryOrigin(source.EntryOrigin),
            source.EntryBatchId,
            Status = "active",
        };
    }

    private static object Summarize(MembershipStateReadModel state)
    {
        return new
        {
            state.MembershipId,
            state.ClientId,
            state.RemainingVisits,
            state.NegativeBalance,
            state.ExtensionDays,
            state.EffectiveEndDate,
            Warnings = state.Warnings.Select(warning => warning.Code).ToArray(),
        };
    }
}
