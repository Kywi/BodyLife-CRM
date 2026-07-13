using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMembershipExtensionDaysStorageTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        13,
        15,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly TestStartDate = new(2026, 7, 1);
    private static readonly DateOnly TestBaseEndDate = new(2026, 7, 30);

    [PostgreSqlFact]
    public async Task MigrationCreatesDerivedExplanationColumnsConstraintsAndIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        Assert.True(await TableExistsAsync(database));
        var expectedConstraints = new[]
        {
            "PK_membership_extension_days",
            "FK_membership_extension_days_issued_memberships_membership_id",
            "ck_membership_extension_days_source_label_not_empty",
            "ck_membership_extension_days_source_type_not_empty",
        };
        foreach (var constraint in expectedConstraints)
        {
            Assert.True(
                await ConstraintExistsAsync(database, constraint),
                $"Expected constraint '{constraint}' was not found.");
        }

        Assert.Equal(
            [
                "id",
                "membership_id",
                "extension_date",
                "source_type",
                "source_id",
                "source_label",
                "is_active",
                "recalculated_at",
            ],
            await ReadColumnNamesAsync(database));

        var sourceDayIndex = await ReadIndexDefinitionAsync(
            database,
            "ux_membership_extension_days_membership_date_source");
        Assert.Contains("UNIQUE INDEX", sourceDayIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "(membership_id, extension_date, source_type, source_id)",
            sourceDayIndex,
            StringComparison.OrdinalIgnoreCase);

        var activeDateIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_membership_extension_days_active_membership_date");
        Assert.Contains(
            "(membership_id, extension_date)",
            activeDateIndex,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE is_active", activeDateIndex, StringComparison.OrdinalIgnoreCase);

        var sourceIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_membership_extension_days_source");
        Assert.Contains("(source_type, source_id)", sourceIndex, StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    public async Task OverlappingSourcesRemainExplainableAndCountAsDistinctActiveDates()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);
        var freezeId = Guid.NewGuid();
        var nonWorkingPeriodId = Guid.NewGuid();
        var adjustmentId = Guid.NewGuid();
        var firstDate = new DateOnly(2026, 7, 10);
        var overlappingDate = firstDate.AddDays(1);

        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            firstDate,
            "freeze",
            freezeId,
            "Freeze 2026-07-10..2026-07-11");
        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            overlappingDate,
            "freeze",
            freezeId,
            "Freeze 2026-07-10..2026-07-11");
        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            overlappingDate,
            "non_working_period",
            nonWorkingPeriodId,
            "Gym closure 2026-07-11");
        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            firstDate.AddDays(2),
            "membership_adjustment",
            adjustmentId,
            "Canceled extension correction",
            isActive: false,
            recalculatedAt: TestNow.AddMinutes(1));

        var rows = await ReadExtensionDaysAsync(database.ConnectionString, membershipId);

        Assert.Equal(4, rows.Count);
        Assert.Contains(
            rows,
            row => row.ExtensionDate == overlappingDate
                && row.SourceType == "freeze"
                && row.SourceId == freezeId
                && row.IsActive);
        Assert.Contains(
            rows,
            row => row.ExtensionDate == overlappingDate
                && row.SourceType == "non_working_period"
                && row.SourceId == nonWorkingPeriodId
                && row.IsActive);
        var inactive = Assert.Single(rows, row => !row.IsActive);
        Assert.Equal("Canceled extension correction", inactive.SourceLabel);
        Assert.Equal(TestNow.AddMinutes(1), inactive.RecalculatedAt);
        Assert.Equal(
            2L,
            await CountDistinctActiveDatesAsync(database, membershipId));
        Assert.Equal(
            2L,
            await CountActiveSourcesOnDateAsync(database, membershipId, overlappingDate));
    }

    [PostgreSqlFact]
    public async Task MetadataAndSourceIdentityConstraintsRejectInvalidRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);
        var sourceId = Guid.NewGuid();
        var extensionDate = new DateOnly(2026, 7, 10);

        await AssertCheckViolationAsync(
            () => InsertExtensionDayAsync(
                database.ConnectionString,
                membershipId,
                extensionDate,
                "   ",
                sourceId,
                "Freeze 2026-07-10"),
            "ck_membership_extension_days_source_type_not_empty");
        await AssertCheckViolationAsync(
            () => InsertExtensionDayAsync(
                database.ConnectionString,
                membershipId,
                extensionDate,
                "freeze",
                sourceId,
                "   "),
            "ck_membership_extension_days_source_label_not_empty");

        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            extensionDate,
            "freeze",
            sourceId,
            "Freeze 2026-07-10");
        await AssertUniqueViolationAsync(
            () => InsertExtensionDayAsync(
                database.ConnectionString,
                membershipId,
                extensionDate,
                "freeze",
                sourceId,
                "Same source and date cannot be duplicated"),
            "ux_membership_extension_days_membership_date_source");

        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            extensionDate,
            "freeze",
            Guid.NewGuid(),
            "A different source may overlap the date");
        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            extensionDate.AddDays(1),
            "freeze",
            sourceId,
            "The same source may contribute another date");

        Assert.Equal(3L, await CountExtensionDaysAsync(database, membershipId));
    }

    [PostgreSqlFact]
    public async Task DerivedRowsRejectUnknownMembershipAndCanBeRebuiltIndependently()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        await AssertForeignKeyViolationAsync(
            () => InsertExtensionDayAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                new DateOnly(2026, 7, 10),
                "freeze",
                Guid.NewGuid(),
                "Unknown membership"),
            "FK_membership_extension_days_issued_memberships_membership_id");

        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);
        var sourceId = Guid.NewGuid();
        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            new DateOnly(2026, 7, 10),
            "freeze",
            sourceId,
            "Freeze day one");
        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            new DateOnly(2026, 7, 11),
            "freeze",
            sourceId,
            "Freeze day two");

        Assert.Equal(2, await DeleteExtensionDaysAsync(database.ConnectionString, membershipId));
        Assert.True(await IssuedMembershipExistsAsync(database, membershipId));

        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            new DateOnly(2026, 7, 10),
            "freeze",
            sourceId,
            "Rebuilt freeze day",
            recalculatedAt: TestNow.AddMinutes(2));
        var rebuilt = Assert.Single(
            await ReadExtensionDaysAsync(database.ConnectionString, membershipId));
        Assert.Equal("Rebuilt freeze day", rebuilt.SourceLabel);
        Assert.Equal(TestNow.AddMinutes(2), rebuilt.RecalculatedAt);

        Assert.Equal(1, await DeleteIssuedMembershipAsync(database.ConnectionString, membershipId));
        Assert.Equal(0L, await CountExtensionDaysAsync(database, membershipId));
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
                'Extension',
                'Storage',
                null,
                'EXTENSION STORAGE',
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
                'Extension storage membership',
                30,
                8,
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
                'Extension storage membership',
                30,
                8,
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

    private static async Task InsertExtensionDayAsync(
        string connectionString,
        Guid membershipId,
        DateOnly extensionDate,
        string sourceType,
        Guid sourceId,
        string sourceLabel,
        bool isActive = true,
        DateTimeOffset? recalculatedAt = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.membership_extension_days (
                id,
                membership_id,
                extension_date,
                source_type,
                source_id,
                source_label,
                is_active,
                recalculated_at)
            values (
                @id,
                @membership_id,
                @extension_date,
                @source_type,
                @source_id,
                @source_label,
                @is_active,
                @recalculated_at)
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("extension_date", NpgsqlDbType.Date, extensionDate);
        command.Parameters.AddWithValue("source_type", sourceType);
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("source_label", sourceLabel);
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.AddWithValue("recalculated_at", recalculatedAt ?? TestNow);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<PersistedExtensionDay>> ReadExtensionDaysAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select extension_date,
                   source_type,
                   source_id,
                   source_label,
                   is_active,
                   recalculated_at
            from bodylife.membership_extension_days
            where membership_id = @membership_id
            order by extension_date, source_type, source_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<PersistedExtensionDay>();
        while (await reader.ReadAsync())
        {
            rows.Add(new PersistedExtensionDay(
                reader.GetFieldValue<DateOnly>(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return rows;
    }

    private static Task<long> CountDistinctActiveDatesAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        return database.ExecuteScalarAsync<long>(
            $"""
            select count(distinct extension_date)
            from bodylife.membership_extension_days
            where membership_id = '{membershipId}'
              and is_active
            """);
    }

    private static Task<long> CountActiveSourcesOnDateAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        DateOnly extensionDate)
    {
        return database.ExecuteScalarAsync<long>(
            $"""
            select count(*)
            from bodylife.membership_extension_days
            where membership_id = '{membershipId}'
              and extension_date = date '{extensionDate:yyyy-MM-dd}'
              and is_active
            """);
    }

    private static Task<long> CountExtensionDaysAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        return database.ExecuteScalarAsync<long>(
            $"""
            select count(*)
            from bodylife.membership_extension_days
            where membership_id = '{membershipId}'
            """);
    }

    private static async Task<int> DeleteExtensionDaysAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            delete from bodylife.membership_extension_days
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> DeleteIssuedMembershipAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            delete from bodylife.issued_memberships
            where id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        return await command.ExecuteNonQueryAsync();
    }

    private static Task<bool> IssuedMembershipExistsAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        return database.ExecuteScalarAsync<bool>(
            $"""
            select exists (
                select 1
                from bodylife.issued_memberships
                where id = '{membershipId}')
            """);
    }

    private static Task<bool> TableExistsAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<bool>(
            """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'bodylife'
                  and table_name = 'membership_extension_days')
            """);
    }

    private static Task<bool> ConstraintExistsAsync(
        PostgreSqlTestDatabase database,
        string constraintName)
    {
        return database.ExecuteScalarAsync<bool>(
            $"""
            select exists (
                select 1
                from pg_constraint constraint_record
                join pg_class table_record on table_record.oid = constraint_record.conrelid
                join pg_namespace schema_record on schema_record.oid = table_record.relnamespace
                where schema_record.nspname = 'bodylife'
                  and table_record.relname = 'membership_extension_days'
                  and constraint_record.conname = '{constraintName}')
            """);
    }

    private static async Task<IReadOnlyList<string>> ReadColumnNamesAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select column_name
            from information_schema.columns
            where table_schema = 'bodylife'
              and table_name = 'membership_extension_days'
            order by ordinal_position
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<string> ReadIndexDefinitionAsync(
        PostgreSqlTestDatabase database,
        string indexName)
    {
        var definition = await database.ExecuteScalarAsync<string>(
            $"""
            select indexdef
            from pg_indexes
            where schemaname = 'bodylife'
              and indexname = '{indexName}'
            """);

        return Assert.IsType<string>(definition);
    }

    private static async Task AssertCheckViolationAsync(
        Func<Task> action,
        string expectedConstraint)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(expectedConstraint, exception.ConstraintName);
    }

    private static async Task AssertUniqueViolationAsync(
        Func<Task> action,
        string expectedConstraint)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal(expectedConstraint, exception.ConstraintName);
    }

    private static async Task AssertForeignKeyViolationAsync(
        Func<Task> action,
        string expectedConstraint)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal(expectedConstraint, exception.ConstraintName);
    }

    private sealed record PersistedExtensionDay(
        DateOnly ExtensionDate,
        string SourceType,
        Guid SourceId,
        string SourceLabel,
        bool IsActive,
        DateTimeOffset RecalculatedAt);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
