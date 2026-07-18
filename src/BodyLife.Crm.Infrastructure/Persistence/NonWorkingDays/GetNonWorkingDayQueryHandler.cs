using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class GetNonWorkingDayQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<GetNonWorkingDayQuery, GetNonWorkingDayResult>
{
    public async Task<GetNonWorkingDayResult> ExecuteAsync(
        GetNonWorkingDayQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await NonWorkingDayQuerySupport.IsOwnerAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetNonWorkingDayResult.Denied();
        }

        if (query.PeriodId == Guid.Empty)
        {
            return GetNonWorkingDayResult.Invalid(
                "Non-working period id is required.",
                "periodId");
        }

        var period = await dbContext.Set<NonWorkingPeriodRecord>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == query.PeriodId)
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
            return GetNonWorkingDayResult.Missing();
        }

        if (!TryMapStatus(period.Status, out var periodStatus))
        {
            return GetNonWorkingDayResult.InconsistentSource();
        }

        var auditEntryIds = await dbContext.Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .Where(audit =>
                audit.ActionType == NonWorkingDayAuditActions.Added
                && audit.EntityType == NonWorkingDayAuditActions.PeriodEntityType
                && audit.EntityId == query.PeriodId)
            .Select(audit => audit.Id)
            .ToArrayAsync(cancellationToken);
        if (auditEntryIds.Length != 1 || auditEntryIds[0] == Guid.Empty)
        {
            return GetNonWorkingDayResult.InconsistentSource();
        }

        var expectedApplicationCount = await dbContext
            .Set<NonWorkingPeriodApplicationRecord>()
            .AsNoTracking()
            .CountAsync(
                application => application.NonWorkingPeriodId == query.PeriodId,
                cancellationToken);
        var applicationRows = await (
            from application in dbContext.Set<NonWorkingPeriodApplicationRecord>()
                .AsNoTracking()
            join state in dbContext.Set<MembershipStateCacheRecord>().AsNoTracking()
                on application.MembershipId equals state.MembershipId
            where application.NonWorkingPeriodId == query.PeriodId
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
            return GetNonWorkingDayResult.InconsistentSource();
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
            return GetNonWorkingDayResult.InconsistentSource();
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
                return GetNonWorkingDayResult.InconsistentSource();
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

        return GetNonWorkingDayResult.Succeeded(
            new NonWorkingDayCanonicalPeriod(
                period.Id,
                new DateRange(period.StartDate, period.EndDate),
                period.ReasonCode,
                period.ReasonComment,
                period.CreatedAt,
                period.CreatedByAccountId,
                period.SessionId,
                periodStatus,
                auditEntryIds[0],
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
