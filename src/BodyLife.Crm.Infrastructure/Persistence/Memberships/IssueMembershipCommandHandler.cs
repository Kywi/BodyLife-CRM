using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class IssueMembershipCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    MembershipStateCacheRebuilder stateCacheRebuilder,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<IssueMembershipCommand>
{
    private const string CommandName = "IssueMembership";

    public async Task<CommandResult> ExecuteAsync(
        IssueMembershipCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !MembershipCommandSupport.IsAllowedActorShape(command.Envelope.Actor))
        {
            return IssueMembershipCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to issue a membership.");
        }

        var validationResult = IssueMembershipCommandSupport.ValidateAndNormalize(
            command,
            out var normalizedIssue);
        if (validationResult is not null)
        {
            return validationResult;
        }

        var issue = normalizedIssue!;
        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = IssueMembershipCommandSupport.CreateFingerprint(
            command.Envelope,
            issue);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await MembershipCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    command.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.PermissionDenied,
                    "The Owner or Admin account or session is not active.");
            }

            var existingIdempotency = await MembershipCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                issue.Envelope.IdempotencyKey,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return IssueMembershipCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    issue,
                    command.Envelope.Actor.AccountId.Value,
                    fingerprint);
            }

            var client = await LockClientAsync(issue.ClientId, cancellationToken);
            if (client is null)
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Client was not found.",
                    "clientId");
            }

            existingIdempotency = await MembershipCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                issue.Envelope.IdempotencyKey,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return IssueMembershipCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    issue,
                    command.Envelope.Actor.AccountId.Value,
                    fingerprint);
            }

            var membershipType = await LockMembershipTypeAsync(
                issue.MembershipTypeId,
                cancellationToken);
            if (membershipType is null)
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Membership type was not found.",
                    "membershipTypeId");
            }

            if (!membershipType.IsActive)
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.MembershipTypeInactive,
                    "Inactive membership type cannot be used for ordinary issue.",
                    "membershipTypeId");
            }

            var activeMemberships = await LockActiveMembershipsAsync(
                issue.ClientId,
                cancellationToken);
            var negativeStateResult = await LoadExistingNegativeStateAsync(
                activeMemberships,
                cancellationToken);
            if (negativeStateResult.Error is not null)
            {
                return negativeStateResult.Error;
            }

            MembershipIssuePreparation preparation;

            try
            {
                var catalogItem = new MembershipTypeCatalogItem(
                    membershipType.Id,
                    membershipType.Name,
                    membershipType.DurationDays,
                    membershipType.VisitsLimit,
                    new Money(
                        membershipType.PriceAmount,
                        membershipType.PriceCurrency),
                    membershipType.IsActive,
                    membershipType.Comment,
                    membershipType.CreatedAt,
                    membershipType.UpdatedAt,
                    membershipType.DeactivatedAt);
                preparation = MembershipIssuePreparationPolicy.Prepare(
                    issue.ClientId,
                    catalogItem,
                    issue.StartDate,
                    negativeStateResult.State,
                    issue.NegativeHandlingDecision);
            }
            catch (ArgumentOutOfRangeException exception)
                when (exception.ParamName == "durationDays")
            {
                return IssueMembershipCommandSupport.ValidationError(
                    "Start date and membership duration exceed the supported calendar range.",
                    "startDate");
            }
            catch (ArgumentException)
                when (negativeStateResult.State is not null
                    && issue.NegativeHandlingDecision is null)
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.NegativeDecisionRequired,
                    "An explicit negative handling decision is required.",
                    "negativeHandlingDecision");
            }
            catch (ArgumentException)
                when (negativeStateResult.State is not null
                    && issue.NegativeHandlingDecision is not null)
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.MembershipNotEligible,
                    "The selected negative handling decision is not available.",
                    "negativeHandlingDecision");
            }
            catch (ArgumentException)
                when (negativeStateResult.State is null
                    && issue.NegativeHandlingDecision is not null)
            {
                return IssueMembershipCommandSupport.ValidationError(
                    "A negative handling decision requires existing negative membership state.",
                    "negativeHandlingDecision");
            }
            catch (ArgumentException)
            {
                return IssueMembershipCommandSupport.ValidationError(
                    "Canonical membership data cannot produce valid issue terms.",
                    "membershipTypeId");
            }
            catch (InvalidOperationException)
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.MembershipTypeInactive,
                    "Inactive membership type cannot be used for ordinary issue.",
                    "membershipTypeId");
            }

            var membershipId = Guid.NewGuid();
            var membership = new IssuedMembershipRecord
            {
                Id = membershipId,
                ClientId = issue.ClientId,
                MembershipTypeId = issue.MembershipTypeId,
                TypeNameSnapshot = preparation.Snapshot.TypeName,
                DurationDaysSnapshot = preparation.Snapshot.DurationDays,
                VisitsLimitSnapshot = preparation.Snapshot.VisitsLimit,
                PriceAmountSnapshot = preparation.Snapshot.Price.Amount,
                PriceCurrencySnapshot = preparation.Snapshot.Price.Currency,
                StartDate = preparation.StartDate,
                BaseEndDate = preparation.BaseEndDate,
                IssuedAt = recordedAt,
                IssuedByAccountId = command.Envelope.Actor.AccountId.Value,
                Status = MembershipQuerySupport.ActiveMembershipStatus,
                EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                    command.Envelope.EntryOrigin),
                EntryBatchId = null,
                Comment = issue.Envelope.Comment,
            };
            dbContext.Set<IssuedMembershipRecord>().Add(membership);
            await dbContext.SaveChangesAsync(cancellationToken);

            var rebuildResult = await stateCacheRebuilder.RebuildAsync(
                membershipId,
                cancellationToken);
            if (!rebuildResult.Succeeded
                || rebuildResult.State is null
                || !MatchesExpectedInitialState(
                    rebuildResult.State,
                    preparation.ExpectedInitialState))
            {
                await MembershipCommandSupport.RollBackAndClearAsync(dbContext, transaction);
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.RecalculationFailed,
                    "New membership state could not be rebuilt from canonical issue terms.");
            }

            var recalculatedState = rebuildResult.State;
            var auditEntryId = auditAppender.Append(
                command.Envelope,
                MembershipAuditActions.Issued,
                MembershipAuditActions.MembershipEntityType,
                membershipId,
                recordedAt,
                relatedEntityRefs: new
                {
                    ClientId = issue.ClientId,
                    MembershipTypeId = issue.MembershipTypeId,
                },
                afterSummary: new
                {
                    MembershipId = membershipId,
                    ClientId = issue.ClientId,
                    MembershipTypeId = issue.MembershipTypeId,
                    Snapshot = new
                    {
                        preparation.Snapshot.TypeName,
                        preparation.Snapshot.DurationDays,
                        preparation.Snapshot.VisitsLimit,
                        PriceAmount = preparation.Snapshot.Price.Amount,
                        PriceCurrency = preparation.Snapshot.Price.Currency,
                    },
                    preparation.StartDate,
                    preparation.BaseEndDate,
                    membership.IssuedAt,
                    membership.Status,
                    NegativeHandlingDecision =
                        IssueMembershipCommandSupport.MapNegativeHandlingDecision(
                            preparation.NegativeHandlingDecision),
                    ExistingNegativeState = preparation.ExistingNegativeState is null
                        ? null
                        : new
                        {
                            preparation.ExistingNegativeState.NegativeBalance,
                            preparation.ExistingNegativeState.FirstNegativeVisitDate,
                        },
                    InitialState = new
                    {
                        recalculatedState.CountedVisits,
                        recalculatedState.RemainingVisits,
                        recalculatedState.NegativeBalance,
                        recalculatedState.FirstNegativeVisitDate,
                        recalculatedState.ExtensionDays,
                        recalculatedState.EffectiveEndDate,
                        recalculatedState.LastCountedVisitAt,
                        rebuildResult.RecalculationVersion,
                    },
                });

            dbContext.Set<CommandIdempotencyRecord>().Add(
                IssueMembershipCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    command.Envelope,
                    issue,
                    recordedAt,
                    membershipId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return IssueMembershipCommandSupport.Success(
                membershipId,
                issue.ClientId,
                auditEntryId,
                preparation.Warnings.Select(warning => warning.Code).ToArray());
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

    private async Task<MembershipTypeRecord?> LockMembershipTypeAsync(
        Guid membershipTypeId,
        CancellationToken cancellationToken)
    {
        var membershipTypes = await dbContext.Set<MembershipTypeRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.membership_types
                where id = {membershipTypeId}
                for share
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return membershipTypes.SingleOrDefault();
    }

    private Task<IssuedMembershipRecord[]> LockActiveMembershipsAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<IssuedMembershipRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.issued_memberships
                where client_id = {clientId}
                  and status = 'active'
                order by id
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
    }

    private async Task<ExistingNegativeStateLoadResult> LoadExistingNegativeStateAsync(
        IReadOnlyCollection<IssuedMembershipRecord> activeMemberships,
        CancellationToken cancellationToken)
    {
        if (activeMemberships.Count == 0)
        {
            return ExistingNegativeStateLoadResult.Completed(state: null);
        }

        var activeMembershipIds = activeMemberships
            .Select(membership => membership.Id)
            .ToArray();
        var cacheRows = await dbContext.Set<MembershipStateCacheRecord>()
            .AsNoTracking()
            .Where(cache => activeMembershipIds.Contains(cache.MembershipId))
            .ToArrayAsync(cancellationToken);
        var cachesByMembershipId = cacheRows.ToDictionary(cache => cache.MembershipId);
        var negativeStates = new List<MembershipIssueNegativeContext>(2);

        foreach (var membership in activeMemberships)
        {
            if (!cachesByMembershipId.TryGetValue(membership.Id, out var cache)
                || cache.RecalculationVersion
                    != MembershipStateCacheRebuilder.CurrentRecalculationVersion)
            {
                return ExistingNegativeStateLoadResult.Failed(
                    IssueMembershipCommandSupport.Error(
                        CommandErrorCode.RecalculationFailed,
                        "Existing membership state is missing or stale."));
            }

            MembershipCalculatedState calculatedState;

            try
            {
                var snapshot = new IssuedMembershipSnapshot(
                    membership.TypeNameSnapshot,
                    membership.DurationDaysSnapshot,
                    membership.VisitsLimitSnapshot,
                    new Money(
                        membership.PriceAmountSnapshot,
                        membership.PriceCurrencySnapshot));
                var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
                    membership.MembershipTypeId,
                    snapshot,
                    membership.StartDate,
                    membership.BaseEndDate);
                calculatedState = MembershipCalculatedState.FromStoredCache(
                    issueTerms,
                    cache.CountedVisits,
                    cache.RemainingVisits,
                    cache.NegativeBalance,
                    cache.FirstNegativeVisitId,
                    cache.FirstNegativeVisitDate,
                    cache.ExtensionDays,
                    cache.EffectiveEndDate,
                    cache.LastCountedVisitAt);
            }
            catch (ArgumentException)
            {
                return ExistingNegativeStateLoadResult.Failed(
                    IssueMembershipCommandSupport.Error(
                        CommandErrorCode.RecalculationFailed,
                        "Existing membership state is inconsistent with canonical issue terms."));
            }

            if (calculatedState.NegativeBalance > 0)
            {
                negativeStates.Add(new MembershipIssueNegativeContext(
                    calculatedState.NegativeBalance,
                    calculatedState.FirstNegativeVisitDate));
            }
        }

        if (negativeStates.Count > 1)
        {
            return ExistingNegativeStateLoadResult.Failed(
                IssueMembershipCommandSupport.ValidationError(
                    "Multiple active memberships have negative balances. Explicit membership selection is required.",
                    "clientId"));
        }

        return ExistingNegativeStateLoadResult.Completed(
            negativeStates.SingleOrDefault());
    }

    private static bool MatchesExpectedInitialState(
        MembershipCalculatedState recalculated,
        MembershipCalculatedState expected)
    {
        return recalculated.CountedVisits == expected.CountedVisits
            && recalculated.RemainingVisits == expected.RemainingVisits
            && recalculated.NegativeBalance == expected.NegativeBalance
            && recalculated.FirstNegativeVisitId == expected.FirstNegativeVisitId
            && recalculated.FirstNegativeVisitDate == expected.FirstNegativeVisitDate
            && recalculated.ExtensionDays == expected.ExtensionDays
            && recalculated.EffectiveEndDate == expected.EffectiveEndDate
            && recalculated.LastCountedVisitAt == expected.LastCountedVisitAt;
    }

    private sealed record ExistingNegativeStateLoadResult(
        MembershipIssueNegativeContext? State,
        CommandResult? Error)
    {
        internal static ExistingNegativeStateLoadResult Completed(
            MembershipIssueNegativeContext? state)
        {
            return new ExistingNegativeStateLoadResult(state, Error: null);
        }

        internal static ExistingNegativeStateLoadResult Failed(CommandResult error)
        {
            return new ExistingNegativeStateLoadResult(State: null, error);
        }
    }
}
