using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlIssuedMembershipsStorageTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 13, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly TestStartDate = new(2026, 7, 1);

    [PostgreSqlFact]
    public async Task MigrationCreatesIssuedMembershipSourceFactConstraintsAndTimelineIndex()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        Assert.True(await TableExistsAsync(database, "issued_memberships"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_issued_memberships_duration_snapshot_positive"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_issued_memberships_visits_snapshot_non_negative"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_issued_memberships_price_snapshot_non_negative"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_issued_memberships_base_end_date"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_issued_memberships_status"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_issued_memberships_entry_origin"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "FK_issued_memberships_clients_client_id"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "FK_issued_memberships_membership_types_membership_type_id"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "FK_issued_memberships_accounts_issued_by_account_id"));

        var timelineIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_issued_memberships_client_timeline");
        Assert.Contains(
            "(client_id, start_date DESC, issued_at DESC)",
            timelineIndex,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNIQUE INDEX", timelineIndex, StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    public async Task CanonicalSourceFactStoresImmutableSnapshotDatesAndIssueMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var clientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var entryBatchId = Guid.NewGuid();
        await InsertClientAsync(database.ConnectionString, clientId, actorAccountId);
        await InsertMembershipTypeAsync(database.ConnectionString, membershipTypeId);

        await InsertIssuedMembershipAsync(
            database.ConnectionString,
            membershipId,
            clientId,
            membershipTypeId,
            actorAccountId,
            entryOrigin: "paper_fallback",
            entryBatchId: entryBatchId,
            comment: "Recorded from the outage log");

        var persisted = await ReadIssuedMembershipAsync(database.ConnectionString, membershipId);

        Assert.Equal(clientId, persisted.ClientId);
        Assert.Equal(membershipTypeId, persisted.MembershipTypeId);
        Assert.Equal("Slice 2 visits / 30 days", persisted.TypeNameSnapshot);
        Assert.Equal(30, persisted.DurationDaysSnapshot);
        Assert.Equal(2, persisted.VisitsLimitSnapshot);
        Assert.Equal(1000m, persisted.PriceAmountSnapshot);
        Assert.Equal("UAH", persisted.PriceCurrencySnapshot);
        Assert.Equal(TestStartDate, persisted.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 30), persisted.BaseEndDate);
        Assert.Equal(TestNow, persisted.IssuedAt);
        Assert.Equal(actorAccountId, persisted.IssuedByAccountId);
        Assert.Equal("active", persisted.Status);
        Assert.Equal("paper_fallback", persisted.EntryOrigin);
        Assert.Equal(entryBatchId, persisted.EntryBatchId);
        Assert.Equal("Recorded from the outage log", persisted.Comment);
    }

    [PostgreSqlFact]
    public async Task SnapshotConstraintsRejectInvalidDurationVisitsAndPrice()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var clientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        await InsertClientAsync(database.ConnectionString, clientId, actorAccountId);
        await InsertMembershipTypeAsync(database.ConnectionString, membershipTypeId);

        await AssertCheckViolationAsync(
            () => InsertIssuedMembershipAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                membershipTypeId,
                actorAccountId,
                durationDaysSnapshot: 0),
            "ck_issued_memberships_duration_snapshot_positive");
        await AssertCheckViolationAsync(
            () => InsertIssuedMembershipAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                membershipTypeId,
                actorAccountId,
                visitsLimitSnapshot: -1),
            "ck_issued_memberships_visits_snapshot_non_negative");
        await AssertCheckViolationAsync(
            () => InsertIssuedMembershipAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                membershipTypeId,
                actorAccountId,
                priceAmountSnapshot: -0.01m),
            "ck_issued_memberships_price_snapshot_non_negative");
    }

    [PostgreSqlFact]
    public async Task DateAndMetadataConstraintsRejectNonCanonicalSourceFacts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var clientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        await InsertClientAsync(database.ConnectionString, clientId, actorAccountId);
        await InsertMembershipTypeAsync(database.ConnectionString, membershipTypeId);

        await AssertCheckViolationAsync(
            () => InsertIssuedMembershipAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                membershipTypeId,
                actorAccountId,
                baseEndDate: new DateOnly(2026, 7, 31)),
            "ck_issued_memberships_base_end_date");
        await AssertCheckViolationAsync(
            () => InsertIssuedMembershipAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                membershipTypeId,
                actorAccountId,
                typeNameSnapshot: "   "),
            "ck_issued_memberships_type_name_snapshot_not_empty");
        await AssertCheckViolationAsync(
            () => InsertIssuedMembershipAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                membershipTypeId,
                actorAccountId,
                priceCurrencySnapshot: "uah"),
            "ck_issued_memberships_currency_snapshot_canonical");
        await AssertCheckViolationAsync(
            () => InsertIssuedMembershipAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                membershipTypeId,
                actorAccountId,
                status: "expired"),
            "ck_issued_memberships_status");
        await AssertCheckViolationAsync(
            () => InsertIssuedMembershipAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                membershipTypeId,
                actorAccountId,
                entryOrigin: "spreadsheet_patch"),
            "ck_issued_memberships_entry_origin");
        await AssertCheckViolationAsync(
            () => InsertIssuedMembershipAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                membershipTypeId,
                actorAccountId,
                comment: "   "),
            "ck_issued_memberships_comment_not_empty");
    }

    [PostgreSqlFact]
    public async Task RelationshipsRejectUnknownClientMembershipTypeAndIssuer()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var clientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        await InsertClientAsync(database.ConnectionString, clientId, actorAccountId);
        await InsertMembershipTypeAsync(database.ConnectionString, membershipTypeId);

        await AssertForeignKeyViolationAsync(
            () => InsertIssuedMembershipAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                Guid.NewGuid(),
                membershipTypeId,
                actorAccountId),
            "FK_issued_memberships_clients_client_id");
        await AssertForeignKeyViolationAsync(
            () => InsertIssuedMembershipAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                Guid.NewGuid(),
                actorAccountId),
            "FK_issued_memberships_membership_types_membership_type_id");
        await AssertForeignKeyViolationAsync(
            () => InsertIssuedMembershipAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                clientId,
                membershipTypeId,
                Guid.NewGuid()),
            "FK_issued_memberships_accounts_issued_by_account_id");
    }

    [PostgreSqlFact]
    public async Task CatalogEditDoesNotRewriteIssuedSnapshotAndReferencedTypeCannotBeDeleted()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var clientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        await InsertClientAsync(database.ConnectionString, clientId, actorAccountId);
        await InsertMembershipTypeAsync(database.ConnectionString, membershipTypeId);
        await InsertIssuedMembershipAsync(
            database.ConnectionString,
            membershipId,
            clientId,
            membershipTypeId,
            actorAccountId);

        await EditMembershipTypeAsync(database.ConnectionString, membershipTypeId);

        var persisted = await ReadIssuedMembershipAsync(database.ConnectionString, membershipId);
        Assert.Equal("Slice 2 visits / 30 days", persisted.TypeNameSnapshot);
        Assert.Equal(30, persisted.DurationDaysSnapshot);
        Assert.Equal(2, persisted.VisitsLimitSnapshot);
        Assert.Equal(1000m, persisted.PriceAmountSnapshot);
        Assert.Equal("UAH", persisted.PriceCurrencySnapshot);
        Assert.Equal(new DateOnly(2026, 7, 30), persisted.BaseEndDate);

        await AssertForeignKeyViolationAsync(
            () => DeleteMembershipTypeAsync(database.ConnectionString, membershipTypeId),
            "FK_issued_memberships_membership_types_membership_type_id");
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
        Guid actorAccountId)
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
                'Ivanenko',
                'Ivan',
                null,
                'IVANENKO IVAN',
                null,
                null,
                null,
                null,
                'active',
                @created_at,
                @created_by_account_id,
                @updated_at)
            """;
        command.Parameters.AddWithValue("id", clientId);
        command.Parameters.AddWithValue("created_at", TestNow);
        command.Parameters.AddWithValue("created_by_account_id", actorAccountId);
        command.Parameters.AddWithValue("updated_at", TestNow);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertMembershipTypeAsync(
        string connectionString,
        Guid membershipTypeId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                @id,
                'Slice 2 visits / 30 days',
                30,
                2,
                1000,
                'UAH',
                true,
                null,
                @created_at,
                @updated_at,
                null)
            """;
        command.Parameters.AddWithValue("id", membershipTypeId);
        command.Parameters.AddWithValue("created_at", TestNow);
        command.Parameters.AddWithValue("updated_at", TestNow);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertIssuedMembershipAsync(
        string connectionString,
        Guid membershipId,
        Guid clientId,
        Guid membershipTypeId,
        Guid actorAccountId,
        string typeNameSnapshot = "Slice 2 visits / 30 days",
        int durationDaysSnapshot = 30,
        int visitsLimitSnapshot = 2,
        decimal priceAmountSnapshot = 1000m,
        string priceCurrencySnapshot = "UAH",
        DateOnly? startDate = null,
        DateOnly? baseEndDate = null,
        DateTimeOffset? issuedAt = null,
        string status = "active",
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        string? comment = null)
    {
        var actualStartDate = startDate ?? TestStartDate;
        var actualBaseEndDate = baseEndDate
            ?? actualStartDate.AddDays(durationDaysSnapshot - 1);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                @id,
                @client_id,
                @membership_type_id,
                @type_name_snapshot,
                @duration_days_snapshot,
                @visits_limit_snapshot,
                @price_amount_snapshot,
                @price_currency_snapshot,
                @start_date,
                @base_end_date,
                @issued_at,
                @issued_by_account_id,
                @status,
                @entry_origin,
                @entry_batch_id,
                @comment)
            """;
        command.Parameters.AddWithValue("id", membershipId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("type_name_snapshot", typeNameSnapshot);
        command.Parameters.AddWithValue("duration_days_snapshot", durationDaysSnapshot);
        command.Parameters.AddWithValue("visits_limit_snapshot", visitsLimitSnapshot);
        command.Parameters.AddWithValue("price_amount_snapshot", priceAmountSnapshot);
        command.Parameters.AddWithValue("price_currency_snapshot", priceCurrencySnapshot);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, actualStartDate);
        command.Parameters.AddWithValue("base_end_date", NpgsqlDbType.Date, actualBaseEndDate);
        command.Parameters.AddWithValue("issued_at", issuedAt ?? TestNow);
        command.Parameters.AddWithValue("issued_by_account_id", actorAccountId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value = comment ?? (object)DBNull.Value;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PersistedIssuedMembership> ReadIssuedMembershipAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
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
                comment
            from bodylife.issued_memberships
            where id = @id
            """;
        command.Parameters.AddWithValue("id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new PersistedIssuedMembership(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetDecimal(5),
            reader.GetString(6),
            reader.GetFieldValue<DateOnly>(7),
            reader.GetFieldValue<DateOnly>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetGuid(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetGuid(13),
            reader.IsDBNull(14) ? null : reader.GetString(14));
    }

    private static async Task EditMembershipTypeAsync(
        string connectionString,
        Guid membershipTypeId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_types
            set name = 'Future 12 visits / 60 days',
                duration_days = 60,
                visits_limit = 12,
                price_amount = 1800,
                price_currency = 'UAH',
                updated_at = @updated_at
            where id = @id
            """;
        command.Parameters.AddWithValue("id", membershipTypeId);
        command.Parameters.AddWithValue("updated_at", TestNow.AddMinutes(1));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeleteMembershipTypeAsync(
        string connectionString,
        Guid membershipTypeId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "delete from bodylife.membership_types where id = @id";
        command.Parameters.AddWithValue("id", membershipTypeId);
        await command.ExecuteNonQueryAsync();
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
        string constraintName)
    {
        return database.ExecuteScalarAsync<bool>(
            $"""
            select exists (
                select 1
                from information_schema.table_constraints
                where constraint_schema = 'bodylife'
                  and table_name = 'issued_memberships'
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
              and tablename = 'issued_memberships'
              and indexname = '{indexName}'
            """)
            ?? throw new InvalidOperationException($"Index '{indexName}' was not found.");
    }

    private sealed record PersistedIssuedMembership(
        Guid ClientId,
        Guid MembershipTypeId,
        string TypeNameSnapshot,
        int DurationDaysSnapshot,
        int VisitsLimitSnapshot,
        decimal PriceAmountSnapshot,
        string PriceCurrencySnapshot,
        DateOnly StartDate,
        DateOnly BaseEndDate,
        DateTimeOffset IssuedAt,
        Guid IssuedByAccountId,
        string Status,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
