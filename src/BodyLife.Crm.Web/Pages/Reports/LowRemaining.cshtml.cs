using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Localization;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace BodyLife.Crm.Web.Pages.Reports;

public sealed class LowRemainingModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<
        ListLowRemainingMembershipsQuery,
        ListLowRemainingMembershipsResult> listLowRemainingMemberships,
    TimeProvider timeProvider,
    IStringLocalizer<BodyLife.Crm.Web.Localization.Reports> localizer)
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
            LoadError = localizer["Error.InvalidInput"];
            return;
        }

        AsOfDate ??= BusinessTimeZone.GetBusinessDate(timeProvider.GetUtcNow());
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
        if (Result is { Status: not ListLowRemainingMembershipsStatus.Success })
            LoadError = ReportsPresentation.Error(localizer, Result.Status);

        if (Result is { Status: ListLowRemainingMembershipsStatus.Success, Page: null })
        {
            LoadError = localizer["Error.Unavailable"];
        }
    }
}
