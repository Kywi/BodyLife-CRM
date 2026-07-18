using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Owner;

public sealed class NonWorkingDaysModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<
        PreviewNonWorkingDayImpactQuery,
        PreviewNonWorkingDayImpactResult> previewImpact,
    IBodyLifeCommandHandler<AddNonWorkingDayCommand> addNonWorkingDay,
    IBodyLifeQueryHandler<
        GetNonWorkingDayQuery,
        GetNonWorkingDayResult> getNonWorkingDay,
    IBodyLifeQueryHandler<
        GetActiveNonWorkingDaysForCorrectionQuery,
        GetActiveNonWorkingDaysForCorrectionResult> getActiveForCorrection,
    IBodyLifeQueryHandler<
        PreviewCorrectNonWorkingDayQuery,
        PreviewCorrectNonWorkingDayResult> previewCorrection,
    TimeProvider timeProvider)
    : PageModel
{
    public NonWorkingDayPreviewWorkspaceViewModel Workspace { get; private set; }
        = NonWorkingDayPreviewWorkspaceViewModel.Empty;

    public NonWorkingDayCorrectionWorkspaceViewModel CorrectionWorkspace
    {
        get;
        private set;
    } = NonWorkingDayCorrectionWorkspaceViewModel.Empty;

    public async Task<IActionResult> OnGetAsync(
        Guid? periodId,
        CancellationToken cancellationToken)
    {
        var activePeriods = await GetActivePeriodsAsync(cancellationToken);
        if (activePeriods.Status
            == GetActiveNonWorkingDaysForCorrectionStatus.PermissionDenied)
        {
            return Forbid();
        }

        CorrectionWorkspace = NonWorkingDayCorrectionWorkspaceViewModel.FromList(
            activePeriods,
            periodId);
        if (periodId is null)
        {
            Workspace = NonWorkingDayPreviewWorkspaceViewModel.New(CurrentDate());
            return Page();
        }

        var result = await getNonWorkingDay.ExecuteAsync(
            new GetNonWorkingDayQuery(
                requestContextResolver.Require().Actor,
                periodId.Value),
            cancellationToken);
        if (result.Status == GetNonWorkingDayStatus.PermissionDenied)
        {
            return Forbid();
        }

        Workspace = result is
        {
            Status: GetNonWorkingDayStatus.Success,
            Period: { } period,
        }
            ? NonWorkingDayPreviewWorkspaceViewModel.FromCanonicalPeriod(period)
            : NonWorkingDayPreviewWorkspaceViewModel.FromCanonicalFailure(
                CurrentDate(),
                result);
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync(
        NonWorkingDayPreviewFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var result = await ExecutePreviewAsync(
            form.ProposedStartDate,
            form.ProposedEndDate,
            form.ReasonCode,
            form.ReasonComment,
            cancellationToken);
        if (result.Status == PreviewNonWorkingDayImpactStatus.PermissionDenied)
        {
            return Forbid();
        }

        Workspace = NonWorkingDayPreviewWorkspaceViewModel.FromPreviewResult(
            form,
            result);
        return await RenderWorkspaceAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostConfirmAsync(
        NonWorkingDayConfirmationFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var adapterErrors = ValidateConfirmationForm(form);
        if (adapterErrors.Count > 0)
        {
            return await RefreshPreviewAfterConfirmationErrorAsync(
                form,
                adapterErrors,
                cancellationToken);
        }

        var command = new AddNonWorkingDayCommand(
            requestContextResolver.CreateCommandEnvelope(
                occurredAt: timeProvider.GetUtcNow(),
                idempotencyKey: form.IdempotencyKey,
                reason: form.ReasonCode,
                comment: form.ReasonComment),
            new DateRange(
                form.ProposedStartDate!.Value,
                form.ProposedEndDate!.Value),
            form.ReasonCode,
            form.ReasonComment,
            form.ConfirmationToken);
        var result = await addNonWorkingDay.ExecuteAsync(
            command,
            cancellationToken);
        if (result.Status == CommandStatus.Success)
        {
            return await RenderSuccessfulConfirmationAsync(
                result,
                cancellationToken);
        }

        if (result.Errors.Any(error => error.Code == CommandErrorCode.PermissionDenied))
        {
            return Forbid();
        }

        return await RefreshPreviewAfterConfirmationErrorAsync(
            form,
            result.Errors,
            cancellationToken);
    }

    public async Task<IActionResult> OnPostCorrectionPreviewAsync(
        NonWorkingDayCorrectionPreviewFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var activePeriods = await GetActivePeriodsAsync(cancellationToken);
        if (activePeriods.Status
            == GetActiveNonWorkingDaysForCorrectionStatus.PermissionDenied)
        {
            return Forbid();
        }

        if (activePeriods.Status
            != GetActiveNonWorkingDaysForCorrectionStatus.Success)
        {
            CorrectionWorkspace =
                NonWorkingDayCorrectionWorkspaceViewModel.FromList(activePeriods);
            return RenderCorrectionWorkspace();
        }

        var adapterErrors = ValidateCorrectionPreviewForm(form, activePeriods.Items);
        if (adapterErrors.Count > 0)
        {
            CorrectionWorkspace = NonWorkingDayCorrectionWorkspaceViewModel
                .FromValidationErrors(activePeriods, form, adapterErrors);
            return RenderCorrectionWorkspace();
        }

        var result = await previewCorrection.ExecuteAsync(
            CreateCorrectionPreviewQuery(form),
            cancellationToken);
        if (result.Status == PreviewCorrectNonWorkingDayStatus.PermissionDenied)
        {
            return Forbid();
        }

        GetNonWorkingDayResult? canonicalResult = null;
        if (result.Status == PreviewCorrectNonWorkingDayStatus.Success)
        {
            canonicalResult = await getNonWorkingDay.ExecuteAsync(
                new GetNonWorkingDayQuery(
                    requestContextResolver.Require().Actor,
                    form.PeriodId!.Value),
                cancellationToken);
            if (canonicalResult.Status == GetNonWorkingDayStatus.PermissionDenied)
            {
                return Forbid();
            }
        }

        CorrectionWorkspace = NonWorkingDayCorrectionWorkspaceViewModel
            .FromPreviewResult(activePeriods, form, result, canonicalResult);
        return RenderCorrectionWorkspace();
    }

    private async Task<IActionResult> RenderSuccessfulConfirmationAsync(
        CommandResult result,
        CancellationToken cancellationToken)
    {
        if (result.PrimaryEntityId is not { } periodId
            || periodId.Type != AddNonWorkingDayCommand.PrimaryEntityType
            || periodId.Value == Guid.Empty
            || result.RereadTargetId is not { } rereadTarget
            || rereadTarget.Type != AddNonWorkingDayCommand.CanonicalRereadEntityType
            || rereadTarget.Value != periodId.Value
            || result.AuditEntryId is not { } auditEntryId
            || auditEntryId.Value == Guid.Empty
            || result.RelatedEntityIds.Any(entityId =>
                entityId.Type != AddNonWorkingDayCommand.MembershipEntityType
                || entityId.Value == Guid.Empty)
            || result.RelatedEntityIds.Select(entityId => entityId.Value)
                .Distinct()
                .Count() != result.RelatedEntityIds.Count)
        {
            throw new InvalidOperationException(
                "AddNonWorkingDay did not return the expected canonical result targets.");
        }

        var canonicalResult = await getNonWorkingDay.ExecuteAsync(
            new GetNonWorkingDayQuery(
                requestContextResolver.Require().Actor,
                periodId.Value),
            cancellationToken);
        if (canonicalResult.Status == GetNonWorkingDayStatus.PermissionDenied)
        {
            return Forbid();
        }

        if (canonicalResult is not
            {
                Status: GetNonWorkingDayStatus.Success,
                Period: { } canonicalPeriod,
            }
            || canonicalPeriod.AuditEntryId != auditEntryId.Value
            || !canonicalPeriod.Applications
                .Select(application => application.MembershipId)
                .SequenceEqual(result.RelatedEntityIds
                    .Select(entityId => entityId.Value)
                    .OrderBy(id => id)))
        {
            throw new InvalidOperationException(
                "AddNonWorkingDay canonical reread did not match the committed command result.");
        }

        if (!IsHtmxRequest())
        {
            return RedirectToPage(
                "/Owner/NonWorkingDays",
                new { periodId = periodId.Value });
        }

        Workspace = NonWorkingDayPreviewWorkspaceViewModel.FromCanonicalPeriod(
            canonicalPeriod);
        Response.Headers["HX-Push-Url"] = Url.Page(
                "/Owner/NonWorkingDays",
                values: new { periodId = periodId.Value })
            ?? "/Owner/NonWorkingDays";
        return Partial("_NonWorkingDayPreviewWorkspace", Workspace);
    }

    private async Task<IActionResult> RefreshPreviewAfterConfirmationErrorAsync(
        NonWorkingDayConfirmationFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        var refreshedPreview = await ExecutePreviewAsync(
            form.ProposedStartDate,
            form.ProposedEndDate,
            form.ReasonCode,
            form.ReasonComment,
            cancellationToken);
        if (refreshedPreview.Status
            == PreviewNonWorkingDayImpactStatus.PermissionDenied)
        {
            return Forbid();
        }

        Workspace = NonWorkingDayPreviewWorkspaceViewModel.FromConfirmationRefresh(
            form,
            refreshedPreview,
            errors);
        return await RenderWorkspaceAsync(cancellationToken);
    }

    private Task<PreviewNonWorkingDayImpactResult> ExecutePreviewAsync(
        DateOnly? startDate,
        DateOnly? endDate,
        string? reasonCode,
        string? reasonComment,
        CancellationToken cancellationToken)
    {
        return previewImpact.ExecuteAsync(
            new PreviewNonWorkingDayImpactQuery(
                requestContextResolver.Require().Actor,
                startDate ?? default,
                endDate ?? default,
                reasonCode,
                reasonComment),
            cancellationToken);
    }

    private async Task<IActionResult> RenderWorkspaceAsync(
        CancellationToken cancellationToken)
    {
        if (IsHtmxRequest())
        {
            return Partial("_NonWorkingDayPreviewWorkspace", Workspace);
        }

        var activePeriods = await GetActivePeriodsAsync(cancellationToken);
        if (activePeriods.Status
            == GetActiveNonWorkingDaysForCorrectionStatus.PermissionDenied)
        {
            return Forbid();
        }

        CorrectionWorkspace = NonWorkingDayCorrectionWorkspaceViewModel.FromList(
            activePeriods);
        return Page();
    }

    private IActionResult RenderCorrectionWorkspace()
    {
        if (IsHtmxRequest())
        {
            return Partial(
                "_NonWorkingDayCorrectionWorkspace",
                CorrectionWorkspace);
        }

        Workspace = NonWorkingDayPreviewWorkspaceViewModel.New(CurrentDate());
        return Page();
    }

    private Task<GetActiveNonWorkingDaysForCorrectionResult>
        GetActivePeriodsAsync(CancellationToken cancellationToken)
    {
        return getActiveForCorrection.ExecuteAsync(
            new GetActiveNonWorkingDaysForCorrectionQuery(
                requestContextResolver.Require().Actor),
            cancellationToken);
    }

    private PreviewCorrectNonWorkingDayQuery CreateCorrectionPreviewQuery(
        NonWorkingDayCorrectionPreviewFormInput form)
    {
        var hasReplacementRange = form.Mode
            == NonWorkingDayCorrectionMode.ReplaceRange;
        var hasReplacementReason = form.Mode is
            NonWorkingDayCorrectionMode.ReplaceRange
            or NonWorkingDayCorrectionMode.ReplaceReason;
        return new PreviewCorrectNonWorkingDayQuery(
            requestContextResolver.Require().Actor,
            form.PeriodId!.Value,
            form.Mode,
            hasReplacementRange ? form.ReplacementStartDate : null,
            hasReplacementRange ? form.ReplacementEndDate : null,
            hasReplacementReason ? form.ReplacementReasonCode : null,
            hasReplacementReason ? form.ReplacementReasonComment : null);
    }

    private DateOnly CurrentDate()
    {
        return DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
    }

    private bool IsHtmxRequest()
    {
        return string.Equals(
            Request.Headers["HX-Request"].ToString(),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<CommandError> ValidateConfirmationForm(
        NonWorkingDayConfirmationFormInput form)
    {
        var errors = new List<CommandError>();
        if (!form.Confirmed)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Confirm the exact affected Membership set and full applied period.",
                "confirmed"));
        }

        if (form.ProposedStartDate is null
            || form.ProposedStartDate == default)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Non-working period start date is required.",
                "period.startDate"));
        }

        if (form.ProposedEndDate is null || form.ProposedEndDate == default)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Non-working period end date is required.",
                "period.endDate"));
        }
        else if (form.ProposedStartDate is { } startDate
            && form.ProposedEndDate < startDate)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Non-working period end date must be on or after the start date.",
                "period"));
        }

        if (string.IsNullOrWhiteSpace(form.ConfirmationToken)
            || form.ConfirmationToken != form.ConfirmationToken.Trim()
            || form.ConfirmationToken.Length
                > NonWorkingDayPreviewConfirmation.MaxTokenLength)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "A canonical preview confirmation token is required.",
                "confirmationToken"));
        }

        if (!IsCanonicalScopeFingerprint(form.ScopeFingerprint))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "A canonical affected-scope fingerprint is required.",
                "scopeFingerprint"));
        }

        if (string.IsNullOrWhiteSpace(form.IdempotencyKey))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "A duplicate-submit protection key is required.",
                "idempotencyKey"));
        }

        return errors.AsReadOnly();
    }

    private static IReadOnlyList<NonWorkingDayCorrectionPreviewFormError>
        ValidateCorrectionPreviewForm(
            NonWorkingDayCorrectionPreviewFormInput form,
            IReadOnlyList<ActiveNonWorkingDayForCorrection> activePeriods)
    {
        var errors = new List<NonWorkingDayCorrectionPreviewFormError>();
        if (form.PeriodId is null || form.PeriodId == Guid.Empty)
        {
            errors.Add(new NonWorkingDayCorrectionPreviewFormError(
                "periodId",
                "Select an active non-working period."));
        }
        else if (!activePeriods.Any(period => period.PeriodId == form.PeriodId))
        {
            errors.Add(new NonWorkingDayCorrectionPreviewFormError(
                "periodId",
                "The selected period is no longer active. Refresh the list."));
        }

        if (!Enum.IsDefined(form.Mode))
        {
            errors.Add(new NonWorkingDayCorrectionPreviewFormError(
                "mode",
                "Select a supported correction mode."));
        }

        var correctionReason = form.CorrectionReason?.Trim();
        if (string.IsNullOrWhiteSpace(correctionReason)
            || correctionReason.Length
                > NonWorkingDayCorrectionWorkspaceViewModel.CorrectionReasonMaxLength)
        {
            errors.Add(new NonWorkingDayCorrectionPreviewFormError(
                "correctionReason",
                $"Correction reason is required and must be {NonWorkingDayCorrectionWorkspaceViewModel.CorrectionReasonMaxLength} characters or fewer."));
        }

        if (form.CorrectionComment?.Trim().Length
            > NonWorkingDayCorrectionWorkspaceViewModel.CorrectionCommentMaxLength)
        {
            errors.Add(new NonWorkingDayCorrectionPreviewFormError(
                "correctionComment",
                $"Correction comment must be {NonWorkingDayCorrectionWorkspaceViewModel.CorrectionCommentMaxLength} characters or fewer."));
        }

        return errors.AsReadOnly();
    }

    private static bool IsCanonicalScopeFingerprint(string? fingerprint)
    {
        return fingerprint is not null
            && fingerprint.Length == NonWorkingDayPreviewConfirmation.FingerprintLength
            && fingerprint.All(character =>
                char.IsAsciiDigit(character)
                || character is >= 'A' and <= 'F');
    }
}
