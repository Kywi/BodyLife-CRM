using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlBusinessAuditAppendOnlyTests
{
    [PostgreSqlFact]
    public async Task BusinessAuditEntriesAllowInsertsButRejectUpdatesAndDeletes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            await dbContext.Database.MigrateAsync();
        }

        var auditEntryId = Guid.NewGuid();
        await InsertAuditEntryAsync(database.ConnectionString, auditEntryId);

        var updateException = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteNonQueryAsync(
                database.ConnectionString,
                """
                update bodylife.business_audit_entries
                set comment = 'rewritten'
                where id = @id
                """,
                auditEntryId));
        var deleteException = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteNonQueryAsync(
                database.ConnectionString,
                "delete from bodylife.business_audit_entries where id = @id",
                auditEntryId));

        AssertAppendOnlyRejection(updateException, "UPDATE");
        AssertAppendOnlyRejection(deleteException, "DELETE");
        Assert.Equal(
            "original",
            await database.ExecuteScalarAsync<string>(
                "select comment from bodylife.business_audit_entries"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
    }

    private static async Task InsertAuditEntryAsync(string connectionString, Guid auditEntryId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            insert into bodylife.business_audit_entries (
                id,
                action_type,
                entity_type,
                entity_id,
                related_entity_refs,
                actor_account_id,
                actor_account_type,
                actor_role,
                session_id,
                device_label,
                occurred_at,
                recorded_at,
                reason,
                comment,
                before_summary,
                after_summary,
                request_correlation_id,
                entry_origin,
                idempotency_key,
                changed_after_close)
            values (
                @id,
                'audit_test.created',
                'audit_test',
                @entity_id,
                '[]'::jsonb,
                @actor_account_id,
                'owner',
                'owner',
                @session_id,
                'integration-test',
                now(),
                now(),
                null,
                'original',
                '{}'::jsonb,
                '{}'::jsonb,
                @request_correlation_id,
                'normal',
                null,
                false)
            """,
            connection);
        command.Parameters.AddWithValue("id", auditEntryId);
        command.Parameters.AddWithValue("entity_id", Guid.NewGuid());
        command.Parameters.AddWithValue("actor_account_id", Guid.NewGuid());
        command.Parameters.AddWithValue("session_id", Guid.NewGuid());
        command.Parameters.AddWithValue("request_correlation_id", $"audit-append-only-{Guid.NewGuid():N}");

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ExecuteNonQueryAsync(
        string connectionString,
        string commandText,
        Guid auditEntryId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddWithValue("id", auditEntryId);

        return await command.ExecuteNonQueryAsync();
    }

    private static void AssertAppendOnlyRejection(PostgresException exception, string operation)
    {
        Assert.Equal("P0001", exception.SqlState);
        Assert.Contains("append-only", exception.MessageText, StringComparison.Ordinal);
        Assert.Contains(operation, exception.MessageText, StringComparison.Ordinal);
    }
}
