using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMembershipStateCacheStorageTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 13, 10, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly TestStartDate = new(2026, 7, 1);
    private static readonly DateOnly TestBaseEndDate = new(2026, 7, 30);

    [PostgreSqlFact]
    public async Task MigrationCreatesStableCacheColumnsConstraintsAndReportIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        Assert.True(await TableExistsAsync(database, "membership_state_cache"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "membership_state_cache",
            "PK_membership_state_cache"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "membership_state_cache",
            "FK_membership_state_cache_issued_memberships_membership_id"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "membership_state_cache",
            "ck_membership_state_cache_counted_visits_non_negative"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "membership_state_cache",
            "ck_membership_state_cache_negative_balance_consistent"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "membership_state_cache",
            "ck_membership_state_cache_extension_days_non_negative"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "membership_state_cache",
            "ck_membership_state_cache_recalculation_version_positive"));

        var effectiveEndIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_membership_state_cache_effective_end_date");
        var remainingVisitsIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_membership_state_cache_remaining_visits");
        var negativeBalanceIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_membership_state_cache_negative_balance_open");
        var lastVisitIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_membership_state_cache_last_counted_visit_at");

        Assert.Contains("(effective_end_date)", effectiveEndIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(remaining_visits)", remainingVisitsIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(negative_balance)", negativeBalanceIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", negativeBalanceIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("negative_balance > 0", negativeBalanceIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(last_counted_visit_at)", lastVisitIndex, StringComparison.OrdinalIgnoreCase);

        var columnNames = await ReadColumnNamesAsync(database);
        Assert.DoesNotContain("is_active", columnNames);
        Assert.DoesNotContain("active_status", columnNames);
        Assert.DoesNotContain("days_left", columnNames);
        Assert.DoesNotContain("warnings", columnNames);
    }

    [PostgreSqlFact]
    public async Task InitialCalculatedStateIsStoredWithRebuildMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);

        await InsertStateCacheAsync(database.ConnectionString, membershipId);

        var persisted = await ReadStateCacheAsync(database.ConnectionString, membershipId);
        Assert.Equal(0, persisted.CountedVisits);
        Assert.Equal(2, persisted.RemainingVisits);
        Assert.Equal(0, persisted.NegativeBalance);
        Assert.Null(persisted.FirstNegativeVisitId);
        Assert.Null(persisted.FirstNegativeVisitDate);
        Assert.Equal(0, persisted.ExtensionDays);
        Assert.Equal(TestBaseEndDate, persisted.EffectiveEndDate);
        Assert.Null(persisted.LastCountedVisitAt);
        Assert.Equal(TestNow, persisted.RecalculatedAt);
        Assert.Equal(1, persisted.RecalculationVersion);
    }

    [PostgreSqlFact]
    public async Task SignedRemainingAndNegativeMetadataAreStored()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);
        var firstNegativeVisitId = Guid.NewGuid();
        var firstNegativeVisitDate = new DateOnly(2026, 7, 3);
        var lastCountedVisitAt = new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero);

        await InsertStateCacheAsync(
            database.ConnectionString,
            membershipId,
            countedVisits: 3,
            remainingVisits: -1,
            negativeBalance: 1,
            firstNegativeVisitId: firstNegativeVisitId,
            firstNegativeVisitDate: firstNegativeVisitDate,
            extensionDays: 2,
            effectiveEndDate: new DateOnly(2026, 8, 1),
            lastCountedVisitAt: lastCountedVisitAt,
            recalculatedAt: TestNow.AddMinutes(1),
            recalculationVersion: 2);

        var persisted = await ReadStateCacheAsync(database.ConnectionString, membershipId);
        Assert.Equal(3, persisted.CountedVisits);
        Assert.Equal(-1, persisted.RemainingVisits);
        Assert.Equal(1, persisted.NegativeBalance);
        Assert.Equal(firstNegativeVisitId, persisted.FirstNegativeVisitId);
        Assert.Equal(firstNegativeVisitDate, persisted.FirstNegativeVisitDate);
        Assert.Equal(2, persisted.ExtensionDays);
        Assert.Equal(new DateOnly(2026, 8, 1), persisted.EffectiveEndDate);
        Assert.Equal(lastCountedVisitAt, persisted.LastCountedVisitAt);
        Assert.Equal(TestNow.AddMinutes(1), persisted.RecalculatedAt);
        Assert.Equal(2, persisted.RecalculationVersion);
    }

    [PostgreSqlFact]
    public async Task StableDerivedValueConstraintsRejectInconsistentRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);

        await AssertCheckViolationAsync(
            () => InsertStateCacheAsync(
                database.ConnectionString,
                membershipId,
                countedVisits: -1),
            "ck_membership_state_cache_counted_visits_non_negative");
        await AssertCheckViolationAsync(
            () => InsertStateCacheAsync(
                database.ConnectionString,
                membershipId,
                remainingVisits: -1,
                negativeBalance: 0),
            "ck_membership_state_cache_negative_balance_consistent");
        await AssertCheckViolationAsync(
            () => InsertStateCacheAsync(
                database.ConnectionString,
                membershipId,
                extensionDays: -1),
            "ck_membership_state_cache_extension_days_non_negative");
        await AssertCheckViolationAsync(
            () => InsertStateCacheAsync(
                database.ConnectionString,
                membershipId,
                recalculationVersion: 0),
            "ck_membership_state_cache_recalculation_version_positive");
    }

    [PostgreSqlFact]
    public async Task CacheHasOneRowPerKnownMembershipAndCanBeRebuiltIndependently()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);

        await AssertForeignKeyViolationAsync(
            () => InsertStateCacheAsync(database.ConnectionString, Guid.NewGuid()),
            "FK_membership_state_cache_issued_memberships_membership_id");

        await InsertStateCacheAsync(database.ConnectionString, membershipId);
        await AssertUniqueViolationAsync(
            () => InsertStateCacheAsync(database.ConnectionString, membershipId),
            "PK_membership_state_cache");

        Assert.Equal(1, await DeleteStateCacheAsync(database.ConnectionString, membershipId));
        Assert.True(await IssuedMembershipExistsAsync(database, membershipId));

        await InsertStateCacheAsync(
            database.ConnectionString,
            membershipId,
            remainingVisits: 1,
            recalculatedAt: TestNow.AddMinutes(2),
            recalculationVersion: 2);

        var rebuilt = await ReadStateCacheAsync(database.ConnectionString, membershipId);
        Assert.Equal(1, rebuilt.RemainingVisits);
        Assert.Equal(TestNow.AddMinutes(2), rebuilt.RecalculatedAt);
        Assert.Equal(2, rebuilt.RecalculationVersion);
    }

    private static async Task<Guid> SeedIssuedMembershipAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var bootstrap = await new OwnerBootstrapper(dbContext, new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var actorAccountId = bootstrap.AccountId!.Value;
        var clientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
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
                @client_id,
                'Ivanenko',
                'Ivan',
                null,
                'IVANENKO IVAN',
                null,
                null,
                null,
                null,
                'active',
                @recorded_at,
                @actor_account_id,
                @recorded_at);

            insert into bodylife.membership_types (
                id,
                name,
                duration_days,
                visits_limit,
                price_amount,
                price_currency,
                is_active,
                comment,
                created_at,
                updated_at,
                deactivated_at)
            values (
                @membership_type_id,
                'Slice 2 visits / 30 days',
                30,
                2,
                1000,
                'UAH',
                true,
                null,
                @recorded_at,
                @recorded_at,
                null);

            insert into bodylife.issued_memberships (
                id,
                client_id,
                membership_type_id,
                type_name_snapshot,
                duration_days_snapshot,
                visits_limit_snapshot,
                price_amount_snapshot,
                price_currency_snapshot,
                start_date,
                base_end_date,
                issued_at,
                issued_by_account_id,
                status,
                entry_origin,
                entry_batch_id,
                comment)
            values (
                @membership_id,
                @client_id,
                @membership_type_id,
                'Slice 2 visits / 30 days',
                30,
                2,
                1000,
                'UAH',
                @start_date,
                @base_end_date,
                @recorded_at,
                @actor_account_id,
                'active',
                'normal',
                null,
                null)
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("actor_account_id", actorAccountId);
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, TestStartDate);
        command.Parameters.AddWithValue("base_end_date", NpgsqlDbType.Date, TestBaseEndDate);
        Assert.Equal(3, await command.ExecuteNonQueryAsync());

        return membershipId;
    }

    private static async Task InsertStateCacheAsync(
        string connectionString,
        Guid membershipId,
        int countedVisits = 0,
        int remainingVisits = 2,
        int negativeBalance = 0,
        Guid? firstNegativeVisitId = null,
        DateOnly? firstNegativeVisitDate = null,
        int extensionDays = 0,
        DateOnly? effectiveEndDate = null,
        DateTimeOffset? lastCountedVisitAt = null,
        DateTimeOffset? recalculatedAt = null,
        int recalculationVersion = 1)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.membership_state_cache (
                membership_id,
                counted_visits,
                remaining_visits,
                negative_balance,
                first_negative_visit_id,
                first_negative_visit_date,
                extension_days,
                effective_end_date,
                last_counted_visit_at,
                recalculated_at,
                recalculation_version)
            values (
                @membership_id,
                @counted_visits,
                @remaining_visits,
                @negative_balance,
                @first_negative_visit_id,
                @first_negative_visit_date,
                @extension_days,
                @effective_end_date,
                @last_counted_visit_at,
                @recalculated_at,
                @recalculation_version)
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("counted_visits", countedVisits);
        command.Parameters.AddWithValue("remaining_visits", remainingVisits);
        command.Parameters.AddWithValue("negative_balance", negativeBalance);
        command.Parameters.Add("first_negative_visit_id", NpgsqlDbType.Uuid).Value =
            firstNegativeVisitId ?? (object)DBNull.Value;
        command.Parameters.Add("first_negative_visit_date", NpgsqlDbType.Date).Value =
            firstNegativeVisitDate ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("extension_days", extensionDays);
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            effectiveEndDate ?? TestBaseEndDate);
        command.Parameters.Add("last_counted_visit_at", NpgsqlDbType.TimestampTz).Value =
            lastCountedVisitAt ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("recalculated_at", recalculatedAt ?? TestNow);
        command.Parameters.AddWithValue("recalculation_version", recalculationVersion);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PersistedMembershipState> ReadStateCacheAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                counted_visits,
                remaining_visits,
                negative_balance,
                first_negative_visit_id,
                first_negative_visit_date,
                extension_days,
                effective_end_date,
                last_counted_visit_at,
                recalculated_at,
                recalculation_version
            from bodylife.membership_state_cache
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new PersistedMembershipState(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
            reader.GetInt32(5),
            reader.GetFieldValue<DateOnly>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetInt32(9));
    }

    private static async Task<int> DeleteStateCacheAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "delete from bodylife.membership_state_cache where membership_id = @membership_id";
        command.Parameters.AddWithValue("membership_id", membershipId);
        return await command.ExecuteNonQueryAsync();
    }

    private static Task<bool> IssuedMembershipExistsAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        return database.ExecuteScalarAsync<bool>(
            $"select exists (select 1 from bodylife.issued_memberships where id = '{membershipId}')");
    }

    private static Task<bool> TableExistsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
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

    private static async Task<string> ReadIndexDefinitionAsync(
        PostgreSqlTestDatabase database,
        string indexName)
    {
        return await database.ExecuteScalarAsync<string>(
            $"""
            select indexdef
            from pg_indexes
            where schemaname = 'bodylife'
              and tablename = 'membership_state_cache'
              and indexname = '{indexName}'
            """)
            ?? throw new InvalidOperationException($"Index '{indexName}' was not found.");
    }

    private static async Task<string[]> ReadColumnNamesAsync(PostgreSqlTestDatabase database)
    {
        var names = await database.ExecuteScalarAsync<string>(
            """
            select string_agg(column_name, ',' order by ordinal_position)
            from information_schema.columns
            where table_schema = 'bodylife'
              and table_name = 'membership_state_cache'
            """);

        return names?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            ?? [];
    }

    private static async Task AssertCheckViolationAsync(
        Func<Task> action,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(constraintName, exception.ConstraintName);
    }

    private static async Task AssertForeignKeyViolationAsync(
        Func<Task> action,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal(constraintName, exception.ConstraintName);
    }

    private static async Task AssertUniqueViolationAsync(
        Func<Task> action,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal(constraintName, exception.ConstraintName);
    }

    private sealed record PersistedMembershipState(
        int CountedVisits,
        int RemainingVisits,
        int NegativeBalance,
        Guid? FirstNegativeVisitId,
        DateOnly? FirstNegativeVisitDate,
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        DateTimeOffset? LastCountedVisitAt,
        DateTimeOffset RecalculatedAt,
        int RecalculationVersion);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
