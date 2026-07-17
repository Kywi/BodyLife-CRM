using System.Globalization;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

public sealed class MembershipFreezeExtensionSourceReader(BodyLifeDbContext dbContext)
    : IMembershipExtensionSourceProvider
{
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

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Membership Freeze extension source reading requires a caller-owned "
                + "database transaction and a locked selected Membership.");
        }

        var records = await dbContext.Set<FreezeRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    id,
                    client_id,
                    membership_id,
                    start_date,
                    end_date,
                    reason,
                    occurred_at,
                    recorded_at,
                    recorded_by_account_id,
                    session_id,
                    entry_origin,
                    entry_batch_id,
                    status
                from bodylife.freezes
                where membership_id = {membershipId}
                order by start_date, end_date, id
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        return records
            .Select(record => new MembershipExtensionSourceRange(
                MembershipExtensionSourceRange.FreezeSourceType,
                record.Id,
                CreateSourceLabel(record),
                new DateRange(record.StartDate, record.EndDate),
                isActive: record.Status switch
                {
                    "active" => true,
                    "canceled" => false,
                    _ => throw new InvalidOperationException(
                        $"Freeze status '{record.Status}' is not supported."),
                }))
            .ToArray();
    }

    private static string CreateSourceLabel(FreezeRecord record)
    {
        var prefix = string.Create(
            CultureInfo.InvariantCulture,
            $"Freeze {record.StartDate:yyyy-MM-dd}..{record.EndDate:yyyy-MM-dd}: ");
        var reasonLength = Math.Min(
            record.Reason.Length,
            MembershipExtensionSourceRange.MaxSourceLabelLength - prefix.Length);
        return prefix + record.Reason[..reasonLength];
    }
}
