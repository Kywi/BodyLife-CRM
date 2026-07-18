using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal static class NonWorkingDayCanonicalPeriodReader
{
    internal static async Task<NonWorkingDayCanonicalPeriodReadResult> ReadAsync(
        BodyLifeDbContext dbContext,
        Guid periodId,
        Guid auditEntryId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var period = await dbContext.Set<NonWorkingPeriodRecord>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == periodId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.StartDate,
                candidate.EndDate,
                candidate.ReasonCode,
                candidate.ReasonComment,
                candidate.CreatedAt,
                candidate.CreatedByAccountId,
                candidate.SessionId,
                candidate.Status,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (period is null)
        {
            return NonWorkingDayCanonicalPeriodReadResult.Missing();
        }

        if (!TryMapStatus(period.Status, out var periodStatus))
        {
            return NonWorkingDayCanonicalPeriodReadResult.Inconsistent();
        }

        var expectedApplicationCount = await dbContext
            .Set<NonWorkingPeriodApplicationRecord>()
            .AsNoTracking()
            .CountAsync(
                application => application.NonWorkingPeriodId == periodId,
                cancellationToken);
        var applicationRows = await (
            from application in dbContext.Set<NonWorkingPeriodApplicationRecord>()
                .AsNoTracking()
            join state in dbContext.Set<MembershipStateCacheRecord>().AsNoTracking()
                on application.MembershipId equals state.MembershipId
            where application.NonWorkingPeriodId == periodId
            orderby application.MembershipId, application.Id
            select new
            {
                application.Id,
                application.MembershipId,
                application.ClientId,
                application.AppliedStartDate,
                application.AppliedEndDate,
                application.PreviewedAt,
                application.ConfirmedAt,
                application.Status,
                state.EffectiveEndDate,
                state.ExtensionDays,
                state.RecalculatedAt,
            })
            .ToArrayAsync(cancellationToken);
        if (applicationRows.Length != expectedApplicationCount)
        {
            return NonWorkingDayCanonicalPeriodReadResult.Inconsistent();
        }

        IReadOnlyDictionary<Guid, string> clientDisplayNames;
        try
        {
            clientDisplayNames = await NonWorkingDayClientProjection
                .LoadDisplayNamesAsync(
                    dbContext,
                    applicationRows.Select(row => row.ClientId).Distinct().ToArray(),
                    cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return NonWorkingDayCanonicalPeriodReadResult.Inconsistent();
        }

        var applications = new List<NonWorkingDayCanonicalApplication>(
            applicationRows.Length);
        foreach (var row in applicationRows)
        {
            if (!TryMapStatus(row.Status, out var applicationStatus)
                || applicationStatus != periodStatus
                || !clientDisplayNames.TryGetValue(
                    row.ClientId,
                    out var clientDisplayName))
            {
                return NonWorkingDayCanonicalPeriodReadResult.Inconsistent();
            }

            applications.Add(new NonWorkingDayCanonicalApplication(
                row.Id,
                row.MembershipId,
                row.ClientId,
                clientDisplayName,
                new DateRange(row.AppliedStartDate, row.AppliedEndDate),
                row.PreviewedAt,
                row.ConfirmedAt,
                applicationStatus,
                row.EffectiveEndDate,
                row.ExtensionDays,
                row.RecalculatedAt));
        }

        return NonWorkingDayCanonicalPeriodReadResult.Succeeded(
            new NonWorkingDayCanonicalPeriod(
                period.Id,
                new DateRange(period.StartDate, period.EndDate),
                period.ReasonCode,
                period.ReasonComment,
                period.CreatedAt,
                period.CreatedByAccountId,
                period.SessionId,
                periodStatus,
                auditEntryId,
                applications));
    }

    private static bool TryMapStatus(
        string status,
        out NonWorkingDayCorrectionSourceStatus mappedStatus)
    {
        mappedStatus = status switch
        {
            "active" => NonWorkingDayCorrectionSourceStatus.Active,
            "corrected" => NonWorkingDayCorrectionSourceStatus.Corrected,
            "canceled" => NonWorkingDayCorrectionSourceStatus.Canceled,
            _ => default,
        };
        return status is "active" or "corrected" or "canceled";
    }
}

internal sealed record NonWorkingDayCanonicalPeriodReadResult(
    NonWorkingDayCanonicalPeriodReadStatus Status,
    NonWorkingDayCanonicalPeriod? Period)
{
    internal static NonWorkingDayCanonicalPeriodReadResult Succeeded(
        NonWorkingDayCanonicalPeriod period) =>
        new(NonWorkingDayCanonicalPeriodReadStatus.Success, period);

    internal static NonWorkingDayCanonicalPeriodReadResult Missing() =>
        new(NonWorkingDayCanonicalPeriodReadStatus.NotFound, Period: null);

    internal static NonWorkingDayCanonicalPeriodReadResult Inconsistent() =>
        new(NonWorkingDayCanonicalPeriodReadStatus.SourceInconsistent, Period: null);
}

internal enum NonWorkingDayCanonicalPeriodReadStatus
{
    Success = 1,
    NotFound,
    SourceInconsistent,
}
