using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlClientsSearchStorageTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 10, 11, 30, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task MigrationCreatesClientsSearchTablesConstraintsAndIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        Assert.True(await TableExistsAsync(database, "clients"));
        Assert.True(await TableExistsAsync(database, "client_card_assignments"));
        Assert.True(await ConstraintExistsAsync(database, "clients", "ck_clients_phone_fields_consistent"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "client_card_assignments",
            "ck_client_card_assignments_lifecycle"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "clients",
            "FK_clients_accounts_created_by_account_id"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "client_card_assignments",
            "FK_client_card_assignments_clients_client_id"));
        Assert.True(await IndexExistsAsync(database, "clients", "ix_clients_normalized_full_name"));
        Assert.True(await IndexExistsAsync(database, "clients", "ix_clients_phone_normalized"));
        Assert.True(await IndexExistsAsync(database, "clients", "ix_clients_phone_last4_status"));
        Assert.True(await IndexExistsAsync(
            database,
            "client_card_assignments",
            "ix_client_card_assignments_client_history"));

        var currentCardIndex = await ReadIndexDefinitionAsync(
            database,
            "client_card_assignments",
            "ux_client_card_assignments_current_card");
        var currentClientIndex = await ReadIndexDefinitionAsync(
            database,
            "client_card_assignments",
            "ux_client_card_assignments_current_client");
        Assert.Contains("UNIQUE INDEX", currentCardIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE is_current", currentCardIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNIQUE INDEX", currentClientIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE is_current", currentClientIndex, StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    public async Task ClientCanExistWithoutCardOrPhone()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        await InsertClientAsync(database.ConnectionString, Guid.NewGuid(), actorAccountId);

        var clientCount = await database.ExecuteScalarAsync<long>("select count(*) from bodylife.clients");
        var cardCount = await database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.client_card_assignments");

        Assert.Equal(1L, clientCount);
        Assert.Equal(0L, cardCount);
    }

    [PostgreSqlFact]
    public async Task ClientPhoneFieldsMustFormOneCanonicalTuple()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);

        var missingNormalizedException = await Assert.ThrowsAsync<PostgresException>(
            () => InsertClientAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                actorAccountId,
                phoneRaw: "+38 (067) 123-45-67"));
        var wrongLastFourException = await Assert.ThrowsAsync<PostgresException>(
            () => InsertClientAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                actorAccountId,
                phoneRaw: "+38 (067) 123-45-67",
                phoneNormalized: "380671234567",
                phoneLastFour: "0000"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, missingNormalizedException.SqlState);
        Assert.Equal("ck_clients_phone_fields_consistent", missingNormalizedException.ConstraintName);
        Assert.Equal(PostgresErrorCodes.CheckViolation, wrongLastFourException.SqlState);
        Assert.Equal("ck_clients_phone_fields_consistent", wrongLastFourException.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task CurrentCardNumberMustBeUniqueAcrossClients()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var firstClientId = Guid.NewGuid();
        var secondClientId = Guid.NewGuid();
        await InsertClientAsync(database.ConnectionString, firstClientId, actorAccountId, surname: "First");
        await InsertClientAsync(database.ConnectionString, secondClientId, actorAccountId, surname: "Second");
        await InsertCardAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            firstClientId,
            actorAccountId,
            "BL-1001");

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => InsertCardAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                secondClientId,
                actorAccountId,
                "BL-1001"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ux_client_card_assignments_current_card", exception.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task ClientCanHaveOnlyOneCurrentCard()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database.ConnectionString, clientId, actorAccountId);
        await InsertCardAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            clientId,
            actorAccountId,
            "BL-1001");

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => InsertCardAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                actorAccountId,
                "BL-1002"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ux_client_card_assignments_current_client", exception.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task HistoricalCardAllowsNumberToBecomeCurrentForAnotherClient()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var firstClientId = Guid.NewGuid();
        var secondClientId = Guid.NewGuid();
        var firstAssignmentId = Guid.NewGuid();
        await InsertClientAsync(database.ConnectionString, firstClientId, actorAccountId, surname: "First");
        await InsertClientAsync(database.ConnectionString, secondClientId, actorAccountId, surname: "Second");
        await InsertCardAsync(
            database.ConnectionString,
            firstAssignmentId,
            firstClientId,
            actorAccountId,
            "BL-1001");
        await EndCardAsync(
            database.ConnectionString,
            firstAssignmentId,
            actorAccountId,
            "Card reassigned");

        await InsertCardAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            secondClientId,
            actorAccountId,
            "BL-1001");

        var assignmentCount = await database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.client_card_assignments where card_number_normalized = 'BL-1001'");
        var currentCount = await database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.client_card_assignments where card_number_normalized = 'BL-1001' and is_current");
        Assert.Equal(2L, assignmentCount);
        Assert.Equal(1L, currentCount);
    }

    [PostgreSqlFact]
    public async Task HistoricalCardRequiresCompleteEndMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database.ConnectionString, clientId, actorAccountId);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => InsertCardAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                actorAccountId,
                "BL-1001",
                isCurrent: false));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_client_card_assignments_lifecycle", exception.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task ConcurrentCurrentCardAssignmentsResolveToUniqueViolation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var firstClientId = Guid.NewGuid();
        var secondClientId = Guid.NewGuid();
        await InsertClientAsync(database.ConnectionString, firstClientId, actorAccountId, surname: "First");
        await InsertClientAsync(database.ConnectionString, secondClientId, actorAccountId, surname: "Second");

        await using var firstConnection = new NpgsqlConnection(database.ConnectionString);
        await using var secondConnection = new NpgsqlConnection(database.ConnectionString);
        await firstConnection.OpenAsync();
        await secondConnection.OpenAsync();
        await using var firstTransaction = await firstConnection.BeginTransactionAsync();
        await using var secondTransaction = await secondConnection.BeginTransactionAsync();
        await InsertCardAsync(
            firstConnection,
            firstTransaction,
            Guid.NewGuid(),
            firstClientId,
            actorAccountId,
            "BL-CONCURRENT");

        var competingInsert = InsertCardAsync(
            secondConnection,
            secondTransaction,
            Guid.NewGuid(),
            secondClientId,
            actorAccountId,
            "BL-CONCURRENT");
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        await firstTransaction.CommitAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => competingInsert);
        await secondTransaction.RollbackAsync();
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ux_client_card_assignments_current_card", exception.ConstraintName);
    }

    private static async Task<Guid> BootstrapOwnerAsync(BodyLifeDbContext dbContext)
    {
        var result = await new OwnerBootstrapper(dbContext, new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, result.Status);
        return result.AccountId!.Value;
    }

    private static async Task InsertClientAsync(
        string connectionString,
        Guid clientId,
        Guid actorAccountId,
        string surname = "Ivanenko",
        string? phoneRaw = null,
        string? phoneNormalized = null,
        string? phoneLastFour = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.clients (
                id,
                surname,
                name,
                patronymic,
                normalized_full_name,
                phone_raw,
                phone_normalized,
                phone_last4,
                comment,
                operational_status,
                created_at,
                created_by_account_id,
                updated_at)
            values (
                @id,
                @surname,
                @name,
                @patronymic,
                @normalized_full_name,
                @phone_raw,
                @phone_normalized,
                @phone_last4,
                @comment,
                @operational_status,
                @created_at,
                @created_by_account_id,
                @updated_at)
            """;
        command.Parameters.AddWithValue("id", clientId);
        command.Parameters.AddWithValue("surname", surname);
        command.Parameters.AddWithValue("name", "Ivan");
        command.Parameters.Add("patronymic", NpgsqlDbType.Text).Value = DBNull.Value;
        command.Parameters.AddWithValue("normalized_full_name", $"{surname.ToUpperInvariant()} IVAN");
        command.Parameters.Add("phone_raw", NpgsqlDbType.Text).Value = phoneRaw ?? (object)DBNull.Value;
        command.Parameters.Add("phone_normalized", NpgsqlDbType.Text).Value = phoneNormalized ?? (object)DBNull.Value;
        command.Parameters.Add("phone_last4", NpgsqlDbType.Text).Value = phoneLastFour ?? (object)DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value = DBNull.Value;
        command.Parameters.AddWithValue("operational_status", "active");
        command.Parameters.AddWithValue("created_at", TestNow);
        command.Parameters.AddWithValue("created_by_account_id", actorAccountId);
        command.Parameters.AddWithValue("updated_at", TestNow);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertCardAsync(
        string connectionString,
        Guid assignmentId,
        Guid clientId,
        Guid actorAccountId,
        string cardNumber,
        bool isCurrent = true)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await InsertCardAsync(
            connection,
            transaction: null,
            assignmentId,
            clientId,
            actorAccountId,
            cardNumber,
            isCurrent);
    }

    private static async Task InsertCardAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid assignmentId,
        Guid clientId,
        Guid actorAccountId,
        string cardNumber,
        bool isCurrent = true)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 10;
        command.CommandText =
            """
            insert into bodylife.client_card_assignments (
                id,
                client_id,
                card_number_raw,
                card_number_normalized,
                assigned_at,
                assigned_by_account_id,
                ended_at,
                ended_by_account_id,
                end_reason,
                is_current)
            values (
                @id,
                @client_id,
                @card_number_raw,
                @card_number_normalized,
                @assigned_at,
                @assigned_by_account_id,
                @ended_at,
                @ended_by_account_id,
                @end_reason,
                @is_current)
            """;
        command.Parameters.AddWithValue("id", assignmentId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("card_number_raw", cardNumber);
        command.Parameters.AddWithValue("card_number_normalized", cardNumber);
        command.Parameters.AddWithValue("assigned_at", TestNow);
        command.Parameters.AddWithValue("assigned_by_account_id", actorAccountId);
        command.Parameters.Add("ended_at", NpgsqlDbType.TimestampTz).Value = DBNull.Value;
        command.Parameters.Add("ended_by_account_id", NpgsqlDbType.Uuid).Value = DBNull.Value;
        command.Parameters.Add("end_reason", NpgsqlDbType.Text).Value = DBNull.Value;
        command.Parameters.AddWithValue("is_current", isCurrent);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EndCardAsync(
        string connectionString,
        Guid assignmentId,
        Guid actorAccountId,
        string reason)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.client_card_assignments
            set is_current = false,
                ended_at = @ended_at,
                ended_by_account_id = @ended_by_account_id,
                end_reason = @end_reason
            where id = @id
            """;
        command.Parameters.AddWithValue("id", assignmentId);
        command.Parameters.AddWithValue("ended_at", TestNow.AddMinutes(1));
        command.Parameters.AddWithValue("ended_by_account_id", actorAccountId);
        command.Parameters.AddWithValue("end_reason", reason);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
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

    private static Task<bool> ConstraintExistsAsync(
        PostgreSqlTestDatabase database,
        string tableName,
        string constraintName)
    {
        return database.ExecuteScalarAsync<bool>(
            $"""
            select exists (
                select 1
                from information_schema.table_constraints
                where constraint_schema = 'bodylife'
                  and table_name = '{tableName}'
                  and constraint_name = '{constraintName}'
            )
            """);
    }

    private static Task<bool> IndexExistsAsync(
        PostgreSqlTestDatabase database,
        string tableName,
        string indexName)
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

    private static async Task<string> ReadIndexDefinitionAsync(
        PostgreSqlTestDatabase database,
        string tableName,
        string indexName)
    {
        return await database.ExecuteScalarAsync<string>(
            $"""
            select indexdef
            from pg_indexes
            where schemaname = 'bodylife'
              and tablename = '{tableName}'
              and indexname = '{indexName}'
            """)
            ?? throw new InvalidOperationException($"Index '{indexName}' was not found.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
