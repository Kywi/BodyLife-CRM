using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

public sealed class AddFreezeCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    MembershipFreezeEligibilityPreparer eligibilityPreparer,
    IMembershipStateRecalculator membershipStateRecalculator,
    IBodyLifeQueryHandler<GetMembershipStateQuery, GetMembershipStateResult>
        membershipStateQueryHandler,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<AddFreezeCommand>
{
    private const string CommandName = "AddFreeze";

    public async Task<CommandResult> ExecuteAsync(
        AddFreezeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !FreezeCommandSupport.IsAllowedActorShape(command.Envelope.Actor))
        {
            return FreezeCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to add a Freeze.");
        }

        var validation = FreezeCommandSupport.ValidateAndNormalize(
            command,
            out var normalizedFreeze);
        if (validation is not null)
        {
            return validation;
        }

        var freeze = normalizedFreeze!;
        var recordedAt = timeProvider.GetUtcNow();
        var currentDate = DateOnly.FromDateTime(recordedAt.UtcDateTime);
        var fingerprint = FreezeCommandSupport.CreateFingerprint(freeze);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await FreezeCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    freeze.Envelope.Actor,
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
                freeze.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(ReplayOrRejectDuplicate(
                    existingIdempotency,
                    freeze,
                    fingerprint));
            }

            MembershipFreezeEligibilityPreparationResult eligibilityPreparation;
            try
            {
                eligibilityPreparation = await eligibilityPreparer.PrepareAsync(
                    freeze.ClientId,
                    freeze.MembershipId,
                    freeze.Range,
                    cancellationToken);
            }
            catch (ArgumentException exception)
                when (FreezeCommandSupport.FindPostgresException(exception) is null)
            {
                return await RollBackAsync(RecalculationFailed());
            }
            catch (InvalidOperationException exception)
                when (FreezeCommandSupport.FindPostgresException(exception) is null)
            {
                return await RollBackAsync(RecalculationFailed());
            }

            existingIdempotency = await FreezeCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                freeze.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(ReplayOrRejectDuplicate(
                    existingIdempotency,
                    freeze,
                    fingerprint));
            }

            if (!eligibilityPreparation.IsPrepared
                || eligibilityPreparation.Eligibility is null)
            {
                return await RollBackAsync(FreezeCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Selected Membership was not found for the Client.",
                    "membershipId"));
            }

            var eligibility = eligibilityPreparation.Eligibility;
            if (!eligibility.IsEligible)
            {
                var error = eligibility.Status
                    == MembershipFreezeEligibilityStatus.ConflictsWithActiveVisit
                        ? FreezeCommandSupport.Error(
                            CommandErrorCode.FreezeConflictsWithVisit,
                            "Freeze range overlaps an active counted Membership Visit.",
                            "range")
                        : FreezeCommandSupport.Error(
                            CommandErrorCode.MembershipNotEligible,
                            "Selected Membership is not eligible for the Freeze range.",
                            "range");
                return await RollBackAsync(error);
            }

            var beforeStateResult = await ReadMembershipStateAsync(
                freeze,
                currentDate,
                cancellationToken);
            if (beforeStateResult.State is null)
            {
                return await RollBackAsync(RecalculationFailed());
            }

            var beforeState = beforeStateResult.State;
            var freezeId = Guid.NewGuid();
            var freezeRecord = new FreezeRecord
            {
                Id = freezeId,
                ClientId = freeze.ClientId,
                MembershipId = freeze.MembershipId,
                StartDate = freeze.Range.StartDate,
                EndDate = freeze.Range.EndDate,
                Reason = freeze.Envelope.Reason!,
                OccurredAt = freeze.Envelope.OccurredAt!.Value,
                RecordedAt = recordedAt,
                RecordedByAccountId = freeze.Envelope.Actor.AccountId.Value,
                SessionId = freeze.Envelope.Actor.SessionId.Value,
                EntryOrigin = FreezeCommandSupport.MapEntryOrigin(
                    freeze.Envelope.EntryOrigin),
                EntryBatchId = freeze.EntryBatchId,
                Status = "active",
            };
            dbContext.Set<FreezeRecord>().Add(freezeRecord);
            await dbContext.SaveChangesAsync(cancellationToken);

            var recalculation = await membershipStateRecalculator.RecalculateAsync(
                freeze.MembershipId,
                cancellationToken);
            if (!recalculation.Succeeded)
            {
                return await RollBackAsync(RecalculationFailed());
            }

            var afterStateResult = await ReadMembershipStateAsync(
                freeze,
                currentDate,
                cancellationToken);
            if (afterStateResult.State is null)
            {
                return await RollBackAsync(RecalculationFailed());
            }

            var afterState = afterStateResult.State;
            var auditEntryId = auditAppender.Append(
                freeze.Envelope,
                FreezeAuditActions.Added,
                FreezeAuditActions.FreezeEntityType,
                freezeId,
                recordedAt,
                relatedEntityRefs: new
                {
                    freeze.ClientId,
                    freeze.MembershipId,
                },
                beforeSummary: new
                {
                    MembershipState = Summarize(beforeState),
                },
                afterSummary: new
                {
                    Freeze = new
                    {
                        FreezeId = freezeId,
                        freeze.ClientId,
                        freeze.MembershipId,
                        freezeRecord.StartDate,
                        freezeRecord.EndDate,
                        freeze.Range.InclusiveDays,
                        freezeRecord.Reason,
                        freezeRecord.OccurredAt,
                        freezeRecord.RecordedAt,
                        freezeRecord.EntryOrigin,
                        freezeRecord.EntryBatchId,
                        freezeRecord.Status,
                    },
                    MembershipState = Summarize(afterState),
                });

            dbContext.Set<CommandIdempotencyRecord>().Add(
                FreezeCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    freeze,
                    recordedAt,
                    freezeId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return FreezeCommandSupport.Success(
                freezeId,
                freeze,
                auditEntryId);
        }
        catch (Exception exception)
        {
            var postgresException = FreezeCommandSupport.FindPostgresException(exception);
            if (postgresException is not null
                && FreezeCommandSupport.TryMapPostgresFailure(
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
        NormalizedAddFreeze freeze,
        DateOnly currentDate,
        CancellationToken cancellationToken)
    {
        return membershipStateQueryHandler.ExecuteAsync(
            new GetMembershipStateQuery(
                freeze.Envelope.Actor,
                freeze.MembershipId,
                currentDate),
            cancellationToken);
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

    private static CommandResult ReplayOrRejectDuplicate(
        CommandIdempotencyRecord record,
        NormalizedAddFreeze freeze,
        string fingerprint)
    {
        return FreezeCommandSupport.TryGetSuccessfulReplay(
            record,
            freeze,
            fingerprint,
            out var freezeId,
            out var auditEntryId)
                ? FreezeCommandSupport.Success(freezeId, freeze, auditEntryId)
                : FreezeCommandSupport.DuplicateSubmission();
    }

    private static CommandResult RecalculationFailed()
    {
        return FreezeCommandSupport.Error(
            CommandErrorCode.RecalculationFailed,
            "Membership state could not be recalculated from canonical Freeze sources.");
    }
}
