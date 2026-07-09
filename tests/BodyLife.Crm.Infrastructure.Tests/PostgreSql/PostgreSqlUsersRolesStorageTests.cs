using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlUsersRolesStorageTests
{
    [PostgreSqlFact]
    public async Task UsersRolesStorageMigrationCreatesAccountsAndSessionsTables()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        var accountsTableExists = await TableExistsAsync(database, "accounts");
        var sessionsTableExists = await TableExistsAsync(database, "sessions");
        var accountCredentialsTableExists = await TableExistsAsync(database, "account_credentials");
        var ownerIndexExists = await IndexExistsAsync(database, "accounts", "ux_accounts_single_owner");
        var loginNameIndexExists = await IndexExistsAsync(
            database,
            "account_credentials",
            "ux_account_credentials_normalized_login_name");
        var activeSessionIndexExists = await IndexExistsAsync(
            database,
            "sessions",
            "ix_sessions_active_account_started_at");
        var sessionAccountForeignKeyExists = await database.ExecuteScalarAsync<bool>(
            """
            select exists (
                select 1
                from information_schema.table_constraints
                where constraint_schema = 'bodylife'
                  and table_name = 'sessions'
                  and constraint_name = 'FK_sessions_accounts_account_id'
                  and constraint_type = 'FOREIGN KEY'
            )
            """);

        Assert.True(accountsTableExists);
        Assert.True(sessionsTableExists);
        Assert.True(accountCredentialsTableExists);
        Assert.True(ownerIndexExists);
        Assert.True(loginNameIndexExists);
        Assert.True(activeSessionIndexExists);
        Assert.True(sessionAccountForeignKeyExists);
    }

    [PostgreSqlFact]
    public async Task AccountsStorageRejectsSecondOwnerAccount()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();
        await InsertAccountAsync(database.ConnectionString, Guid.NewGuid(), "Owner", "owner", "owner");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertAccountAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            "Second owner",
            "owner",
            "owner"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ux_accounts_single_owner", exception.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task AccountsStorageRejectsAccountTypeRoleMismatch()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertAccountAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            "Shared desk",
            "shared_reception_admin",
            "owner"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_accounts_account_type_role", exception.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task SessionsStorageRequiresExistingAccount()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertSessionAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            accountId: Guid.NewGuid()));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal("FK_sessions_accounts_account_id", exception.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task SessionsStorageRejectsLastSeenBeforeStarted()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        var accountId = Guid.NewGuid();
        await InsertAccountAsync(database.ConnectionString, accountId, "Reception", "shared_reception_admin", "admin");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertSessionAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            accountId,
            lastSeenAt: new DateTimeOffset(2026, 7, 9, 11, 59, 0, TimeSpan.Zero)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_sessions_last_seen_after_started", exception.ConstraintName);
    }

    private static Task<bool> TableExistsAsync(PostgreSqlTestDatabase database, string tableName)
    {
        return database.ExecuteScalarAsync<bool>(
            $"""
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'bodylife'
                  and table_name = '{tableName}'
            )
            """);
    }

    private static Task<bool> IndexExistsAsync(PostgreSqlTestDatabase database, string tableName, string indexName)
    {
        return database.ExecuteScalarAsync<bool>(
            $"""
            select exists (
                select 1
                from pg_indexes
                where schemaname = 'bodylife'
                  and tablename = '{tableName}'
                  and indexname = '{indexName}'
            )
            """);
    }

    private static async Task InsertAccountAsync(
        string connectionString,
        Guid id,
        string displayName,
        string accountType,
        string role)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            insert into bodylife.accounts (
                id,
                display_name,
                account_type,
                role,
                is_active,
                created_at,
                deactivated_at)
            values (
                @id,
                @display_name,
                @account_type,
                @role,
                @is_active,
                @created_at,
                @deactivated_at)
            """;

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("display_name", displayName);
        command.Parameters.AddWithValue("account_type", accountType);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("is_active", true);
        command.Parameters.AddWithValue(
            "created_at",
            new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero));
        command.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value = DBNull.Value;

        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertSessionAsync(
        string connectionString,
        Guid id,
        Guid accountId,
        DateTimeOffset? lastSeenAt = null)
    {
        var startedAt = new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            insert into bodylife.sessions (
                id,
                account_id,
                device_label,
                started_at,
                ended_at,
                last_seen_at)
            values (
                @id,
                @account_id,
                @device_label,
                @started_at,
                @ended_at,
                @last_seen_at)
            """;

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("device_label", "front-desk-tablet");
        command.Parameters.AddWithValue("started_at", startedAt);
        command.Parameters.Add("ended_at", NpgsqlDbType.TimestampTz).Value = DBNull.Value;
        command.Parameters.AddWithValue("last_seen_at", lastSeenAt ?? startedAt);

        await command.ExecuteNonQueryAsync();
    }
}
