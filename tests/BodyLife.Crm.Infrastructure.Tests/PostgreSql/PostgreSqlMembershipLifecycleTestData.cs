using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

internal static class PostgreSqlMembershipLifecycleTestData
{
    internal static async Task<Guid> CreateOpeningAsync(
        PostgreSqlTestDatabase database, ActorContext actor, Guid clientId, Guid typeId,
        int remaining, DateOnly startDate, DateTimeOffset now, Guid? predecessorId = null)
    {
        await using var context = database.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var id = Guid.NewGuid();
        var endDate = startDate.AddDays(29);
        var issuedAt = now.AddDays(-3);
        var reasonCode = "negative_balance_rollover";
        if (predecessorId is { } previousId)
        {
            var before = await new MembershipStateCacheRebuilder(context, TimeProvider.System).RebuildAsync(previousId);
            Assert.True(before.State!.RemainingVisits <= 0);
            reasonCode = before.State.RemainingVisits == 0 ? "zero_balance_rollover" : "negative_balance_rollover";
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"update bodylife.issued_memberships set status = 'closed' where id = {previousId}");
        }
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            insert into bodylife.issued_memberships (id, client_id, membership_type_id, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot, price_currency_snapshot,
                issuance_mode, start_date, base_end_date, issued_at, issued_by_account_id, status, entry_origin)
            values ({id}, {clientId}, {typeId}, 'Historical opening', 30, 2, 900, 'UAH',
                'opening_state', {startDate}, {endDate}, {issuedAt}, {actor.AccountId.Value}, 'active', 'manual_backfill');
            insert into bodylife.membership_opening_states (id, membership_id, opening_as_of_date,
                declared_remaining_visits, declared_negative_balance, known_effective_end_date, known_extension_days,
                source_reference, reason, recorded_at, recorded_by_account_id, recorded_session_id, entry_origin, status)
            values ({Guid.NewGuid()}, {id}, {startDate}, {remaining}, {Math.Max(0, -remaining)}, {endDate}, 0,
                'Opening test source', 'Known opening balance', {issuedAt}, {actor.AccountId.Value},
                {actor.SessionId.Value}, 'manual_backfill', 'active');
            """);
        if (predecessorId is { } closedId)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                insert into bodylife.membership_lifecycle_closures (id, client_id, source_membership_id,
                    successor_membership_id, reason_code, recorded_by_account_id, session_id, correlation_id,
                    idempotency_key, entry_origin, occurred_at, recorded_at)
                values ({Guid.NewGuid()}, {clientId}, {closedId}, {id}, {reasonCode}, {actor.AccountId.Value},
                    {actor.SessionId.Value}, 'lifecycle-fixture', {id.ToString()}, 'normal', {now}, {now});
                """);
        }
        await new MembershipStateCacheRebuilder(context, TimeProvider.System).RebuildAsync(id);
        await transaction.CommitAsync();
        return id;
    }
}
