using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Reports;

public sealed class LowRemainingModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<
        ListLowRemainingMembershipsQuery,
        ListLowRemainingMembershipsResult> listLowRemainingMemberships,
    TimeProvider timeProvider)
    : PageModel
{
    private const int PageSize = 10;

    [BindProperty(SupportsGet = true, Name = "asOf")]
    public DateOnly? AsOfDate { get; set; }

    [BindProperty(SupportsGet = true, Name = "remaining")]
    public int? RemainingVisitsThreshold { get; set; }

    [BindProperty(SupportsGet = true, Name = "offset")]
    public int? Offset { get; set; }

    public ListLowRemainingMembershipsResult? Result { get; private set; }

    public LowRemainingMembershipsPage? ReportPage =>
        Result is { Status: ListLowRemainingMembershipsStatus.Success }
            ? Result.Page
            : null;

    public int? PreviousOffset => ReportPage is { Offset: > 0 } page
        ? Math.Max(0, page.Offset - PageSize)
        : null;

    public int CurrentPageNumber =>
        ((ReportPage?.Offset ?? 0) / PageSize) + 1;

    public string? LoadError { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            LoadError = "Enter a valid as-of date, threshold and page offset.";
            return;
        }

        AsOfDate ??= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        RemainingVisitsThreshold ??=
            GetLowRemainingMembershipStateRowsQuery.DefaultRemainingVisitsThreshold;
        Offset ??= 0;
        Result = await listLowRemainingMemberships.ExecuteAsync(
            new ListLowRemainingMembershipsQuery(
                requestContextResolver.Require().Actor,
                AsOfDate.Value,
                RemainingVisitsThreshold.Value,
                Limit: PageSize,
                Offset: Offset.Value),
            cancellationToken);

        if (Result is { Status: ListLowRemainingMembershipsStatus.Success, Page: null })
        {
            LoadError = "Low-remaining report returned no canonical Memberships page.";
        }
    }
}
