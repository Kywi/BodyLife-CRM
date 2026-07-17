using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class AddNonWorkingDayCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    IMembershipNonWorkingDayImpactPreparer impactPreparer,
    IMembershipStateRecalculator membershipStateRecalculator,
    INonWorkingDayPreviewTokenService previewTokenService,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<AddNonWorkingDayCommand>
{
    private const string CommandName = "AddNonWorkingDay";

    public async Task<CommandResult> ExecuteAsync(
        AddNonWorkingDayCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !NonWorkingDayCommandSupport.IsOwnerActorShape(command.Envelope.Actor))
        {
            return NonWorkingDayCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner session is required to add a non-working period.");
        }

        var validation = NonWorkingDayCommandSupport.ValidateAndNormalize(
            command,
            out var normalizedCommand);
        if (validation is not null)
        {
            return validation;
        }

        var normalized = normalizedCommand!;
        var recordedAt = timeProvider.GetUtcNow().ToUniversalTime();
        var fingerprint = NonWorkingDayCommandSupport.CreateFingerprint(normalized);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);

        try
        {
            if (!await NonWorkingDayQuerySupport.IsOwnerAuthorizedAsync(
                    dbContext,
                    normalized.Envelope.Actor,
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
                    normalized.Envelope.IdempotencyKey!,
                    cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(await ReplayOrRejectDuplicateAsync(
                    existingIdempotency,
                    normalized,
                    fingerprint,
                    cancellationToken));
            }

            MembershipNonWorkingDayImpactPreparation preparation;
            try
            {
                preparation = await impactPreparer.PrepareImpactAsync(
                    normalized.Input.Period,
                    cancellationToken);
            }
            catch (ArgumentException exception)
                when (NonWorkingDayCommandSupport.FindPostgresException(exception) is null)
            {
                return await RollBackAsync(RecalculationFailed());
            }
            catch (InvalidOperationException exception)
                when (NonWorkingDayCommandSupport.FindPostgresException(exception) is null)
            {
                return await RollBackAsync(RecalculationFailed());
            }

            existingIdempotency = await NonWorkingDayCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                normalized.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(await ReplayOrRejectDuplicateAsync(
                    existingIdempotency,
                    normalized,
                    fingerprint,
                    cancellationToken));
            }

            var tokenValidation = previewTokenService.Validate(
                normalized.ConfirmationToken,
                normalized.Input,
                preparation.AffectedScope);
            var tokenError = MapTokenError(tokenValidation);
            if (tokenError is not null)
            {
                return await RollBackAsync(tokenError);
            }

            var previewedAt = tokenValidation.IssuedAt
                ?? throw new InvalidOperationException(
                    "A valid preview token did not retain its issue time.");
            var periodId = Guid.NewGuid();
            var period = new NonWorkingPeriodRecord
            {
                Id = periodId,
                StartDate = normalized.Input.Period.StartDate,
                EndDate = normalized.Input.Period.EndDate,
                ReasonCode = normalized.Input.ReasonCode,
                ReasonComment = normalized.Input.ReasonComment,
                CreatedAt = recordedAt,
                CreatedByAccountId = normalized.Envelope.Actor.AccountId.Value,
                SessionId = normalized.Envelope.Actor.SessionId.Value,
                Status = "active",
            };
            dbContext.Set<NonWorkingPeriodRecord>().Add(period);

            var applications = preparation.AffectedScope.AffectedMemberships
                .Select(item => new NonWorkingPeriodApplicationRecord
                {
                    Id = Guid.NewGuid(),
                    NonWorkingPeriodId = periodId,
                    MembershipId = item.MembershipId,
                    ClientId = item.ClientId,
                    AppliedStartDate = item.AppliedRange.StartDate,
                    AppliedEndDate = item.AppliedRange.EndDate,
                    PreviewedAt = previewedAt,
                    ConfirmedAt = recordedAt,
                    Status = "active",
                })
                .ToArray();
            dbContext.Set<NonWorkingPeriodApplicationRecord>().AddRange(applications);
            await dbContext.SaveChangesAsync(cancellationToken);

            var recalculatedMembershipIds = new List<Guid>(applications.Length);
            foreach (var item in preparation.AffectedScope.AffectedMemberships)
            {
                var recalculation = await membershipStateRecalculator.RecalculateAsync(
                    item.MembershipId,
                    cancellationToken);
                if (!recalculation.Succeeded
                    || recalculation.MembershipId != item.MembershipId)
                {
                    return await RollBackAsync(RecalculationFailed());
                }

                recalculatedMembershipIds.Add(item.MembershipId);
            }

            var auditEntryId = auditAppender.Append(
                normalized.Envelope,
                NonWorkingDayAuditActions.Added,
                NonWorkingDayAuditActions.PeriodEntityType,
                periodId,
                recordedAt,
                relatedEntityRefs: new
                {
                    AffectedMembershipIds = preparation.AffectedScope
                        .AffectedMemberships
                        .Select(item => item.MembershipId)
                        .ToArray(),
                    AffectedClientIds = preparation.AffectedScope
                        .AffectedMemberships
                        .Select(item => item.ClientId)
                        .ToArray(),
                },
                beforeSummary: new
                {
                    Preview = new
                    {
                        tokenValidation.ScopeFingerprint,
                        tokenValidation.IssuedAt,
                        tokenValidation.ExpiresAt,
                        preparation.AffectedCount,
                    },
                },
                afterSummary: new
                {
                    Period = new
                    {
                        PeriodId = periodId,
                        period.StartDate,
                        period.EndDate,
                        normalized.Input.Period.InclusiveDays,
                        period.ReasonCode,
                        period.ReasonComment,
                        period.CreatedAt,
                        period.Status,
                    },
                    AffectedMembershipCount = applications.Length,
                    Applications = applications.Select(application => new
                    {
                        ApplicationId = application.Id,
                        application.MembershipId,
                        application.ClientId,
                        application.AppliedStartDate,
                        application.AppliedEndDate,
                    }).ToArray(),
                    Recalculation = new
                    {
                        RequestedCount = applications.Length,
                        SucceededCount = recalculatedMembershipIds.Count,
                        MembershipIds = recalculatedMembershipIds.ToArray(),
                    },
                });

            dbContext.Set<CommandIdempotencyRecord>().Add(
                NonWorkingDayCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    normalized,
                    recordedAt,
                    periodId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return NonWorkingDayCommandSupport.Success(
                periodId,
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
                var existing = await NonWorkingDayCommandSupport.FindIdempotencyAsync(
                    dbContext,
                    CommandName,
                    normalized.Envelope.IdempotencyKey!,
                    cancellationToken);
                if (existing is not null)
                {
                    return await ReplayOrRejectDuplicateAsync(
                        existing,
                        normalized,
                        fingerprint,
                        cancellationToken);
                }

                return NonWorkingDayCommandSupport.TryMapPostgresFailure(
                    postgresException,
                    out var concurrentResult)
                        ? concurrentResult
                        : NonWorkingDayCommandSupport.ConcurrencyConflict();
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

    private async Task<CommandResult> ReplayOrRejectDuplicateAsync(
        CommandIdempotencyRecord record,
        NormalizedAddNonWorkingDay command,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (!NonWorkingDayCommandSupport.TryGetSuccessfulReplay(
                record,
                command,
                fingerprint,
                out var periodId,
                out var auditEntryId))
        {
            return NonWorkingDayCommandSupport.DuplicateSubmission();
        }

        var membershipIds = await NonWorkingDayCommandSupport
            .ReadAppliedMembershipIdsAsync(dbContext, periodId, cancellationToken);
        return NonWorkingDayCommandSupport.Success(
            periodId,
            membershipIds,
            auditEntryId);
    }

    private static CommandResult? MapTokenError(
        NonWorkingDayPreviewTokenValidation validation)
    {
        return validation.Status switch
        {
            NonWorkingDayPreviewTokenValidationStatus.Valid => null,
            NonWorkingDayPreviewTokenValidationStatus.Expired =>
                NonWorkingDayCommandSupport.Error(
                    CommandErrorCode.PreviewExpired,
                    "The non-working period preview has expired. Create a new preview before confirming.",
                    "confirmationToken"),
            NonWorkingDayPreviewTokenValidationStatus.InputOrScopeMismatch =>
                NonWorkingDayCommandSupport.Error(
                    CommandErrorCode.AffectedScopeChanged,
                    "The affected Membership scope changed after preview. Review and confirm a new preview.",
                    "confirmationToken"),
            NonWorkingDayPreviewTokenValidationStatus.InvalidToken =>
                NonWorkingDayCommandSupport.ValidationError(
                    "The preview confirmation token is invalid.",
                    "confirmationToken"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(validation),
                validation.Status,
                "Preview token validation status is not supported."),
        };
    }

    private static CommandResult RecalculationFailed()
    {
        return NonWorkingDayCommandSupport.Error(
            CommandErrorCode.RecalculationFailed,
            "Affected Membership state could not be recalculated from canonical sources.");
    }
}
