using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Owner;

public sealed class StaffAccountsModel(
    StaffAccountQueryService accountQueryService,
    StaffAccountLifecycleService accountLifecycleService,
    StaffCredentialsService credentialsService,
    IBodyLifeRequestContextResolver requestContextResolver) : PageModel
{
    [TempData]
    public string? OperationMessage { get; set; }

    [TempData]
    public string? OperationTone { get; set; }

    public IReadOnlyList<StaffAccountSummary> StaffAccounts { get; private set; } = [];

    public bool OperationSucceeded => string.Equals(OperationTone, "success", StringComparison.Ordinal);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        StaffAccounts = await accountQueryService.ListStaffAccountsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(
        AccountKind accountKind,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var result = await accountLifecycleService.CreateStaffAccountAsync(
            requestContextResolver.CreateCommandEnvelope(),
            accountKind,
            displayName,
            cancellationToken);

        return RedirectWithResult(result.Succeeded, result.Message, result.AuditEntryId);
    }

    public async Task<IActionResult> OnPostUpdateDisplayNameAsync(
        Guid accountId,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var result = await accountLifecycleService.UpdateStaffAccountDisplayNameAsync(
            requestContextResolver.CreateCommandEnvelope(),
            accountId,
            displayName,
            cancellationToken);

        return RedirectWithResult(result.Succeeded, result.Message, result.AuditEntryId);
    }

    public async Task<IActionResult> OnPostSetCredentialsAsync(
        Guid accountId,
        string? loginName,
        string? password,
        string? reason,
        CancellationToken cancellationToken)
    {
        var result = await credentialsService.SetStaffCredentialsAsync(
            requestContextResolver.CreateCommandEnvelope(reason: reason),
            accountId,
            loginName,
            password,
            cancellationToken);

        return RedirectWithResult(result.Succeeded, result.Message, result.AuditEntryId);
    }

    public async Task<IActionResult> OnPostSetActiveStateAsync(
        Guid accountId,
        bool isActive,
        string? reason,
        CancellationToken cancellationToken)
    {
        var result = await accountLifecycleService.SetStaffAccountActiveStateAsync(
            requestContextResolver.CreateCommandEnvelope(reason: reason),
            accountId,
            isActive,
            cancellationToken);

        return RedirectWithResult(result.Succeeded, result.Message, result.AuditEntryId);
    }

    public static string AccountKindLabel(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.NamedAdmin => "Named Admin",
            AccountKind.SharedReceptionAdmin => "Shared Reception/Admin",
            _ => "Staff account",
        };
    }

    private RedirectToPageResult RedirectWithResult(
        bool succeeded,
        string message,
        AuditEntryId? auditEntryId)
    {
        OperationTone = succeeded ? "success" : "error";
        OperationMessage = auditEntryId is { } value
            ? $"{message} Audit reference {value.Value.ToString("N")[..8]}."
            : message;

        return RedirectToPage();
    }
}
