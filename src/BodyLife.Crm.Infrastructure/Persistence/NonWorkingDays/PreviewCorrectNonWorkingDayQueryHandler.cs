using System.Data;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class PreviewCorrectNonWorkingDayQueryHandler(
    BodyLifeDbContext dbContext,
    IMembershipNonWorkingDayReplacementImpactPreparer replacementImpactPreparer,
    CorrectNonWorkingDaySourcePreparer sourcePreparer,
    INonWorkingDayCorrectionTokenService correctionTokenService,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        PreviewCorrectNonWorkingDayQuery,
        PreviewCorrectNonWorkingDayResult>
{
    private static readonly DateRange ReasonValidationPeriod = new(
        new DateOnly(2000, 1, 1),
        new DateOnly(2000, 1, 1));

    public async Task<PreviewCorrectNonWorkingDayResult> ExecuteAsync(
        PreviewCorrectNonWorkingDayQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await NonWorkingDayQuerySupport.IsOwnerAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return PreviewCorrectNonWorkingDayResult.Denied();
        }

        var inputValidation = TryCreateReplacementInput(query, out var replacementInput);
        if (inputValidation is not null)
        {
            return inputValidation;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        try
        {
            MembershipNonWorkingDayReplacementImpactPreparation? replacementImpact = null;
            if (query.Mode == NonWorkingDayCorrectionMode.ReplaceRange)
            {
                replacementImpact = await replacementImpactPreparer
                    .PrepareReplacementImpactAsync(
                        query.PeriodId,
                        replacementInput!.Period,
                        cancellationToken);
            }

            // Range replacement locks every active candidate before old source rows.
            var sourcePreparation = await sourcePreparer.PrepareAsync(
                query.PeriodId,
                query.Mode,
                cancellationToken);
            var sourceFailure = MapSourceFailure(sourcePreparation.Status);
            if (sourceFailure is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return sourceFailure;
            }

            var source = sourcePreparation.Source
                ?? throw new InvalidOperationException(
                    "Prepared NonWorkingDay correction source is missing.");
            var material = CreateConfirmationMaterial(
                query.Mode,
                source,
                replacementInput,
                replacementImpact);
            var confirmation = correctionTokenService.Issue(material);
            var impactItems = replacementImpact is null
                ? Array.Empty<NonWorkingDayImpactMembershipPreview>()
                : NonWorkingDayImpactPreviewMapper.Map(
                    replacementImpact.ReplacementImpact);
            var preview = new NonWorkingDayCorrectionPreview(
                material,
                impactItems,
                confirmation);

            await transaction.CommitAsync(cancellationToken);
            return PreviewCorrectNonWorkingDayResult.Succeeded(preview);
        }
        catch (ArgumentException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PreviewCorrectNonWorkingDayResult.InconsistentSource();
        }
        catch (InvalidOperationException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PreviewCorrectNonWorkingDayResult.RecalculationFailed();
        }
    }

    private static PreviewCorrectNonWorkingDayResult? TryCreateReplacementInput(
        PreviewCorrectNonWorkingDayQuery query,
        out NonWorkingDayPreviewInput? replacementInput)
    {
        replacementInput = null;
        if (query.PeriodId == Guid.Empty)
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                "NonWorkingDay period id is required.",
                "periodId");
        }

        if (!Enum.IsDefined(query.Mode))
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                "Correction mode is not supported.",
                "mode");
        }

        return query.Mode switch
        {
            NonWorkingDayCorrectionMode.ReplaceRange =>
                TryCreateRangeReplacementInput(query, out replacementInput),
            NonWorkingDayCorrectionMode.ReplaceReason =>
                TryCreateReasonReplacementInput(query, out replacementInput),
            NonWorkingDayCorrectionMode.Cancel => ValidateCancelShape(query),
            _ => throw new ArgumentOutOfRangeException(
                nameof(query),
                query.Mode,
                "Correction mode is not supported."),
        };
    }

    private static PreviewCorrectNonWorkingDayResult? TryCreateRangeReplacementInput(
        PreviewCorrectNonWorkingDayQuery query,
        out NonWorkingDayPreviewInput? replacementInput)
    {
        replacementInput = null;
        if (query.ReplacementStartDate is null)
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                "Replacement start date is required.",
                "replacementStartDate");
        }

        if (query.ReplacementEndDate is null)
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                "Replacement end date is required.",
                "replacementEndDate");
        }

        if (query.ReplacementEndDate < query.ReplacementStartDate)
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                "Replacement end date must be on or after the start date.",
                "replacementEndDate");
        }

        return TryCreateInput(
            new DateRange(
                query.ReplacementStartDate.Value,
                query.ReplacementEndDate.Value),
            query.ReplacementReasonCode,
            query.ReplacementReasonComment,
            out replacementInput);
    }

    private static PreviewCorrectNonWorkingDayResult? TryCreateReasonReplacementInput(
        PreviewCorrectNonWorkingDayQuery query,
        out NonWorkingDayPreviewInput? replacementInput)
    {
        replacementInput = null;
        if (query.ReplacementStartDate is not null)
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                "Reason-only replacement must preserve the original start date.",
                "replacementStartDate");
        }

        if (query.ReplacementEndDate is not null)
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                "Reason-only replacement must preserve the original end date.",
                "replacementEndDate");
        }

        return TryCreateInput(
            ReasonValidationPeriod,
            query.ReplacementReasonCode,
            query.ReplacementReasonComment,
            out replacementInput);
    }

    private static PreviewCorrectNonWorkingDayResult? ValidateCancelShape(
        PreviewCorrectNonWorkingDayQuery query)
    {
        if (query.ReplacementStartDate is not null)
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                "Cancel mode cannot include a replacement start date.",
                "replacementStartDate");
        }

        if (query.ReplacementEndDate is not null)
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                "Cancel mode cannot include a replacement end date.",
                "replacementEndDate");
        }

        if (!string.IsNullOrWhiteSpace(query.ReplacementReasonCode))
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                "Cancel mode cannot include a replacement reason code.",
                "replacementReasonCode");
        }

        if (!string.IsNullOrWhiteSpace(query.ReplacementReasonComment))
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                "Cancel mode cannot include a replacement reason comment.",
                "replacementReasonComment");
        }

        return null;
    }

    private static PreviewCorrectNonWorkingDayResult? TryCreateInput(
        DateRange period,
        string? reasonCode,
        string? reasonComment,
        out NonWorkingDayPreviewInput? replacementInput)
    {
        replacementInput = null;
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                "Replacement reason code is required.",
                "replacementReasonCode");
        }

        try
        {
            replacementInput = new NonWorkingDayPreviewInput(
                period,
                reasonCode,
                reasonComment);
            return null;
        }
        catch (ArgumentException exception)
            when (exception.ParamName == "reasonCode")
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                $"Replacement reason code must be {NonWorkingDayPreviewInput.ReasonCodeMaxLength} "
                + "characters or fewer.",
                "replacementReasonCode");
        }
        catch (ArgumentException exception)
            when (exception.ParamName == "reasonComment")
        {
            return PreviewCorrectNonWorkingDayResult.Invalid(
                $"Replacement reason comment must be {NonWorkingDayPreviewInput.ReasonCommentMaxLength} "
                + "characters or fewer.",
                "replacementReasonComment");
        }
    }

    private static PreviewCorrectNonWorkingDayResult? MapSourceFailure(
        CorrectNonWorkingDaySourcePreparationStatus status)
    {
        return status switch
        {
            CorrectNonWorkingDaySourcePreparationStatus.Prepared => null,
            CorrectNonWorkingDaySourcePreparationStatus.NotFound =>
                PreviewCorrectNonWorkingDayResult.Missing(),
            CorrectNonWorkingDaySourcePreparationStatus.AlreadyCanceled =>
                PreviewCorrectNonWorkingDayResult.AlreadyCanceled(),
            CorrectNonWorkingDaySourcePreparationStatus.AlreadyCorrected =>
                PreviewCorrectNonWorkingDayResult.Stale(),
            CorrectNonWorkingDaySourcePreparationStatus.InconsistentSource =>
                PreviewCorrectNonWorkingDayResult.InconsistentSource(),
            _ => PreviewCorrectNonWorkingDayResult.InconsistentSource(),
        };
    }

    private static NonWorkingDayCorrectionConfirmationMaterial
        CreateConfirmationMaterial(
            NonWorkingDayCorrectionMode mode,
            NonWorkingDayCorrectionSource source,
            NonWorkingDayPreviewInput? replacementInput,
            MembershipNonWorkingDayReplacementImpactPreparation? replacementImpact)
    {
        return mode switch
        {
            NonWorkingDayCorrectionMode.ReplaceRange =>
                NonWorkingDayCorrectionConfirmationMaterial.ForReplaceRange(
                    source,
                    replacementInput
                        ?? throw new InvalidOperationException(
                            "Range replacement input is missing."),
                    replacementImpact
                        ?? throw new InvalidOperationException(
                            "Range replacement impact is missing.")),
            NonWorkingDayCorrectionMode.ReplaceReason =>
                NonWorkingDayCorrectionConfirmationMaterial.ForReplaceReason(
                    source,
                    new NonWorkingDayPreviewInput(
                        source.Period,
                        replacementInput?.ReasonCode
                            ?? throw new InvalidOperationException(
                                "Reason replacement input is missing."),
                        replacementInput.ReasonComment)),
            NonWorkingDayCorrectionMode.Cancel =>
                NonWorkingDayCorrectionConfirmationMaterial.ForCancel(source),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
