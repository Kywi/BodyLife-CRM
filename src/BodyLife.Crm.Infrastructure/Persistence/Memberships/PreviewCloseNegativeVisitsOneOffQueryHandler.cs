using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class PreviewCloseNegativeVisitsOneOffQueryHandler(
    BodyLifeDbContext dbContext,
    MembershipNegativeVisitSelector negativeVisitSelector,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        PreviewCloseNegativeVisitsOneOffQuery,
        PreviewCloseNegativeVisitsOneOffResult>
{
    public async Task<PreviewCloseNegativeVisitsOneOffResult> ExecuteAsync(
        PreviewCloseNegativeVisitsOneOffQuery query,
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
                PreviewCloseNegativeVisitsOneOffStatus.PermissionDenied,
                "permission_denied",
                "An active Owner, named Admin or shared Reception/Admin session is required.",
                null));
        }

        var validation = Validate(query, out var normalizedLines, out var visitsCount);
        if (validation is not null)
        {
            return await CompleteAsync(Invalid(validation.Message, validation.Field));
        }

        if (!await dbContext.Set<ClientRecord>().AsNoTracking()
                .AnyAsync(client => client.Id == query.ClientId, cancellationToken))
        {
            return await CompleteAsync(Failure(
                PreviewCloseNegativeVisitsOneOffStatus.NotFound,
                "not_found",
                "Client was not found.",
                "clientId"));
        }

        var selectionResult = await negativeVisitSelector.SelectAsync(
            query.ClientId,
            cancellationToken);
        if (selectionResult.Status
            == MembershipNegativeVisitSelectionStatus.MissingCanonicalState)
        {
            return await CompleteAsync(Failure(
                PreviewCloseNegativeVisitsOneOffStatus.RecalculationFailed,
                "recalculation_failed",
                "Canonical membership state is missing, stale or unavailable.",
                null));
        }

        if (selectionResult.Status
            != MembershipNegativeVisitSelectionStatus.Succeeded)
        {
            return await CompleteAsync(Failure(
                PreviewCloseNegativeVisitsOneOffStatus.CanonicalStateInvalid,
                "canonical_state_invalid",
                "Canonical negative-coverage facts are inconsistent.",
                null));
        }

        var selection = selectionResult.Selection!;
        var selectors = await NegativeVisitCoveragePreviewSupport.LoadSelectorsAsync(
            dbContext,
            selection.OldestOpenConcreteVisitId,
            cancellationToken);
        if (selectors is null)
        {
            return await CompleteAsync(Failure(
                PreviewCloseNegativeVisitsOneOffStatus.CanonicalStateInvalid,
                "canonical_state_invalid",
                "Active one-off catalog facts are inconsistent.",
                null));
        }

        if (selection.OldestOpenConcreteVisitId
            != query.ExpectedOldestOpenNegativeVisitId)
        {
            return await CompleteAsync(Failure(
                PreviewCloseNegativeVisitsOneOffStatus.StaleState,
                "stale_state",
                "The oldest open negative Visit changed. Refresh canonical state.",
                "expectedOldestOpenNegativeVisitId",
                selectors));
        }

        if (visitsCount > selection.OpenConcreteVisits.Count)
        {
            return await CompleteAsync(Failure(
                PreviewCloseNegativeVisitsOneOffStatus.ValidationFailed,
                "validation_failed",
                "One-off closure cannot exceed current open concrete negative Visits.",
                "lines",
                selectors));
        }

        var preparedResult = await OneOffNegativeClosureLinePreparer.PrepareReadOnlyAsync(
            dbContext,
            normalizedLines,
            "lines",
            cancellationToken);
        if (preparedResult.Error is not null)
        {
            return await CompleteAsync(MapPreparationFailure(
                NegativeVisitCoveragePreviewSupport.FromCommandError(
                    preparedResult.Error),
                selectors));
        }

        var prepared = preparedResult.Preparation!;
        var coveredVisits = selection.OpenConcreteVisits
            .Take(prepared.VisitsCount)
            .Select(NegativeVisitCoveragePreviewSupport.ToReadModel)
            .ToArray();
        var preview = new OneOffNegativeClosurePreview(
            query.ClientId,
            query.ExpectedOldestOpenNegativeVisitId,
            NegativeVisitCoveragePreviewSupport.CreatePreviewLines(prepared),
            new Money(prepared.TotalAmount, prepared.Currency),
            coveredVisits,
            selection.TotalNegativeBalance - prepared.VisitsCount,
            selection.UnknownNegativeBalance,
            selectors);
        return await CompleteAsync(
            PreviewCloseNegativeVisitsOneOffResult.Succeeded(preview));

        async Task<PreviewCloseNegativeVisitsOneOffResult> CompleteAsync(
            PreviewCloseNegativeVisitsOneOffResult result)
        {
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
    }

    private static PreviewInputError? Validate(
        PreviewCloseNegativeVisitsOneOffQuery query,
        out IReadOnlyList<NormalizedOneOffNegativeClosureLine> normalizedLines,
        out int visitsCount)
    {
        normalizedLines = [];
        visitsCount = 0;
        if (query.ClientId == Guid.Empty)
        {
            return new PreviewInputError(
                CommandErrorCode.ValidationFailed,
                "Client id is required.",
                "clientId");
        }

        if (query.ExpectedOldestOpenNegativeVisitId == Guid.Empty)
        {
            return new PreviewInputError(
                CommandErrorCode.ValidationFailed,
                "Expected oldest open negative Visit id is required.",
                "expectedOldestOpenNegativeVisitId");
        }

        return NegativeVisitCoveragePreviewSupport.NormalizeOneOffLines(
            query.Lines,
            "lines",
            required: true,
            out normalizedLines,
            out visitsCount);
    }

    private static PreviewCloseNegativeVisitsOneOffResult MapPreparationFailure(
        PreviewInputError error,
        NegativeVisitCoverageStaleSelectors selectors) => error.Code switch
        {
            CommandErrorCode.NotFound => Failure(
                PreviewCloseNegativeVisitsOneOffStatus.NotFound,
                "not_found",
                error.Message,
                error.Field,
                selectors),
            CommandErrorCode.MembershipTypeInactive => Failure(
                PreviewCloseNegativeVisitsOneOffStatus.MembershipTypeInactive,
                "membership_type_inactive",
                error.Message,
                error.Field,
                selectors),
            CommandErrorCode.MembershipNotEligible => Failure(
                PreviewCloseNegativeVisitsOneOffStatus.MembershipNotEligible,
                "membership_not_eligible",
                error.Message,
                error.Field,
                selectors),
            CommandErrorCode.StaleState => Failure(
                PreviewCloseNegativeVisitsOneOffStatus.StaleState,
                "stale_state",
                error.Message,
                error.Field,
                selectors),
            _ => Failure(
                PreviewCloseNegativeVisitsOneOffStatus.ValidationFailed,
                "validation_failed",
                error.Message,
                error.Field,
                selectors),
        };

    private static PreviewCloseNegativeVisitsOneOffResult Invalid(
        string message,
        string? field) => Failure(
        PreviewCloseNegativeVisitsOneOffStatus.ValidationFailed,
        "validation_failed",
        message,
        field);

    private static PreviewCloseNegativeVisitsOneOffResult Failure(
        PreviewCloseNegativeVisitsOneOffStatus status,
        string errorCode,
        string errorMessage,
        string? errorField,
        NegativeVisitCoverageStaleSelectors? selectors = null) =>
        PreviewCloseNegativeVisitsOneOffResult.Failure(
            status,
            errorCode,
            errorMessage,
            errorField,
            selectors);
}
