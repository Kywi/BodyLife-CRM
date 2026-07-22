using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Web.Localization;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace BodyLife.Crm.Web.Pages.Reports;

public sealed class NegativeClientsModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<ListNegativeClientsQuery, ListNegativeClientsResult>
        listNegativeClients,
    TimeProvider timeProvider,
    IStringLocalizer<BodyLife.Crm.Web.Localization.Reports> localizer)
    : PageModel
{
    private const int PageSize = 10;

    [BindProperty(SupportsGet = true, Name = "asOf")]
    public DateOnly? AsOfDate { get; set; }

    [BindProperty(SupportsGet = true, Name = "offset")]
    public int? Offset { get; set; }

    public ListNegativeClientsResult? Result { get; private set; }

    public NegativeClientsPage? ReportPage =>
        Result is { Status: ListNegativeClientsStatus.Success }
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
        Offset ??= 0;
        Result = await listNegativeClients.ExecuteAsync(
            new ListNegativeClientsQuery(
                requestContextResolver.Require().Actor,
                AsOfDate.Value,
                Limit: PageSize,
                Offset: Offset.Value),
            cancellationToken);
        if (Result is { Status: not ListNegativeClientsStatus.Success })
            LoadError = ReportsPresentation.Error(localizer, Result.Status);

        if (Result is { Status: ListNegativeClientsStatus.Success, Page: null })
        {
            LoadError = localizer["Error.Unavailable"];
        }
    }
}
