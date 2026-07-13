using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMembershipAdjustmentsStorageTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        13,
        19,
        30,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly TestStartDate = new(2026, 7, 1);
    private static readonly DateOnly TestBaseEndDate = new(2026, 7, 30);
    private static readonly DateOnly TestEffectiveDate = new(2026, 7, 14);

    [PostgreSqlFact]
    public async Task MigrationCreatesSourceColumnsConstraintsAndIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        Assert.True(await TableExistsAsync(database));
        var expectedConstraints = new[]
        {
            "PK_membership_adjustments",
            "FK_membership_adjustments_accounts_recorded_by_account_id",
            "FK_membership_adjustments_issued_memberships_membership_id",
            "FK_membership_adjustments_sessions_recorded_session_id",
            "ck_membership_adjustments_adjustment_type_not_empty",
            "ck_membership_adjustments_delta_non_zero",
            "ck_membership_adjustments_entry_origin",
            "ck_membership_adjustments_reason_not_empty",
            "ck_membership_adjustments_status",
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
                "adjustment_type",
                "days_delta",
                "visits_delta",
                "money_delta",
                "effective_date",
                "reason",
                "recorded_at",
                "recorded_by_account_id",
                "recorded_session_id",
                "entry_origin",
                "entry_batch_id",
                "status",
            ],
            await ReadColumnNamesAsync(database));

        var activeIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_membership_adjustments_active_membership_effective_date");
        Assert.DoesNotContain("UNIQUE INDEX", activeIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "(membership_id, effective_date, adjustment_type)",
            activeIndex,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", activeIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status", activeIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active", activeIndex, StringComparison.OrdinalIgnoreCase);

        var timelineIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_membership_adjustments_membership_timeline");
        Assert.Contains(
            "(membership_id, effective_date DESC, recorded_at DESC)",
            timelineIndex,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "(recorded_by_account_id)",
            await ReadIndexDefinitionAsync(
                database,
                "ix_membership_adjustments_recorded_by_account_id"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "(recorded_session_id)",
            await ReadIndexDefinitionAsync(
                database,
                "ix_membership_adjustments_recorded_session_id"),
            StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    public async Task SourceFactsPreserveSignedDeltasAndAccountabilityMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedIssuedMembershipAsync(database, dbContext);
        var entryBatchId = Guid.NewGuid();

        await InsertAdjustmentAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId,
            "extension_days",
            daysDelta: 3,
            reason: "Honor three closure days from the paper register",
            entryOrigin: "paper_fallback",
            entryBatchId: entryBatchId,
            recordedAt: TestNow.AddMinutes(-2));
        await InsertAdjustmentAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId,
            "visit_balance",
            visitsDelta: -2,
            reason: "Correct two visits missing from pre-launch history",
            entryOrigin: "manual_backfill",
            entryBatchId: entryBatchId,
            recordedAt: TestNow.AddMinutes(-1));
        await InsertAdjustmentAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId,
            "money_correction",
            moneyDelta: -150.50m,
            reason: "Retain the signed owner-authorized balance correction",
            status: "corrected");

        var rows = await ReadAdjustmentsAsync(database.ConnectionString, fixture.MembershipId);

        Assert.Equal(3, rows.Count);
        Assert.Collection(
            rows,
            days =>
            {
                Assert.Equal("extension_days", days.AdjustmentType);
                Assert.Equal(3, days.DaysDelta);
                Assert.Null(days.VisitsDelta);
                Assert.Null(days.MoneyDelta);
                Assert.Equal("paper_fallback", days.EntryOrigin);
                Assert.Equal(entryBatchId, days.EntryBatchId);
                Assert.Equal("active", days.Status);
            },
            visits =>
            {
                Assert.Equal("visit_balance", visits.AdjustmentType);
                Assert.Null(visits.DaysDelta);
                Assert.Equal(-2, visits.VisitsDelta);
                Assert.Null(visits.MoneyDelta);
                Assert.Equal("manual_backfill", visits.EntryOrigin);
                Assert.Equal(entryBatchId, visits.EntryBatchId);
            },
            money =>
            {
                Assert.Equal("money_correction", money.AdjustmentType);
                Assert.Null(money.DaysDelta);
                Assert.Null(money.VisitsDelta);
                Assert.Equal(-150.50m, money.MoneyDelta);
                Assert.Equal("normal", money.EntryOrigin);
                Assert.Null(money.EntryBatchId);
                Assert.Equal("corrected", money.Status);
            });

        Assert.All(
            rows,
            row =>
            {
                Assert.Equal(fixture.MembershipId, row.MembershipId);
                Assert.Equal(TestEffectiveDate, row.EffectiveDate);
                Assert.False(string.IsNullOrWhiteSpace(row.Reason));
                Assert.Equal(fixture.ActorAccountId, row.RecordedByAccountId);
                Assert.Equal(fixture.SessionId, row.RecordedSessionId);
            });
    }

    [PostgreSqlFact]
    public async Task DeltaAndMetadataConstraintsRejectMeaninglessFacts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedIssuedMembershipAsync(database, dbContext);

        await AssertCheckViolationAsync(
            () => InsertAdjustmentAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                "visit_balance"),
            "ck_membership_adjustments_delta_non_zero");
        await AssertCheckViolationAsync(
            () => InsertAdjustmentAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                "visit_balance",
                daysDelta: 0,
                visitsDelta: 0,
                moneyDelta: 0m),
            "ck_membership_adjustments_delta_non_zero");
        await AssertCheckViolationAsync(
            () => InsertAdjustmentAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                "   ",
                visitsDelta: 1),
            "ck_membership_adjustments_adjustment_type_not_empty");
        await AssertCheckViolationAsync(
            () => InsertAdjustmentAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                "visit_balance",
                visitsDelta: 1,
                reason: "   "),
            "ck_membership_adjustments_reason_not_empty");
        await AssertCheckViolationAsync(
            () => InsertAdjustmentAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                "visit_balance",
                visitsDelta: 1,
                entryOrigin: "spreadsheet"),
            "ck_membership_adjustments_entry_origin");
        await AssertCheckViolationAsync(
            () => InsertAdjustmentAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                fixture.SessionId,
                "visit_balance",
                visitsDelta: 1,
                status: "deleted"),
            "ck_membership_adjustments_status");
    }

    [PostgreSqlFact]
    public async Task ActiveAndHistoricalAdjustmentsCanCoexistWithoutLosingFacts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedIssuedMembershipAsync(database, dbContext);
        var firstActiveId = Guid.NewGuid();

        await InsertAdjustmentAsync(
            database.ConnectionString,
            firstActiveId,
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId,
            "visit_balance",
            visitsDelta: -1);
        await InsertAdjustmentAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId,
            "visit_balance",
            visitsDelta: 1,
            status: "canceled");
        await InsertAdjustmentAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId,
            "visit_balance",
            visitsDelta: 2,
            status: "corrected");
        await InsertAdjustmentAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId,
            "visit_balance",
            visitsDelta: -3);

        Assert.Equal(
            4,
            await CountAdjustmentsAsync(
                database.ConnectionString,
                fixture.MembershipId,
                activeOnly: false));
        Assert.Equal(
            2,
            await CountAdjustmentsAsync(
                database.ConnectionString,
                fixture.MembershipId,
                activeOnly: true));

        await UpdateAdjustmentStatusAsync(
            database.ConnectionString,
            firstActiveId,
            "corrected");

        Assert.Equal(
            4,
            await CountAdjustmentsAsync(
                database.ConnectionString,
                fixture.MembershipId,
                activeOnly: false));
        Assert.Equal(
            1,
            await CountAdjustmentsAsync(
                database.ConnectionString,
                fixture.MembershipId,
                activeOnly: true));
    }

    [PostgreSqlFact]
    public async Task RelationshipsRejectUnknownReferencesAndProtectSourceHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedIssuedMembershipAsync(database, dbContext);

        await AssertForeignKeyViolationAsync(
            () => InsertAdjustmentAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                Guid.NewGuid(),
                fixture.ActorAccountId,
                fixture.SessionId,
                "visit_balance",
                visitsDelta: 1),
            "FK_membership_adjustments_issued_memberships_membership_id");
        await AssertForeignKeyViolationAsync(
            () => InsertAdjustmentAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                Guid.NewGuid(),
                fixture.SessionId,
                "visit_balance",
                visitsDelta: 1),
            "FK_membership_adjustments_accounts_recorded_by_account_id");
        await AssertForeignKeyViolationAsync(
            () => InsertAdjustmentAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ActorAccountId,
                Guid.NewGuid(),
                "visit_balance",
                visitsDelta: 1),
            "FK_membership_adjustments_sessions_recorded_session_id");

        await InsertAdjustmentAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            fixture.MembershipId,
            fixture.ActorAccountId,
            fixture.SessionId,
            "visit_balance",
            visitsDelta: 1);

        await AssertForeignKeyViolationAsync(
            () => DeleteIssuedMembershipAsync(
                database.ConnectionString,
                fixture.MembershipId),
            "FK_membership_adjustments_issued_memberships_membership_id");
        await AssertForeignKeyViolationAsync(
            () => DeleteSessionAsync(
                database.ConnectionString,
                fixture.SessionId),
            "FK_membership_adjustments_sessions_recorded_session_id");
    }

    private static async Task<AdjustmentFixture> SeedIssuedMembershipAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var bootstrap = await new OwnerBootstrapper(dbContext, new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var fixture = new AdjustmentFixture(
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
                'normal',
                null,
                'Created for membership-adjustment storage tests')
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

    private static async Task InsertAdjustmentAsync(
        string connectionString,
        Guid adjustmentId,
        Guid membershipId,
        Guid actorAccountId,
        Guid sessionId,
        string adjustmentType,
        int? daysDelta = null,
        int? visitsDelta = null,
        decimal? moneyDelta = null,
        DateOnly? effectiveDate = null,
        string reason = "Owner-authorized membership correction",
        DateTimeOffset? recordedAt = null,
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        string status = "active")
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.membership_adjustments (
                id,
                membership_id,
                adjustment_type,
                days_delta,
                visits_delta,
                money_delta,
                effective_date,
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
                @adjustment_type,
                @days_delta,
                @visits_delta,
                @money_delta,
                @effective_date,
                @reason,
                @recorded_at,
                @recorded_by_account_id,
                @recorded_session_id,
                @entry_origin,
                @entry_batch_id,
                @status)
            """;
        command.Parameters.AddWithValue("id", adjustmentId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("adjustment_type", adjustmentType);
        command.Parameters.Add("days_delta", NpgsqlDbType.Integer).Value =
            daysDelta ?? (object)DBNull.Value;
        command.Parameters.Add("visits_delta", NpgsqlDbType.Integer).Value =
            visitsDelta ?? (object)DBNull.Value;
        command.Parameters.Add("money_delta", NpgsqlDbType.Numeric).Value =
            moneyDelta ?? (object)DBNull.Value;
        command.Parameters.AddWithValue(
            "effective_date",
            NpgsqlDbType.Date,
            effectiveDate ?? TestEffectiveDate);
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

    private static async Task<IReadOnlyList<PersistedAdjustment>> ReadAdjustmentsAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                membership_id,
                adjustment_type,
                days_delta,
                visits_delta,
                money_delta,
                effective_date,
                reason,
                recorded_at,
                recorded_by_account_id,
                recorded_session_id,
                entry_origin,
                entry_batch_id,
                status
            from bodylife.membership_adjustments
            where membership_id = @membership_id
            order by recorded_at, id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        var adjustments = new List<PersistedAdjustment>();
        while (await reader.ReadAsync())
        {
            adjustments.Add(
                new PersistedAdjustment(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                    reader.GetFieldValue<DateOnly>(5),
                    reader.GetString(6),
                    reader.GetFieldValue<DateTimeOffset>(7),
                    reader.GetGuid(8),
                    reader.GetGuid(9),
                    reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetGuid(11),
                    reader.GetString(12)));
        }

        return adjustments;
    }

    private static async Task UpdateAdjustmentStatusAsync(
        string connectionString,
        Guid adjustmentId,
        string status)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_adjustments
            set status = @status
            where id = @id
            """;
        command.Parameters.AddWithValue("id", adjustmentId);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<int> CountAdjustmentsAsync(
        string connectionString,
        Guid membershipId,
        bool activeOnly)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = activeOnly
            ? "select count(*)::integer from bodylife.membership_adjustments where membership_id = @membership_id and status = 'active'"
            : "select count(*)::integer from bodylife.membership_adjustments where membership_id = @membership_id";
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

    private static Task<bool> TableExistsAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<bool>(
            """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'bodylife'
                  and table_name = 'membership_adjustments'
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
                  and table_name = 'membership_adjustments'
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
              and table_name = 'membership_adjustments'
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
              and tablename = 'membership_adjustments'
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

    private static async Task AssertForeignKeyViolationAsync(
        Func<Task> action,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal(constraintName, exception.ConstraintName);
    }

    private sealed record AdjustmentFixture(
        Guid ClientId,
        Guid MembershipTypeId,
        Guid MembershipId,
        Guid ActorAccountId,
        Guid SessionId);

    private sealed record PersistedAdjustment(
        Guid MembershipId,
        string AdjustmentType,
        int? DaysDelta,
        int? VisitsDelta,
        decimal? MoneyDelta,
        DateOnly EffectiveDate,
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
