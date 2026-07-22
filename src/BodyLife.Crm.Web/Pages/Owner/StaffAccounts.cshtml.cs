using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace BodyLife.Crm.Web.Pages.Owner;

public sealed class StaffAccountsModel(
    StaffAccountQueryService accountQueryService,
    StaffAccountLifecycleService accountLifecycleService,
    StaffCredentialsService credentialsService,
    IBodyLifeRequestContextResolver requestContextResolver,
    IStringLocalizer<BodyLife.Crm.Web.Localization.Owner> localizer) : PageModel
{
    [TempData]
    public string? OperationMessage { get; set; }

    [TempData]
    public string? OperationTone { get; set; }

    public IReadOnlyList<StaffAccountSummary> StaffAccounts { get; private set; } = [];

    public bool OperationSucceeded => string.Equals(OperationTone, "success", StringComparison.Ordinal);

    public string T(string key, params object[] arguments) => localizer[key, arguments];

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

        return RedirectWithLifecycleResult(result);
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

        return RedirectWithLifecycleResult(result);
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

        return RedirectWithCredentialsResult(result);
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

        return RedirectWithLifecycleResult(result);
    }

    public string AccountKindLabel(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.NamedAdmin => T("StaffAccounts.AccountKind.NamedAdmin"),
            AccountKind.SharedReceptionAdmin => T("StaffAccounts.AccountKind.SharedReceptionAdmin"),
            _ => T("StaffAccounts.AccountKind.Staff"),
        };
    }

    private RedirectToPageResult RedirectWithLifecycleResult(StaffAccountLifecycleResult result)
    {
        OperationTone = result.Succeeded ? "success" : "error";
        OperationMessage = WithAuditReference(LifecycleMessage(result.Status), result.AuditEntryId);

        return RedirectToPage();
    }

    private RedirectToPageResult RedirectWithCredentialsResult(StaffCredentialsResult result)
    {
        OperationTone = result.Succeeded ? "success" : "error";
        OperationMessage = WithAuditReference(CredentialsMessage(result.Status), result.AuditEntryId);

        return RedirectToPage();
    }

    private string WithAuditReference(string message, AuditEntryId? auditEntryId) =>
        auditEntryId is { } value
            ? T("StaffAccounts.AuditReference", message, value.Value.ToString("N")[..8])
            : message;

    private string LifecycleMessage(StaffAccountLifecycleStatus status) => status switch
    {
        StaffAccountLifecycleStatus.Created => T("StaffAccounts.Result.Created"),
        StaffAccountLifecycleStatus.DisplayNameUpdated => T("StaffAccounts.Result.DisplayNameUpdated"),
        StaffAccountLifecycleStatus.Activated => T("StaffAccounts.Result.Activated"),
        StaffAccountLifecycleStatus.Deactivated => T("StaffAccounts.Result.Deactivated"),
        StaffAccountLifecycleStatus.AlreadyActive => T("StaffAccounts.Result.AlreadyActive"),
        StaffAccountLifecycleStatus.AlreadyInactive => T("StaffAccounts.Result.AlreadyInactive"),
        StaffAccountLifecycleStatus.PermissionDenied => T("StaffAccounts.Error.PermissionDenied"),
        StaffAccountLifecycleStatus.ValidationFailed => T("StaffAccounts.Error.Validation"),
        StaffAccountLifecycleStatus.NotFound => T("StaffAccounts.Error.NotFound"),
        StaffAccountLifecycleStatus.OwnerAccountProtected => T("StaffAccounts.Error.OwnerProtected"),
        _ => T("StaffAccounts.Error.Generic"),
    };

    private string CredentialsMessage(StaffCredentialsStatus status) => status switch
    {
        StaffCredentialsStatus.Configured => T("StaffAccounts.Result.CredentialsConfigured"),
        StaffCredentialsStatus.Reset => T("StaffAccounts.Result.CredentialsReset"),
        StaffCredentialsStatus.PermissionDenied => T("StaffAccounts.Error.PermissionDenied"),
        StaffCredentialsStatus.ValidationFailed => T("StaffAccounts.Error.Validation"),
        StaffCredentialsStatus.NotFound => T("StaffAccounts.Error.NotFound"),
        StaffCredentialsStatus.OwnerAccountProtected => T("StaffAccounts.Error.OwnerProtected"),
        StaffCredentialsStatus.LoginNameAlreadyInUse => T("StaffAccounts.Error.LoginInUse"),
        _ => T("StaffAccounts.Error.Generic"),
    };
}
