using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Reports;

public sealed class DailyModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<GenerateDailyReportQuery, GenerateDailyReportResult>
        generateDailyReport,
    TimeProvider timeProvider)
    : PageModel
{
    [BindProperty(SupportsGet = true, Name = "date")]
    public DateOnly? BusinessDate { get; set; }

    public GenerateDailyReportResult? Result { get; private set; }

    public DailyReportSnapshot? Report =>
        Result is { Status: GenerateDailyReportStatus.Success }
            ? Result.Report
            : null;

    public string? LoadError { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            LoadError = "Enter a valid business date.";
            return;
        }

        BusinessDate ??= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        Result = await generateDailyReport.ExecuteAsync(
            new GenerateDailyReportQuery(
                requestContextResolver.Require().Actor,
                BusinessDate.Value,
                IncludeDrillDown: true),
            cancellationToken);
    }
}
