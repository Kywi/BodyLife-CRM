using System.Data;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class CorrectNonWorkingDaySourcePreparer(BodyLifeDbContext dbContext)
{
    public async Task<CorrectNonWorkingDaySourcePreparationResult> PrepareAsync(
        Guid periodId,
        NonWorkingDayCorrectionMode mode,
        CancellationToken cancellationToken = default)
    {
        if (periodId == Guid.Empty)
        {
            throw new ArgumentException(
                "NonWorkingDay period id is required.",
                nameof(periodId));
        }

        _ = NonWorkingDayCorrectionPolicy.GetScopeBehavior(mode);

        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "CorrectNonWorkingDay source preparation requires a caller-owned "
                + "consistent database transaction.");
        var isolationLevel = transaction.GetDbTransaction().IsolationLevel;
        if (isolationLevel is not IsolationLevel.RepeatableRead
            and not IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "CorrectNonWorkingDay source preparation requires RepeatableRead "
                + "or Serializable transaction isolation.");
        }

        var membershipRows = await LockApplicationMembershipsAsync(
            periodId,
            cancellationToken);
        var periodRows = await dbContext.Set<NonWorkingPeriodRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    id,
                    start_date,
                    end_date,
                    reason_code,
                    reason_comment,
                    created_at,
                    created_by_account_id,
                    session_id,
                    status
                from bodylife.non_working_periods
                where id = {periodId}
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var period = periodRows.SingleOrDefault();
        if (period is null)
        {
            return CorrectNonWorkingDaySourcePreparationResult.NotFound(
                periodId,
                mode);
        }

        var applicationRows = await dbContext
            .Set<NonWorkingPeriodApplicationRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    id,
                    non_working_period_id,
                    membership_id,
                    client_id,
                    applied_start_date,
                    applied_end_date,
                    previewed_at,
                    confirmed_at,
                    status
                from bodylife.non_working_period_applications
                where non_working_period_id = {periodId}
                order by membership_id, id
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var cancellationRows = await dbContext
            .Set<NonWorkingPeriodCancellationRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    id,
                    non_working_period_id,
                    reason,
                    recorded_at,
                    recorded_by_account_id,
                    session_id
                from bodylife.non_working_period_cancellations
                where non_working_period_id = {periodId}
                order by id
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        if (cancellationRows.Length > 1
            || membershipRows.Length != applicationRows.Length
            || !MembershipRowsMatchApplications(membershipRows, applicationRows)
            || !TryMapStatus(period.Status, out var sourceStatus))
        {
            return CorrectNonWorkingDaySourcePreparationResult.InconsistentSource(
                periodId,
                mode);
        }

        var applicationSources = new List<NonWorkingDayCorrectionApplicationSource>(
            applicationRows.Length);
        foreach (var application in applicationRows)
        {
            if (!TryMapStatus(application.Status, out var applicationStatus))
            {
                return CorrectNonWorkingDaySourcePreparationResult.InconsistentSource(
                    periodId,
                    mode);
            }

            try
            {
                applicationSources.Add(new NonWorkingDayCorrectionApplicationSource(
                    application.Id,
                    application.MembershipId,
                    application.ClientId,
                    new DateRange(
                        application.AppliedStartDate,
                        application.AppliedEndDate),
                    application.PreviewedAt,
                    application.ConfirmedAt,
                    applicationStatus));
            }
            catch (ArgumentException)
            {
                return CorrectNonWorkingDaySourcePreparationResult.InconsistentSource(
                    periodId,
                    mode);
            }
        }

        NonWorkingDayCorrectionSource source;
        try
        {
            source = new NonWorkingDayCorrectionSource(
                period.Id,
                new DateRange(period.StartDate, period.EndDate),
                period.ReasonCode,
                period.ReasonComment,
                period.CreatedAt,
                period.CreatedByAccountId,
                period.SessionId,
                sourceStatus,
                applicationSources,
                cancellationRows.SingleOrDefault()?.Id);
        }
        catch (ArgumentException)
        {
            return CorrectNonWorkingDaySourcePreparationResult.InconsistentSource(
                periodId,
                mode);
        }

        return source.Status switch
        {
            NonWorkingDayCorrectionSourceStatus.Active =>
                CorrectNonWorkingDaySourcePreparationResult.Prepared(mode, source),
            NonWorkingDayCorrectionSourceStatus.Canceled =>
                CorrectNonWorkingDaySourcePreparationResult.AlreadyCanceled(mode, source),
            NonWorkingDayCorrectionSourceStatus.Corrected =>
                CorrectNonWorkingDaySourcePreparationResult.AlreadyCorrected(mode, source),
            _ => CorrectNonWorkingDaySourcePreparationResult.InconsistentSource(
                periodId,
                mode),
        };
    }

    private async Task<IssuedMembershipRecord[]> LockApplicationMembershipsAsync(
        Guid periodId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<IssuedMembershipRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    issued.id,
                    issued.client_id,
                    issued.membership_type_id,
                    issued.type_name_snapshot,
                    issued.duration_days_snapshot,
                    issued.visits_limit_snapshot,
                    issued.price_amount_snapshot,
                    issued.price_currency_snapshot,
                    issued.start_date,
                    issued.base_end_date,
                    issued.issued_at,
                    issued.issued_by_account_id,
                    issued.status,
                    issued.entry_origin,
                    issued.entry_batch_id,
                    issued.comment
                from bodylife.issued_memberships as issued
                inner join bodylife.non_working_period_applications as source_application
                    on source_application.membership_id = issued.id
                    and source_application.client_id = issued.client_id
                where source_application.non_working_period_id = {periodId}
                order by issued.id, source_application.id
                for update of issued
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
    }

    private static bool MembershipRowsMatchApplications(
        IReadOnlyList<IssuedMembershipRecord> memberships,
        IReadOnlyList<NonWorkingPeriodApplicationRecord> applications)
    {
        for (var index = 0; index < memberships.Count; index++)
        {
            if (memberships[index].Id != applications[index].MembershipId
                || memberships[index].ClientId != applications[index].ClientId)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryMapStatus(
        string status,
        out NonWorkingDayCorrectionSourceStatus sourceStatus)
    {
        switch (status)
        {
            case "active":
                sourceStatus = NonWorkingDayCorrectionSourceStatus.Active;
                return true;
            case "canceled":
                sourceStatus = NonWorkingDayCorrectionSourceStatus.Canceled;
                return true;
            case "corrected":
                sourceStatus = NonWorkingDayCorrectionSourceStatus.Corrected;
                return true;
            default:
                sourceStatus = default;
                return false;
        }
    }
}
