using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMembershipOpeningStatesStorageTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 13, 11, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly TestStartDate = new(2026, 7, 1);
    private static readonly DateOnly TestBaseEndDate = new(2026, 7, 30);
    private static readonly DateOnly TestOpeningAsOfDate = new(2026, 7, 13);
    private static readonly DateOnly TestKnownEffectiveEndDate = new(2026, 8, 3);

    [PostgreSqlFact]
    public async Task MigrationCreatesCanonicalSourceColumnsConstraintsAndIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        Assert.True(await TableExistsAsync(database, "membership_opening_states"));

        var expectedConstraints = new[]
        {
            "PK_membership_opening_states",
            "FK_membership_opening_states_accounts_recorded_by_account_id",
            "FK_membership_opening_states_issued_memberships_membership_id",
            "FK_membership_opening_states_sessions_recorded_session_id",
            "ck_membership_opening_states_entry_origin",
            "ck_membership_opening_states_known_end_not_before_opening",
            "ck_membership_opening_states_known_extension_days_non_negative",
            "ck_membership_opening_states_negative_balance_consistent",
            "ck_membership_opening_states_reason_not_empty",
            "ck_membership_opening_states_source_reference_not_empty",
            "ck_membership_opening_states_status",
        };

        foreach (var constraint in expectedConstraints)
        {
            Assert.True(
                await ConstraintExistsAsync(database, constraint),
                $"Expected constraint '{constraint}' was not found.");
        }

        var columns = await ReadColumnNamesAsync(database);
        Assert.Equal(
            [
                "id",
                "membership_id",
                "opening_as_of_date",
                "declared_remaining_visits",
                "declared_negative_balance",
                "known_effective_end_date",
                "known_extension_days",
                "source_reference",
                "reason",
                "recorded_at",
                "recorded_by_account_id",
                "recorded_session_id",
                "entry_origin",
                "entry_batch_id",
                "status",
            ],
            columns);

        var activeIndex = await ReadIndexDefinitionAsync(
            database,
            "ux_membership_opening_states_active_membership");
        Assert.Contains("UNIQUE INDEX", activeIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(membership_id)", activeIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", activeIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status", activeIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active", activeIndex, StringComparison.OrdinalIgnoreCase);

        var timelineIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_membership_opening_states_membership_timeline");
        Assert.Contains(
            "(membership_id, opening_as_of_date DESC, recorded_at DESC)",
            timelineIndex,
            StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    public async Task SourceFactStoresDeclaredStateAndBackfillAccountabilityMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedIssuedMembershipAsync(database, dbContext);
        var openingStateId = Guid.NewGuid();
        var entryBatchId = Guid.NewGuid();

        await InsertOpeningStateAsync(
            database.ConnectionString,
            openingStateId,
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId,
            declaredRemainingVisits: -2,
            declaredNegativeBalance: 2,
            knownEffectiveEndDate: TestKnownEffectiveEndDate,
            knownExtensionDays: 4,
            sourceReference: "Paper register 2026, page 12",
            reason: "Active membership history before launch is incomplete",
            entryBatchId: entryBatchId);

        var persisted = await ReadOpeningStateAsync(database.ConnectionString, openingStateId);

        Assert.Equal(fixture.MembershipId, persisted.MembershipId);
        Assert.Equal(TestOpeningAsOfDate, persisted.OpeningAsOfDate);
        Assert.Equal(-2, persisted.DeclaredRemainingVisits);
        Assert.Equal(2, persisted.DeclaredNegativeBalance);
        Assert.Equal(TestKnownEffectiveEndDate, persisted.KnownEffectiveEndDate);
        Assert.Equal(4, persisted.KnownExtensionDays);
        Assert.Equal("Paper register 2026, page 12", persisted.SourceReference);
        Assert.Equal("Active membership history before launch is incomplete", persisted.Reason);
        Assert.Equal(TestNow, persisted.RecordedAt);
        Assert.Equal(fixture.ActorAccountId, persisted.RecordedByAccountId);
        Assert.Equal(fixture.SessionId, persisted.RecordedSessionId);
        Assert.Equal("manual_backfill", persisted.EntryOrigin);
        Assert.Equal(entryBatchId, persisted.EntryBatchId);
        Assert.Equal("active", persisted.Status);
    }

    [PostgreSqlFact]
    public async Task KnownEndExtensionAndBatchMetadataRemainOptionalWhenUnknown()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedIssuedMembershipAsync(database, dbContext);
        var openingStateId = Guid.NewGuid();

        await InsertOpeningStateAsync(
            database.ConnectionString,
            openingStateId,
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId,
            declaredRemainingVisits: 1,
            declaredNegativeBalance: 0,
            knownEffectiveEndDate: null,
            knownExtensionDays: null,
            sourceReference: "Outage log, page 3",
            reason: "Only the current remaining visits are known",
            entryOrigin: "paper_fallback",
            entryBatchId: null);

        var persisted = await ReadOpeningStateAsync(database.ConnectionString, openingStateId);
        Assert.Null(persisted.KnownEffectiveEndDate);
        Assert.Null(persisted.KnownExtensionDays);
        Assert.Null(persisted.EntryBatchId);
        Assert.Equal("paper_fallback", persisted.EntryOrigin);
    }

    [PostgreSqlFact]
    public async Task ValueAndMetadataConstraintsRejectDishonestOpeningFacts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedIssuedMembershipAsync(database, dbContext);

        await AssertCheckViolationAsync(
            () => InsertOpeningStateAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                declaredRemainingVisits: -1,
                declaredNegativeBalance: 0),
            "ck_membership_opening_states_negative_balance_consistent");
        await AssertCheckViolationAsync(
            () => InsertOpeningStateAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                knownExtensionDays: -1),
            "ck_membership_opening_states_known_extension_days_non_negative");
        await AssertCheckViolationAsync(
            () => InsertOpeningStateAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                knownEffectiveEndDate: TestOpeningAsOfDate.AddDays(-1)),
            "ck_membership_opening_states_known_end_not_before_opening");
        await AssertCheckViolationAsync(
            () => InsertOpeningStateAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                sourceReference: "   "),
            "ck_membership_opening_states_source_reference_not_empty");
        await AssertCheckViolationAsync(
            () => InsertOpeningStateAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                reason: "   "),
            "ck_membership_opening_states_reason_not_empty");
        await AssertCheckViolationAsync(
            () => InsertOpeningStateAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                entryOrigin: "normal"),
            "ck_membership_opening_states_entry_origin");
        await AssertCheckViolationAsync(
            () => InsertOpeningStateAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                status: "deleted"),
            "ck_membership_opening_states_status");
    }

    [PostgreSqlFact]
    public async Task PartialUniqueIndexAllowsHistoryButOnlyOneActiveState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedIssuedMembershipAsync(database, dbContext);
        var originalActiveId = Guid.NewGuid();

        await InsertOpeningStateAsync(
            database.ConnectionString,
            originalActiveId,
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId);
        await AssertUniqueViolationAsync(
            () => InsertOpeningStateAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId),
            "ux_membership_opening_states_active_membership");

        await InsertOpeningStateAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId,
            status: "canceled");
        await UpdateOpeningStateStatusAsync(
            database.ConnectionString,
            originalActiveId,
            "corrected");
        await InsertOpeningStateAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId);

        Assert.Equal(
            1,
            await CountOpeningStatesAsync(
                database.ConnectionString,
                fixture.MembershipId,
                activeOnly: true));
        Assert.Equal(
            3,
            await CountOpeningStatesAsync(
                database.ConnectionString,
                fixture.MembershipId,
                activeOnly: false));
    }

    [PostgreSqlFact]
    public async Task RelationshipsRejectUnknownReferencesAndProtectSourceHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedIssuedMembershipAsync(database, dbContext);

        await AssertForeignKeyViolationAsync(
            () => InsertOpeningStateAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                Guid.NewGuid(),
                fixture.ActorAccountId,
                fixture.SessionId),
            "FK_membership_opening_states_issued_memberships_membership_id");
        await AssertForeignKeyViolationAsync(
            () => InsertOpeningStateAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                Guid.NewGuid(),
                fixture.SessionId),
            "FK_membership_opening_states_accounts_recorded_by_account_id");
        await AssertForeignKeyViolationAsync(
            () => InsertOpeningStateAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                Guid.NewGuid()),
            "FK_membership_opening_states_sessions_recorded_session_id");

        await InsertOpeningStateAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId);

        await AssertForeignKeyViolationAsync(
            () => DeleteIssuedMembershipAsync(
                database.ConnectionString,
                fixture.MembershipId),
            "FK_membership_opening_states_issued_memberships_membership_id");
        await AssertForeignKeyViolationAsync(
            () => DeleteSessionAsync(
                database.ConnectionString,
                fixture.SessionId),
            "FK_membership_opening_states_sessions_recorded_session_id");
    }

    private static async Task<OpeningStateFixture> SeedIssuedMembershipAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var bootstrap = await new OwnerBootstrapper(dbContext, new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var fixture = new OpeningStateFixture(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            bootstrap.AccountId!.Value,
            Guid.NewGuid());

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.sessions (
                id,
                account_id,
                device_label,
                started_at,
                expires_at,
                ended_at,
                last_seen_at)
            values (
                @session_id,
                @actor_account_id,
                'Reception tablet',
                @session_started_at,
                @session_expires_at,
                null,
                @recorded_at);

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
                'manual_backfill',
                null,
                'Created for opening-state storage tests')
            """;
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("actor_account_id", fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("session_expires_at", TestNow.AddHours(11));
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, TestStartDate);
        command.Parameters.AddWithValue("base_end_date", NpgsqlDbType.Date, TestBaseEndDate);
        Assert.Equal(4, await command.ExecuteNonQueryAsync());

        return fixture;
    }

    private static async Task InsertOpeningStateAsync(
        string connectionString,
        Guid openingStateId,
        Guid membershipId,
        Guid actorAccountId,
        Guid sessionId,
        DateOnly? openingAsOfDate = null,
        int declaredRemainingVisits = 2,
        int declaredNegativeBalance = 0,
        DateOnly? knownEffectiveEndDate = null,
        int? knownExtensionDays = null,
        string sourceReference = "Paper register 2026, page 12",
        string reason = "Active membership history before launch is incomplete",
        DateTimeOffset? recordedAt = null,
        string entryOrigin = "manual_backfill",
        Guid? entryBatchId = null,
        string status = "active")
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.membership_opening_states (
                id,
                membership_id,
                opening_as_of_date,
                declared_remaining_visits,
                declared_negative_balance,
                known_effective_end_date,
                known_extension_days,
                source_reference,
                reason,
                recorded_at,
                recorded_by_account_id,
                recorded_session_id,
                entry_origin,
                entry_batch_id,
                status)
            values (
                @id,
                @membership_id,
                @opening_as_of_date,
                @declared_remaining_visits,
                @declared_negative_balance,
                @known_effective_end_date,
                @known_extension_days,
                @source_reference,
                @reason,
                @recorded_at,
                @recorded_by_account_id,
                @recorded_session_id,
                @entry_origin,
                @entry_batch_id,
                @status)
            """;
        command.Parameters.AddWithValue("id", openingStateId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue(
            "opening_as_of_date",
            NpgsqlDbType.Date,
            openingAsOfDate ?? TestOpeningAsOfDate);
        command.Parameters.AddWithValue("declared_remaining_visits", declaredRemainingVisits);
        command.Parameters.AddWithValue("declared_negative_balance", declaredNegativeBalance);
        command.Parameters.Add("known_effective_end_date", NpgsqlDbType.Date).Value =
            knownEffectiveEndDate ?? (object)DBNull.Value;
        command.Parameters.Add("known_extension_days", NpgsqlDbType.Integer).Value =
            knownExtensionDays ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("source_reference", sourceReference);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("recorded_at", recordedAt ?? TestNow);
        command.Parameters.AddWithValue("recorded_by_account_id", actorAccountId);
        command.Parameters.AddWithValue("recorded_session_id", sessionId);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PersistedOpeningState> ReadOpeningStateAsync(
        string connectionString,
        Guid openingStateId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                membership_id,
                opening_as_of_date,
                declared_remaining_visits,
                declared_negative_balance,
                known_effective_end_date,
                known_extension_days,
                source_reference,
                reason,
                recorded_at,
                recorded_by_account_id,
                recorded_session_id,
                entry_origin,
                entry_batch_id,
                status
            from bodylife.membership_opening_states
            where id = @id
            """;
        command.Parameters.AddWithValue("id", openingStateId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new PersistedOpeningState(
            reader.GetGuid(0),
            reader.GetFieldValue<DateOnly>(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetGuid(9),
            reader.GetGuid(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetGuid(12),
            reader.GetString(13));
    }

    private static async Task UpdateOpeningStateStatusAsync(
        string connectionString,
        Guid openingStateId,
        string status)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_opening_states
            set status = @status
            where id = @id
            """;
        command.Parameters.AddWithValue("id", openingStateId);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<int> CountOpeningStatesAsync(
        string connectionString,
        Guid membershipId,
        bool activeOnly)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = activeOnly
            ? "select count(*)::integer from bodylife.membership_opening_states where membership_id = @membership_id and status = 'active'"
            : "select count(*)::integer from bodylife.membership_opening_states where membership_id = @membership_id";
        command.Parameters.AddWithValue("membership_id", membershipId);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task DeleteIssuedMembershipAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "delete from bodylife.issued_memberships where id = @id";
        command.Parameters.AddWithValue("id", membershipId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteSessionAsync(
        string connectionString,
        Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "delete from bodylife.sessions where id = @id";
        command.Parameters.AddWithValue("id", sessionId);
        await command.ExecuteNonQueryAsync();
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
                  and table_name = 'membership_opening_states'
                  and constraint_name = '{constraintName}'
            )
            """);
    }

    private static async Task<string[]> ReadColumnNamesAsync(PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select column_name
            from information_schema.columns
            where table_schema = 'bodylife'
              and table_name = 'membership_opening_states'
            order by ordinal_position
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return [.. columns];
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
              and tablename = 'membership_opening_states'
              and indexname = '{indexName}'
            """)
            ?? throw new InvalidOperationException($"Index '{indexName}' was not found.");
    }

    private static async Task AssertCheckViolationAsync(
        Func<Task> action,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
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

    private static async Task AssertForeignKeyViolationAsync(
        Func<Task> action,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal(constraintName, exception.ConstraintName);
    }

    private sealed record OpeningStateFixture(
        Guid ClientId,
        Guid MembershipTypeId,
        Guid MembershipId,
        Guid ActorAccountId,
        Guid SessionId);

    private sealed record PersistedOpeningState(
        Guid MembershipId,
        DateOnly OpeningAsOfDate,
        int DeclaredRemainingVisits,
        int DeclaredNegativeBalance,
        DateOnly? KnownEffectiveEndDate,
        int? KnownExtensionDays,
        string SourceReference,
        string Reason,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid RecordedSessionId,
        string EntryOrigin,
        Guid? EntryBatchId,
        string Status);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
