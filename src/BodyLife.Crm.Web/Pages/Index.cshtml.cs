using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages;

public sealed class IndexModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<GenerateDailyReportQuery, GenerateDailyReportResult> dailyReport,
    IBodyLifeQueryHandler<GetReceptionAttentionSummaryQuery, GetReceptionAttentionSummaryResult> attention,
    IBodyLifeQueryHandler<GetReceptionActivityQuery, GetReceptionActivityResult> activity,
    TimeProvider timeProvider) : PageModel
{
    public DateOnly BusinessDate { get; private set; }
    public DateTimeOffset Now { get; private set; }
    public GenerateDailyReportResult? DailyResult { get; private set; }
    public GetReceptionAttentionSummaryResult? AttentionResult { get; private set; }
    public GetReceptionActivityResult? ActivityResult { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var actor = requestContextResolver.Require().Actor;
        Now = timeProvider.GetUtcNow();
        BusinessDate = BusinessTimeZone.GetBusinessDate(Now);
        ViewData["IsHome"] = true;
        DailyResult = await dailyReport.ExecuteAsync(
            new GenerateDailyReportQuery(actor, BusinessDate, IncludeDrillDown: false), cancellationToken);
        AttentionResult = await attention.ExecuteAsync(
            new GetReceptionAttentionSummaryQuery(actor, BusinessDate, EndingSoonDaysThreshold: 7), cancellationToken);
        ActivityResult = await activity.ExecuteAsync(
            new GetReceptionActivityQuery(actor, BusinessDate, Limit: 5), cancellationToken);
    }
}
