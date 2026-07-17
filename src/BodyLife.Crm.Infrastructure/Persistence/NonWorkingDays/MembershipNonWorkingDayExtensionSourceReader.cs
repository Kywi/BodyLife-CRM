using System.Data;
using System.Globalization;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class MembershipNonWorkingDayExtensionSourceReader(
    BodyLifeDbContext dbContext)
    : IMembershipExtensionSourceProvider
{
    private const string SourceSql =
        """
        select
            applied.id,
            applied.applied_start_date,
            applied.applied_end_date,
            applied.status as application_status,
            period.reason_code,
            period.reason_comment,
            period.status as period_status
        from bodylife.non_working_period_applications as applied
        inner join bodylife.non_working_periods as period
            on period.id = applied.non_working_period_id
        where applied.membership_id = @membership_id
        order by
            applied.applied_start_date,
            applied.applied_end_date,
            applied.non_working_period_id,
            applied.id
        for update of period, applied
        """;

    public async Task<IReadOnlyList<MembershipExtensionSourceRange>>
        GetForMembershipAsync(
            Guid membershipId,
            CancellationToken cancellationToken = default)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Membership NonWorkingDay extension source reading requires a "
                + "caller-owned database transaction and a locked selected Membership.");
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = SourceSql;
        AddParameter(command, "membership_id", DbType.Guid, membershipId);

        var rows = new List<NonWorkingDayExtensionSourceRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new NonWorkingDayExtensionSourceRow(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6)));
        }

        return rows
            .Select(row => new MembershipExtensionSourceRange(
                sourceType: "non_working_period",
                row.ApplicationId,
                CreateSourceLabel(row),
                new DateRange(row.AppliedStartDate, row.AppliedEndDate),
                isActive: IsActive(row)))
            .ToArray();
    }

    private static bool IsActive(NonWorkingDayExtensionSourceRow row)
    {
        var applicationIsActive = ReadIsActiveStatus(
            "application",
            row.ApplicationStatus);
        var periodIsActive = ReadIsActiveStatus("period", row.PeriodStatus);
        return applicationIsActive && periodIsActive;
    }

    private static bool ReadIsActiveStatus(string sourceName, string status)
    {
        return status switch
        {
            "active" => true,
            "canceled" or "corrected" => false,
            _ => throw new InvalidOperationException(
                $"NonWorkingDay {sourceName} status '{status}' is not supported."),
        };
    }

    private static string CreateSourceLabel(NonWorkingDayExtensionSourceRow row)
    {
        var prefix = string.Create(
            CultureInfo.InvariantCulture,
            $"Non-working period {row.AppliedStartDate:yyyy-MM-dd}..{row.AppliedEndDate:yyyy-MM-dd}: ");
        var reason = row.ReasonComment is null
            ? row.ReasonCode
            : $"{row.ReasonCode} - {row.ReasonComment}";
        var reasonLength = Math.Min(
            reason.Length,
            MembershipExtensionSourceRange.MaxSourceLabelLength - prefix.Length);
        return prefix + reason[..reasonLength];
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        DbType dbType,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = dbType;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record NonWorkingDayExtensionSourceRow(
        Guid ApplicationId,
        DateOnly AppliedStartDate,
        DateOnly AppliedEndDate,
        string ApplicationStatus,
        string ReasonCode,
        string? ReasonComment,
        string PeriodStatus);
}
