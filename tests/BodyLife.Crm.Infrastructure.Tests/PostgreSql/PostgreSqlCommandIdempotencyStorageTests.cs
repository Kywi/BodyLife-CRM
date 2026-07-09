using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlCommandIdempotencyStorageTests
{
    [PostgreSqlFact]
    public async Task CommandIdempotencyStorageMigrationCreatesTableAndIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        var tableExists = await database.ExecuteScalarAsync<bool>(
            """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'bodylife'
                  and table_name = 'command_idempotency_keys'
            )
            """);
        var uniqueIndexExists = await database.ExecuteScalarAsync<bool>(
            """
            select exists (
                select 1
                from pg_indexes
                where schemaname = 'bodylife'
                  and tablename = 'command_idempotency_keys'
                  and indexname = 'ux_command_idempotency_keys_command_key'
            )
            """);
        var expiresAtIndexExists = await database.ExecuteScalarAsync<bool>(
            """
            select exists (
                select 1
                from pg_indexes
                where schemaname = 'bodylife'
                  and tablename = 'command_idempotency_keys'
                  and indexname = 'ix_command_idempotency_keys_expires_at'
            )
            """);

        Assert.True(tableExists);
        Assert.True(uniqueIndexExists);
        Assert.True(expiresAtIndexExists);
    }

    [PostgreSqlFact]
    public async Task CommandIdempotencyStorageRejectsDuplicateCommandKey()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();
        await InsertCommandIdempotencyRecordAsync(
            database.ConnectionString,
            id: Guid.NewGuid(),
            commandName: "MarkVisit",
            idempotencyKey: "visit-submit-1");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertCommandIdempotencyRecordAsync(
            database.ConnectionString,
            id: Guid.NewGuid(),
            commandName: "MarkVisit",
            idempotencyKey: "visit-submit-1"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ux_command_idempotency_keys_command_key", exception.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task CommandIdempotencyStorageRejectsInvalidStatus()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertCommandIdempotencyRecordAsync(
            database.ConnectionString,
            id: Guid.NewGuid(),
            commandName: "CreatePayment",
            idempotencyKey: "payment-submit-1",
            status: "unknown",
            completedAt: new DateTimeOffset(2026, 7, 9, 12, 5, 0, TimeSpan.Zero)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_command_idempotency_keys_status", exception.ConstraintName);
    }

    private static async Task InsertCommandIdempotencyRecordAsync(
        string connectionString,
        Guid id,
        string commandName,
        string idempotencyKey,
        string status = "started",
        DateTimeOffset? completedAt = null)
    {
        var createdAt = new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            insert into bodylife.command_idempotency_keys (
                id,
                command_name,
                idempotency_key,
                request_correlation_id,
                account_id,
                actor_role,
                account_kind,
                session_id,
                device_label,
                entry_origin,
                status,
                created_at,
                completed_at,
                expires_at)
            values (
                @id,
                @command_name,
                @idempotency_key,
                @request_correlation_id,
                @account_id,
                @actor_role,
                @account_kind,
                @session_id,
                @device_label,
                @entry_origin,
                @status,
                @created_at,
                @completed_at,
                @expires_at)
            """;

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("command_name", commandName);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("request_correlation_id", "test-correlation-id");
        command.Parameters.AddWithValue("account_id", Guid.NewGuid());
        command.Parameters.AddWithValue("actor_role", "Reception");
        command.Parameters.AddWithValue("account_kind", "NamedAdmin");
        command.Parameters.AddWithValue("session_id", Guid.NewGuid());
        command.Parameters.AddWithValue("device_label", "front-desk-tablet");
        command.Parameters.AddWithValue("entry_origin", "normal");
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("created_at", createdAt);
        command.Parameters.Add("completed_at", NpgsqlDbType.TimestampTz).Value = completedAt.HasValue
            ? completedAt.Value
            : DBNull.Value;
        command.Parameters.AddWithValue("expires_at", createdAt.AddHours(24));

        await command.ExecuteNonQueryAsync();
    }
}
