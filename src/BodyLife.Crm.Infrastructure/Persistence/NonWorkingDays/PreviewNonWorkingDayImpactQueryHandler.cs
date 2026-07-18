using System.Data;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class PreviewNonWorkingDayImpactQueryHandler(
    BodyLifeDbContext dbContext,
    IMembershipNonWorkingDayImpactPreparer impactPreparer,
    INonWorkingDayPreviewTokenService previewTokenService,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        PreviewNonWorkingDayImpactQuery,
        PreviewNonWorkingDayImpactResult>
{
    public async Task<PreviewNonWorkingDayImpactResult> ExecuteAsync(
        PreviewNonWorkingDayImpactQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await NonWorkingDayQuerySupport.IsOwnerAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return PreviewNonWorkingDayImpactResult.Denied();
        }

        var inputValidation = TryCreateInput(query, out var input);
        if (inputValidation is not null)
        {
            return inputValidation;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        try
        {
            var preparation = await impactPreparer.PrepareImpactAsync(
                input!.Period,
                cancellationToken);
            var clientDisplayNames = await NonWorkingDayClientProjection.LoadDisplayNamesAsync(
                dbContext,
                preparation,
                cancellationToken);
            var confirmation = previewTokenService.Issue(
                input,
                preparation.AffectedScope);
            var preview = CreatePreview(
                input,
                preparation,
                clientDisplayNames,
                confirmation);

            await transaction.CommitAsync(cancellationToken);
            return PreviewNonWorkingDayImpactResult.Succeeded(preview);
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PreviewNonWorkingDayImpactResult.RecalculationFailed();
        }
    }

    private static PreviewNonWorkingDayImpactResult? TryCreateInput(
        PreviewNonWorkingDayImpactQuery query,
        out NonWorkingDayPreviewInput? input)
    {
        input = null;
        if (query.ProposedStartDate == default)
        {
            return PreviewNonWorkingDayImpactResult.Invalid(
                "Proposed start date is required.",
                "proposedStartDate");
        }

        if (query.ProposedEndDate == default)
        {
            return PreviewNonWorkingDayImpactResult.Invalid(
                "Proposed end date is required.",
                "proposedEndDate");
        }

        if (query.ProposedEndDate < query.ProposedStartDate)
        {
            return PreviewNonWorkingDayImpactResult.Invalid(
                "Proposed end date must be on or after the start date.",
                "proposedEndDate");
        }

        var reasonCode = query.ReasonCode;
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return PreviewNonWorkingDayImpactResult.Invalid(
                "Reason code is required.",
                "reasonCode");
        }

        try
        {
            input = new NonWorkingDayPreviewInput(
                new DateRange(query.ProposedStartDate, query.ProposedEndDate),
                reasonCode,
                query.ReasonComment);
            return null;
        }
        catch (ArgumentException exception)
            when (exception.ParamName == "reasonCode")
        {
            return PreviewNonWorkingDayImpactResult.Invalid(
                $"Reason code must be {NonWorkingDayPreviewInput.ReasonCodeMaxLength} "
                + "characters or fewer.",
                "reasonCode");
        }
        catch (ArgumentException exception)
            when (exception.ParamName == "reasonComment")
        {
            return PreviewNonWorkingDayImpactResult.Invalid(
                $"Reason comment must be {NonWorkingDayPreviewInput.ReasonCommentMaxLength} "
                + "characters or fewer.",
                "reasonComment");
        }
    }

    private static NonWorkingDayImpactPreview CreatePreview(
        NonWorkingDayPreviewInput input,
        MembershipNonWorkingDayImpactPreparation preparation,
        IReadOnlyDictionary<Guid, string> clientDisplayNames,
        NonWorkingDayPreviewConfirmation confirmation)
    {
        return new NonWorkingDayImpactPreview(
            input,
            NonWorkingDayImpactPreviewMapper.Map(preparation, clientDisplayNames),
            confirmation);
    }

}
