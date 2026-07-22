using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

public sealed class MarkVisitCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    MembershipVisitEligibilityPreparer eligibilityPreparer,
    IMembershipStateRecalculator membershipStateRecalculator,
    IBodyLifeQueryHandler<GetMembershipStateQuery, GetMembershipStateResult>
        membershipStateQueryHandler,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<MarkVisitCommand>
{
    private const string CommandName = "MarkVisit";

    public async Task<CommandResult> ExecuteAsync(
        MarkVisitCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !VisitCommandSupport.IsAllowedActorShape(command.Envelope.Actor))
        {
            return VisitCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to mark a Visit.");
        }

        var validation = VisitCommandSupport.ValidateAndNormalize(
            command,
            out var normalizedVisit);
        if (validation is not null)
        {
            return validation;
        }

        var visit = normalizedVisit!;
        var recordedAt = timeProvider.GetUtcNow();
        var currentDate = BusinessTimeZone.GetBusinessDate(recordedAt);
        var fingerprint = VisitCommandSupport.CreateFingerprint(visit);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await VisitCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    visit.Envelope.Actor,
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
                visit.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                var replay = await ReplayOrRejectDuplicateAsync(
                    existingIdempotency,
                    visit,
                    fingerprint,
                    currentDate,
                    cancellationToken);
                return await RollBackAsync(replay);
            }

            var client = await LockClientAsync(visit.ClientId, cancellationToken);
            if (client is null)
            {
                return await RollBackAsync(VisitCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Client was not found.",
                    "clientId"));
            }

            existingIdempotency = await VisitCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                visit.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                var replay = await ReplayOrRejectDuplicateAsync(
                    existingIdempotency,
                    visit,
                    fingerprint,
                    currentDate,
                    cancellationToken);
                return await RollBackAsync(replay);
            }

            MarkVisitPreparation preparation;
            MembershipStateReadModel? beforeMembershipState = null;

            if (visit.VisitKind == VisitKind.Membership)
            {
                MembershipVisitEligibilityPreparationResult eligibilityPreparation;
                try
                {
                    eligibilityPreparation = await eligibilityPreparer.PrepareAsync(
                        visit.ClientId,
                        visit.MembershipId!.Value,
                        visit.Envelope.OccurredAt!.Value,
                        cancellationToken);
                }
                catch (ArgumentException exception)
                    when (VisitCommandSupport.FindPostgresException(exception) is null)
                {
                    return await RollBackAsync(RecalculationFailed());
                }
                catch (InvalidOperationException exception)
                    when (VisitCommandSupport.FindPostgresException(exception) is null)
                {
                    return await RollBackAsync(RecalculationFailed());
                }

                if (!eligibilityPreparation.IsPrepared
                    || eligibilityPreparation.Eligibility is null)
                {
                    return await RollBackAsync(VisitCommandSupport.Error(
                        CommandErrorCode.NotFound,
                        "Selected Membership was not found for the Client.",
                        "membershipId"));
                }

                var eligibility = eligibilityPreparation.Eligibility;
                if (!eligibility.IsEligible)
                {
                    var error = eligibility.Status
                        == MembershipVisitEligibilityStatus.DuringActiveFreeze
                            ? VisitCommandSupport.Error(
                                CommandErrorCode.VisitDuringFreeze,
                                "Membership Visit is blocked by an active Freeze on the Visit date.",
                                "membershipId")
                            : VisitCommandSupport.Error(
                                CommandErrorCode.MembershipNotEligible,
                                "Selected Membership is not eligible for the Visit date.",
                                "membershipId");
                    return await RollBackAsync(error);
                }

                var preparationResult = TryPrepare(visit, eligibility, out preparation);
                if (preparationResult is not null)
                {
                    return await RollBackAsync(preparationResult);
                }

                var beforeStateResult = await ReadMembershipStateAsync(
                    visit,
                    currentDate,
                    cancellationToken);
                if (beforeStateResult.State is null)
                {
                    return await RollBackAsync(MapMembershipStateReadFailure(
                        beforeStateResult));
                }

                beforeMembershipState = beforeStateResult.State;
            }
            else
            {
                var preparationResult = TryPrepare(
                    visit,
                    membershipEligibility: null,
                    out preparation);
                if (preparationResult is not null)
                {
                    return await RollBackAsync(preparationResult);
                }
            }

            var visitId = Guid.NewGuid();
            var visitRecord = new VisitRecord
            {
                Id = visitId,
                ClientId = visit.ClientId,
                OccurredAt = visit.Envelope.OccurredAt!.Value,
                RecordedAt = recordedAt,
                RecordedByAccountId = visit.Envelope.Actor.AccountId.Value,
                SessionId = visit.Envelope.Actor.SessionId.Value,
                VisitKind = VisitCommandSupport.MapVisitKind(visit.VisitKind),
                EntryOrigin = VisitCommandSupport.MapEntryOrigin(
                    visit.Envelope.EntryOrigin),
                EntryBatchId = visit.EntryBatchId,
                Comment = visit.Envelope.Comment,
                Status = "active",
            };
            dbContext.Set<VisitRecord>().Add(visitRecord);

            VisitConsumptionRecord? consumptionRecord = null;
            if (preparation.CreatesMembershipConsumption)
            {
                consumptionRecord = new VisitConsumptionRecord
                {
                    Id = Guid.NewGuid(),
                    VisitId = visitId,
                    ClientId = visit.ClientId,
                    VisitKind = "membership",
                    MembershipId = visit.MembershipId!.Value,
                    ConsumptionType = "counted",
                    SourceFactType = "visit",
                    SourceFactId = visitId,
                    RecordedAt = recordedAt,
                    RecordedByAccountId = visit.Envelope.Actor.AccountId.Value,
                    RecordedSessionId = visit.Envelope.Actor.SessionId.Value,
                    Status = "active",
                };
                dbContext.Set<VisitConsumptionRecord>().Add(consumptionRecord);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            MembershipStateReadModel? afterMembershipState = null;
            if (preparation.RequiresMembershipRecalculation)
            {
                var recalculation = await membershipStateRecalculator.RecalculateAsync(
                    visit.MembershipId!.Value,
                    cancellationToken);
                if (!recalculation.Succeeded)
                {
                    return await RollBackAsync(RecalculationFailed());
                }

                var afterStateResult = await ReadMembershipStateAsync(
                    visit,
                    currentDate,
                    cancellationToken);
                if (afterStateResult.State is null)
                {
                    return await RollBackAsync(MapMembershipStateReadFailure(
                        afterStateResult));
                }

                afterMembershipState = afterStateResult.State;
            }

            var beforeSummary = beforeMembershipState is null
                ? null
                : Summarize(beforeMembershipState);
            var afterSummary = new
            {
                Visit = new
                {
                    VisitId = visitId,
                    visit.ClientId,
                    VisitKind = VisitCommandSupport.MapVisitKind(visit.VisitKind),
                    visit.MembershipId,
                    visitRecord.OccurredAt,
                    visitRecord.RecordedAt,
                    visitRecord.EntryOrigin,
                    visitRecord.EntryBatchId,
                    visitRecord.Comment,
                    visitRecord.Status,
                    ConsumptionId = consumptionRecord?.Id,
                    Acknowledgements = preparation.AcceptedAcknowledgements
                        .Select(VisitCommandSupport.MapAcknowledgement)
                        .ToArray(),
                    Selection = visit.MembershipId is null
                        ? "explicit_non_membership_context"
                        : "explicit_membership",
                },
                MembershipState = afterMembershipState is null
                    ? null
                    : Summarize(afterMembershipState),
            };
            var auditEntryId = auditAppender.Append(
                visit.Envelope,
                VisitAuditActions.Marked,
                VisitAuditActions.VisitEntityType,
                visitId,
                recordedAt,
                relatedEntityRefs: new
                {
                    visit.ClientId,
                    visit.MembershipId,
                    ConsumptionId = consumptionRecord?.Id,
                },
                beforeSummary,
                afterSummary);

            dbContext.Set<CommandIdempotencyRecord>().Add(
                VisitCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    visit,
                    recordedAt,
                    visitId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var warnings = afterMembershipState?.Warnings
                .Select(warning => warning.Code)
                .ToArray() ?? [];
            return VisitCommandSupport.Success(
                visitId,
                visit,
                auditEntryId,
                warnings);
        }
        catch (Exception exception)
        {
            var postgresException = VisitCommandSupport.FindPostgresException(exception);
            if (postgresException is not null
                && VisitCommandSupport.TryMapPostgresFailure(
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

    private async Task<ClientRecord?> LockClientAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var clients = await dbContext.Set<ClientRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.clients
                where id = {clientId}
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return clients.SingleOrDefault();
    }

    private Task<GetMembershipStateResult> ReadMembershipStateAsync(
        NormalizedMarkVisit visit,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        return membershipStateQueryHandler.ExecuteAsync(
            new GetMembershipStateQuery(
                visit.Envelope.Actor,
                visit.MembershipId!.Value,
                asOfDate),
            cancellationToken);
    }

    private async Task<CommandResult> ReplayOrRejectDuplicateAsync(
        CommandIdempotencyRecord record,
        NormalizedMarkVisit visit,
        string fingerprint,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        if (!VisitCommandSupport.TryGetSuccessfulReplay(
                record,
                visit,
                fingerprint,
                out var visitId,
                out var auditEntryId))
        {
            return VisitCommandSupport.DuplicateSubmission();
        }

        IReadOnlyList<string> warnings = [];
        if (visit.MembershipId is not null)
        {
            var stateResult = await ReadMembershipStateAsync(
                visit,
                asOfDate,
                cancellationToken);
            warnings = stateResult.State?.Warnings
                .Select(warning => warning.Code)
                .ToArray() ?? [];
        }

        return VisitCommandSupport.Success(
            visitId,
            visit,
            auditEntryId,
            warnings);
    }

    private static CommandResult? TryPrepare(
        NormalizedMarkVisit visit,
        MembershipVisitEligibility? membershipEligibility,
        out MarkVisitPreparation preparation)
    {
        try
        {
            preparation = MarkVisitPreparationPolicy.Prepare(
                visit.ClientId,
                visit.VisitKind,
                visit.MembershipId,
                visit.Acknowledgements,
                membershipEligibility);
            return null;
        }
        catch (ArgumentException exception)
            when (exception.ParamName == "acknowledgements")
        {
            preparation = null!;
            return VisitCommandSupport.Error(
                CommandErrorCode.WarningAcknowledgementRequired,
                "Acknowledgements must exactly match current Membership warnings.",
                "acknowledgements");
        }
        catch (ArgumentException exception)
        {
            preparation = null!;
            return VisitCommandSupport.ValidationError(
                exception.Message,
                exception.ParamName);
        }
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
            "Membership state could not be rebuilt from canonical Visit sources.");
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
