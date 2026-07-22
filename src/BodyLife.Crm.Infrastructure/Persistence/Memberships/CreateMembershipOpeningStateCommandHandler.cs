using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class CreateMembershipOpeningStateCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    MembershipStateCacheRebuilder stateCacheRebuilder,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<CreateMembershipOpeningStateCommand>
{
    private const string CommandName = "CreateMembershipOpeningState";

    public async Task<CommandResult> ExecuteAsync(
        CreateMembershipOpeningStateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null)
        {
            return MembershipCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to create a membership opening state.");
        }

        var validationResult = MembershipCommandSupport.ValidateAndNormalizeCreateOpeningState(
            command,
            out var normalizedCreate);

        if (validationResult is not null)
        {
            return validationResult;
        }

        var create = normalizedCreate!;
        if (!MembershipCommandSupport.IsAllowedActorShape(create.Envelope.Actor))
        {
            return MembershipCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to create a membership opening state.");
        }

        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = MembershipCommandSupport.CreateOpeningStateFingerprint(create);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await MembershipCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    create.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return MembershipCommandSupport.Error(
                    CommandErrorCode.PermissionDenied,
                    "The Owner or Admin account or session is not active.");
            }

            var existingIdempotency = await MembershipCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                create.IdempotencyKey,
                cancellationToken);

            if (existingIdempotency is not null)
            {
                return MembershipCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    create.Envelope.Actor.AccountId.Value,
                    fingerprint);
            }

            var membership = await LoadMembershipForUpdateAsync(
                dbContext,
                create.MembershipId,
                cancellationToken);

            if (membership is null)
            {
                return MembershipCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Issued membership was not found.",
                    "membershipId");
            }

            existingIdempotency = await MembershipCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                create.IdempotencyKey,
                cancellationToken);

            if (existingIdempotency is not null)
            {
                return MembershipCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    create.Envelope.Actor.AccountId.Value,
                    fingerprint);
            }

            if (!string.Equals(membership.Status, "active", StringComparison.Ordinal))
            {
                return MembershipCommandSupport.Error(
                    CommandErrorCode.MembershipNotEligible,
                    "Opening state can be created only for an active issued membership.",
                    "membershipId");
            }

            var activeOpeningStateExists = await dbContext.Set<MembershipOpeningStateRecord>()
                .AsNoTracking()
                .AnyAsync(
                    openingState => openingState.MembershipId == create.MembershipId
                        && openingState.Status == "active",
                    cancellationToken);

            if (activeOpeningStateExists)
            {
                return MembershipCommandSupport.Error(
                    CommandErrorCode.StaleState,
                    "An active opening state already exists. Refresh canonical membership state.",
                    "membershipId");
            }

            var issueTerms = CreateIssueTerms(membership);

            try
            {
                MembershipStateCalculator.CalculateFromOpeningState(
                    issueTerms,
                    create.Declaration);
            }
            catch (ArgumentException exception)
            {
                return MembershipCommandSupport.ValidationError(
                    exception.Message,
                    "openingState");
            }

            var openingStateId = Guid.NewGuid();
            var openingState = new MembershipOpeningStateRecord
            {
                Id = openingStateId,
                MembershipId = create.MembershipId,
                OpeningAsOfDate = create.Declaration.OpeningAsOfDate,
                DeclaredRemainingVisits = create.Declaration.DeclaredRemainingVisits,
                DeclaredNegativeBalance = create.Declaration.DeclaredNegativeBalance,
                KnownEffectiveEndDate = create.Declaration.KnownEffectiveEndDate,
                KnownExtensionDays = create.Declaration.KnownExtensionDays,
                SourceReference = create.SourceReference,
                Reason = create.Reason,
                RecordedAt = recordedAt,
                RecordedByAccountId = create.Envelope.Actor.AccountId.Value,
                RecordedSessionId = create.Envelope.Actor.SessionId.Value,
                EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                    create.Envelope.EntryOrigin),
                EntryBatchId = create.EntryBatchId,
                Status = "active",
            };
            dbContext.Set<MembershipOpeningStateRecord>().Add(openingState);
            await dbContext.SaveChangesAsync(cancellationToken);

            var rebuildResult = await stateCacheRebuilder.RebuildAsync(
                create.MembershipId,
                cancellationToken);

            if (!rebuildResult.Succeeded || rebuildResult.State is null)
            {
                await MembershipCommandSupport.RollBackAndClearAsync(dbContext, transaction);
                return MembershipCommandSupport.Error(
                    CommandErrorCode.RecalculationFailed,
                    "Membership state could not be rebuilt from the new opening source.");
            }

            var recalculatedState = rebuildResult.State;
            var auditEntryId = auditAppender.Append(
                create.Envelope,
                MembershipAuditActions.OpeningStateCreated,
                MembershipAuditActions.OpeningStateEntityType,
                openingStateId,
                recordedAt,
                relatedEntityRefs: new
                {
                    membership.ClientId,
                    MembershipId = membership.Id,
                },
                afterSummary: new
                {
                    OpeningStateId = openingStateId,
                    MembershipId = membership.Id,
                    membership.ClientId,
                    openingState.OpeningAsOfDate,
                    openingState.DeclaredRemainingVisits,
                    openingState.DeclaredNegativeBalance,
                    openingState.KnownEffectiveEndDate,
                    openingState.KnownExtensionDays,
                    openingState.SourceReference,
                    openingState.EntryBatchId,
                    openingState.Status,
                    RecalculatedState = new
                    {
                        recalculatedState.RemainingVisits,
                        recalculatedState.NegativeBalance,
                        recalculatedState.EffectiveEndDate,
                        recalculatedState.ExtensionDays,
                        rebuildResult.RecalculationVersion,
                    },
                });

            dbContext.Set<CommandIdempotencyRecord>().Add(
                MembershipCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    create,
                    recordedAt,
                    openingStateId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return MembershipCommandSupport.Success(
                openingStateId,
                create.MembershipId,
                auditEntryId);
        }
        catch (Exception exception)
        {
            var postgresException = MembershipCommandSupport.FindPostgresException(exception);

            if (postgresException is null
                || !MembershipCommandSupport.TryMapPostgresFailure(
                    postgresException,
                    out var errorResult))
            {
                throw;
            }

            await MembershipCommandSupport.RollBackAndClearAsync(dbContext, transaction);
            return errorResult;
        }
    }

    private static async Task<IssuedMembershipRecord?> LoadMembershipForUpdateAsync(
        BodyLifeDbContext dbContext,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var memberships = await dbContext.Set<IssuedMembershipRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    id,
                    client_id,
                    membership_type_id,
                    type_name_snapshot,
                    duration_days_snapshot,
                    visits_limit_snapshot,
                    price_amount_snapshot,
                    price_currency_snapshot,
                    start_date,
                    base_end_date,
                    issued_at,
                    issued_by_account_id,
                    status,
                    entry_origin,
                    entry_batch_id,
                    comment
                from bodylife.issued_memberships
                where id = {membershipId}
                for update
                """)
            .ToArrayAsync(cancellationToken);

        return memberships.SingleOrDefault();
    }

    private static MembershipIssueTerms CreateIssueTerms(IssuedMembershipRecord membership)
    {
        var snapshot = new IssuedMembershipSnapshot(
            membership.TypeNameSnapshot,
            membership.DurationDaysSnapshot,
            membership.VisitsLimitSnapshot,
            new Money(
                membership.PriceAmountSnapshot,
                membership.PriceCurrencySnapshot));
        return MembershipIssueTerms.FromIssuedSnapshot(
            membership.MembershipTypeId,
            snapshot,
            membership.StartDate,
            membership.BaseEndDate);
    }
}
