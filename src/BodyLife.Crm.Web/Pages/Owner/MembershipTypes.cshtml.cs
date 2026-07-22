using System.Globalization;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace BodyLife.Crm.Web.Pages.Owner;

public sealed class MembershipTypesModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<
        GetMembershipTypesForIssueQuery,
        GetMembershipTypesForIssueResult> getMembershipTypes,
    IBodyLifeCommandHandler<CreateMembershipTypeCommand> createMembershipType,
    IBodyLifeCommandHandler<EditMembershipTypeCommand> editMembershipType,
    IBodyLifeCommandHandler<DeactivateMembershipTypeCommand> deactivateMembershipType,
    IStringLocalizer<BodyLife.Crm.Web.Localization.Owner> localizer)
    : PageModel
{
    [TempData]
    public string? OperationMessage { get; set; }

    public IReadOnlyList<MembershipTypeCatalogItem> MembershipTypes { get; private set; } = [];

    public QueryPermissionSet AllowedActions { get; private set; } = QueryPermissionSet.Empty;

    public CreateMembershipTypeFormViewModel CreateForm { get; private set; }
        = CreateMembershipTypeFormViewModel.New();

    public IReadOnlyDictionary<Guid, EditMembershipTypeFormViewModel> EditForms { get; private set; }
        = new Dictionary<Guid, EditMembershipTypeFormViewModel>();

    public IReadOnlyDictionary<Guid, DeactivateMembershipTypeFormViewModel> DeactivateForms { get; private set; }
        = new Dictionary<Guid, DeactivateMembershipTypeFormViewModel>();

    public IReadOnlyList<CommandError> CatalogErrors { get; private set; } = [];

    public int ActiveCount => MembershipTypes.Count(membershipType => membershipType.IsActive);

    public int InactiveCount => MembershipTypes.Count - ActiveCount;

    public bool CanCreateCatalog =>
        AllowedActions.IsAllowed(MembershipTypeCatalogActionKeys.Create);

    public bool CanEditCatalog =>
        AllowedActions.IsAllowed(MembershipTypeCatalogActionKeys.Edit);

    public bool CanDeactivateCatalog =>
        AllowedActions.IsAllowed(MembershipTypeCatalogActionKeys.Deactivate);

    public bool CanManageCatalog =>
        CanCreateCatalog
        && CanEditCatalog
        && CanDeactivateCatalog;

    public string T(string key, params object[] arguments) => localizer[key, arguments];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        CreateForm = CreateMembershipTypeFormViewModel.New();
        return await LoadCatalogAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(
        CreateMembershipTypeFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var inputErrors = ValidateRepresentableInput(
            form.DurationDays,
            form.VisitsLimit,
            form.PriceAmount,
            form.PriceCurrency,
            out var price);
        if (inputErrors.Count > 0)
        {
            return await RenderCreateErrorsAsync(form, inputErrors, cancellationToken);
        }

        var command = new CreateMembershipTypeCommand(
            requestContextResolver.CreateCommandEnvelope(idempotencyKey: form.IdempotencyKey),
            form.Name ?? string.Empty,
            form.DurationDays!.Value,
            form.VisitsLimit!.Value,
            price,
            form.Comment);
        var result = await createMembershipType.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            if (result.PrimaryEntityId is not { } primaryEntity
                || result.RereadTargetId is not { } rereadTarget
                || primaryEntity != rereadTarget)
            {
                throw new InvalidOperationException(
                    "CreateMembershipType did not return the expected canonical reread target.");
            }

            OperationMessage = result.AuditEntryId is { } auditEntryId
                ? T("MembershipTypes.Success.CreatedWithAudit", auditEntryId.Value.ToString("N")[..8])
                : T("MembershipTypes.Success.Created");

            return RedirectToPage();
        }

        if (result.Errors.Any(error => error.Code == CommandErrorCode.PermissionDenied))
        {
            return Forbid();
        }

        return await RenderCreateErrorsAsync(form, result.Errors, cancellationToken);
    }

    public async Task<IActionResult> OnPostEditAsync(
        EditMembershipTypeFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var inputErrors = ValidateRepresentableInput(
            form.DurationDays,
            form.VisitsLimit,
            form.PriceAmount,
            form.PriceCurrency,
            out var price);
        if (inputErrors.Count > 0)
        {
            return await RenderEditErrorsAsync(
                form,
                inputErrors,
                useCanonicalValues: false,
                cancellationToken);
        }

        var command = new EditMembershipTypeCommand(
            requestContextResolver.CreateCommandEnvelope(
                idempotencyKey: form.IdempotencyKey,
                reason: form.Reason),
            form.MembershipTypeId,
            form.ExpectedUpdatedAt,
            form.Name ?? string.Empty,
            form.DurationDays!.Value,
            form.VisitsLimit!.Value,
            price,
            form.Comment);
        var result = await editMembershipType.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            if (result.PrimaryEntityId is not { } primaryEntity
                || result.RereadTargetId is not { } rereadTarget
                || primaryEntity != rereadTarget
                || primaryEntity.Value != form.MembershipTypeId)
            {
                throw new InvalidOperationException(
                    "EditMembershipType did not return the expected canonical reread target.");
            }

            OperationMessage = result.AuditEntryId is { } auditEntryId
                ? T("MembershipTypes.Success.UpdatedWithAudit", auditEntryId.Value.ToString("N")[..8])
                : T("MembershipTypes.Success.Updated");

            return RedirectToPage();
        }

        if (result.Errors.Any(error => error.Code == CommandErrorCode.PermissionDenied))
        {
            return Forbid();
        }

        return await RenderEditErrorsAsync(
            form,
            result.Errors,
            RequiresCanonicalEditRefresh(result.Errors),
            cancellationToken);
    }

    public async Task<IActionResult> OnPostDeactivateAsync(
        DeactivateMembershipTypeFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var command = new DeactivateMembershipTypeCommand(
            requestContextResolver.CreateCommandEnvelope(
                idempotencyKey: form.IdempotencyKey,
                reason: form.Reason),
            form.MembershipTypeId,
            form.ExpectedUpdatedAt);
        var result = await deactivateMembershipType.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            if (result.PrimaryEntityId is not { } primaryEntity
                || result.RereadTargetId is not { } rereadTarget
                || primaryEntity != rereadTarget
                || primaryEntity.Value != form.MembershipTypeId)
            {
                throw new InvalidOperationException(
                    "DeactivateMembershipType did not return the expected canonical reread target.");
            }

            OperationMessage = result.AuditEntryId is { } auditEntryId
                ? T("MembershipTypes.Success.DeactivatedWithAudit", auditEntryId.Value.ToString("N")[..8])
                : T("MembershipTypes.Success.Deactivated");

            return RedirectToPage();
        }

        if (result.Errors.Any(error => error.Code == CommandErrorCode.PermissionDenied))
        {
            return Forbid();
        }

        return await RenderDeactivateErrorsAsync(
            form,
            result.Errors,
            RequiresCanonicalDeactivateRefresh(result.Errors),
            cancellationToken);
    }

    private async Task<IActionResult> LoadCatalogAsync(
        CancellationToken cancellationToken,
        EditFormRenderState? editState = null,
        DeactivateFormRenderState? deactivateState = null)
    {
        var actor = requestContextResolver.Require().Actor;
        var result = await getMembershipTypes.ExecuteAsync(
            new GetMembershipTypesForIssueQuery(actor, IncludeInactive: true),
            cancellationToken);

        if (result.Status != GetMembershipTypesForIssueStatus.Success)
        {
            return Forbid();
        }

        MembershipTypes = result.Items;
        AllowedActions = result.AllowedActions;
        CatalogErrors = [];
        BuildEditForms(editState);
        BuildDeactivateForms(deactivateState);

        return Page();
    }

    private async Task<IActionResult> RenderCreateErrorsAsync(
        CreateMembershipTypeFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        CreateForm = CreateMembershipTypeFormViewModel.FromSubmission(form, errors);
        return await LoadCatalogAsync(cancellationToken);
    }

    private Task<IActionResult> RenderEditErrorsAsync(
        EditMembershipTypeFormInput form,
        IReadOnlyList<CommandError> errors,
        bool useCanonicalValues,
        CancellationToken cancellationToken)
    {
        return LoadCatalogAsync(
            cancellationToken,
            editState: new EditFormRenderState(form, errors, useCanonicalValues));
    }

    private Task<IActionResult> RenderDeactivateErrorsAsync(
        DeactivateMembershipTypeFormInput form,
        IReadOnlyList<CommandError> errors,
        bool useCanonicalValues,
        CancellationToken cancellationToken)
    {
        return LoadCatalogAsync(
            cancellationToken,
            deactivateState: new DeactivateFormRenderState(form, errors, useCanonicalValues));
    }

    private void BuildEditForms(EditFormRenderState? state)
    {
        if (!CanEditCatalog)
        {
            EditForms = new Dictionary<Guid, EditMembershipTypeFormViewModel>();
            return;
        }

        var forms = MembershipTypes.ToDictionary(
            membershipType => membershipType.MembershipTypeId,
            membershipType => EditMembershipTypeFormViewModel.FromCatalog(membershipType));

        if (state is not null)
        {
            var canonicalMembershipType = MembershipTypes.SingleOrDefault(
                membershipType => membershipType.MembershipTypeId == state.Form.MembershipTypeId);

            if (canonicalMembershipType is null)
            {
                CatalogErrors = state.Errors.ToArray();
            }
            else
            {
                forms[state.Form.MembershipTypeId] = state.UseCanonicalValues
                    ? EditMembershipTypeFormViewModel.FromCatalog(
                        canonicalMembershipType,
                        state.Errors,
                        isOpen: true)
                    : EditMembershipTypeFormViewModel.FromSubmission(state.Form, state.Errors);
            }
        }

        EditForms = forms;
    }

    private void BuildDeactivateForms(DeactivateFormRenderState? state)
    {
        if (!CanDeactivateCatalog)
        {
            DeactivateForms = new Dictionary<Guid, DeactivateMembershipTypeFormViewModel>();
            return;
        }

        var forms = MembershipTypes
            .Where(membershipType => membershipType.IsActive)
            .ToDictionary(
                membershipType => membershipType.MembershipTypeId,
                membershipType => DeactivateMembershipTypeFormViewModel.FromCatalog(membershipType));

        if (state is not null)
        {
            var canonicalMembershipType = MembershipTypes.SingleOrDefault(
                membershipType => membershipType.MembershipTypeId == state.Form.MembershipTypeId);

            if (canonicalMembershipType is null || !canonicalMembershipType.IsActive)
            {
                CatalogErrors = state.Errors.ToArray();
            }
            else
            {
                forms[state.Form.MembershipTypeId] = state.UseCanonicalValues
                    ? DeactivateMembershipTypeFormViewModel.FromCatalog(
                        canonicalMembershipType,
                        state.Errors,
                        isOpen: true)
                    : DeactivateMembershipTypeFormViewModel.FromSubmission(state.Form, state.Errors);
            }
        }

        DeactivateForms = forms;
    }

    private static bool RequiresCanonicalEditRefresh(IReadOnlyList<CommandError> errors)
    {
        return errors.Any(error => error.Code is
            CommandErrorCode.StaleState
            or CommandErrorCode.ConcurrencyConflict
            or CommandErrorCode.NotFound
            or CommandErrorCode.DuplicateSubmission);
    }

    private static bool RequiresCanonicalDeactivateRefresh(IReadOnlyList<CommandError> errors)
    {
        return errors.Any(error => error.Code is
            CommandErrorCode.StaleState
            or CommandErrorCode.ConcurrencyConflict
            or CommandErrorCode.NotFound
            or CommandErrorCode.DuplicateSubmission
            or CommandErrorCode.AlreadyInactive);
    }

    private static IReadOnlyList<CommandError> ValidateRepresentableInput(
        int? durationDays,
        int? visitsLimit,
        decimal? priceAmount,
        string? priceCurrency,
        out Money price)
    {
        var errors = new List<CommandError>();

        if (durationDays is null)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Duration days must be a whole number.",
                "durationDays"));
        }

        if (visitsLimit is null)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Visit limit must be a whole number.",
                "visitsLimit"));
        }

        if (priceAmount is null)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Price amount must be a valid number.",
                "price.amount"));
        }
        else if (priceAmount < 0)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Price amount cannot be negative.",
                "price.amount"));
        }

        if (string.IsNullOrWhiteSpace(priceCurrency))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Price currency is required.",
                "price.currency"));
        }

        if (errors.Count > 0)
        {
            price = default;
            return errors;
        }

        price = new Money(priceAmount!.Value, priceCurrency!);
        return [];
    }

    public static string FormatPrice(Money price)
    {
        return $"{price.Amount.ToString("0.00", CultureInfo.CurrentCulture)} {price.Currency}";
    }

    public static string FormatTimestamp(DateTimeOffset timestamp) =>
        BodyLife.Crm.Web.Localization.ReceptionDisplayFormatter.DateTime(timestamp);

    public static string? FormatPriceInput(decimal? amount)
    {
        return amount?.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public string DisplayError(CommandError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Code == CommandErrorCode.ValidationFailed)
        {
            return error.Field switch
            {
                "durationDays" => T("MembershipTypes.Error.Duration"),
                "visitsLimit" => T("MembershipTypes.Error.VisitLimit"),
                "price.amount" => T("MembershipTypes.Error.Price"),
                "price.currency" => T("MembershipTypes.Error.Currency"),
                _ => T("MembershipTypes.Error.Validation"),
            };
        }

        return error.Code switch
        {
            CommandErrorCode.DuplicateSubmission => T("MembershipTypes.Error.DuplicateSubmission"),
            CommandErrorCode.StaleState => T("MembershipTypes.Error.Stale"),
            CommandErrorCode.ConcurrencyConflict => T("MembershipTypes.Error.Concurrency"),
            CommandErrorCode.NotFound => T("MembershipTypes.Error.NotFound"),
            CommandErrorCode.AlreadyInactive => T("MembershipTypes.Error.AlreadyInactive"),
            _ => T("MembershipTypes.Error.Generic"),
        };
    }

    private sealed record EditFormRenderState(
        EditMembershipTypeFormInput Form,
        IReadOnlyList<CommandError> Errors,
        bool UseCanonicalValues);

    private sealed record DeactivateFormRenderState(
        DeactivateMembershipTypeFormInput Form,
        IReadOnlyList<CommandError> Errors,
        bool UseCanonicalValues);
}
