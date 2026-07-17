using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class CorrectNonWorkingDayCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    CorrectNonWorkingDayCommandRevalidationPreparer revalidationPreparer,
    IMembershipStateRecalculator membershipStateRecalculator,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<CorrectNonWorkingDayCommand>
{
    private const string CommandName = "CorrectNonWorkingDay";

    public async Task<CommandResult> ExecuteAsync(
        CorrectNonWorkingDayCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !NonWorkingDayCommandSupport.IsOwnerActorShape(command.Envelope.Actor))
        {
            return NonWorkingDayCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner session is required to correct a non-working period.");
        }

        var preparationResult = CorrectNonWorkingDayPreparationPolicy.Prepare(command);
        if (!preparationResult.IsPrepared)
        {
            return CommandResult.Error(preparationResult.Errors);
        }

        var correction = preparationResult.Preparation!;
        var recordedAt = timeProvider.GetUtcNow().ToUniversalTime();
        var fingerprint = CorrectNonWorkingDayCommandSupport.CreateFingerprint(
            correction);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);

        try
        {
            if (!await NonWorkingDayQuerySupport.IsOwnerAuthorizedAsync(
                    dbContext,
                    correction.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return await RollBackAsync(NonWorkingDayCommandSupport.Error(
                    CommandErrorCode.PermissionDenied,
                    "The Owner account or session is not active."));
            }

            var existingIdempotency = await NonWorkingDayCommandSupport
                .FindIdempotencyAsync(
                    dbContext,
                    CommandName,
                    correction.Envelope.IdempotencyKey!,
                    cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(await ReplayOrRejectDuplicateAsync(
                    existingIdempotency,
                    correction,
                    fingerprint,
                    cancellationToken));
            }

            var revalidation = await revalidationPreparer.PrepareAsync(
                correction,
                cancellationToken);
            if (!revalidation.IsPrepared)
            {
                return await RollBackAsync(CommandResult.Error(revalidation.Errors));
            }

            existingIdempotency = await NonWorkingDayCommandSupport
                .FindIdempotencyAsync(
                    dbContext,
                    CommandName,
                    correction.Envelope.IdempotencyKey!,
                    cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(await ReplayOrRejectDuplicateAsync(
                    existingIdempotency,
                    correction,
                    fingerprint,
                    cancellationToken));
            }

            var material = revalidation.ConfirmationMaterial
                ?? throw new InvalidOperationException(
                    "Prepared correction confirmation material is missing.");
            var source = material.OriginalSource;
            var tokenValidation = revalidation.TokenValidation
                ?? throw new InvalidOperationException(
                    "Prepared correction token validation is missing.");
            var previewedAt = tokenValidation.IssuedAt
                ?? throw new InvalidOperationException(
                    "A valid correction token did not retain its issue time.");
            var sourceStatus = correction.Mode == NonWorkingDayCorrectionMode.Cancel
                ? "canceled"
                : "corrected";
            if (!await TransitionOriginalSourceAsync(
                    source,
                    sourceStatus,
                    cancellationToken))
            {
                return await RollBackAsync(
                    CorrectNonWorkingDayCommandSupport.SourceChangedConcurrently());
            }

            Guid primaryEntityId;
            Guid? replacementPeriodId = null;
            NonWorkingPeriodRecord? replacementPeriod = null;
            NonWorkingPeriodApplicationRecord[] replacementApplications = [];
            NonWorkingPeriodCancellationRecord? cancellationRecord = null;
            if (correction.Mode == NonWorkingDayCorrectionMode.Cancel)
            {
                primaryEntityId = Guid.NewGuid();
                cancellationRecord = new NonWorkingPeriodCancellationRecord
                {
                    Id = primaryEntityId,
                    NonWorkingPeriodId = source.PeriodId,
                    Reason = correction.Envelope.Reason!,
                    RecordedAt = recordedAt,
                    RecordedByAccountId = correction.Envelope.Actor.AccountId.Value,
                    SessionId = correction.Envelope.Actor.SessionId.Value,
                };
                dbContext.Set<NonWorkingPeriodCancellationRecord>()
                    .Add(cancellationRecord);
            }
            else
            {
                var replacementInput = material.ReplacementInput
                    ?? throw new InvalidOperationException(
                        "Prepared replacement input is missing.");
                var replacementScope = material.ConfirmedScope
                    ?? throw new InvalidOperationException(
                        "Prepared replacement scope is missing.");
                replacementPeriodId = Guid.NewGuid();
                primaryEntityId = replacementPeriodId.Value;
                replacementPeriod = new NonWorkingPeriodRecord
                {
                    Id = replacementPeriodId.Value,
                    StartDate = replacementInput.Period.StartDate,
                    EndDate = replacementInput.Period.EndDate,
                    ReasonCode = replacementInput.ReasonCode,
                    ReasonComment = replacementInput.ReasonComment,
                    CreatedAt = recordedAt,
                    CreatedByAccountId = correction.Envelope.Actor.AccountId.Value,
                    SessionId = correction.Envelope.Actor.SessionId.Value,
                    Status = "active",
                };
                replacementApplications = replacementScope.AffectedMemberships
                    .Select(item => new NonWorkingPeriodApplicationRecord
                    {
                        Id = Guid.NewGuid(),
                        NonWorkingPeriodId = replacementPeriodId.Value,
                        MembershipId = item.MembershipId,
                        ClientId = item.ClientId,
                        AppliedStartDate = item.AppliedRange.StartDate,
                        AppliedEndDate = item.AppliedRange.EndDate,
                        PreviewedAt = previewedAt,
                        ConfirmedAt = recordedAt,
                        Status = "active",
                    })
                    .ToArray();
                dbContext.Set<NonWorkingPeriodRecord>().Add(replacementPeriod);
                dbContext.Set<NonWorkingPeriodApplicationRecord>()
                    .AddRange(replacementApplications);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var affectedMembershipIds = source.Applications
                .Select(application => application.MembershipId)
                .Concat(replacementApplications.Select(
                    application => application.MembershipId))
                .Distinct()
                .Order()
                .ToArray();
            var recalculatedMembershipIds = new List<Guid>(
                affectedMembershipIds.Length);
            foreach (var membershipId in affectedMembershipIds)
            {
                var recalculation = await membershipStateRecalculator
                    .RecalculateAsync(membershipId, cancellationToken);
                if (!recalculation.Succeeded
                    || recalculation.MembershipId != membershipId)
                {
                    return await RollBackAsync(
                        CorrectNonWorkingDayCommandSupport.RecalculationFailed());
                }

                recalculatedMembershipIds.Add(membershipId);
            }

            var actionType = correction.Mode == NonWorkingDayCorrectionMode.Cancel
                ? NonWorkingDayAuditActions.Canceled
                : NonWorkingDayAuditActions.Corrected;
            var auditEntryId = auditAppender.Append(
                correction.Envelope,
                actionType,
                NonWorkingDayAuditActions.PeriodEntityType,
                source.PeriodId,
                recordedAt,
                relatedEntityRefs: new
                {
                    OriginalPeriodId = source.PeriodId,
                    ReplacementPeriodId = replacementPeriod?.Id,
                    CancellationId = cancellationRecord?.Id,
                    OldMembershipIds = source.Applications
                        .Select(application => application.MembershipId)
                        .ToArray(),
                    NewMembershipIds = replacementApplications
                        .Select(application => application.MembershipId)
                        .ToArray(),
                    AffectedMembershipIds = affectedMembershipIds,
                    AffectedClientIds = source.Applications
                        .Select(application => application.ClientId)
                        .Concat(replacementApplications.Select(
                            application => application.ClientId))
                        .Distinct()
                        .Order()
                        .ToArray(),
                },
                beforeSummary: new
                {
                    Period = SummarizeSourcePeriod(source),
                    Applications = source.Applications.Select(application => new
                    {
                        application.ApplicationId,
                        application.MembershipId,
                        application.ClientId,
                        application.AppliedRange.StartDate,
                        application.AppliedRange.EndDate,
                        application.PreviewedAt,
                        application.ConfirmedAt,
                        Status = "active",
                    }).ToArray(),
                    Preview = new
                    {
                        tokenValidation.ConfirmationFingerprint,
                        tokenValidation.IssuedAt,
                        tokenValidation.ExpiresAt,
                        OldAffectedCount = source.Applications.Count,
                        NewAffectedCount = replacementApplications.Length,
                    },
                },
                afterSummary: new
                {
                    Mode = MapMode(correction.Mode),
                    OriginalPeriod = SummarizeSourcePeriod(source, sourceStatus),
                    ReplacementPeriod = replacementPeriod is null
                        ? null
                        : new
                        {
                            PeriodId = replacementPeriod.Id,
                            replacementPeriod.StartDate,
                            replacementPeriod.EndDate,
                            InclusiveDays = material.ReplacementInput!.Period
                                .InclusiveDays,
                            replacementPeriod.ReasonCode,
                            replacementPeriod.ReasonComment,
                            replacementPeriod.CreatedAt,
                            replacementPeriod.Status,
                        },
                    ReplacementApplications = replacementApplications.Select(
                        application => new
                        {
                            ApplicationId = application.Id,
                            application.MembershipId,
                            application.ClientId,
                            application.AppliedStartDate,
                            application.AppliedEndDate,
                        }).ToArray(),
                    Cancellation = cancellationRecord is null
                        ? null
                        : new
                        {
                            CancellationId = cancellationRecord.Id,
                            cancellationRecord.NonWorkingPeriodId,
                            cancellationRecord.Reason,
                            cancellationRecord.RecordedAt,
                        },
                    OldAffectedCount = source.Applications.Count,
                    NewAffectedCount = replacementApplications.Length,
                    AffectedUnionCount = affectedMembershipIds.Length,
                    Recalculation = new
                    {
                        RequestedCount = affectedMembershipIds.Length,
                        SucceededCount = recalculatedMembershipIds.Count,
                        MembershipIds = recalculatedMembershipIds.ToArray(),
                    },
                });

            dbContext.Set<CommandIdempotencyRecord>().Add(
                CorrectNonWorkingDayCommandSupport
                    .CreateSucceededIdempotencyRecord(
                        CommandName,
                        correction,
                        recordedAt,
                        primaryEntityId,
                        auditEntryId,
                        fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CorrectNonWorkingDayCommandSupport.Success(
                correction.Mode,
                primaryEntityId,
                source.PeriodId,
                recalculatedMembershipIds,
                auditEntryId);
        }
        catch (Exception exception)
        {
            var postgresException = NonWorkingDayCommandSupport
                .FindPostgresException(exception);
            if (postgresException is not null
                && NonWorkingDayCommandSupport.IsRetryableConcurrencyFailure(
                    postgresException))
            {
                await NonWorkingDayCommandSupport.RollBackAndClearAsync(
                    dbContext,
                    transaction);
                var existing = await NonWorkingDayCommandSupport
                    .FindIdempotencyAsync(
                        dbContext,
                        CommandName,
                        correction.Envelope.IdempotencyKey!,
                        cancellationToken);
                if (existing is not null)
                {
                    return await ReplayOrRejectDuplicateAsync(
                        existing,
                        correction,
                        fingerprint,
                        cancellationToken);
                }

                return NonWorkingDayCommandSupport.TryMapPostgresFailure(
                    postgresException,
                    out var concurrentResult)
                        ? concurrentResult
                        : CorrectNonWorkingDayCommandSupport
                            .SourceChangedConcurrently();
            }

            if (postgresException is not null
                && NonWorkingDayCommandSupport.TryMapPostgresFailure(
                    postgresException,
                    out var errorResult))
            {
                return await RollBackAsync(errorResult);
            }

            await NonWorkingDayCommandSupport.RollBackAndClearAsync(
                dbContext,
                transaction);
            throw;
        }

        async Task<CommandResult> RollBackAsync(CommandResult result)
        {
            return await NonWorkingDayCommandSupport.RollBackAndReturnAsync(
                dbContext,
                transaction,
                result);
        }
    }

    private async Task<bool> TransitionOriginalSourceAsync(
        NonWorkingDayCorrectionSource source,
        string status,
        CancellationToken cancellationToken)
    {
        var updatedPeriodCount = await dbContext.Set<NonWorkingPeriodRecord>()
            .Where(period => period.Id == source.PeriodId
                && period.Status == "active")
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(period => period.Status, status),
                cancellationToken);
        if (updatedPeriodCount != 1)
        {
            return false;
        }

        var updatedApplicationCount = await dbContext
            .Set<NonWorkingPeriodApplicationRecord>()
            .Where(application => application.NonWorkingPeriodId == source.PeriodId
                && application.Status == "active")
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    application => application.Status,
                    status),
                cancellationToken);
        return updatedApplicationCount == source.Applications.Count;
    }

    private async Task<CommandResult> ReplayOrRejectDuplicateAsync(
        CommandIdempotencyRecord record,
        CorrectNonWorkingDayPreparation correction,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (!CorrectNonWorkingDayCommandSupport.TryGetSuccessfulReplay(
                record,
                correction,
                fingerprint,
                out var primaryEntityId,
                out var auditEntryId))
        {
            return CorrectNonWorkingDayCommandSupport.DuplicateSubmission();
        }

        var expectedAction = correction.Mode == NonWorkingDayCorrectionMode.Cancel
            ? NonWorkingDayAuditActions.Canceled
            : NonWorkingDayAuditActions.Corrected;
        var auditExists = await dbContext.Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .AnyAsync(
                entry => entry.Id == auditEntryId.Value
                    && entry.ActionType == expectedAction
                    && entry.EntityType == NonWorkingDayAuditActions.PeriodEntityType
                    && entry.EntityId == correction.PeriodId,
                cancellationToken);
        if (!auditExists)
        {
            return CorrectNonWorkingDayCommandSupport.DuplicateSubmission();
        }

        Guid? replacementPeriodId = null;
        if (correction.Mode == NonWorkingDayCorrectionMode.Cancel)
        {
            var cancellationExists = await dbContext
                .Set<NonWorkingPeriodCancellationRecord>()
                .AsNoTracking()
                .AnyAsync(
                    cancellation => cancellation.Id == primaryEntityId
                        && cancellation.NonWorkingPeriodId == correction.PeriodId,
                    cancellationToken);
            if (!cancellationExists)
            {
                return CorrectNonWorkingDayCommandSupport.DuplicateSubmission();
            }
        }
        else
        {
            var replacementExists = await dbContext
                .Set<NonWorkingPeriodRecord>()
                .AsNoTracking()
                .AnyAsync(
                    period => period.Id == primaryEntityId
                        && period.Id != correction.PeriodId,
                    cancellationToken);
            if (!replacementExists)
            {
                return CorrectNonWorkingDayCommandSupport.DuplicateSubmission();
            }

            replacementPeriodId = primaryEntityId;
        }

        var membershipIds = await CorrectNonWorkingDayCommandSupport
            .ReadAffectedMembershipIdsAsync(
                dbContext,
                correction.PeriodId,
                replacementPeriodId,
                cancellationToken);
        return CorrectNonWorkingDayCommandSupport.Success(
            correction.Mode,
            primaryEntityId,
            correction.PeriodId,
            membershipIds,
            auditEntryId);
    }

    private static object SummarizeSourcePeriod(
        NonWorkingDayCorrectionSource source,
        string status = "active")
    {
        return new
        {
            source.PeriodId,
            source.Period.StartDate,
            source.Period.EndDate,
            source.Period.InclusiveDays,
            source.ReasonCode,
            source.ReasonComment,
            source.CreatedAt,
            source.CreatedByAccountId,
            source.SessionId,
            Status = status,
        };
    }

    private static string MapMode(NonWorkingDayCorrectionMode mode)
    {
        return mode switch
        {
            NonWorkingDayCorrectionMode.ReplaceRange => "replace_range",
            NonWorkingDayCorrectionMode.ReplaceReason => "replace_reason",
            NonWorkingDayCorrectionMode.Cancel => "cancel",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
