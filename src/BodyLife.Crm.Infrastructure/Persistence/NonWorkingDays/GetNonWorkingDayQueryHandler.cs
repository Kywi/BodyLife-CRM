using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Modules.NonWorkingDays;
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

        var auditEntryIds = await dbContext.Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .Where(audit =>
                audit.ActionType == NonWorkingDayAuditActions.Added
                && audit.EntityType == NonWorkingDayAuditActions.PeriodEntityType
                && audit.EntityId == query.PeriodId)
            .Select(audit => audit.Id)
            .ToArrayAsync(cancellationToken);
        if (auditEntryIds.Length == 0)
        {
            var periodExists = await dbContext.Set<NonWorkingPeriodRecord>()
                .AsNoTracking()
                .AnyAsync(
                    candidate => candidate.Id == query.PeriodId,
                    cancellationToken);
            return periodExists
                ? GetNonWorkingDayResult.InconsistentSource()
                : GetNonWorkingDayResult.Missing();
        }

        if (auditEntryIds.Length != 1 || auditEntryIds[0] == Guid.Empty)
        {
            return GetNonWorkingDayResult.InconsistentSource();
        }

        var canonicalRead = await NonWorkingDayCanonicalPeriodReader.ReadAsync(
            dbContext,
            query.PeriodId,
            auditEntryIds[0],
            cancellationToken);
        return canonicalRead.Status switch
        {
            NonWorkingDayCanonicalPeriodReadStatus.Success =>
                GetNonWorkingDayResult.Succeeded(canonicalRead.Period!),
            NonWorkingDayCanonicalPeriodReadStatus.NotFound =>
                GetNonWorkingDayResult.Missing(),
            NonWorkingDayCanonicalPeriodReadStatus.SourceInconsistent =>
                GetNonWorkingDayResult.InconsistentSource(),
            _ => throw new InvalidOperationException(
                $"Unsupported canonical NonWorkingDay read status {canonicalRead.Status}."),
        };
    }
}
