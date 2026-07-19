using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Modules.Visits;

namespace BodyLife.Crm.Infrastructure.Persistence.Reports;

public sealed class GenerateDailyReportQueryHandler(
    IBodyLifeQueryHandler<GetDailyVisitSourceRowsQuery, GetDailyVisitSourceRowsResult>
        getDailyVisitSourceRows,
    IBodyLifeQueryHandler<GetDailyPaymentSourceRowsQuery, GetDailyPaymentSourceRowsResult>
        getDailyPaymentSourceRows)
    : IBodyLifeQueryHandler<GenerateDailyReportQuery, GenerateDailyReportResult>
{
    public async Task<GenerateDailyReportResult> ExecuteAsync(
        GenerateDailyReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var visitResult = await getDailyVisitSourceRows.ExecuteAsync(
            new GetDailyVisitSourceRowsQuery(query.Actor, query.BusinessDate),
            cancellationToken);
        if (visitResult.Status != GetDailyVisitSourceRowsStatus.Success)
        {
            return MapVisitFailure(visitResult);
        }

        if (visitResult.Snapshot is null)
        {
            return GenerateDailyReportResult.InconsistentSource();
        }

        var paymentResult = await getDailyPaymentSourceRows.ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(query.Actor, query.BusinessDate),
            cancellationToken);
        if (paymentResult.Status != GetDailyPaymentSourceRowsStatus.Success)
        {
            return MapPaymentFailure(paymentResult);
        }

        if (paymentResult.Snapshot is null
            || !DailyReportSnapshot.TryCreate(
                query.BusinessDate,
                query.IncludeDrillDown,
                visitResult.Snapshot,
                paymentResult.Snapshot,
                out var report)
            || report is null)
        {
            return GenerateDailyReportResult.InconsistentSource();
        }

        return GenerateDailyReportResult.Succeeded(report);
    }

    private static GenerateDailyReportResult MapVisitFailure(
        GetDailyVisitSourceRowsResult result)
    {
        return result.Status switch
        {
            GetDailyVisitSourceRowsStatus.PermissionDenied
                => GenerateDailyReportResult.Denied(),
            GetDailyVisitSourceRowsStatus.ValidationFailed
                => GenerateDailyReportResult.Invalid(
                    result.ErrorMessage ?? "Daily Visit source request is invalid.",
                    result.ErrorField),
            _ => GenerateDailyReportResult.InconsistentSource(),
        };
    }

    private static GenerateDailyReportResult MapPaymentFailure(
        GetDailyPaymentSourceRowsResult result)
    {
        return result.Status switch
        {
            GetDailyPaymentSourceRowsStatus.PermissionDenied
                => GenerateDailyReportResult.Denied(),
            GetDailyPaymentSourceRowsStatus.ValidationFailed
                => GenerateDailyReportResult.Invalid(
                    result.ErrorMessage ?? "Daily Payment source request is invalid.",
                    result.ErrorField),
            _ => GenerateDailyReportResult.InconsistentSource(),
        };
    }
}
