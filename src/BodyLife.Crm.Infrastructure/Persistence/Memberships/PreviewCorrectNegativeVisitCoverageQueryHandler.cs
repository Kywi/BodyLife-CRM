using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class PreviewCorrectNegativeVisitCoverageQueryHandler(
    BodyLifeDbContext dbContext,
    MembershipNegativeVisitSelector negativeVisitSelector,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        PreviewCorrectNegativeVisitCoverageQuery,
        PreviewCorrectNegativeVisitCoverageResult>
{
    private const int ReasonMaxLength = 1000;

    public async Task<PreviewCorrectNegativeVisitCoverageResult> ExecuteAsync(
        PreviewCorrectNegativeVisitCoverageQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "set transaction read only",
            cancellationToken);

        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return await CompleteAsync(Failure(
                PreviewCorrectNegativeVisitCoverageStatus.PermissionDenied,
                "permission_denied",
                "An active Owner, named Admin or shared Reception/Admin session is required.",
                null));
        }

        var validation = Validate(
            query,
            out var reason,
            out var normalizedLines,
            out var oneOffVisitsCount);
        if (validation is not null)
        {
            return await CompleteAsync(MapInputFailure(validation));
        }

        var sourceResult = await new NegativeVisitCoverageCorrectionSourceReader(
                dbContext)
            .ReadAsync(query.OriginalNegativeClosureId, cancellationToken);
        if (sourceResult.Status
            != NegativeVisitCoverageCorrectionSourceStatus.Prepared)
        {
            return await CompleteAsync(await MapSourceFailureAsync(
                sourceResult,
                cancellationToken));
        }

        var source = sourceResult.Source!;
        if (!ReplacementShapeMatches(query, source.Closure.ClosureType))
        {
            return await CompleteAsync(Failure(
                PreviewCorrectNegativeVisitCoverageStatus.ValidationFailed,
                "validation_failed",
                "Replacement must preserve the original coverage method.",
                "replacement"));
        }

        var selectionResult = await negativeVisitSelector
            .SelectHypotheticallyWithoutClosureAsync(
            source.Closure.ClientId,
            source.Closure.Id,
            cancellationToken);
        if (selectionResult.Status
            == MembershipNegativeVisitSelectionStatus.MissingCanonicalState)
        {
            return await CompleteAsync(Failure(
                PreviewCorrectNegativeVisitCoverageStatus.RecalculationFailed,
                "recalculation_failed",
                "Canonical membership state is missing, stale or unavailable.",
                null));
        }

        if (selectionResult.Status
            != MembershipNegativeVisitSelectionStatus.Succeeded)
        {
            return await CompleteAsync(Failure(
                PreviewCorrectNegativeVisitCoverageStatus.CanonicalStateInvalid,
                "canonical_state_invalid",
                "Canonical negative-coverage facts are inconsistent.",
                null));
        }

        var selection = selectionResult.Selection!;
        var restoredCandidates = selection.OpenConcreteVisits
            .Select(NegativeVisitCoveragePreviewSupport.ToReadModel)
            .ToArray();
        var restoredTotal = selection.TotalNegativeBalance;
        var restoredUnknown = selection.UnknownNegativeBalance;

        var currentOldest = restoredCandidates.FirstOrDefault()?.VisitId;
        var selectors = await NegativeVisitCoveragePreviewSupport.LoadSelectorsAsync(
            dbContext,
            currentOldest,
            cancellationToken);
        if (selectors is null)
        {
            return await CompleteAsync(Failure(
                PreviewCorrectNegativeVisitCoverageStatus.CanonicalStateInvalid,
                "canonical_state_invalid",
                "Active one-off catalog facts are inconsistent.",
                null));
        }

        if (query.Mode == NegativeVisitCoverageCorrectionMode.Replace
            && currentOldest != query.ExpectedOldestOpenNegativeVisitId)
        {
            return await CompleteAsync(Failure(
                PreviewCorrectNegativeVisitCoverageStatus.StaleState,
                "stale_state",
                "The oldest restored negative Visit changed. Refresh canonical state.",
                "expectedOldestOpenNegativeVisitId",
                selectors));
        }

        PreparedOneOffClosureLines? preparedOneOff = null;
        var replacementCount = 0;
        if (query.Mode == NegativeVisitCoverageCorrectionMode.Replace
            && source.Closure.ClosureType == "one_off")
        {
            var preparedResult = await OneOffNegativeClosureLinePreparer
                .PrepareReadOnlyAsync(
                    dbContext,
                    normalizedLines,
                    "replacementOneOffLines",
                    cancellationToken);
            if (preparedResult.Error is not null)
            {
                return await CompleteAsync(MapPreparationFailure(
                    NegativeVisitCoveragePreviewSupport.FromCommandError(
                        preparedResult.Error),
                    selectors));
            }

            preparedOneOff = preparedResult.Preparation;
            replacementCount = oneOffVisitsCount;
        }
        else if (query.Mode == NegativeVisitCoverageCorrectionMode.Replace)
        {
            replacementCount = query.ReplacementNewMembershipCoverageCount!.Value;
        }

        if (replacementCount > restoredCandidates.Length)
        {
            return await CompleteAsync(Failure(
                PreviewCorrectNegativeVisitCoverageStatus.ValidationFailed,
                "validation_failed",
                "Replacement cannot exceed current restored concrete negative Visits.",
                "replacement",
                selectors));
        }

        var replacementVisits = restoredCandidates.Take(replacementCount).ToArray();
        NegativeVisitCoverageCoveringMembershipPreview? coveringContext = null;
        if (source.Closure.ClosureType == "new_membership")
        {
            var membership = source.CoveringMembership!;
            var cache = source.CoveringCache!;
            var restored = await new MembershipStateCacheRebuilder(dbContext, timeProvider)
                .CalculateCanonicalStateForNegativeCoveragePreviewAsync(
                    membership, source.Closure.Id, cancellationToken);
            var restoredRemaining = restored.State.RemainingVisits;

            int? replacementRemaining = null;
            if (query.Mode == NegativeVisitCoverageCorrectionMode.Replace)
            {
                if (replacementCount > membership.VisitsLimitSnapshot
                    || replacementCount > restoredRemaining
                    || replacementVisits.Length == 0
                    || membership.StartDate != replacementVisits[0].BusinessDate)
                {
                    return await CompleteAsync(Failure(
                        PreviewCorrectNegativeVisitCoverageStatus.MembershipNotEligible,
                        "membership_not_eligible",
                        "The covering Membership cannot accept the replacement allocation.",
                        "replacementNewMembershipCoverageCount",
                        selectors));
                }

                replacementRemaining = restoredRemaining - replacementCount;
            }

            coveringContext = new NegativeVisitCoverageCoveringMembershipPreview(
                NegativeVisitCoveragePreviewSupport.CreateMembershipSnapshot(membership),
                cache.RemainingVisits,
                restoredRemaining,
                replacementRemaining,
                cache.NegativeBalance,
                cache.EffectiveEndDate);
        }

        var projectedCoverage = selection.OpenConcreteVisits.Take(replacementCount)
            .Select(candidate => new MembershipNegativeCoverageSourceFact(
                Guid.NewGuid(), candidate.VisitId, candidate.SourceMembershipId,
                source.Closure.CoveringMembershipId, candidate.BusinessDate,
                candidate.OccurredAt, timeProvider.GetUtcNow(), MembershipNegativeCoverageSourceStatus.Active))
            .ToArray();
        if (await MembershipLifecycleCorrectionGuard.FindBlockedMembershipAsync(
                dbContext, selection.Memberships.Select(row => row.Id).ToArray(),
                null, source.Closure.Id, projectedCoverage, cancellationToken) is not null)
        {
            return await CompleteAsync(Failure(
                PreviewCorrectNegativeVisitCoverageStatus.LifecycleDependency,
                "lifecycle_dependency",
                "This correction would leave unused visits on a closed Membership.",
                "originalNegativeClosureId", selectors));
        }

        var originalPayment = source.OriginalPayment is null
            ? null
            : CreateOriginalPayment(source.OriginalPayment);
        var replacementPayment = preparedOneOff is null
            ? null
            : new NegativeVisitCoveragePaymentContextReadModel(
                null,
                new Money(preparedOneOff.TotalAmount, preparedOneOff.Currency),
                "cash",
                "negative_closure",
                null,
                null,
                "preview");
        var preview = new NegativeVisitCoverageCorrectionPreview(
            source.Closure.Id,
            source.Closure.ClosureType,
            query.Mode,
            reason,
            source.Closure.VisitsCount,
            source.OriginalLines,
            source.RestoredVisits,
            preparedOneOff is null
                ? []
                : NegativeVisitCoveragePreviewSupport.CreatePreviewLines(
                    preparedOneOff),
            replacementVisits,
            originalPayment,
            replacementPayment,
            coveringContext,
            restoredTotal,
            restoredUnknown,
            restoredTotal - replacementCount,
            restoredUnknown,
            selectors);
        return await CompleteAsync(
            PreviewCorrectNegativeVisitCoverageResult.Succeeded(preview));

        async Task<PreviewCorrectNegativeVisitCoverageResult> CompleteAsync(
            PreviewCorrectNegativeVisitCoverageResult result)
        {
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
    }

    private async Task<PreviewCorrectNegativeVisitCoverageResult> MapSourceFailureAsync(
        NegativeVisitCoverageCorrectionSourceResult source,
        CancellationToken cancellationToken)
    {
        NegativeVisitCoverageStaleSelectors? selectors = null;
        if (source.ClientId is { } clientId)
        {
            var selection = await negativeVisitSelector.SelectAsync(
                clientId,
                cancellationToken);
            if (selection.Status == MembershipNegativeVisitSelectionStatus.Succeeded)
            {
                selectors = await NegativeVisitCoveragePreviewSupport.LoadSelectorsAsync(
                    dbContext,
                    selection.Selection!.OldestOpenConcreteVisitId,
                    cancellationToken);
            }
        }

        return source.Status switch
        {
            NegativeVisitCoverageCorrectionSourceStatus.NotFound => Failure(
                PreviewCorrectNegativeVisitCoverageStatus.NotFound,
                "not_found",
                "Negative coverage was not found.",
                "originalNegativeClosureId"),
            NegativeVisitCoverageCorrectionSourceStatus.AlreadyCanceled => Failure(
                PreviewCorrectNegativeVisitCoverageStatus.AlreadyCanceled,
                "already_canceled",
                "Negative coverage has already been canceled.",
                "originalNegativeClosureId",
                selectors),
            NegativeVisitCoverageCorrectionSourceStatus.Stale => Failure(
                PreviewCorrectNegativeVisitCoverageStatus.StaleState,
                "stale_state",
                "Negative coverage has already been corrected or replaced.",
                "originalNegativeClosureId",
                selectors),
            NegativeVisitCoverageCorrectionSourceStatus.MissingCanonicalState => Failure(
                PreviewCorrectNegativeVisitCoverageStatus.RecalculationFailed,
                "recalculation_failed",
                "Canonical membership state is missing, stale or unavailable.",
                null),
            _ => Failure(
                PreviewCorrectNegativeVisitCoverageStatus.CanonicalStateInvalid,
                "canonical_state_invalid",
                "Canonical negative-coverage source facts are inconsistent.",
                null),
        };
    }

    private static PreviewInputError? Validate(
        PreviewCorrectNegativeVisitCoverageQuery query,
        out string reason,
        out IReadOnlyList<NormalizedOneOffNegativeClosureLine> normalizedLines,
        out int oneOffVisitsCount)
    {
        reason = string.Empty;
        normalizedLines = [];
        oneOffVisitsCount = 0;
        if (query.OriginalNegativeClosureId == Guid.Empty)
        {
            return new PreviewInputError(
                CommandErrorCode.ValidationFailed,
                "Original negative closure id is required.",
                "originalNegativeClosureId");
        }

        if (!Enum.IsDefined(query.Mode))
        {
            return new PreviewInputError(
                CommandErrorCode.ValidationFailed,
                "Correction mode is not supported.",
                "mode");
        }

        reason = query.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return new PreviewInputError(
                CommandErrorCode.ReasonRequired,
                "Reason is required for coverage correction.",
                "reason");
        }

        if (reason.Length > ReasonMaxLength)
        {
            return new PreviewInputError(
                CommandErrorCode.ValidationFailed,
                $"Reason must be {ReasonMaxLength} characters or fewer.",
                "reason");
        }

        var linesError = NegativeVisitCoveragePreviewSupport.NormalizeOneOffLines(
            query.ReplacementOneOffLines,
            "replacementOneOffLines",
            required: false,
            out normalizedLines,
            out oneOffVisitsCount);
        if (linesError is not null)
        {
            return linesError;
        }

        if (query.Mode == NegativeVisitCoverageCorrectionMode.Cancel)
        {
            return normalizedLines.Count > 0
                || query.ReplacementNewMembershipCoverageCount is not null
                || query.ExpectedOldestOpenNegativeVisitId is not null
                ? new PreviewInputError(
                    CommandErrorCode.ValidationFailed,
                    "Cancellation cannot carry replacement selections.",
                    "mode")
                : null;
        }

        if (query.ReplacementNewMembershipCoverageCount is <= 0)
        {
            return new PreviewInputError(
                CommandErrorCode.ValidationFailed,
                "New-Membership replacement coverage count must be positive.",
                "replacementNewMembershipCoverageCount");
        }

        var hasOneOff = normalizedLines.Count > 0;
        var hasNewMembership = query.ReplacementNewMembershipCoverageCount is > 0;
        if (hasOneOff == hasNewMembership)
        {
            return new PreviewInputError(
                CommandErrorCode.ValidationFailed,
                "Replacement requires exactly one same-method replacement selection.",
                "replacement");
        }

        if (!query.ExpectedOldestOpenNegativeVisitId.HasValue
            || query.ExpectedOldestOpenNegativeVisitId.Value == Guid.Empty)
        {
            return new PreviewInputError(
                CommandErrorCode.ValidationFailed,
                "Expected oldest open negative Visit id is required for replacement.",
                "expectedOldestOpenNegativeVisitId");
        }

        return null;
    }

    private static bool ReplacementShapeMatches(
        PreviewCorrectNegativeVisitCoverageQuery query,
        string closureType)
    {
        if (query.Mode == NegativeVisitCoverageCorrectionMode.Cancel)
        {
            return true;
        }

        return closureType switch
        {
            "one_off" => query.ReplacementOneOffLines is { Count: > 0 }
                && query.ReplacementNewMembershipCoverageCount is null,
            "new_membership" => query.ReplacementOneOffLines is null or { Count: 0 }
                && query.ReplacementNewMembershipCoverageCount is > 0,
            _ => false,
        };
    }

    private static NegativeVisitCoveragePaymentContextReadModel CreateOriginalPayment(
        PaymentRecord payment) => new(
        payment.Id,
        new Money(payment.Amount, payment.Currency),
        payment.Method,
        payment.PaymentContext,
        payment.OccurredAt,
        payment.RecordedAt,
        payment.Status);

    private static PreviewCorrectNegativeVisitCoverageResult MapPreparationFailure(
        PreviewInputError error,
        NegativeVisitCoverageStaleSelectors selectors) => error.Code switch
        {
            CommandErrorCode.NotFound => Failure(
                PreviewCorrectNegativeVisitCoverageStatus.NotFound,
                "not_found",
                error.Message,
                error.Field,
                selectors),
            CommandErrorCode.MembershipTypeInactive => Failure(
                PreviewCorrectNegativeVisitCoverageStatus.MembershipTypeInactive,
                "membership_type_inactive",
                error.Message,
                error.Field,
                selectors),
            CommandErrorCode.MembershipNotEligible => Failure(
                PreviewCorrectNegativeVisitCoverageStatus.MembershipNotEligible,
                "membership_not_eligible",
                error.Message,
                error.Field,
                selectors),
            CommandErrorCode.StaleState => Failure(
                PreviewCorrectNegativeVisitCoverageStatus.StaleState,
                "stale_state",
                error.Message,
                error.Field,
                selectors),
            _ => Failure(
                PreviewCorrectNegativeVisitCoverageStatus.ValidationFailed,
                "validation_failed",
                error.Message,
                error.Field,
                selectors),
        };

    private static PreviewCorrectNegativeVisitCoverageResult MapInputFailure(
        PreviewInputError error) => error.Code == CommandErrorCode.ReasonRequired
        ? Failure(
            PreviewCorrectNegativeVisitCoverageStatus.ReasonRequired,
            "reason_required",
            error.Message,
            error.Field)
        : Failure(
            PreviewCorrectNegativeVisitCoverageStatus.ValidationFailed,
            "validation_failed",
            error.Message,
            error.Field);

    private static PreviewCorrectNegativeVisitCoverageResult Failure(
        PreviewCorrectNegativeVisitCoverageStatus status,
        string errorCode,
        string errorMessage,
        string? errorField,
        NegativeVisitCoverageStaleSelectors? selectors = null) =>
        PreviewCorrectNegativeVisitCoverageResult.Failure(
            status,
            errorCode,
            errorMessage,
            errorField,
            selectors);
}
