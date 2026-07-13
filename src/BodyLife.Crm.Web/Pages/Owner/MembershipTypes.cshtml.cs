using System.Globalization;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Owner;

public sealed class MembershipTypesModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<
        GetMembershipTypesForIssueQuery,
        GetMembershipTypesForIssueResult> getMembershipTypes,
    IBodyLifeCommandHandler<CreateMembershipTypeCommand> createMembershipType)
    : PageModel
{
    [TempData]
    public string? OperationMessage { get; set; }

    public IReadOnlyList<MembershipTypeCatalogItem> MembershipTypes { get; private set; } = [];

    public QueryPermissionSet AllowedActions { get; private set; } = QueryPermissionSet.Empty;

    public CreateMembershipTypeFormViewModel CreateForm { get; private set; }
        = CreateMembershipTypeFormViewModel.New();

    public int ActiveCount => MembershipTypes.Count(membershipType => membershipType.IsActive);

    public int InactiveCount => MembershipTypes.Count - ActiveCount;

    public bool CanCreateCatalog =>
        AllowedActions.IsAllowed(MembershipTypeCatalogActionKeys.Create);

    public bool CanManageCatalog =>
        CanCreateCatalog
        && AllowedActions.IsAllowed(MembershipTypeCatalogActionKeys.Edit)
        && AllowedActions.IsAllowed(MembershipTypeCatalogActionKeys.Deactivate);

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

        var inputErrors = ValidateRepresentableInput(form, out var price);
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
                ? $"Membership type created. Audit reference {auditEntryId.Value.ToString("N")[..8]}."
                : "Membership type created.";

            return RedirectToPage();
        }

        if (result.Errors.Any(error => error.Code == CommandErrorCode.PermissionDenied))
        {
            return Forbid();
        }

        return await RenderCreateErrorsAsync(form, result.Errors, cancellationToken);
    }

    private async Task<IActionResult> LoadCatalogAsync(CancellationToken cancellationToken)
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

    private static IReadOnlyList<CommandError> ValidateRepresentableInput(
        CreateMembershipTypeFormInput form,
        out Money price)
    {
        var errors = new List<CommandError>();

        if (form.DurationDays is null)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Duration days must be a whole number.",
                "durationDays"));
        }

        if (form.VisitsLimit is null)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Visit limit must be a whole number.",
                "visitsLimit"));
        }

        if (form.PriceAmount is null)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Price amount must be a valid number.",
                "price.amount"));
        }
        else if (form.PriceAmount < 0)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Price amount cannot be negative.",
                "price.amount"));
        }

        if (string.IsNullOrWhiteSpace(form.PriceCurrency))
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

        price = new Money(form.PriceAmount!.Value, form.PriceCurrency!);
        return [];
    }

    public static string FormatPrice(Money price)
    {
        return $"{price.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {price.Currency}";
    }

    public static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return $"{timestamp.ToUniversalTime():yyyy-MM-dd HH:mm} UTC";
    }

    public static string? FormatPriceInput(decimal? amount)
    {
        return amount?.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
