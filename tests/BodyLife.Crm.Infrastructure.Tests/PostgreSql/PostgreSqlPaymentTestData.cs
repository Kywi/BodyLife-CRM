using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

internal static class PostgreSqlPaymentTestData
{
    internal static async Task<Guid> InsertMembershipSalePaymentAsync(
        PostgreSqlTestDatabase database,
        Guid sourceMembershipId,
        Guid accountId,
        Guid sessionId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt)
    {
        var membershipId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.issued_memberships (
                id, client_id, membership_type_id, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot,
                price_amount_snapshot, price_currency_snapshot, issuance_mode,
                start_date, base_end_date, issued_at, issued_by_account_id,
                status, entry_origin, entry_batch_id, comment)
            select
                @membership_id, client_id, membership_type_id, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot,
                price_amount_snapshot, price_currency_snapshot, 'sale',
                start_date, base_end_date, @occurred_at, @account_id,
                'active', 'normal', null, null
            from bodylife.issued_memberships
            where id = @source_membership_id;

            insert into bodylife.payments (
                id, client_id, membership_id, amount, currency, method,
                payment_context, occurred_at, recorded_at,
                recorded_by_account_id, session_id, entry_origin,
                entry_batch_id, comment, status)
            select
                @payment_id, client_id, id, price_amount_snapshot,
                price_currency_snapshot, 'cash', 'membership_sale',
                @occurred_at, @recorded_at, @account_id, @session_id,
                'normal', null, null, 'active'
            from bodylife.issued_memberships
            where id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("payment_id", paymentId);
        command.Parameters.AddWithValue("source_membership_id", sourceMembershipId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        if (await command.ExecuteNonQueryAsync() != 2)
        {
            throw new InvalidOperationException(
                "The canonical Membership sale fixture was not created.");
        }

        return paymentId;
    }
}
