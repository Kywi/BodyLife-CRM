using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Web.Localization;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace BodyLife.Crm.Web.Pages.Reports;

public sealed class EndingSoonModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<
        ListEndingSoonMembershipsQuery,
        ListEndingSoonMembershipsResult> listEndingSoonMemberships,
    TimeProvider timeProvider,
    IStringLocalizer<BodyLife.Crm.Web.Localization.Reports> localizer)
    : PageModel
{
    private const int PageSize = 10;

    [BindProperty(SupportsGet = true, Name = "asOf")]
    public DateOnly? AsOfDate { get; set; }

    [BindProperty(SupportsGet = true, Name = "days")]
    public int? DaysThreshold { get; set; }

    [BindProperty(SupportsGet = true, Name = "offset")]
    public int? Offset { get; set; }

    public int MaximumDaysThreshold =>
        GetEndingSoonMembershipStateRowsQuery.MaxDaysThreshold;

    public ListEndingSoonMembershipsResult? Result { get; private set; }

    public EndingSoonMembershipsPage? ReportPage =>
        Result is { Status: ListEndingSoonMembershipsStatus.Success }
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

        AsOfDate ??= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        DaysThreshold ??= GetEndingSoonMembershipStateRowsQuery.DefaultDaysThreshold;
        Offset ??= 0;
        Result = await listEndingSoonMemberships.ExecuteAsync(
            new ListEndingSoonMembershipsQuery(
                requestContextResolver.Require().Actor,
                AsOfDate.Value,
                DaysThreshold.Value,
                Limit: PageSize,
                Offset: Offset.Value),
            cancellationToken);
        if (Result is { Status: not ListEndingSoonMembershipsStatus.Success })
            LoadError = ReportsPresentation.Error(localizer, Result.Status);

        if (Result is { Status: ListEndingSoonMembershipsStatus.Success, Page: null })
        {
            LoadError = localizer["Error.Unavailable"];
        }
    }
}
