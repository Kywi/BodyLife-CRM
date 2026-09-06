using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class CorrectNegativeVisitCoverageCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    INegativeClosurePaymentWriter paymentWriter,
    MembershipNegativeVisitSelector negativeVisitSelector,
    MembershipStateCacheRebuilder stateCacheRebuilder,
    IPaymentDayReconciliationStatusProvider dayReconciliationStatusProvider,
    TimeProvider timeProvider,
    PaperFallbackEntryRowBinder? paperFallbackEntryRowBinder = null)
    : IBodyLifeCommandHandler<CorrectNegativeVisitCoverageCommand>
{
    private const string CommandName = "CorrectNegativeVisitCoverage";

    public async Task<CommandResult> ExecuteAsync(
        CorrectNegativeVisitCoverageCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = CorrectNegativeVisitCoverageCommandSupport.ValidateAndNormalize(
            command,
            out var normalized);
        if (validation is not null)
        {
            return validation;
        }

        var correction = normalized!;
        var rowBinder = paperFallbackEntryRowBinder
            ?? new PaperFallbackEntryRowBinder(dbContext);
        if (!MembershipCommandSupport.IsAllowedActorShape(correction.Envelope.Actor))
        {
            return CorrectNegativeVisitCoverageCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to correct negative coverage.");
        }

        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = CorrectNegativeVisitCoverageCommandSupport
            .CreateFingerprint(correction);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await MembershipCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    correction.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return await RollBackAsync(
                    CorrectNegativeVisitCoverageCommandSupport.Error(
                        CommandErrorCode.PermissionDenied,
                        "The Owner or Admin account or session is not active."));
            }

            var existingIdempotency = await MembershipCommandSupport
                .FindIdempotencyAsync(
                    dbContext,
                    CommandName,
                    correction.IdempotencyKey,
                    cancellationToken);
            if (existingIdempotency is not null)
            {
                return await ReplayOrRejectDuplicateAsync(
                    existingIdempotency,
                    correction,
                    fingerprint,
                    cancellationToken);
            }

            var originalReference = await dbContext
                .Set<MembershipNegativeClosureRecord>()
                .AsNoTracking()
                .Where(closure => closure.Id == correction.OriginalNegativeClosureId)
                .Select(closure => new { closure.ClientId })
                .SingleOrDefaultAsync(cancellationToken);
            if (originalReference is null)
            {
                return await RollBackAsync(
                    CorrectNegativeVisitCoverageCommandSupport.Error(
                        CommandErrorCode.NotFound,
                        "Negative coverage was not found.",
                        "originalNegativeClosureId"));
            }

            if (!await LockClientAsync(originalReference.ClientId, cancellationToken))
            {
                return await RollBackAsync(
                    CorrectNegativeVisitCoverageCommandSupport.Error(
                        CommandErrorCode.NotFound,
                        "Coverage Client was not found.",
                        "originalNegativeClosureId"));
            }

            existingIdempotency = await MembershipCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                correction.IdempotencyKey,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return await ReplayOrRejectDuplicateAsync(
                    existingIdempotency,
                    correction,
                    fingerprint,
                    cancellationToken);
            }

            var paperBinding = await rowBinder.PrepareAsync(
                correction.Envelope,
                PaperFallbackEventType.CorrectionOrCancellation,
                cancellationToken);
            if (paperBinding.RowAlreadyLinked)
            {
                existingIdempotency = await MembershipCommandSupport.FindIdempotencyAsync(
                    dbContext,
                    CommandName,
                    correction.IdempotencyKey,
                    cancellationToken);
                if (existingIdempotency is not null)
                {
                    return await ReplayOrRejectDuplicateAsync(
                        existingIdempotency,
                        correction,
                        fingerprint,
                        cancellationToken);
                }
            }

            if (paperBinding.Error is not null)
            {
                return await RollBackAsync(paperBinding.Error);
            }

            var paperReference = paperBinding.Reference;
            var entryBatchId = paperReference?.EntryBatchId;

            var initialSelectionResult = await negativeVisitSelector
                .SelectForUpdateAfterClientLockAsync(
                    originalReference.ClientId,
                    cancellationToken);
            if (initialSelectionResult.Status
                != MembershipNegativeVisitSelectionStatus.Succeeded)
            {
                return await RollBackAsync(RecalculationFailure(
                    "Canonical membership state is missing or inconsistent."));
            }

            var original = await LockClosureAsync(
                correction.OriginalNegativeClosureId,
                cancellationToken);
            if (original is null || original.ClientId != originalReference.ClientId)
            {
                return await RollBackAsync(
                    CorrectNegativeVisitCoverageCommandSupport.Error(
                        CommandErrorCode.NotFound,
                        "Negative coverage was not found.",
                        "originalNegativeClosureId"));
            }

            if (!string.Equals(original.Status, "active", StringComparison.Ordinal)
                || await HasCorrectionAsync(original.Id, cancellationToken))
            {
                return await RollBackAsync(
                    CorrectNegativeVisitCoverageCommandSupport.Error(
                        CommandErrorCode.AlreadyCanceled,
                        "Negative coverage has already been canceled or replaced.",
                        "originalNegativeClosureId"));
            }

            if (!ReplacementShapeMatchesOriginal(correction, original.ClosureType))
            {
                return await RollBackAsync(
                    CorrectNegativeVisitCoverageCommandSupport.ValidationError(
                        "Replacement must preserve the original coverage method.",
                        "replacement"));
            }

            var items = await LockItemsAsync(original.Id, cancellationToken);
            if (items.Length != original.VisitsCount || items.Length == 0)
            {
                return await RollBackAsync(RecalculationFailure(
                    "Negative coverage items are inconsistent."));
            }

            var sourceMembershipIds = items
                .Select(item => item.SourceMembershipId)
                .ToHashSet();
            var affectedMembershipIds = new HashSet<Guid>(sourceMembershipIds);
            if (original.CoveringMembershipId is { } coveringMembershipId)
            {
                affectedMembershipIds.Add(coveringMembershipId);
            }

            var newConsumptions = await LockNewConsumptionsAsync(
                items,
                cancellationToken);
            if (string.Equals(original.ClosureType, "new_membership", StringComparison.Ordinal)
                && newConsumptions.Length != items.Length
                || string.Equals(original.ClosureType, "one_off", StringComparison.Ordinal)
                && newConsumptions.Length != 0)
            {
                return await RollBackAsync(RecalculationFailure(
                    "Negative coverage consumption shape is inconsistent."));
            }

            var originalPayments = await LockClosurePaymentsAsync(
                original.Id,
                cancellationToken);
            if (string.Equals(original.ClosureType, "one_off", StringComparison.Ordinal)
                && originalPayments.Length != 1
                || string.Equals(original.ClosureType, "new_membership", StringComparison.Ordinal)
                && originalPayments.Length != 0)
            {
                return await RollBackAsync(RecalculationFailure(
                    "Negative coverage Payment shape is inconsistent."));
            }

            PreparedOneOffClosureLines? preparedOneOffReplacement = null;
            if (correction.Mode == NegativeVisitCoverageCorrectionMode.Replace
                && string.Equals(original.ClosureType, "one_off", StringComparison.Ordinal))
            {
                var preparationResult = await OneOffNegativeClosureLinePreparer
                    .PrepareAsync(
                        dbContext,
                        correction.ReplacementOneOffLines,
                        "replacementOneOffLines",
                        cancellationToken);
                if (preparationResult.Error is not null)
                {
                    return await RollBackAsync(preparationResult.Error);
                }

                preparedOneOffReplacement = preparationResult.Preparation;
            }

            var restoredSelectionResult = await negativeVisitSelector
                .SelectHypotheticallyWithoutClosureAsync(original.ClientId, original.Id, cancellationToken);
            if (restoredSelectionResult.Status != MembershipNegativeVisitSelectionStatus.Succeeded)
            {
                return await RollBackAsync(RecalculationFailure(
                    "Restored negative Visit state is missing or inconsistent."));
            }
            var restoredSelection = restoredSelectionResult.Selection!;
            var replacementVisitsCount = 0;
            if (correction.Mode == NegativeVisitCoverageCorrectionMode.Replace)
            {
                if (restoredSelection.OldestOpenConcreteVisitId
                    != correction.ExpectedOldestOpenNegativeVisitId)
                {
                    return await RollBackAsync(
                        CorrectNegativeVisitCoverageCommandSupport.Error(
                            CommandErrorCode.StaleState,
                            "The oldest open negative Visit changed. Refresh canonical state.",
                            "expectedOldestOpenNegativeVisitId"));
                }

                replacementVisitsCount = preparedOneOffReplacement?.VisitsCount
                    ?? correction.ReplacementNewMembershipCoverageCount!.Value;
                if (replacementVisitsCount > restoredSelection.OpenConcreteVisits.Count)
                {
                    return await RollBackAsync(
                        CorrectNegativeVisitCoverageCommandSupport.ValidationError(
                            "Replacement cannot exceed open concrete negative Visits.",
                            "replacement"));
                }

                if (string.Equals(
                        original.ClosureType,
                        "new_membership",
                        StringComparison.Ordinal)
                    && !await HasCoveringCapacityAsync(
                        original.CoveringMembershipId!.Value,
                        replacementVisitsCount,
                        original.Id,
                        restoredSelection.OpenConcreteVisits[0].BusinessDate,
                        cancellationToken))
                {
                    return await RollBackAsync(
                        CorrectNegativeVisitCoverageCommandSupport.Error(
                            CommandErrorCode.MembershipNotEligible,
                            "The covering Membership cannot accept the replacement allocation.",
                            "replacementNewMembershipCoverageCount"));
                }

            }
            var replacementPlan = restoredSelection.OpenConcreteVisits.Take(replacementVisitsCount)
                .Select(candidate => new ReplacementCoverageItemPlan(
                    candidate, Guid.NewGuid(),
                    original.CoveringMembershipId.HasValue ? Guid.NewGuid() : null))
                .ToArray();
            foreach (var item in replacementPlan)
            {
                affectedMembershipIds.Add(item.Candidate.SourceMembershipId);
            }
            var projectedCoverage = replacementPlan.Select(item => new MembershipNegativeCoverageSourceFact(
                item.ItemId, item.Candidate.VisitId, item.Candidate.SourceMembershipId,
                original.CoveringMembershipId, item.Candidate.BusinessDate,
                item.Candidate.OccurredAt, recordedAt, MembershipNegativeCoverageSourceStatus.Active))
                .ToArray();
            if (await MembershipLifecycleCorrectionGuard.FindBlockedMembershipAsync(
                    dbContext, affectedMembershipIds, null, original.Id,
                    projectedCoverage, cancellationToken) is not null)
            {
                return await RollBackAsync(CorrectNegativeVisitCoverageCommandSupport.Error(
                    CommandErrorCode.LifecycleDependency,
                    "This correction would leave unused visits on a closed Membership.",
                    "originalNegativeClosureId"));
            }

            var originalLines = await dbContext
                .Set<MembershipNegativeClosureLineRecord>()
                .AsNoTracking()
                .Where(line => line.NegativeClosureId == original.Id)
                .OrderBy(line => line.Sequence)
                .ToArrayAsync(cancellationToken);
            var initialSelection = initialSelectionResult.Selection!;
            var beforeSummary = SummarizeOriginal(
                original,
                items,
                originalLines,
                originalPayments.SingleOrDefault(),
                initialSelection.TotalNegativeBalance);
            var correctionId = Guid.NewGuid();
            var targetStatus = correction.Mode
                == NegativeVisitCoverageCorrectionMode.Cancel
                ? "canceled"
                : "replaced";
            original.Status = targetStatus;
            foreach (var item in items)
            {
                item.Status = targetStatus;
            }

            foreach (var consumption in newConsumptions)
            {
                consumption.Status = "canceled";
            }

            var originalPayment = originalPayments.SingleOrDefault();
            var changedAfterClose = originalPayment is not null
                && await IsChangedAfterCloseAsync(
                    originalPayment.OccurredAt,
                    correction.Mode == NegativeVisitCoverageCorrectionMode.Replace
                        ? correction.Envelope.OccurredAt
                        : null,
                    cancellationToken);
            if (changedAfterClose
                && correction.Envelope.Actor.Role != ActorRole.Owner)
            {
                return await RollBackAsync(
                    CorrectNegativeVisitCoverageCommandSupport.Error(
                        CommandErrorCode.DayClosedRequiresOwner,
                        "Only the Owner can correct negative coverage that affects a reconciled cash day.",
                        "originalNegativeClosureId"));
            }

            if (originalPayment is not null)
            {
                originalPayment.Status = targetStatus;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (!await RebuildMembershipsAsync(
                    affectedMembershipIds,
                    cancellationToken))
            {
                return await RollBackAsync(RecalculationFailure(
                    "Membership state could not be rebuilt after removing coverage."));
            }

            Guid? replacementClosureId = null;
            Guid? replacementPaymentId = null;
            AuditEntryId? replacementPaymentAuditId = null;
            AuditEntryId? replacementClosureAuditId = null;
            PaymentRecord? replacementPayment = null;
            IReadOnlyList<Guid> replacementVisitIds = [];
            IReadOnlyList<object> replacementLineSummaries = [];
            if (correction.Mode == NegativeVisitCoverageCorrectionMode.Replace)
            {
                replacementClosureId = Guid.NewGuid();
                var replacement = new MembershipNegativeClosureRecord
                {
                    Id = replacementClosureId.Value,
                    ClientId = original.ClientId,
                    ClosureType = original.ClosureType,
                    CoveringMembershipId = original.CoveringMembershipId,
                    OldestOpenNegativeVisitId =
                        restoredSelection.OldestOpenConcreteVisitId!.Value,
                    VisitsCount = replacementVisitsCount,
                    Comment = correction.Envelope.Comment,
                    OccurredAt = correction.Envelope.OccurredAt!.Value,
                    RecordedAt = recordedAt,
                    RecordedByAccountId = correction.Envelope.Actor.AccountId.Value,
                    SessionId = correction.Envelope.Actor.SessionId.Value,
                    EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                        correction.Envelope.EntryOrigin),
                    EntryBatchId = entryBatchId,
                    IdempotencyKey = correction.IdempotencyKey,
                    Status = "active",
                };
                dbContext.Set<MembershipNegativeClosureRecord>().Add(replacement);
                if (paperReference is not null)
                {
                    rowBinder.LinkEntity(
                        paperReference,
                        MembershipNegativeClosureAuditActions.EntityType,
                        replacement.Id);
                }

                var staged = StageReplacementFacts(
                    correction,
                    replacement,
                    replacementPlan,
                    preparedOneOffReplacement,
                    affectedMembershipIds,
                    rowBinder,
                    paperReference);
                replacementVisitIds = staged.VisitIds;
                replacementLineSummaries = staged.LineSummaries;
                if (preparedOneOffReplacement is not null)
                {
                    var paymentWrite = paymentWriter.StageExactClosurePayment(
                        correction.Envelope,
                        original.ClientId,
                        replacement.Id,
                        new Money(
                            preparedOneOffReplacement.TotalAmount,
                            preparedOneOffReplacement.Currency),
                        entryBatchId,
                        recordedAt,
                        paperReference,
                        correctionId,
                        changedAfterClose);
                    replacementPaymentId = paymentWrite.PaymentId;
                    replacementPaymentAuditId = paymentWrite.AuditEntryId;
                    replacementPayment = dbContext.Set<PaymentRecord>().Local
                        .Single(payment => payment.Id == paymentWrite.PaymentId);
                    if (paperReference is not null)
                    {
                        rowBinder.LinkEntity(
                            paperReference,
                            PaymentAuditActions.EntityType,
                            paymentWrite.PaymentId);
                    }
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                if (!await RebuildMembershipsAsync(
                        affectedMembershipIds,
                        cancellationToken))
                {
                    return await RollBackAsync(RecalculationFailure(
                        "Membership state could not be rebuilt from replacement coverage."));
                }
            }

            var remainingNegativeBalance = await GetClientNegativeBalanceAsync(
                original.ClientId,
                cancellationToken);
            var expectedRemaining = restoredSelection.TotalNegativeBalance
                - replacementVisitsCount;
            if (remainingNegativeBalance != expectedRemaining)
            {
                return await RollBackAsync(RecalculationFailure(
                    "Coverage correction produced an unexpected negative balance."));
            }

            var correctionRecord = new MembershipNegativeClosureCorrectionRecord
            {
                Id = correctionId,
                OriginalClosureId = original.Id,
                ReplacementClosureId = replacementClosureId,
                Mode = correction.Mode == NegativeVisitCoverageCorrectionMode.Cancel
                    ? "cancel"
                    : "replace",
                Reason = correction.Envelope.Reason!,
                OccurredAt = correction.Envelope.OccurredAt!.Value,
                RecordedAt = recordedAt,
                RecordedByAccountId = correction.Envelope.Actor.AccountId.Value,
                SessionId = correction.Envelope.Actor.SessionId.Value,
                EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                    correction.Envelope.EntryOrigin),
                EntryBatchId = entryBatchId,
                IdempotencyKey = correction.IdempotencyKey,
            };
            dbContext.Set<MembershipNegativeClosureCorrectionRecord>()
                .Add(correctionRecord);
            if (paperReference is not null)
            {
                rowBinder.LinkEntity(
                    paperReference,
                    "membership_negative_closure_correction",
                    correctionId);
            }

            if (replacementClosureId is { } createdClosureId)
            {
                replacementClosureAuditId = auditAppender.Append(
                    correction.Envelope,
                    MembershipNegativeClosureAuditActions.Created,
                    MembershipNegativeClosureAuditActions.EntityType,
                    createdClosureId,
                    recordedAt,
                    relatedEntityRefs: paperReference is { } replacementAuditPaperReference
                    ? new
                    {
                        ClientId = original.ClientId,
                        CorrectionId = correctionId,
                        OriginalNegativeClosureId = original.Id,
                        original.CoveringMembershipId,
                        ReplacementPaymentId = replacementPaymentId,
                        ReplacementPaymentAuditEntryId =
                            replacementPaymentAuditId?.Value,
                        SourceMembershipIds = affectedMembershipIds.Order().ToArray(),
                        VisitIds = replacementVisitIds,
                        replacementAuditPaperReference.EntryBatchId,
                        replacementAuditPaperReference.EntryBatchRowId,
                        replacementAuditPaperReference.PaperSheetNumber,
                        replacementAuditPaperReference.LineNumber,
                        PaperExplanation = replacementAuditPaperReference.Explanation,
                    }
                    : new
                    {
                        ClientId = original.ClientId,
                        CorrectionId = correctionId,
                        OriginalNegativeClosureId = original.Id,
                        original.CoveringMembershipId,
                        ReplacementPaymentId = replacementPaymentId,
                        ReplacementPaymentAuditEntryId =
                            replacementPaymentAuditId?.Value,
                        SourceMembershipIds = affectedMembershipIds.Order().ToArray(),
                        VisitIds = replacementVisitIds,
                    },
                    beforeSummary: new
                    {
                        RestoredNegativeBalance = restoredSelection.TotalNegativeBalance,
                        restoredSelection.UnknownNegativeBalance,
                        restoredSelection.OldestOpenConcreteVisitId,
                    },
                    afterSummary: new
                    {
                        NegativeClosureId = createdClosureId,
                        original.ClosureType,
                        VisitsCount = replacementVisitsCount,
                        Lines = replacementLineSummaries,
                        CoveredVisitIds = replacementVisitIds,
                        original.CoveringMembershipId,
                        ReplacementPaymentId = replacementPaymentId,
                        ReplacementPaymentAuditEntryId =
                            replacementPaymentAuditId?.Value,
                        ReplacementPayment = replacementPayment is null
                            ? null
                            : SummarizePayment(replacementPayment),
                        RemainingNegativeBalance = remainingNegativeBalance,
                        OccurredAt = correction.Envelope.OccurredAt!.Value,
                        RecordedAt = recordedAt,
                        correctionRecord.EntryOrigin,
                        correctionRecord.EntryBatchId,
                        ChangedAfterClose = changedAfterClose,
                        Status = "active",
                    },
                    changedAfterClose: changedAfterClose);
            }

            AuditEntryId? paymentLifecycleAuditId = null;
            if (originalPayment is not null)
            {
                paymentLifecycleAuditId = AppendPaymentLifecycleAudit(
                    correction,
                    original,
                    originalPayment,
                    correctionId,
                    replacementClosureId,
                    replacementPaymentId,
                    replacementPayment,
                    replacementLineSummaries,
                    replacementPaymentAuditId,
                    recordedAt,
                    paperReference,
                    changedAfterClose);
            }

            var actionType = correction.Mode
                == NegativeVisitCoverageCorrectionMode.Cancel
                ? MembershipNegativeClosureAuditActions.Canceled
                : MembershipNegativeClosureAuditActions.Replaced;
            var auditEntryId = auditAppender.Append(
                correction.Envelope,
                actionType,
                MembershipNegativeClosureAuditActions.EntityType,
                original.Id,
                recordedAt,
                relatedEntityRefs: paperReference is { } lifecycleAuditPaperReference
                ? new
                {
                    ClientId = original.ClientId,
                    CorrectionId = correctionId,
                    ReplacementNegativeClosureId = replacementClosureId,
                    ReplacementClosureAuditId = replacementClosureAuditId?.Value,
                    OriginalPaymentId = originalPayment?.Id,
                    ReplacementPaymentId = replacementPaymentId,
                    PaymentLifecycleAuditId = paymentLifecycleAuditId?.Value,
                    MembershipIds = affectedMembershipIds.Order().ToArray(),
                    lifecycleAuditPaperReference.EntryBatchId,
                    lifecycleAuditPaperReference.EntryBatchRowId,
                    lifecycleAuditPaperReference.PaperSheetNumber,
                    lifecycleAuditPaperReference.LineNumber,
                    PaperExplanation = lifecycleAuditPaperReference.Explanation,
                }
                : new
                {
                    ClientId = original.ClientId,
                    CorrectionId = correctionId,
                    ReplacementNegativeClosureId = replacementClosureId,
                    ReplacementClosureAuditId = replacementClosureAuditId?.Value,
                    OriginalPaymentId = originalPayment?.Id,
                    ReplacementPaymentId = replacementPaymentId,
                    PaymentLifecycleAuditId = paymentLifecycleAuditId?.Value,
                    MembershipIds = affectedMembershipIds.Order().ToArray(),
                },
                beforeSummary: beforeSummary,
                afterSummary: new
                {
                    Correction = new
                    {
                        CorrectionId = correctionId,
                        Mode = correctionRecord.Mode,
                        correctionRecord.Reason,
                        correctionRecord.OccurredAt,
                        correctionRecord.RecordedAt,
                        correctionRecord.EntryOrigin,
                        correctionRecord.EntryBatchId,
                        ChangedAfterClose = changedAfterClose,
                    },
                    OriginalClosure = new
                    {
                        original.Id,
                        original.ClosureType,
                        Status = targetStatus,
                    },
                    Replacement = replacementClosureId is null
                        ? null
                        : new
                        {
                            NegativeClosureId = replacementClosureId.Value,
                            VisitsCount = replacementVisitsCount,
                            VisitIds = replacementVisitIds,
                            PaymentId = replacementPaymentId,
                        },
                    RemainingNegativeBalance = remainingNegativeBalance,
                },
                changedAfterClose: changedAfterClose);

            dbContext.Set<CommandIdempotencyRecord>().Add(
                CorrectNegativeVisitCoverageCommandSupport
                    .CreateSucceededIdempotencyRecord(
                        CommandName,
                        correction,
                        recordedAt,
                        correctionId,
                        original.ClientId,
                        auditEntryId,
                        fingerprint));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CorrectNegativeVisitCoverageCommandSupport.Success(
                correctionId,
                original.Id,
                replacementClosureId,
                original.ClientId,
                affectedMembershipIds,
                originalPayment?.Id,
                replacementPaymentId,
                auditEntryId,
                remainingNegativeBalance,
                changedAfterClose);
        }
        catch (Exception exception)
        {
            var postgresException = MembershipCommandSupport.FindPostgresException(
                exception);
            if (postgresException is not null
                && CorrectNegativeVisitCoverageCommandSupport.TryMapPostgresFailure(
                    postgresException,
                    out var error))
            {
                return await RollBackAsync(error);
            }

            await MembershipCommandSupport.RollBackAndClearAsync(
                dbContext,
                transaction);
            throw;
        }

        async Task<CommandResult> RollBackAsync(CommandResult result)
        {
            await MembershipCommandSupport.RollBackAndClearAsync(
                dbContext,
                transaction);
            return result;
        }
    }

    private async Task<CommandResult> ReplayOrRejectDuplicateAsync(
        CommandIdempotencyRecord record,
        NormalizedNegativeCoverageCorrection correction,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (!CorrectNegativeVisitCoverageCommandSupport.TryGetSuccessfulReplay(
                record,
                correction,
                fingerprint,
                out var correctionId,
                out var clientId,
                out var auditEntryId))
        {
            return CorrectNegativeVisitCoverageCommandSupport.DuplicateSubmission();
        }

        var correctionRecord = await dbContext
            .Set<MembershipNegativeClosureCorrectionRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == correctionId
                    && item.OriginalClosureId
                        == correction.OriginalNegativeClosureId,
                cancellationToken);
        var expectedMode = correction.Mode
            == NegativeVisitCoverageCorrectionMode.Cancel
            ? "cancel"
            : "replace";
        if (correctionRecord is null
            || !string.Equals(correctionRecord.Mode, expectedMode, StringComparison.Ordinal))
        {
            return CorrectNegativeVisitCoverageCommandSupport.DuplicateSubmission();
        }

        var original = await dbContext.Set<MembershipNegativeClosureRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                closure => closure.Id == correction.OriginalNegativeClosureId
                    && closure.ClientId == clientId,
                cancellationToken);
        if (original is null)
        {
            return CorrectNegativeVisitCoverageCommandSupport.DuplicateSubmission();
        }

        var membershipIds = (await dbContext
            .Set<MembershipNegativeClosureItemRecord>()
            .AsNoTracking()
            .Where(item => item.NegativeClosureId == original.Id
                || correctionRecord.ReplacementClosureId.HasValue
                && item.NegativeClosureId
                    == correctionRecord.ReplacementClosureId.Value)
            .Select(item => item.SourceMembershipId)
            .Distinct()
            .ToArrayAsync(cancellationToken))
            .ToHashSet();
        if (original.CoveringMembershipId is { } coveringMembershipId)
        {
            membershipIds.Add(coveringMembershipId);
        }

        var payments = await dbContext.Set<PaymentRecord>()
            .AsNoTracking()
            .Where(payment => payment.NegativeClosureId == original.Id
                || correctionRecord.ReplacementClosureId.HasValue
                && payment.NegativeClosureId
                    == correctionRecord.ReplacementClosureId.Value)
            .Select(payment => new
            {
                payment.Id,
                payment.NegativeClosureId,
            })
            .ToArrayAsync(cancellationToken);
        var originalPaymentId = payments
            .SingleOrDefault(payment => payment.NegativeClosureId == original.Id)
            ?.Id;
        var replacementPaymentId = correctionRecord.ReplacementClosureId is { } replacementId
            ? payments.SingleOrDefault(
                payment => payment.NegativeClosureId == replacementId)?.Id
            : null;
        var remaining = await GetClientNegativeBalanceAsync(
            clientId,
            cancellationToken);
        var expectedAction = correction.Mode
            == NegativeVisitCoverageCorrectionMode.Cancel
            ? MembershipNegativeClosureAuditActions.Canceled
            : MembershipNegativeClosureAuditActions.Replaced;
        var audit = await dbContext.Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .Where(entry => entry.Id == auditEntryId.Value
                && entry.ActionType == expectedAction
                && entry.EntityType
                    == MembershipNegativeClosureAuditActions.EntityType
                && entry.EntityId == original.Id)
            .Select(entry => new { entry.ChangedAfterClose })
            .SingleOrDefaultAsync(cancellationToken);
        if (audit is null)
        {
            return CorrectNegativeVisitCoverageCommandSupport.DuplicateSubmission();
        }

        return CorrectNegativeVisitCoverageCommandSupport.Success(
            correctionId,
            original.Id,
            correctionRecord.ReplacementClosureId,
            clientId,
            membershipIds,
            originalPaymentId,
            replacementPaymentId,
            auditEntryId,
            remaining,
            audit.ChangedAfterClose);
    }

    private async Task<bool> LockClientAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<ClientRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.clients
                where id = {clientId}
                for no key update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return rows.Length == 1;
    }

    private async Task<MembershipNegativeClosureRecord?> LockClosureAsync(
        Guid closureId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<MembershipNegativeClosureRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.membership_negative_closures
                where id = {closureId}
                for update
                """)
            .ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    private Task<bool> HasCorrectionAsync(
        Guid closureId,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<MembershipNegativeClosureCorrectionRecord>()
            .AsNoTracking()
            .AnyAsync(
                correction => correction.OriginalClosureId == closureId,
                cancellationToken);
    }

    private Task<MembershipNegativeClosureItemRecord[]> LockItemsAsync(
        Guid closureId,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<MembershipNegativeClosureItemRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.membership_negative_closure_items
                where negative_closure_id = {closureId}
                order by sequence
                for update
                """)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<VisitConsumptionRecord[]> LockNewConsumptionsAsync(
        IReadOnlyCollection<MembershipNegativeClosureItemRecord> items,
        CancellationToken cancellationToken)
    {
        var ids = items
            .Where(item => item.NewConsumptionId.HasValue)
            .Select(item => item.NewConsumptionId!.Value)
            .Order()
            .ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await dbContext.Set<VisitConsumptionRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.visit_consumptions
                where id = any ({ids})
                order by id
                for update
                """)
            .ToArrayAsync(cancellationToken);
    }

    private Task<PaymentRecord[]> LockClosurePaymentsAsync(
        Guid closureId,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<PaymentRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.payments
                where negative_closure_id = {closureId}
                  and payment_context = 'negative_closure'
                order by id
                for update
                """)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<bool> RebuildMembershipsAsync(
        IReadOnlyCollection<Guid> membershipIds,
        CancellationToken cancellationToken)
    {
        foreach (var membershipId in membershipIds.Order())
        {
            var rebuild = await stateCacheRebuilder.RebuildAsync(
                membershipId,
                cancellationToken);
            if (!rebuild.Succeeded || rebuild.State is null)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> HasCoveringCapacityAsync(
        Guid membershipId,
        int replacementCount,
        Guid excludedClosureId,
        DateOnly oldestVisitDate,
        CancellationToken cancellationToken)
    {
        var membership = await dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == membershipId
                && (row.Status == "active" || row.Status == "closed"), cancellationToken);
        if (membership is null)
        {
            return false;
        }
        var restored = await stateCacheRebuilder.CalculateCanonicalStateForNegativeCoveragePreviewAsync(
            membership, excludedClosureId, cancellationToken);
        return membership.StartDate == oldestVisitDate
            && replacementCount <= membership.VisitsLimitSnapshot
            && replacementCount <= restored.State.RemainingVisits
            && restored.State.NegativeBalance == 0;
    }

    private sealed record ReplacementCoverageItemPlan(
        MembershipNegativeVisitCoverageCandidate Candidate,
        Guid ItemId,
        Guid? NewConsumptionId);

    private StagedReplacementFacts StageReplacementFacts(
        NormalizedNegativeCoverageCorrection correction,
        MembershipNegativeClosureRecord replacement,
        IReadOnlyList<ReplacementCoverageItemPlan> replacementPlan,
        PreparedOneOffClosureLines? preparedOneOff,
        ISet<Guid> affectedMembershipIds,
        PaperFallbackEntryRowBinder rowBinder,
        PaperFallbackEntryRowReference? paperReference)
    {
        var visitIds = new List<Guid>(replacement.VisitsCount);
        var lineSummaries = new List<object>();
        var candidateIndex = 0;
        if (preparedOneOff is not null)
        {
            foreach (var preparedLine in preparedOneOff.Lines)
            {
                var lineId = Guid.NewGuid();
                dbContext.Set<MembershipNegativeClosureLineRecord>().Add(
                    new MembershipNegativeClosureLineRecord
                    {
                        Id = lineId,
                        NegativeClosureId = replacement.Id,
                        MembershipTypeId = preparedLine.Record.Id,
                        TypeNameSnapshot = preparedLine.Record.Name,
                        DurationDaysSnapshot = preparedLine.Record.DurationDays,
                        VisitsLimitSnapshot = preparedLine.Record.VisitsLimit,
                        Quantity = preparedLine.Selection.Quantity,
                        UnitPriceAmountSnapshot = preparedLine.Record.PriceAmount,
                        CurrencySnapshot = preparedLine.Record.PriceCurrency,
                        LineTotal = preparedLine.LineTotal,
                        Sequence = preparedLine.Selection.Sequence,
                    });
                if (paperReference is not null)
                {
                    rowBinder.LinkEntity(
                        paperReference,
                        "membership_negative_closure_line",
                        lineId);
                }
                lineSummaries.Add(new
                {
                    LineId = lineId,
                    preparedLine.Selection.Sequence,
                    MembershipTypeId = preparedLine.Record.Id,
                    TypeName = preparedLine.Record.Name,
                    preparedLine.Selection.Quantity,
                    UnitPriceAmount = preparedLine.Record.PriceAmount,
                    Currency = preparedLine.Record.PriceCurrency,
                    preparedLine.LineTotal,
                });

                for (var quantity = 0;
                     quantity < preparedLine.Selection.Quantity;
                     quantity++)
                {
                    var plannedItem = replacementPlan[candidateIndex++];
                    var candidate = plannedItem.Candidate;
                    visitIds.Add(candidate.VisitId);
                    affectedMembershipIds.Add(candidate.SourceMembershipId);
                    var itemId = plannedItem.ItemId;
                    dbContext.Set<MembershipNegativeClosureItemRecord>().Add(
                        new MembershipNegativeClosureItemRecord
                        {
                            Id = itemId,
                            NegativeClosureId = replacement.Id,
                            ClientId = replacement.ClientId,
                            ClosureLineId = lineId,
                            Sequence = candidateIndex,
                            VisitId = candidate.VisitId,
                            SourceMembershipId = candidate.SourceMembershipId,
                            OldConsumptionId = candidate.OldConsumptionId,
                            CoveringMembershipId = null,
                            NewConsumptionId = null,
                            Status = "active",
                        });
                    if (paperReference is not null)
                    {
                        rowBinder.LinkEntity(
                            paperReference,
                            "membership_negative_closure_item",
                            itemId);
                    }
                }
            }
        }
        else
        {
            foreach (var plannedItem in replacementPlan)
            {
                candidateIndex++;
                var candidate = plannedItem.Candidate;
                var itemId = plannedItem.ItemId;
                var consumptionId = plannedItem.NewConsumptionId!.Value;
                visitIds.Add(candidate.VisitId);
                affectedMembershipIds.Add(candidate.SourceMembershipId);
                dbContext.Set<VisitConsumptionRecord>().Add(
                    new VisitConsumptionRecord
                    {
                        Id = consumptionId,
                        VisitId = candidate.VisitId,
                        ClientId = replacement.ClientId,
                        VisitKind = "membership",
                        MembershipId = replacement.CoveringMembershipId!.Value,
                        ConsumptionType = "negative_coverage",
                        SourceFactType = "negative_closure_item",
                        SourceFactId = itemId,
                        RecordedAt = replacement.RecordedAt,
                        RecordedByAccountId = correction.Envelope.Actor.AccountId.Value,
                        RecordedSessionId = correction.Envelope.Actor.SessionId.Value,
                        Status = "active",
                    });
                if (paperReference is not null)
                {
                    rowBinder.LinkEntity(
                        paperReference,
                        "visit_consumption",
                        consumptionId);
                }
                dbContext.Set<MembershipNegativeClosureItemRecord>().Add(
                    new MembershipNegativeClosureItemRecord
                    {
                        Id = itemId,
                        NegativeClosureId = replacement.Id,
                        ClientId = replacement.ClientId,
                        ClosureLineId = null,
                        Sequence = candidateIndex,
                        VisitId = candidate.VisitId,
                        SourceMembershipId = candidate.SourceMembershipId,
                        OldConsumptionId = candidate.OldConsumptionId,
                        CoveringMembershipId = replacement.CoveringMembershipId,
                        NewConsumptionId = consumptionId,
                        Status = "active",
                    });
                if (paperReference is not null)
                {
                    rowBinder.LinkEntity(
                        paperReference,
                        "membership_negative_closure_item",
                        itemId);
                }
            }
        }

        return new StagedReplacementFacts(
            visitIds.AsReadOnly(),
            lineSummaries.AsReadOnly());
    }

    private async Task<int> GetClientNegativeBalanceAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        return await (
            from membership in dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
            join cache in dbContext.Set<MembershipStateCacheRecord>().AsNoTracking()
                on membership.Id equals cache.MembershipId
            where membership.ClientId == clientId
                && (membership.Status == "active" || membership.Status == "closed")
            select cache.NegativeBalance)
            .SumAsync(cancellationToken);
    }

    private async Task<bool> IsChangedAfterCloseAsync(
        DateTimeOffset originalPaymentOccurredAt,
        DateTimeOffset? replacementPaymentOccurredAt,
        CancellationToken cancellationToken)
    {
        var businessDates = new HashSet<DateOnly>
        {
            BusinessTimeZone.GetBusinessDate(originalPaymentOccurredAt),
        };
        if (replacementPaymentOccurredAt is { } replacementTime)
        {
            businessDates.Add(BusinessTimeZone.GetBusinessDate(replacementTime));
        }

        var changedAfterClose = false;
        foreach (var businessDate in businessDates)
        {
            var status = await dayReconciliationStatusProvider.GetStatusAsync(
                businessDate,
                cancellationToken);
            if (!Enum.IsDefined(status))
            {
                throw new InvalidOperationException(
                    $"Payment day reconciliation status '{status}' is not supported.");
            }

            changedAfterClose |= status == PaymentDayReconciliationStatus.Reconciled;
        }

        return changedAfterClose;
    }

    private AuditEntryId AppendPaymentLifecycleAudit(
        NormalizedNegativeCoverageCorrection correction,
        MembershipNegativeClosureRecord original,
        PaymentRecord originalPayment,
        Guid correctionId,
        Guid? replacementClosureId,
        Guid? replacementPaymentId,
        PaymentRecord? replacementPayment,
        IReadOnlyList<object> replacementLineSummaries,
        AuditEntryId? replacementPaymentAuditId,
        DateTimeOffset recordedAt,
        PaperFallbackEntryRowReference? paperReference,
        bool changedAfterClose)
    {
        var action = correction.Mode == NegativeVisitCoverageCorrectionMode.Cancel
            ? PaymentAuditActions.Canceled
            : PaymentAuditActions.Corrected;
        return auditAppender.Append(
            correction.Envelope,
            action,
            PaymentAuditActions.EntityType,
            originalPayment.Id,
            recordedAt,
            relatedEntityRefs: paperReference is { } paymentAuditPaperReference
            ? new
            {
                original.ClientId,
                OriginalNegativeClosureId = original.Id,
                CorrectionId = correctionId,
                ReplacementNegativeClosureId = replacementClosureId,
                ReplacementPaymentId = replacementPaymentId,
                ReplacementPaymentAuditEntryId =
                    replacementPaymentAuditId?.Value,
                paymentAuditPaperReference.EntryBatchId,
                paymentAuditPaperReference.EntryBatchRowId,
                paymentAuditPaperReference.PaperSheetNumber,
                paymentAuditPaperReference.LineNumber,
                PaperExplanation = paymentAuditPaperReference.Explanation,
            }
            : new
            {
                original.ClientId,
                OriginalNegativeClosureId = original.Id,
                CorrectionId = correctionId,
                ReplacementNegativeClosureId = replacementClosureId,
                ReplacementPaymentId = replacementPaymentId,
                ReplacementPaymentAuditEntryId =
                    replacementPaymentAuditId?.Value,
            },
            beforeSummary: new
            {
                Payment = SummarizePayment(originalPayment, statusOverride: "active"),
            },
            afterSummary: new
            {
                Payment = SummarizePayment(originalPayment),
                ReplacementPaymentId = replacementPaymentId,
                ReplacementPaymentAuditEntryId =
                    replacementPaymentAuditId?.Value,
                ReplacementPayment = replacementPayment is null
                    ? null
                    : SummarizePayment(replacementPayment),
                ReplacementCoverageWitness = replacementPayment is null
                    || replacementPaymentAuditId is null
                    ? null
                    : new
                    {
                        Lines = replacementLineSummaries,
                        ExpectedAmount = replacementPayment.Amount,
                        ExpectedCurrency = replacementPayment.Currency,
                        PaymentAudit = new
                        {
                            AuditEntryId = replacementPaymentAuditId.Value.Value,
                            ActionType = PaymentAuditActions.Created,
                            EntityType = PaymentAuditActions.EntityType,
                            EntityId = replacementPayment.Id,
                        },
                    },
                CoverageCorrectionId = correctionId,
                Correction = new
                {
                    CorrectionId = correctionId,
                    correction.Envelope.Reason,
                    OccurredAt = correction.Envelope.OccurredAt!.Value,
                    RecordedAt = recordedAt,
                    EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                        correction.Envelope.EntryOrigin),
                    EntryBatchId = paperReference?.EntryBatchId,
                    ChangedAfterClose = changedAfterClose,
                },
                NoRefundOrDeltaCalculated = true,
                ChangedAfterClose = changedAfterClose,
            },
            changedAfterClose);
    }

    private static object SummarizeOriginal(
        MembershipNegativeClosureRecord closure,
        IReadOnlyCollection<MembershipNegativeClosureItemRecord> items,
        IReadOnlyCollection<MembershipNegativeClosureLineRecord> lines,
        PaymentRecord? payment,
        int visibleNegativeBalance)
    {
        return new
        {
            Closure = new
            {
                closure.Id,
                closure.ClientId,
                closure.ClosureType,
                closure.CoveringMembershipId,
                closure.OldestOpenNegativeVisitId,
                closure.VisitsCount,
                Status = "active",
            },
            ItemIds = items.OrderBy(item => item.Sequence).Select(item => item.Id),
            VisitIds = items.OrderBy(item => item.Sequence).Select(item => item.VisitId),
            Lines = lines.Select(line => new
            {
                line.Id,
                line.MembershipTypeId,
                line.TypeNameSnapshot,
                line.Quantity,
                line.UnitPriceAmountSnapshot,
                line.CurrencySnapshot,
                line.LineTotal,
            }),
            Payment = payment is null
                ? null
                : SummarizePayment(payment, statusOverride: "active"),
            VisibleNegativeBalance = visibleNegativeBalance,
        };
    }

    private static object SummarizePayment(
        PaymentRecord payment,
        string? statusOverride = null)
    {
        return new
        {
            PaymentId = payment.Id,
            payment.ClientId,
            payment.NegativeClosureId,
            payment.Amount,
            payment.Currency,
            payment.Method,
            payment.PaymentContext,
            payment.OccurredAt,
            payment.RecordedAt,
            payment.EntryOrigin,
            payment.EntryBatchId,
            payment.Comment,
            Status = statusOverride ?? payment.Status,
        };
    }

    private static bool ReplacementShapeMatchesOriginal(
        NormalizedNegativeCoverageCorrection correction,
        string closureType)
    {
        if (correction.Mode == NegativeVisitCoverageCorrectionMode.Cancel)
        {
            return true;
        }

        return closureType switch
        {
            "one_off" => correction.ReplacementOneOffLines.Count > 0
                && correction.ReplacementNewMembershipCoverageCount is null,
            "new_membership" => correction.ReplacementOneOffLines.Count == 0
                && correction.ReplacementNewMembershipCoverageCount is > 0,
            _ => false,
        };
    }

    private static CommandResult RecalculationFailure(string message)
    {
        return CorrectNegativeVisitCoverageCommandSupport.Error(
            CommandErrorCode.RecalculationFailed,
            message);
    }

    private sealed record StagedReplacementFacts(
        IReadOnlyList<Guid> VisitIds,
        IReadOnlyList<object> LineSummaries);
}
