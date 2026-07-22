using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

public sealed class CancelVisitCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    CancelVisitSourcePreparer sourcePreparer,
    IMembershipStateRecalculator membershipStateRecalculator,
    IBodyLifeQueryHandler<GetMembershipStateQuery, GetMembershipStateResult>
        membershipStateQueryHandler,
    IVisitDayReconciliationStatusProvider dayReconciliationStatusProvider,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<CancelVisitCommand>
{
    private const string CommandName = "CancelVisit";

    public async Task<CommandResult> ExecuteAsync(
        CancelVisitCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !VisitCommandSupport.IsAllowedActorShape(command.Envelope.Actor))
        {
            return VisitCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to cancel a Visit.");
        }

        var validation = VisitCommandSupport.ValidateAndNormalize(
            command,
            out var normalizedCancellation);
        if (validation is not null)
        {
            return validation;
        }

        var cancellation = normalizedCancellation!;
        var recordedAt = timeProvider.GetUtcNow();
        var currentDate = BusinessTimeZone.GetBusinessDate(recordedAt);
        var fingerprint = VisitCommandSupport.CreateFingerprint(cancellation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await VisitCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    cancellation.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return await RollBackAsync(VisitCommandSupport.Error(
                    CommandErrorCode.PermissionDenied,
                    "The Owner or Admin account or session is not active."));
            }

            var existingIdempotency = await VisitCommandSupport.FindIdempotencyAsync(
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
                cancellation.VisitId,
                cancellationToken);

            existingIdempotency = await VisitCommandSupport.FindIdempotencyAsync(
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
            var businessDate = BusinessTimeZone.GetBusinessDate(source.OccurredAt);
            var dayStatus = await dayReconciliationStatusProvider.GetStatusAsync(
                businessDate,
                cancellationToken);
            if (!Enum.IsDefined(dayStatus))
            {
                throw new InvalidOperationException(
                    $"Visit day reconciliation status '{dayStatus}' is not supported.");
            }

            var changedAfterClose = dayStatus
                == VisitDayReconciliationStatus.Reconciled;
            if (changedAfterClose
                && cancellation.Envelope.Actor.Role != ActorRole.Owner)
            {
                return await RollBackAsync(VisitCommandSupport.Error(
                    CommandErrorCode.DayClosedRequiresOwner,
                    "Only the Owner can cancel a Visit from a reconciled day.",
                    "visitId"));
            }

            CancelVisitPreparation preparation;
            try
            {
                preparation = CancelVisitPreparationPolicy.Prepare(
                    new CancelVisitCommand(
                        cancellation.Envelope,
                        cancellation.VisitId,
                        cancellation.EntryBatchId),
                    source,
                    changedAfterClose);
            }
            catch (ArgumentException exception)
            {
                return await RollBackAsync(VisitCommandSupport.ValidationError(
                    exception.Message,
                    exception.ParamName));
            }
            catch (InvalidOperationException)
            {
                return await RollBackAsync(SourceChangedConcurrently());
            }

            MembershipStateReadModel? beforeMembershipState = null;
            if (preparation.RequiresMembershipRecalculation)
            {
                var beforeRecalculation = await membershipStateRecalculator
                    .RecalculateAsync(source.MembershipId!.Value, cancellationToken);
                if (!beforeRecalculation.Succeeded)
                {
                    return await RollBackAsync(RecalculationFailed());
                }

                var beforeStateResult = await ReadMembershipStateAsync(
                    cancellation.Envelope.Actor,
                    source.MembershipId.Value,
                    currentDate,
                    cancellationToken);
                if (beforeStateResult.State is null)
                {
                    return await RollBackAsync(MapMembershipStateReadFailure(
                        beforeStateResult));
                }

                beforeMembershipState = beforeStateResult.State;
            }

            var updatedVisitCount = await dbContext.Set<VisitRecord>()
                .Where(visit => visit.Id == source.VisitId
                    && visit.Status == "active")
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        visit => visit.Status,
                        "canceled"),
                    cancellationToken);
            if (updatedVisitCount != 1)
            {
                return await RollBackAsync(SourceChangedConcurrently());
            }

            if (source.ActiveConsumptionId is { } consumptionId)
            {
                var updatedConsumptionCount = await dbContext
                    .Set<VisitConsumptionRecord>()
                    .Where(consumption => consumption.Id == consumptionId
                        && consumption.VisitId == source.VisitId
                        && consumption.Status == "active")
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            consumption => consumption.Status,
                            "canceled"),
                        cancellationToken);
                if (updatedConsumptionCount != 1)
                {
                    return await RollBackAsync(SourceChangedConcurrently());
                }
            }

            var cancellationId = Guid.NewGuid();
            var cancellationRecord = new VisitCancellationRecord
            {
                Id = cancellationId,
                VisitId = source.VisitId,
                Reason = preparation.Envelope.Reason!,
                OccurredAt = preparation.Envelope.OccurredAt!.Value,
                RecordedAt = recordedAt,
                RecordedByAccountId = preparation.Envelope.Actor.AccountId.Value,
                SessionId = preparation.Envelope.Actor.SessionId.Value,
                EntryOrigin = VisitCommandSupport.MapEntryOrigin(
                    preparation.Envelope.EntryOrigin),
                EntryBatchId = preparation.EntryBatchId,
            };
            dbContext.Set<VisitCancellationRecord>().Add(cancellationRecord);
            await dbContext.SaveChangesAsync(cancellationToken);

            MembershipStateReadModel? afterMembershipState = null;
            if (preparation.RequiresMembershipRecalculation)
            {
                var afterRecalculation = await membershipStateRecalculator
                    .RecalculateAsync(source.MembershipId!.Value, cancellationToken);
                if (!afterRecalculation.Succeeded)
                {
                    return await RollBackAsync(RecalculationFailed());
                }

                var afterStateResult = await ReadMembershipStateAsync(
                    cancellation.Envelope.Actor,
                    source.MembershipId.Value,
                    currentDate,
                    cancellationToken);
                if (afterStateResult.State is null)
                {
                    return await RollBackAsync(MapMembershipStateReadFailure(
                        afterStateResult));
                }

                afterMembershipState = afterStateResult.State;
            }

            var auditEntryId = auditAppender.Append(
                preparation.Envelope,
                VisitAuditActions.Canceled,
                VisitAuditActions.VisitEntityType,
                source.VisitId,
                recordedAt,
                relatedEntityRefs: new
                {
                    source.ClientId,
                    source.MembershipId,
                    source.ActiveConsumptionId,
                    CancellationId = cancellationId,
                },
                beforeSummary: new
                {
                    Visit = SummarizeSource(source),
                    MembershipState = beforeMembershipState is null
                        ? null
                        : Summarize(beforeMembershipState),
                },
                afterSummary: new
                {
                    Cancellation = new
                    {
                        CancellationId = cancellationId,
                        source.VisitId,
                        cancellationRecord.Reason,
                        cancellationRecord.OccurredAt,
                        cancellationRecord.RecordedAt,
                        cancellationRecord.EntryOrigin,
                        cancellationRecord.EntryBatchId,
                        preparation.ChangedAfterClose,
                    },
                    Visit = new
                    {
                        source.VisitId,
                        Status = "canceled",
                        ConsumptionId = source.ActiveConsumptionId,
                        ConsumptionStatus = source.ActiveConsumptionId is null
                            ? null
                            : "canceled",
                    },
                    MembershipState = afterMembershipState is null
                        ? null
                        : Summarize(afterMembershipState),
                },
                preparation.ChangedAfterClose);

            dbContext.Set<CommandIdempotencyRecord>().Add(
                VisitCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    cancellation,
                    recordedAt,
                    cancellationId,
                    source.ClientId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return VisitCommandSupport.CancelVisitSuccess(
                cancellationId,
                source.VisitId,
                source.ClientId,
                auditEntryId,
                preparation.ChangedAfterClose);
        }
        catch (Exception exception)
        {
            var postgresException = VisitCommandSupport.FindPostgresException(exception);
            if (postgresException is not null
                && VisitCommandSupport.TryMapCancelVisitPostgresFailure(
                    postgresException,
                    out var errorResult))
            {
                return await RollBackAsync(errorResult);
            }

            await VisitCommandSupport.RollBackAndClearAsync(dbContext, transaction);
            throw;
        }

        async Task<CommandResult> RollBackAsync(CommandResult result)
        {
            return await VisitCommandSupport.RollBackAndReturnAsync(
                dbContext,
                transaction,
                result);
        }
    }

    private Task<GetMembershipStateResult> ReadMembershipStateAsync(
        ActorContext actor,
        Guid membershipId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        return membershipStateQueryHandler.ExecuteAsync(
            new GetMembershipStateQuery(actor, membershipId, asOfDate),
            cancellationToken);
    }

    private async Task<CommandResult> ReplayOrRejectDuplicateAsync(
        CommandIdempotencyRecord record,
        NormalizedCancelVisit cancellation,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (!VisitCommandSupport.TryGetSuccessfulReplay(
                record,
                cancellation,
                fingerprint,
                out var cancellationId,
                out var clientId,
                out var auditEntryId))
        {
            return VisitCommandSupport.CancelVisitDuplicateSubmission();
        }

        var audit = await dbContext.Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entry => entry.Id == auditEntryId.Value
                    && entry.ActionType == VisitAuditActions.Canceled
                    && entry.EntityType == VisitAuditActions.VisitEntityType
                    && entry.EntityId == cancellation.VisitId,
                cancellationToken);
        if (audit is null)
        {
            return VisitCommandSupport.CancelVisitDuplicateSubmission();
        }

        return VisitCommandSupport.CancelVisitSuccess(
            cancellationId,
            cancellation.VisitId,
            clientId,
            auditEntryId,
            audit.ChangedAfterClose);
    }

    private static CommandResult MapSourcePreparationFailure(
        CancelVisitSourcePreparationResult result)
    {
        return result.Status switch
        {
            CancelVisitSourcePreparationStatus.NotFound => VisitCommandSupport.Error(
                CommandErrorCode.NotFound,
                "Visit was not found.",
                "visitId"),
            CancelVisitSourcePreparationStatus.AlreadyCanceled =>
                VisitCommandSupport.Error(
                    CommandErrorCode.AlreadyCanceled,
                    "Visit has already been canceled.",
                    "visitId"),
            CancelVisitSourcePreparationStatus.InconsistentSource =>
                SourceChangedConcurrently(),
            _ => SourceChangedConcurrently(),
        };
    }

    private static CommandResult MapMembershipStateReadFailure(
        GetMembershipStateResult result)
    {
        return result.Status switch
        {
            GetMembershipStateStatus.PermissionDenied => VisitCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                result.ErrorMessage ?? "Membership state access was denied."),
            GetMembershipStateStatus.NotFound => VisitCommandSupport.Error(
                CommandErrorCode.NotFound,
                result.ErrorMessage ?? "Membership was not found.",
                result.ErrorField),
            _ => RecalculationFailed(),
        };
    }

    private static CommandResult RecalculationFailed()
    {
        return VisitCommandSupport.Error(
            CommandErrorCode.RecalculationFailed,
            "Membership state could not be rebuilt after Visit cancellation.");
    }

    private static CommandResult SourceChangedConcurrently()
    {
        return VisitCommandSupport.Error(
            CommandErrorCode.ConcurrencyConflict,
            "Visit cancellation source changed. Refresh canonical state and try again.");
    }

    private static object SummarizeSource(VisitCancellationSource source)
    {
        return new
        {
            source.VisitId,
            source.ClientId,
            VisitKind = VisitCommandSupport.MapVisitKind(source.VisitKind),
            source.MembershipId,
            ConsumptionId = source.ActiveConsumptionId,
            source.OccurredAt,
            source.RecordedAt,
            EntryOrigin = VisitCommandSupport.MapEntryOrigin(source.EntryOrigin),
            source.EntryBatchId,
            source.Comment,
            Status = "active",
            ConsumptionStatus = source.ActiveConsumptionId is null
                ? null
                : "active",
        };
    }

    private static MembershipStateAuditSummary Summarize(
        MembershipStateReadModel state)
    {
        return new MembershipStateAuditSummary(
            state.MembershipId,
            state.CountedVisits,
            state.RemainingVisits,
            state.NegativeBalance,
            state.FirstNegativeVisitId,
            state.FirstNegativeVisitDate,
            state.ExtensionDays,
            state.EffectiveEndDate,
            state.LastCountedVisitAt,
            state.Warnings.Select(warning => warning.Code).ToArray());
    }

    private sealed record MembershipStateAuditSummary(
        Guid MembershipId,
        int CountedVisits,
        int RemainingVisits,
        int NegativeBalance,
        Guid? FirstNegativeVisitId,
        DateOnly? FirstNegativeVisitDate,
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        DateTimeOffset? LastCountedVisitAt,
        IReadOnlyList<string> Warnings);
}
