using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Reports;

public sealed class InactiveClientsModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<ListInactiveClientsQuery, ListInactiveClientsResult>
        listInactiveClients,
    TimeProvider timeProvider)
    : PageModel
{
    private const int PageSize = 10;

    public static IReadOnlyList<int> SupportedThresholds { get; } = [14, 30, 60];

    [BindProperty(SupportsGet = true, Name = "asOf")]
    public DateOnly? AsOfDate { get; set; }

    [BindProperty(SupportsGet = true, Name = "threshold")]
    public int? ThresholdDays { get; set; }

    [BindProperty(SupportsGet = true, Name = "includeNoVisits")]
    public bool IncludeClientsWithNoVisits { get; set; }

    [BindProperty(SupportsGet = true, Name = "offset")]
    public int? Offset { get; set; }

    public ListInactiveClientsResult? Result { get; private set; }

    public InactiveClientsPage? ReportPage =>
        Result is { Status: ListInactiveClientsStatus.Success }
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
            LoadError = "Enter a valid as-of date, inactivity threshold and page offset.";
            return;
        }

        AsOfDate ??= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        Offset ??= 0;

        if (ThresholdDays is null)
        {
            return;
        }

        Result = await listInactiveClients.ExecuteAsync(
            new ListInactiveClientsQuery(
                requestContextResolver.Require().Actor,
                AsOfDate.Value,
                ThresholdDays.Value,
                IncludeClientsWithNoVisits,
                Limit: PageSize,
                Offset: Offset.Value),
            cancellationToken);

        if (Result is { Status: ListInactiveClientsStatus.Success, Page: null })
        {
            LoadError = "Inactive-clients report returned no canonical report page.";
        }
    }
}
