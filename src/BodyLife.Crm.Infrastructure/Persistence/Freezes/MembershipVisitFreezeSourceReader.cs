using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

public sealed class MembershipVisitFreezeSourceReader(BodyLifeDbContext dbContext)
    : IMembershipVisitFreezeSourceProvider
{
    public async Task<IReadOnlyList<MembershipVisitFreezeSource>> GetForVisitAsync(
        Guid membershipId,
        DateOnly visitDate,
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
                "Membership Visit Freeze source reading requires a caller-owned "
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
                    and start_date <= {visitDate}
                    and end_date >= {visitDate}
                order by start_date, end_date, id
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        return records
            .Select(record => new MembershipVisitFreezeSource(
                record.MembershipId,
                record.Id,
                new DateRange(record.StartDate, record.EndDate),
                record.Status switch
                {
                    "active" => true,
                    "canceled" => false,
                    _ => throw new InvalidOperationException(
                        $"Freeze status '{record.Status}' is not supported."),
                }))
            .ToArray();
    }
}
