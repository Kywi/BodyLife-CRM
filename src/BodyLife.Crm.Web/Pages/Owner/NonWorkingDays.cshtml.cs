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
    TimeProvider timeProvider)
    : PageModel
{
    public NonWorkingDayPreviewWorkspaceViewModel Workspace { get; private set; }
        = NonWorkingDayPreviewWorkspaceViewModel.Empty;

    public async Task<IActionResult> OnGetAsync(
        Guid? periodId,
        CancellationToken cancellationToken)
    {
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
        return RenderWorkspace();
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
        return RenderWorkspace();
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

    private IActionResult RenderWorkspace()
    {
        return IsHtmxRequest()
            ? Partial("_NonWorkingDayPreviewWorkspace", Workspace)
            : Page();
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

    private static bool IsCanonicalScopeFingerprint(string? fingerprint)
    {
        return fingerprint is not null
            && fingerprint.Length == NonWorkingDayPreviewConfirmation.FingerprintLength
            && fingerprint.All(character =>
                char.IsAsciiDigit(character)
                || character is >= 'A' and <= 'F');
    }
}
