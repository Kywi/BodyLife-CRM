using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class IssueMembershipCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    IMembershipIssuePaymentWriter paymentWriter,
    MembershipNegativeVisitSelector negativeVisitSelector,
    MembershipStateCacheRebuilder stateCacheRebuilder,
    TimeProvider timeProvider,
    PaperFallbackEntryRowBinder? paperFallbackEntryRowBinder = null)
    : IBodyLifeCommandHandler<IssueMembershipCommand>
{
    private const string CommandName = "IssueMembership";

    public async Task<CommandResult> ExecuteAsync(
        IssueMembershipCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null)
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
        var paperFallbackBinder = paperFallbackEntryRowBinder
            ?? new PaperFallbackEntryRowBinder(dbContext);
        if (!MembershipCommandSupport.IsAllowedActorShape(issue.Envelope.Actor))
        {
            return IssueMembershipCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to issue a membership.");
        }

        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = IssueMembershipCommandSupport.CreateFingerprint(issue);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await MembershipCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    issue.Envelope.Actor,
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
                issue.IdempotencyKey,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return IssueMembershipCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    issue,
                    issue.Envelope.Actor.AccountId.Value,
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
                issue.IdempotencyKey,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return IssueMembershipCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    issue,
                    issue.Envelope.Actor.AccountId.Value,
                    fingerprint);
            }

            var paperBinding = await paperFallbackBinder.PrepareAsync(
                issue.Envelope,
                PaperFallbackEventType.MembershipSale,
                cancellationToken);
            if (paperBinding.RowAlreadyLinked)
            {
                existingIdempotency = await MembershipCommandSupport.FindIdempotencyAsync(
                    dbContext,
                    CommandName,
                    issue.IdempotencyKey,
                    cancellationToken);
                if (existingIdempotency is not null)
                {
                    return IssueMembershipCommandSupport.ReplayOrRejectDuplicate(
                        existingIdempotency,
                        issue,
                        issue.Envelope.Actor.AccountId.Value,
                        fingerprint);
                }
            }

            if (paperBinding.Error is not null)
            {
                return paperBinding.Error;
            }

            var entryBatchId = paperBinding.Reference?.EntryBatchId;

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

            if (!string.Equals(membershipType.Kind, "ordinary", StringComparison.Ordinal))
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.MembershipNotEligible,
                    "Only ordinary membership types can be issued as a membership sale.",
                    "membershipTypeId");
            }

            if (membershipType.UpdatedAt != issue.ExpectedMembershipTypeUpdatedAt)
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.StaleState,
                    "Membership type changed after the issue preview was loaded. Refresh canonical state.",
                    "expectedMembershipTypeUpdatedAt");
            }

            var negativeSelectionResult = await negativeVisitSelector
                .SelectForUpdateAfterClientLockAsync(
                    issue.ClientId,
                    cancellationToken);
            if (negativeSelectionResult.Status
                != MembershipNegativeVisitSelectionStatus.Succeeded)
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.RecalculationFailed,
                    negativeSelectionResult.Status
                        == MembershipNegativeVisitSelectionStatus.MissingCanonicalState
                        ? "Canonical membership state is missing or stale."
                        : "Canonical membership Visit state is inconsistent.");
            }

            var negativeSelection = negativeSelectionResult.Selection!;
            var existingNegativeState = negativeSelection.TotalNegativeBalance > 0
                ? new MembershipIssueNegativeContext(
                    negativeSelection.TotalNegativeBalance,
                    negativeSelection.FirstNegativeVisitDate,
                    negativeSelection.OpenConcreteVisits)
                : null;
            var usesNewMembershipCoverage = issue.NegativeHandlingDecision
                == MembershipNegativeHandlingDecision.CoverWithNewMembership;
            if (usesNewMembershipCoverage
                && negativeSelection.OldestOpenConcreteVisitId
                    != issue.ExpectedOldestOpenNegativeVisitId)
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.StaleState,
                    "The oldest open negative Visit changed after preview. Refresh canonical state.",
                    "expectedOldestOpenNegativeVisitId");
            }

            if (usesNewMembershipCoverage
                && issue.NegativeCoverageCount > membershipType.VisitsLimit)
            {
                return IssueMembershipCommandSupport.ValidationError(
                    "Negative coverage count cannot exceed the issued Membership visit limit.",
                    "negativeCoverageCount");
            }

            if (usesNewMembershipCoverage
                && issue.NegativeCoverageCount
                    > negativeSelection.OpenConcreteVisits.Count)
            {
                return IssueMembershipCommandSupport.ValidationError(
                    "Negative coverage count cannot exceed the current open concrete negative Visit count.",
                    "negativeCoverageCount");
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
                    membershipType.DeactivatedAt,
                    MapMembershipTypeKind(membershipType.Kind));
                preparation = MembershipIssuePreparationPolicy.Prepare(
                    issue.ClientId,
                    catalogItem,
                    issue.StartDate,
                    existingNegativeState,
                    issue.NegativeHandlingDecision,
                    issue.NegativeCoverageCount,
                    BusinessTimeZone.GetBusinessDate(recordedAt));
            }
            catch (ArgumentOutOfRangeException exception)
                when (exception.ParamName == "durationDays")
            {
                return IssueMembershipCommandSupport.ValidationError(
                    "Start date and membership duration exceed the supported calendar range.",
                    "startDate");
            }
            catch (ArgumentException)
                when (existingNegativeState is not null
                    && issue.NegativeHandlingDecision is null)
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.NegativeDecisionRequired,
                    "An explicit negative handling decision is required.",
                    "negativeHandlingDecision");
            }
            catch (ArgumentException)
                when (existingNegativeState is not null
                    && issue.NegativeHandlingDecision is not null)
            {
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.MembershipNotEligible,
                    "The selected negative handling decision is not available.",
                    "negativeHandlingDecision");
            }
            catch (ArgumentException)
                when (existingNegativeState is null
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
                IssuanceMode = "sale",
                StartDate = preparation.StartDate,
                BaseEndDate = preparation.BaseEndDate,
                IssuedAt = recordedAt,
                IssuedByAccountId = issue.Envelope.Actor.AccountId.Value,
                Status = MembershipQuerySupport.ActiveMembershipStatus,
                EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                    issue.Envelope.EntryOrigin),
                EntryBatchId = entryBatchId,
                Comment = issue.Envelope.Comment,
            };
            dbContext.Set<IssuedMembershipRecord>().Add(membership);
            if (paperBinding.Reference is { } membershipPaperReference)
            {
                paperFallbackBinder.LinkEntity(
                    membershipPaperReference,
                    MembershipAuditActions.MembershipEntityType,
                    membershipId);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            if (!await dbContext.Set<IssuedMembershipRecord>()
                    .AsNoTracking()
                    .AnyAsync(
                        candidate => candidate.Id == membershipId,
                        cancellationToken))
            {
                await MembershipCommandSupport.RollBackAndClearAsync(
                    dbContext,
                    transaction);
                return IssueMembershipCommandSupport.Error(
                    CommandErrorCode.RecalculationFailed,
                    "New membership source could not be persisted for recalculation.");
            }

            Guid? negativeClosureId = null;
            var sourceMembershipIds = new HashSet<Guid>();
            var coveredVisitIds = new List<Guid>();
            if (usesNewMembershipCoverage)
            {
                negativeClosureId = Guid.NewGuid();
                var coveredVisits = preparation.CoveredNegativeVisits;
                var closureRecord = new MembershipNegativeClosureRecord
                {
                    Id = negativeClosureId.Value,
                    ClientId = issue.ClientId,
                    ClosureType = "new_membership",
                    CoveringMembershipId = membershipId,
                    OldestOpenNegativeVisitId = coveredVisits[0].VisitId,
                    VisitsCount = coveredVisits.Count,
                    Comment = issue.Envelope.Comment,
                    OccurredAt = issue.Envelope.OccurredAt ?? recordedAt,
                    RecordedAt = recordedAt,
                    RecordedByAccountId = issue.Envelope.Actor.AccountId.Value,
                    SessionId = issue.Envelope.Actor.SessionId.Value,
                    EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                        issue.Envelope.EntryOrigin),
                    EntryBatchId = entryBatchId,
                    IdempotencyKey = issue.IdempotencyKey,
                    Status = "active",
                };
                dbContext.Set<MembershipNegativeClosureRecord>().Add(closureRecord);
                if (paperBinding.Reference is { } closurePaperReference)
                {
                    paperFallbackBinder.LinkEntity(
                        closurePaperReference,
                        MembershipNegativeClosureAuditActions.EntityType,
                        negativeClosureId.Value);
                }

                var sequence = 0;
                foreach (var coveredVisit in coveredVisits)
                {
                    sequence++;
                    var itemId = Guid.NewGuid();
                    var newConsumptionId = Guid.NewGuid();
                    sourceMembershipIds.Add(coveredVisit.SourceMembershipId);
                    coveredVisitIds.Add(coveredVisit.VisitId);
                    dbContext.Set<VisitConsumptionRecord>().Add(
                        new VisitConsumptionRecord
                        {
                            Id = newConsumptionId,
                            VisitId = coveredVisit.VisitId,
                            ClientId = issue.ClientId,
                            VisitKind = "membership",
                            MembershipId = membershipId,
                            ConsumptionType = "negative_coverage",
                            SourceFactType = "negative_closure_item",
                            SourceFactId = itemId,
                            RecordedAt = recordedAt,
                            RecordedByAccountId = issue.Envelope.Actor.AccountId.Value,
                            RecordedSessionId = issue.Envelope.Actor.SessionId.Value,
                            Status = "active",
                        });
                    if (paperBinding.Reference is { } consumptionPaperReference)
                    {
                        paperFallbackBinder.LinkEntity(
                            consumptionPaperReference,
                            "visit_consumption",
                            newConsumptionId);
                    }
                    dbContext.Set<MembershipNegativeClosureItemRecord>().Add(
                        new MembershipNegativeClosureItemRecord
                        {
                            Id = itemId,
                            NegativeClosureId = negativeClosureId.Value,
                            ClientId = issue.ClientId,
                            ClosureLineId = null,
                            Sequence = sequence,
                            VisitId = coveredVisit.VisitId,
                            SourceMembershipId = coveredVisit.SourceMembershipId,
                            OldConsumptionId = coveredVisit.OldConsumptionId,
                            CoveringMembershipId = membershipId,
                            NewConsumptionId = newConsumptionId,
                            Status = "active",
                        });
                    if (paperBinding.Reference is { } itemPaperReference)
                    {
                        paperFallbackBinder.LinkEntity(
                            itemPaperReference,
                            "membership_negative_closure_item",
                            itemId);
                    }
                }
            }

            var paymentWrite = paymentWriter.StageExactSale(
                issue.Envelope,
                issue.ClientId,
                membershipId,
                preparation.Snapshot.Price,
                entryBatchId,
                recordedAt,
                paperReference: paperBinding.Reference);
            if (paperBinding.Reference is { } paymentPaperReference)
            {
                paperFallbackBinder.LinkEntity(
                    paymentPaperReference,
                    PaymentAuditActions.EntityType,
                    paymentWrite.PaymentId);
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var sourceMembershipId in sourceMembershipIds.Order())
            {
                var sourceRebuild = await stateCacheRebuilder.RebuildAsync(
                    sourceMembershipId,
                    cancellationToken);
                if (!sourceRebuild.Succeeded || sourceRebuild.State is null)
                {
                    await MembershipCommandSupport.RollBackAndClearAsync(
                        dbContext,
                        transaction);
                    return IssueMembershipCommandSupport.Error(
                        CommandErrorCode.RecalculationFailed,
                        "Source membership state could not be rebuilt from coverage facts.");
                }
            }

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
                    "New membership state could not be rebuilt from canonical issue terms and coverage facts.");
            }

            var recalculatedState = rebuildResult.State;
            var remainingNegativeBalance = negativeSelection.TotalNegativeBalance;
            if (usesNewMembershipCoverage)
            {
                var activeMembershipIds = negativeSelection.ActiveMemberships
                    .Select(activeMembership => activeMembership.Id)
                    .ToArray();
                remainingNegativeBalance = await dbContext
                    .Set<MembershipStateCacheRecord>()
                    .Where(cache => activeMembershipIds.Contains(cache.MembershipId))
                    .SumAsync(cache => cache.NegativeBalance, cancellationToken);
                if (remainingNegativeBalance
                    != negativeSelection.TotalNegativeBalance
                        - preparation.CoveredNegativeVisits.Count)
                {
                    await MembershipCommandSupport.RollBackAndClearAsync(
                        dbContext,
                        transaction);
                    return IssueMembershipCommandSupport.Error(
                        CommandErrorCode.RecalculationFailed,
                        "Coverage facts did not produce the expected canonical negative balance.");
                }
            }

            AuditEntryId? negativeClosureAuditEntryId = null;
            if (negativeClosureId is { } closureId)
            {
                object closureRelatedEntityRefs = paperBinding.Reference is { } closureAuditPaperReference
                    ? new
                    {
                        ClientId = issue.ClientId,
                        CoveringMembershipId = membershipId,
                        SalePaymentId = paymentWrite.PaymentId,
                        SalePaymentAuditEntryId = paymentWrite.AuditEntryId.Value,
                        SourceMembershipIds = sourceMembershipIds.Order().ToArray(),
                        VisitIds = coveredVisitIds,
                        closureAuditPaperReference.EntryBatchId,
                        closureAuditPaperReference.EntryBatchRowId,
                        closureAuditPaperReference.PaperSheetNumber,
                        closureAuditPaperReference.LineNumber,
                        PaperExplanation = closureAuditPaperReference.Explanation,
                    }
                    : new
                    {
                        ClientId = issue.ClientId,
                        CoveringMembershipId = membershipId,
                        SalePaymentId = paymentWrite.PaymentId,
                        SalePaymentAuditEntryId = paymentWrite.AuditEntryId.Value,
                        SourceMembershipIds = sourceMembershipIds.Order().ToArray(),
                        VisitIds = coveredVisitIds,
                    };
                negativeClosureAuditEntryId = auditAppender.Append(
                    issue.Envelope,
                    MembershipNegativeClosureAuditActions.Created,
                    MembershipNegativeClosureAuditActions.EntityType,
                    closureId,
                    recordedAt,
                    relatedEntityRefs: closureRelatedEntityRefs,
                    beforeSummary: new
                    {
                        negativeSelection.TotalNegativeBalance,
                        OpenConcreteVisitCount =
                            negativeSelection.OpenConcreteVisits.Count,
                        negativeSelection.UnknownNegativeBalance,
                        OldestOpenNegativeVisitId =
                            negativeSelection.OldestOpenConcreteVisitId,
                    },
                    afterSummary: new
                    {
                        NegativeClosureId = closureId,
                        ClosureType = "new_membership",
                        CoveringMembershipId = membershipId,
                        CoveredVisitIds = coveredVisitIds,
                        CoveredVisitCount = coveredVisitIds.Count,
                        RemainingNegativeBalance = remainingNegativeBalance,
                        ForcedStartDate = preparation.StartDate,
                        CoveringMembershipState = new
                        {
                            recalculatedState.CountedVisits,
                            recalculatedState.RemainingVisits,
                            recalculatedState.EffectiveEndDate,
                            recalculatedState.LastCountedVisitAt,
                        },
                        OccurredAt = issue.Envelope.OccurredAt ?? recordedAt,
                        RecordedAt = recordedAt,
                        EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                            issue.Envelope.EntryOrigin),
                        EntryBatchId = entryBatchId,
                        Status = "active",
                    });
            }

            var membershipRelatedEntityRefs = new Dictionary<string, object?>
            {
                ["clientId"] = issue.ClientId,
                ["membershipTypeId"] = issue.MembershipTypeId,
                ["paymentId"] = paymentWrite.PaymentId,
            };
            if (negativeClosureId is not null)
            {
                membershipRelatedEntityRefs["negativeClosureId"] = negativeClosureId;
                membershipRelatedEntityRefs["negativeClosureAuditEntryId"] =
                    negativeClosureAuditEntryId?.Value;
                membershipRelatedEntityRefs["sourceMembershipIds"] =
                    sourceMembershipIds.Order().ToArray();
                membershipRelatedEntityRefs["coveredVisitIds"] = coveredVisitIds;
            }
            if (paperBinding.Reference is { } membershipAuditPaperReference)
            {
                membershipRelatedEntityRefs["entryBatchId"] =
                    membershipAuditPaperReference.EntryBatchId;
                membershipRelatedEntityRefs["entryBatchRowId"] =
                    membershipAuditPaperReference.EntryBatchRowId;
                membershipRelatedEntityRefs["paperSheetNumber"] =
                    membershipAuditPaperReference.PaperSheetNumber;
                membershipRelatedEntityRefs["lineNumber"] =
                    membershipAuditPaperReference.LineNumber;
                membershipRelatedEntityRefs["paperExplanation"] =
                    membershipAuditPaperReference.Explanation;
            }

            var membershipAfterSummary = new Dictionary<string, object?>
            {
                ["membershipId"] = membershipId,
                ["issuanceMode"] = membership.IssuanceMode,
                ["clientId"] = issue.ClientId,
                ["membershipTypeId"] = issue.MembershipTypeId,
                ["snapshot"] = new
                {
                    preparation.Snapshot.TypeName,
                    preparation.Snapshot.DurationDays,
                    preparation.Snapshot.VisitsLimit,
                    PriceAmount = preparation.Snapshot.Price.Amount,
                    PriceCurrency = preparation.Snapshot.Price.Currency,
                },
                ["startDate"] = preparation.StartDate,
                ["baseEndDate"] = preparation.BaseEndDate,
                ["issuedAt"] = membership.IssuedAt,
                ["status"] = membership.Status,
                ["entryOrigin"] = membership.EntryOrigin,
                ["entryBatchId"] = membership.EntryBatchId,
                ["comment"] = membership.Comment,
                ["negativeHandlingDecision"] =
                    IssueMembershipCommandSupport.MapNegativeHandlingDecision(
                        preparation.NegativeHandlingDecision),
                ["existingNegativeState"] = preparation.ExistingNegativeState is null
                    ? null
                    : new
                    {
                        preparation.ExistingNegativeState.NegativeBalance,
                        preparation.ExistingNegativeState.FirstNegativeVisitDate,
                        preparation.ExistingNegativeState.OpenConcreteVisitCount,
                        preparation.ExistingNegativeState.UnknownNegativeBalance,
                    },
                ["payment"] = new
                {
                    paymentWrite.PaymentId,
                    PaymentAuditEntryId = paymentWrite.AuditEntryId.Value,
                    Amount = preparation.Snapshot.Price.Amount,
                    Currency = preparation.Snapshot.Price.Currency,
                    Method = "cash",
                    PaymentContext = "membership_sale",
                    OccurredAt = issue.Envelope.OccurredAt ?? recordedAt,
                },
                ["initialState"] = new
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
            };
            if (negativeClosureId is not null)
            {
                membershipAfterSummary["negativeCoverage"] = new
                {
                    NegativeClosureId = negativeClosureId.Value,
                    Count = preparation.CoveredNegativeVisits.Count,
                    CoveredVisitIds = coveredVisitIds,
                    RemainingExistingNegativeBalance = remainingNegativeBalance,
                    ForcedStartDate = preparation.StartDate,
                    preparation.IsAlreadyExpiredAtIssue,
                };
            }

            var auditEntryId = auditAppender.Append(
                issue.Envelope,
                MembershipAuditActions.Issued,
                MembershipAuditActions.MembershipEntityType,
                membershipId,
                recordedAt,
                relatedEntityRefs: membershipRelatedEntityRefs,
                afterSummary: membershipAfterSummary);

            dbContext.Set<CommandIdempotencyRecord>().Add(
                IssueMembershipCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    issue,
                    recordedAt,
                    membershipId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var warningCodes = new List<string>();
            if (issue.NegativeHandlingDecision
                    == MembershipNegativeHandlingDecision.LeaveVisible
                || usesNewMembershipCoverage && remainingNegativeBalance > 0)
            {
                warningCodes.Add(MembershipWarningCodes.NegativeBalance);
            }

            if (preparation.IsAlreadyExpiredAtIssue)
            {
                warningCodes.Add(MembershipWarningCodes.ExpiredByDate);
            }

            return IssueMembershipCommandSupport.Success(
                membershipId,
                issue.ClientId,
                auditEntryId,
                warningCodes);
        }
        catch (Exception exception)
        {
            var postgresException = MembershipCommandSupport.FindPostgresException(exception);

            if (postgresException is null
                || !MembershipCommandSupport.TryMapPostgresFailure(
                    postgresException,
                    out var errorResult))
            {
                await MembershipCommandSupport.RollBackAndClearAsync(
                    dbContext,
                    transaction);
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

    private static MembershipTypeKind MapMembershipTypeKind(string kind) => kind switch
    {
        "ordinary" => MembershipTypeKind.Ordinary,
        "one_off" => MembershipTypeKind.OneOff,
        _ => throw new InvalidOperationException("Stored membership type kind is invalid."),
    };

}
