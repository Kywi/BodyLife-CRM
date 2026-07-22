using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

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
    IBodyLifeCommandHandler<CorrectNonWorkingDayCommand> correctNonWorkingDay,
    IBodyLifeQueryHandler<
        GetNonWorkingDayCorrectionOutcomeQuery,
        GetNonWorkingDayCorrectionOutcomeResult> getCorrectionOutcome,
    TimeProvider timeProvider,
    IStringLocalizer<BodyLife.Crm.Web.Localization.Owner> localizer)
    : PageModel
{
    public string T(string key, params object[] arguments) => localizer[key, arguments];
    public NonWorkingDayPreviewWorkspaceViewModel Workspace { get; private set; }
        = NonWorkingDayPreviewWorkspaceViewModel.Empty;

    public NonWorkingDayCorrectionWorkspaceViewModel CorrectionWorkspace
    {
        get;
        private set;
    } = NonWorkingDayCorrectionWorkspaceViewModel.Empty;

    public async Task<IActionResult> OnGetAsync(
        Guid? periodId,
        Guid? correctionAuditId,
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
        if (correctionAuditId is { } auditId)
        {
            var correctionResult = periodId is { } originalPeriodId
                ? await getCorrectionOutcome.ExecuteAsync(
                    new GetNonWorkingDayCorrectionOutcomeQuery(
                        requestContextResolver.Require().Actor,
                        originalPeriodId,
                        auditId),
                    cancellationToken)
                : GetNonWorkingDayCorrectionOutcomeResult.Invalid(
                    T("NonWorkingDays.Error.PeriodRequired"),
                    "periodId");
            if (correctionResult.Status
                == GetNonWorkingDayCorrectionOutcomeStatus.PermissionDenied)
            {
                return Forbid();
            }

            Workspace = NonWorkingDayPreviewWorkspaceViewModel.New(CurrentDate());
            CorrectionWorkspace = NonWorkingDayCorrectionWorkspaceViewModel
                .FromCanonicalOutcome(activePeriods, correctionResult);
            return Page();
        }

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
                cancellationToken,
                preserveLocalizedAdapterMessages: true);
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

    public async Task<IActionResult> OnPostCorrectionConfirmAsync(
        NonWorkingDayCorrectionConfirmationFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var adapterErrors = ValidateCorrectionConfirmationForm(form);
        if (adapterErrors.Count > 0)
        {
            return await RefreshCorrectionPreviewAfterConfirmationErrorAsync(
                form,
                adapterErrors,
                cancellationToken,
                preserveLocalizedAdapterMessages: true);
        }

        var hasReplacementRange = form.Mode
            == NonWorkingDayCorrectionMode.ReplaceRange;
        var hasReplacementReason = form.Mode is
            NonWorkingDayCorrectionMode.ReplaceRange
            or NonWorkingDayCorrectionMode.ReplaceReason;
        var command = new CorrectNonWorkingDayCommand(
            requestContextResolver.CreateCommandEnvelope(
                occurredAt: timeProvider.GetUtcNow(),
                idempotencyKey: form.IdempotencyKey,
                reason: form.CorrectionReason,
                comment: form.CorrectionComment),
            form.PeriodId!.Value,
            form.Mode,
            hasReplacementRange ? form.ReplacementStartDate : null,
            hasReplacementRange ? form.ReplacementEndDate : null,
            hasReplacementReason ? form.ReplacementReasonCode : null,
            hasReplacementReason ? form.ReplacementReasonComment : null,
            form.ConfirmationToken);
        var result = await correctNonWorkingDay.ExecuteAsync(
            command,
            cancellationToken);
        if (result.Status == CommandStatus.Success)
        {
            return await RenderSuccessfulCorrectionAsync(
                form,
                result,
                cancellationToken);
        }

        if (result.Errors.Any(error =>
            error.Code == CommandErrorCode.PermissionDenied))
        {
            return Forbid();
        }

        return await RefreshCorrectionPreviewAfterConfirmationErrorAsync(
            form,
            result.Errors,
            cancellationToken);
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

    private async Task<IActionResult> RenderSuccessfulCorrectionAsync(
        NonWorkingDayCorrectionConfirmationFormInput form,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        var expectedPrimaryType = form.Mode
            == NonWorkingDayCorrectionMode.Cancel
                ? CorrectNonWorkingDayCommand.CancellationEntityType
                : CorrectNonWorkingDayCommand.PeriodEntityType;
        var membershipIds = result.RelatedEntityIds
            .Skip(1)
            .Select(entityId => entityId.Value)
            .ToArray();
        if (result.PrimaryEntityId is not { } primaryEntity
            || primaryEntity.Type != expectedPrimaryType
            || primaryEntity.Value == Guid.Empty
            || result.RereadTargetId is not { } rereadTarget
            || rereadTarget.Type
                != CorrectNonWorkingDayCommand.CanonicalRereadEntityType
            || rereadTarget.Value != form.PeriodId
            || result.AuditEntryId is not { } auditEntryId
            || auditEntryId.Value == Guid.Empty
            || result.RelatedEntityIds.Count == 0
            || result.RelatedEntityIds[0] != new EntityId(
                CorrectNonWorkingDayCommand.PeriodEntityType,
                form.PeriodId!.Value)
            || result.RelatedEntityIds.Skip(1).Any(entityId =>
                entityId.Type != CorrectNonWorkingDayCommand.MembershipEntityType
                || entityId.Value == Guid.Empty)
            || membershipIds.Distinct().Count() != membershipIds.Length
            || !membershipIds.SequenceEqual(membershipIds.Order()))
        {
            throw new InvalidOperationException(
                "CorrectNonWorkingDay did not return the expected correction facts, affected Memberships and canonical reread target.");
        }

        var correctionResult = await getCorrectionOutcome.ExecuteAsync(
            new GetNonWorkingDayCorrectionOutcomeQuery(
                requestContextResolver.Require().Actor,
                form.PeriodId.Value,
                auditEntryId.Value),
            cancellationToken);
        if (correctionResult.Status
            == GetNonWorkingDayCorrectionOutcomeStatus.PermissionDenied)
        {
            return Forbid();
        }

        if (correctionResult is not
            {
                Status: GetNonWorkingDayCorrectionOutcomeStatus.Success,
                Correction: { } correction,
            }
            || correction.Mode != form.Mode
            || correction.PrimaryEntityId != primaryEntity.Value
            || correction.AuditEntryId != auditEntryId.Value
            || correction.OriginalPeriod.PeriodId != form.PeriodId.Value
            || !correction.AffectedMembershipIds.SequenceEqual(membershipIds))
        {
            throw new InvalidOperationException(
                "CorrectNonWorkingDay canonical reread did not match the committed command result.");
        }

        if (!IsHtmxRequest())
        {
            return RedirectToPage(
                "/Owner/NonWorkingDays",
                new
                {
                    periodId = form.PeriodId.Value,
                    correctionAuditId = auditEntryId.Value,
                });
        }

        var activePeriods = await GetActivePeriodsAsync(cancellationToken);
        if (activePeriods.Status
            == GetActiveNonWorkingDaysForCorrectionStatus.PermissionDenied)
        {
            return Forbid();
        }

        CorrectionWorkspace = NonWorkingDayCorrectionWorkspaceViewModel
            .FromCanonicalOutcome(activePeriods, correctionResult);
        Response.Headers["HX-Push-Url"] = Url.Page(
                "/Owner/NonWorkingDays",
                values: new
                {
                    periodId = form.PeriodId.Value,
                    correctionAuditId = auditEntryId.Value,
                })
            ?? "/Owner/NonWorkingDays";
        return Partial(
            "_NonWorkingDayCorrectionWorkspace",
            CorrectionWorkspace);
    }

    private async Task<IActionResult> RefreshPreviewAfterConfirmationErrorAsync(
        NonWorkingDayConfirmationFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken,
        bool preserveLocalizedAdapterMessages = false)
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
            errors,
            preserveLocalizedAdapterMessages);
        return await RenderWorkspaceAsync(cancellationToken);
    }

    private async Task<IActionResult>
        RefreshCorrectionPreviewAfterConfirmationErrorAsync(
            NonWorkingDayCorrectionConfirmationFormInput form,
            IReadOnlyList<CommandError> errors,
            CancellationToken cancellationToken,
            bool preserveLocalizedAdapterMessages = false)
    {
        var activePeriods = await GetActivePeriodsAsync(cancellationToken);
        if (activePeriods.Status
            == GetActiveNonWorkingDaysForCorrectionStatus.PermissionDenied)
        {
            return Forbid();
        }

        if (activePeriods.Status
                != GetActiveNonWorkingDaysForCorrectionStatus.Success
            || form.PeriodId is null
            || !activePeriods.Items.Any(period =>
                period.PeriodId == form.PeriodId.Value))
        {
            CorrectionWorkspace = NonWorkingDayCorrectionWorkspaceViewModel
                .FromConfirmationFailure(
                    activePeriods,
                    form,
                    errors,
                    preserveLocalizedAdapterMessages);
            return RenderCorrectionWorkspace();
        }

        var refreshedPreview = await previewCorrection.ExecuteAsync(
            CreateCorrectionPreviewQuery(form),
            cancellationToken);
        if (refreshedPreview.Status
            == PreviewCorrectNonWorkingDayStatus.PermissionDenied)
        {
            return Forbid();
        }

        GetNonWorkingDayResult? canonicalResult = null;
        if (refreshedPreview.Status
            == PreviewCorrectNonWorkingDayStatus.Success)
        {
            canonicalResult = await getNonWorkingDay.ExecuteAsync(
                new GetNonWorkingDayQuery(
                    requestContextResolver.Require().Actor,
                    form.PeriodId.Value),
                cancellationToken);
            if (canonicalResult.Status == GetNonWorkingDayStatus.PermissionDenied)
            {
                return Forbid();
            }
        }

        CorrectionWorkspace = NonWorkingDayCorrectionWorkspaceViewModel
            .FromConfirmationRefresh(
                activePeriods,
                form,
                refreshedPreview,
                canonicalResult,
                errors,
                preserveLocalizedAdapterMessages);
        return RenderCorrectionWorkspace();
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

    private IReadOnlyList<CommandError> ValidateConfirmationForm(
        NonWorkingDayConfirmationFormInput form)
    {
        var errors = new List<CommandError>();
        if (!form.Confirmed)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.ConfirmImpact"),
                "confirmed"));
        }

        if (form.ProposedStartDate is null
            || form.ProposedStartDate == default)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.StartRequired"),
                "period.startDate"));
        }

        if (form.ProposedEndDate is null || form.ProposedEndDate == default)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.EndRequired"),
                "period.endDate"));
        }
        else if (form.ProposedStartDate is { } startDate
            && form.ProposedEndDate < startDate)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.EndBeforeStart"),
                "period"));
        }

        if (string.IsNullOrWhiteSpace(form.ConfirmationToken)
            || form.ConfirmationToken != form.ConfirmationToken.Trim()
            || form.ConfirmationToken.Length
                > NonWorkingDayPreviewConfirmation.MaxTokenLength)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.PreviewToken"),
                "confirmationToken"));
        }

        if (!IsCanonicalScopeFingerprint(form.ScopeFingerprint))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.ScopeFingerprint"),
                "scopeFingerprint"));
        }

        if (string.IsNullOrWhiteSpace(form.IdempotencyKey))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.IdempotencyKey"),
                "idempotencyKey"));
        }

        return errors.AsReadOnly();
    }

    private IReadOnlyList<NonWorkingDayCorrectionPreviewFormError>
        ValidateCorrectionPreviewForm(
            NonWorkingDayCorrectionPreviewFormInput form,
            IReadOnlyList<ActiveNonWorkingDayForCorrection> activePeriods)
    {
        var errors = new List<NonWorkingDayCorrectionPreviewFormError>();
        if (form.PeriodId is null || form.PeriodId == Guid.Empty)
        {
            errors.Add(new NonWorkingDayCorrectionPreviewFormError(
                "periodId",
                T("NonWorkingDays.Error.SelectActivePeriod")));
        }
        else if (!activePeriods.Any(period => period.PeriodId == form.PeriodId))
        {
            errors.Add(new NonWorkingDayCorrectionPreviewFormError(
                "periodId",
                T("NonWorkingDays.Error.PeriodNoLongerActive")));
        }

        if (!Enum.IsDefined(form.Mode))
        {
            errors.Add(new NonWorkingDayCorrectionPreviewFormError(
                "mode",
                T("NonWorkingDays.Error.SelectCorrectionMode")));
        }

        var correctionReason = form.CorrectionReason?.Trim();
        if (string.IsNullOrWhiteSpace(correctionReason)
            || correctionReason.Length
                > NonWorkingDayCorrectionWorkspaceViewModel.CorrectionReasonMaxLength)
        {
            errors.Add(new NonWorkingDayCorrectionPreviewFormError(
                "correctionReason",
                T("NonWorkingDays.Error.CorrectionReason", NonWorkingDayCorrectionWorkspaceViewModel.CorrectionReasonMaxLength)));
        }

        var correctionComment = form.CorrectionComment?.Trim();
        if (string.IsNullOrWhiteSpace(correctionComment)
            || correctionComment.Length
                > NonWorkingDayCorrectionWorkspaceViewModel.CorrectionCommentMaxLength)
        {
            errors.Add(new NonWorkingDayCorrectionPreviewFormError(
                "correctionComment",
                T("NonWorkingDays.Error.CorrectionComment", NonWorkingDayCorrectionWorkspaceViewModel.CorrectionCommentMaxLength)));
        }

        return errors.AsReadOnly();
    }

    private IReadOnlyList<CommandError>
        ValidateCorrectionConfirmationForm(
            NonWorkingDayCorrectionConfirmationFormInput form)
    {
        var errors = new List<CommandError>();
        if (!form.Confirmed)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.ConfirmCorrection"),
                "confirmed"));
        }

        if (form.PeriodId is null || form.PeriodId == Guid.Empty)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.PeriodRequired"),
                "periodId"));
        }

        if (!Enum.IsDefined(form.Mode))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.SelectCorrectionMode"),
                "mode"));
        }

        if (form.Mode == NonWorkingDayCorrectionMode.ReplaceRange)
        {
            if (form.ReplacementStartDate is null
                || form.ReplacementStartDate == default)
            {
                errors.Add(new CommandError(
                    CommandErrorCode.ValidationFailed,
                    T("NonWorkingDays.Error.ReplacementStartRequired"),
                    "replacementStartDate"));
            }

            if (form.ReplacementEndDate is null
                || form.ReplacementEndDate == default)
            {
                errors.Add(new CommandError(
                    CommandErrorCode.ValidationFailed,
                    T("NonWorkingDays.Error.ReplacementEndRequired"),
                    "replacementEndDate"));
            }
            else if (form.ReplacementStartDate is { } startDate
                && form.ReplacementEndDate < startDate)
            {
                errors.Add(new CommandError(
                    CommandErrorCode.ValidationFailed,
                    T("NonWorkingDays.Error.ReplacementEndBeforeStart"),
                    "replacementEndDate"));
            }
        }

        if (form.Mode is NonWorkingDayCorrectionMode.ReplaceRange
            or NonWorkingDayCorrectionMode.ReplaceReason)
        {
            var replacementReasonCode = form.ReplacementReasonCode?.Trim();
            if (string.IsNullOrWhiteSpace(replacementReasonCode)
                || replacementReasonCode.Length
                    > NonWorkingDayCorrectionWorkspaceViewModel
                        .ReplacementReasonCodeMaxLength)
            {
                errors.Add(new CommandError(
                    CommandErrorCode.ValidationFailed,
                    T("NonWorkingDays.Error.ReplacementReasonCode", NonWorkingDayCorrectionWorkspaceViewModel.ReplacementReasonCodeMaxLength),
                    "replacementReasonCode"));
            }

            if (form.ReplacementReasonComment?.Trim().Length
                > NonWorkingDayCorrectionWorkspaceViewModel
                    .ReplacementReasonCommentMaxLength)
            {
                errors.Add(new CommandError(
                    CommandErrorCode.ValidationFailed,
                    T("NonWorkingDays.Error.ReplacementReasonComment", NonWorkingDayCorrectionWorkspaceViewModel.ReplacementReasonCommentMaxLength),
                    "replacementReasonComment"));
            }
        }

        var correctionReason = form.CorrectionReason?.Trim();
        if (string.IsNullOrWhiteSpace(correctionReason)
            || correctionReason.Length
                > NonWorkingDayCorrectionWorkspaceViewModel.CorrectionReasonMaxLength)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ReasonRequired,
                T("NonWorkingDays.Error.CorrectionReason", NonWorkingDayCorrectionWorkspaceViewModel.CorrectionReasonMaxLength),
                "reason"));
        }

        var correctionComment = form.CorrectionComment?.Trim();
        if (string.IsNullOrWhiteSpace(correctionComment)
            || correctionComment.Length
                > NonWorkingDayCorrectionWorkspaceViewModel.CorrectionCommentMaxLength)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ReasonRequired,
                T("NonWorkingDays.Error.CorrectionComment", NonWorkingDayCorrectionWorkspaceViewModel.CorrectionCommentMaxLength),
                "comment"));
        }

        if (string.IsNullOrWhiteSpace(form.ConfirmationToken)
            || form.ConfirmationToken != form.ConfirmationToken.Trim()
            || form.ConfirmationToken.Length
                > NonWorkingDayCorrectionConfirmation.MaxTokenLength)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.CorrectionToken"),
                "confirmationToken"));
        }

        if (!IsCanonicalCorrectionFingerprint(form.ScopeFingerprint))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.CorrectionFingerprint"),
                "scopeFingerprint"));
        }

        if (string.IsNullOrWhiteSpace(form.IdempotencyKey))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                T("NonWorkingDays.Error.IdempotencyKey"),
                "idempotencyKey"));
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

    private static bool IsCanonicalCorrectionFingerprint(string? fingerprint)
    {
        return fingerprint is not null
            && fingerprint.Length
                == NonWorkingDayCorrectionConfirmation.FingerprintLength
            && fingerprint.All(character =>
                char.IsAsciiDigit(character)
                || character is >= 'A' and <= 'F');
    }
}
