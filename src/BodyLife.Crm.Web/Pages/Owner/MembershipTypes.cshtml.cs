using System.Globalization;
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
        GetMembershipTypesForIssueResult> getMembershipTypes)
    : PageModel
{
    public IReadOnlyList<MembershipTypeCatalogItem> MembershipTypes { get; private set; } = [];

    public QueryPermissionSet AllowedActions { get; private set; } = QueryPermissionSet.Empty;

    public int ActiveCount => MembershipTypes.Count(membershipType => membershipType.IsActive);

    public int InactiveCount => MembershipTypes.Count - ActiveCount;

    public bool CanManageCatalog =>
        AllowedActions.IsAllowed(MembershipTypeCatalogActionKeys.Create)
        && AllowedActions.IsAllowed(MembershipTypeCatalogActionKeys.Edit)
        && AllowedActions.IsAllowed(MembershipTypeCatalogActionKeys.Deactivate);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
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

    public static string FormatPrice(Money price)
    {
        return $"{price.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {price.Currency}";
    }

    public static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return $"{timestamp.ToUniversalTime():yyyy-MM-dd HH:mm} UTC";
    }
}
