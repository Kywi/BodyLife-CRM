using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class GetActiveNonWorkingDaysForCorrectionQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        GetActiveNonWorkingDaysForCorrectionQuery,
        GetActiveNonWorkingDaysForCorrectionResult>
{
    public async Task<GetActiveNonWorkingDaysForCorrectionResult> ExecuteAsync(
        GetActiveNonWorkingDaysForCorrectionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await NonWorkingDayQuerySupport.IsOwnerAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetActiveNonWorkingDaysForCorrectionResult.Denied();
        }

        var rows = await dbContext.Set<NonWorkingPeriodRecord>()
            .AsNoTracking()
            .Where(period => period.Status == "active")
            .OrderByDescending(period => period.StartDate)
            .ThenByDescending(period => period.CreatedAt)
            .ThenBy(period => period.Id)
            .Select(period => new
            {
                period.Id,
                period.StartDate,
                period.EndDate,
                period.ReasonCode,
                period.ReasonComment,
                period.CreatedAt,
                ApplicationCount = dbContext
                    .Set<NonWorkingPeriodApplicationRecord>()
                    .Count(application =>
                        application.NonWorkingPeriodId == period.Id),
                CanonicalApplicationCount = dbContext
                    .Set<NonWorkingPeriodApplicationRecord>()
                    .Count(application =>
                        application.NonWorkingPeriodId == period.Id
                        && application.Status == "active"
                        && application.AppliedStartDate == period.StartDate
                        && application.AppliedEndDate == period.EndDate),
            })
            .ToArrayAsync(cancellationToken);

        if (rows.Any(row =>
            row.ApplicationCount != row.CanonicalApplicationCount))
        {
            return GetActiveNonWorkingDaysForCorrectionResult.InconsistentSource();
        }

        try
        {
            return GetActiveNonWorkingDaysForCorrectionResult.Succeeded(
                rows.Select(row => new ActiveNonWorkingDayForCorrection(
                    row.Id,
                    new DateRange(row.StartDate, row.EndDate),
                    row.ReasonCode,
                    row.ReasonComment,
                    row.CreatedAt,
                    row.ApplicationCount)));
        }
        catch (ArgumentException)
        {
            return GetActiveNonWorkingDaysForCorrectionResult.InconsistentSource();
        }
    }
}
