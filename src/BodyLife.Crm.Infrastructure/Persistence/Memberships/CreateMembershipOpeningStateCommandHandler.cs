using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
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

            if (await LockClientAsync(create.ClientId, cancellationToken) is null)
            {
                return MembershipCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Client was not found.",
                    "clientId");
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

            var membershipType = await LockMembershipTypeAsync(
                create.MembershipTypeId,
                cancellationToken);
            if (membershipType is null)
            {
                return MembershipCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Membership type was not found.",
                    "membershipTypeId");
            }

            if (!string.Equals(membershipType.Kind, "ordinary", StringComparison.Ordinal))
            {
                return MembershipCommandSupport.Error(
                    CommandErrorCode.MembershipNotEligible,
                    "Opening state requires an ordinary membership type.",
                    "membershipTypeId");
            }

            MembershipIssueTerms issueTerms;
            try
            {
                var snapshot = new IssuedMembershipSnapshot(
                    membershipType.Name,
                    membershipType.DurationDays,
                    membershipType.VisitsLimit,
                    new Money(membershipType.PriceAmount, membershipType.PriceCurrency));
                issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
                    membershipType.Id,
                    snapshot,
                    create.StartDate,
                    MembershipDateRules.CalculateBaseEndDate(
                        create.StartDate,
                        snapshot.DurationDays));
                MembershipStateCalculator.CalculateFromOpeningState(
                    issueTerms,
                    create.Declaration);
            }
            catch (ArgumentOutOfRangeException exception)
                when (exception.ParamName is "durationDays" or "startDate")
            {
                return MembershipCommandSupport.ValidationError(
                    "Start date and membership duration exceed the supported calendar range.",
                    "startDate");
            }
            catch (ArgumentException exception)
            {
                return MembershipCommandSupport.ValidationError(
                    exception.Message,
                    "openingState");
            }

            var membershipId = Guid.NewGuid();
            var openingStateId = Guid.NewGuid();
            var membership = new IssuedMembershipRecord
            {
                Id = membershipId,
                ClientId = create.ClientId,
                MembershipTypeId = membershipType.Id,
                TypeNameSnapshot = issueTerms.Snapshot.TypeName,
                DurationDaysSnapshot = issueTerms.Snapshot.DurationDays,
                VisitsLimitSnapshot = issueTerms.Snapshot.VisitsLimit,
                PriceAmountSnapshot = issueTerms.Snapshot.Price.Amount,
                PriceCurrencySnapshot = issueTerms.Snapshot.Price.Currency,
                IssuanceMode = "opening_state",
                StartDate = issueTerms.StartDate,
                BaseEndDate = issueTerms.BaseEndDate,
                IssuedAt = recordedAt,
                IssuedByAccountId = create.Envelope.Actor.AccountId.Value,
                Status = MembershipQuerySupport.ActiveMembershipStatus,
                EntryOrigin = "manual_backfill",
                EntryBatchId = create.EntryBatchId,
                Comment = create.Envelope.Comment,
            };
            var openingState = new MembershipOpeningStateRecord
            {
                Id = openingStateId,
                MembershipId = membershipId,
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
                EntryOrigin = "manual_backfill",
                EntryBatchId = create.EntryBatchId,
                Status = "active",
            };
            dbContext.Set<IssuedMembershipRecord>().Add(membership);
            dbContext.Set<MembershipOpeningStateRecord>().Add(openingState);
            await dbContext.SaveChangesAsync(cancellationToken);

            var rebuildResult = await stateCacheRebuilder.RebuildAsync(membershipId, cancellationToken);
            if (!rebuildResult.Succeeded || rebuildResult.State is null)
            {
                await MembershipCommandSupport.RollBackAndClearAsync(dbContext, transaction);
                return MembershipCommandSupport.Error(
                    CommandErrorCode.RecalculationFailed,
                    "Membership state could not be rebuilt from the new opening source.");
            }

            var auditEntryId = auditAppender.Append(
                create.Envelope,
                MembershipAuditActions.OpeningStateCreated,
                MembershipAuditActions.OpeningStateEntityType,
                openingStateId,
                recordedAt,
                relatedEntityRefs: new
                {
                    ClientId = create.ClientId,
                    MembershipId = membershipId,
                    MembershipTypeId = membershipType.Id,
                },
                afterSummary: new
                {
                    OpeningStateId = openingStateId,
                    MembershipId = membershipId,
                    ClientId = create.ClientId,
                    MembershipTypeId = membershipType.Id,
                    IssuanceMode = membership.IssuanceMode,
                    Snapshot = new
                    {
                        membership.TypeNameSnapshot,
                        membership.DurationDaysSnapshot,
                        membership.VisitsLimitSnapshot,
                        membership.PriceAmountSnapshot,
                        membership.PriceCurrencySnapshot,
                    },
                    membership.StartDate,
                    membership.BaseEndDate,
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
                        rebuildResult.State.RemainingVisits,
                        rebuildResult.State.NegativeBalance,
                        rebuildResult.State.EffectiveEndDate,
                        rebuildResult.State.ExtensionDays,
                        rebuildResult.RecalculationVersion,
                    },
                });

            dbContext.Set<CommandIdempotencyRecord>().Add(
                MembershipCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    create,
                    recordedAt,
                    membershipId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return MembershipCommandSupport.Success(membershipId, create.ClientId, auditEntryId);
        }
        catch (Exception exception)
        {
            var postgresException = MembershipCommandSupport.FindPostgresException(exception);
            if (postgresException is null
                || !MembershipCommandSupport.TryMapPostgresFailure(postgresException, out var errorResult))
            {
                throw;
            }

            await MembershipCommandSupport.RollBackAndClearAsync(dbContext, transaction);
            return errorResult;
        }
    }

    private async Task<ClientRecord?> LockClientAsync(Guid clientId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<ClientRecord>()
            .FromSqlInterpolated($"select * from bodylife.clients where id = {clientId} for update")
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    private async Task<MembershipTypeRecord?> LockMembershipTypeAsync(
        Guid membershipTypeId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<MembershipTypeRecord>()
            .FromSqlInterpolated($"select * from bodylife.membership_types where id = {membershipTypeId} for share")
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }
}
