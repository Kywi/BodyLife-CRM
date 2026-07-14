using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlFreezesStorageTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        14,
        17,
        30,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly MembershipStartDate = new(2026, 7, 1);
    private static readonly DateOnly MembershipBaseEndDate = new(2026, 7, 30);

    [PostgreSqlFact]
    public async Task MigrationCreatesFreezeSourceTablesConstraintsAndIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        Assert.Equal(
            [
                "id",
                "client_id",
                "membership_id",
                "start_date",
                "end_date",
                "reason",
                "occurred_at",
                "recorded_at",
                "recorded_by_account_id",
                "session_id",
                "entry_origin",
                "entry_batch_id",
                "status",
            ],
            await ReadColumnNamesAsync(database, "freezes"));
        Assert.Equal(
            [
                "id",
                "freeze_id",
                "reason",
                "occurred_at",
                "recorded_at",
                "recorded_by_account_id",
                "session_id",
                "entry_origin",
                "entry_batch_id",
            ],
            await ReadColumnNamesAsync(database, "freeze_cancellations"));

        string[] expectedConstraints =
        [
            "FK_freezes_issued_memberships_membership_client",
            "ck_freezes_entry_origin",
            "ck_freezes_inclusive_range",
            "ck_freezes_reason_not_empty",
            "ck_freezes_status",
            "ck_freeze_cancellations_entry_origin",
            "ck_freeze_cancellations_reason_not_empty",
        ];
        foreach (var constraint in expectedConstraints)
        {
            Assert.True(
                await ConstraintExistsAsync(database, constraint),
                $"Expected constraint '{constraint}' was not found.");
        }

        Assert.Contains(
            "(membership_id, status, start_date, end_date)",
            await ReadIndexDefinitionAsync(
                database,
                "ix_freezes_membership_status_range"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "UNIQUE INDEX",
            await ReadIndexDefinitionAsync(
                database,
                "ux_freeze_cancellations_freeze_id"),
            StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    public async Task CanonicalProjectionRetainsCanceledHistoryAndBlocksInclusiveActiveRange()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var entryBatchId = Guid.NewGuid();
        var canceledFreezeId = await InsertFreezeAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            new DateOnly(2026, 7, 10),
            new DateOnly(2026, 7, 12),
            entryOrigin: "paper_fallback",
            entryBatchId: entryBatchId);
        var cancellationId = await CancelFreezeAsync(
            database.ConnectionString,
            fixture,
            canceledFreezeId,
            entryOrigin: "manual_backfill",
            entryBatchId: entryBatchId);
        var activeFreezeId = await InsertFreezeAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            new DateOnly(2026, 7, 14),
            new DateOnly(2026, 7, 16));
        var reader = new MembershipVisitFreezeSourceReader(dbContext);

        await using (var projectionTransaction =
            await dbContext.Database.BeginTransactionAsync())
        {
            var canceledSources = await reader.GetForVisitAsync(
                fixture.MembershipId,
                new DateOnly(2026, 7, 11));
            var inclusiveEndSources = await reader.GetForVisitAsync(
                fixture.MembershipId,
                new DateOnly(2026, 7, 16));

            var canceled = Assert.Single(canceledSources);
            Assert.Equal(canceledFreezeId, canceled.FreezeId);
            Assert.False(canceled.IsActive);
            Assert.Equal(new DateOnly(2026, 7, 10), canceled.Range.StartDate);
            Assert.Equal(new DateOnly(2026, 7, 12), canceled.Range.EndDate);
            var active = Assert.Single(inclusiveEndSources);
            Assert.Equal(activeFreezeId, active.FreezeId);
            Assert.True(active.IsActive);

            await projectionTransaction.RollbackAsync();
        }

        await using (var eligibilityTransaction =
            await dbContext.Database.BeginTransactionAsync())
        {
            var preparer = new MembershipVisitEligibilityPreparer(
                dbContext,
                new MembershipStateCacheRebuilder(
                    dbContext,
                    new FixedTimeProvider(TestNow)),
                reader);
            var canceledRangeResult = await preparer.PrepareAsync(
                fixture.ClientId,
                fixture.MembershipId,
                new DateTimeOffset(2026, 7, 11, 9, 0, 0, TimeSpan.Zero));
            var activeRangeResult = await preparer.PrepareAsync(
                fixture.ClientId,
                fixture.MembershipId,
                new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.Zero));

            Assert.NotNull(canceledRangeResult.Eligibility);
            Assert.True(canceledRangeResult.Eligibility.IsEligible);
            Assert.NotNull(activeRangeResult.Eligibility);
            Assert.Equal(
                MembershipVisitEligibilityStatus.DuringActiveFreeze,
                activeRangeResult.Eligibility.Status);

            await eligibilityTransaction.RollbackAsync();
        }

        var cancellation = await ReadCancellationAsync(
            database.ConnectionString,
            cancellationId);
        Assert.Equal(canceledFreezeId, cancellation.FreezeId);
        Assert.Equal("Mistaken freeze range", cancellation.Reason);
        Assert.Equal("manual_backfill", cancellation.EntryOrigin);
        Assert.Equal(entryBatchId, cancellation.EntryBatchId);
        Assert.Equal("canceled", await ReadFreezeStatusAsync(
            database.ConnectionString,
            canceledFreezeId));
        Assert.Equal(2L, await CountRowsAsync(database, "freezes"));
        Assert.Equal(1L, await CountRowsAsync(database, "freeze_cancellations"));
    }

    [PostgreSqlFact]
    public async Task ConstraintsRejectInvalidMetadataAndCrossClientOwnership()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);

        await AssertPostgresViolationAsync(
            () => InsertFreezeAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                fixture.MembershipId,
                new DateOnly(2026, 7, 16),
                new DateOnly(2026, 7, 14)),
            PostgresErrorCodes.CheckViolation,
            "ck_freezes_inclusive_range");
        await AssertPostgresViolationAsync(
            () => InsertFreezeAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                fixture.MembershipId,
                new DateOnly(2026, 7, 14),
                new DateOnly(2026, 7, 16),
                reason: "   "),
            PostgresErrorCodes.CheckViolation,
            "ck_freezes_reason_not_empty");
        await AssertPostgresViolationAsync(
            () => InsertFreezeAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                fixture.MembershipId,
                new DateOnly(2026, 7, 14),
                new DateOnly(2026, 7, 16),
                entryOrigin: "spreadsheet"),
            PostgresErrorCodes.CheckViolation,
            "ck_freezes_entry_origin");
        await AssertPostgresViolationAsync(
            () => InsertFreezeAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                fixture.MembershipId,
                new DateOnly(2026, 7, 14),
                new DateOnly(2026, 7, 16),
                status: "deleted"),
            PostgresErrorCodes.CheckViolation,
            "ck_freezes_status");
        await AssertPostgresViolationAsync(
            () => InsertFreezeAsync(
                database.ConnectionString,
                fixture,
                fixture.OtherClientId,
                fixture.MembershipId,
                new DateOnly(2026, 7, 14),
                new DateOnly(2026, 7, 16)),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_freezes_issued_memberships_membership_client");

        var freezeId = await InsertFreezeAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            new DateOnly(2026, 7, 14),
            new DateOnly(2026, 7, 16));
        await AssertPostgresViolationAsync(
            () => InsertCancellationAsync(
                database.ConnectionString,
                fixture,
                freezeId,
                reason: "  "),
            PostgresErrorCodes.CheckViolation,
            "ck_freeze_cancellations_reason_not_empty");
        await AssertPostgresViolationAsync(
            () => InsertCancellationAsync(
                database.ConnectionString,
                fixture,
                freezeId,
                entryOrigin: "spreadsheet"),
            PostgresErrorCodes.CheckViolation,
            "ck_freeze_cancellations_entry_origin");
    }

    [PostgreSqlFact]
    public async Task CancellationIsUniqueAndSourceHistoryUsesRestrictiveDeletes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var freezeId = await InsertFreezeAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            new DateOnly(2026, 7, 14),
            new DateOnly(2026, 7, 16));
        await InsertCancellationAsync(
            database.ConnectionString,
            fixture,
            freezeId);

        await AssertPostgresViolationAsync(
            () => InsertCancellationAsync(
                database.ConnectionString,
                fixture,
                freezeId,
                recordedAt: TestNow.AddMinutes(2)),
            PostgresErrorCodes.UniqueViolation,
            "ux_freeze_cancellations_freeze_id");
        await AssertPostgresViolationAsync(
            () => DeleteByIdAsync(
                database.ConnectionString,
                "freezes",
                freezeId),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_freeze_cancellations_freezes_freeze_id");
        await AssertPostgresViolationAsync(
            () => DeleteByIdAsync(
                database.ConnectionString,
                "issued_memberships",
                fixture.MembershipId),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_freezes_issued_memberships_membership_client");
    }

    [PostgreSqlFact]
    public async Task ProjectionRequiresTransactionAndKeepsRelevantFreezeRowsLocked()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var freezeId = await InsertFreezeAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            new DateOnly(2026, 7, 14),
            new DateOnly(2026, 7, 16));
        var reader = new MembershipVisitFreezeSourceReader(dbContext);

        var missingTransaction = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reader.GetForVisitAsync(
                fixture.MembershipId,
                new DateOnly(2026, 7, 14)));
        Assert.Contains(
            "caller-owned",
            missingTransaction.Message,
            StringComparison.OrdinalIgnoreCase);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var result = await new MembershipVisitEligibilityPreparer(
            dbContext,
            new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(TestNow)),
            reader)
            .PrepareAsync(
                fixture.ClientId,
                fixture.MembershipId,
                new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.Zero));

        Assert.NotNull(result.Eligibility);
        Assert.Equal(
            MembershipVisitEligibilityStatus.DuringActiveFreeze,
            result.Eligibility.Status);
        var lockException = await AssertFreezeUpdateBlockedAsync(
            database.ConnectionString,
            freezeId);
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, lockException.SqlState);

        await transaction.RollbackAsync();
        await UpdateFreezeReasonAsync(database.ConnectionString, freezeId);
    }

    private static async Task<FreezeFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var bootstrap = await new OwnerBootstrapper(
            dbContext,
            new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var actorAccountId = bootstrap.AccountId!.Value;
        var sessionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var otherMembershipId = Guid.NewGuid();

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
            values
                (
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
                    @recorded_at),
                (
                    @other_client_id,
                    'Petrenko',
                    'Olena',
                    null,
                    'PETRENKO OLENA',
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
                'Freeze storage fixture',
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
            values
                (
                    @membership_id,
                    @client_id,
                    @membership_type_id,
                    'Freeze storage fixture',
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
                    null),
                (
                    @other_membership_id,
                    @other_client_id,
                    @membership_type_id,
                    'Freeze storage fixture',
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
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("actor_account_id", actorAccountId);
        command.Parameters.AddWithValue("session_started_at", TestNow.AddMinutes(-1));
        command.Parameters.AddWithValue("session_expires_at", TestNow.AddHours(12));
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("other_client_id", otherClientId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("other_membership_id", otherMembershipId);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            MembershipStartDate);
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            MembershipBaseEndDate);
        Assert.Equal(6, await command.ExecuteNonQueryAsync());

        return new FreezeFixture(
            actorAccountId,
            sessionId,
            clientId,
            otherClientId,
            membershipId);
    }

    private static async Task<Guid> InsertFreezeAsync(
        string connectionString,
        FreezeFixture fixture,
        Guid clientId,
        Guid membershipId,
        DateOnly startDate,
        DateOnly endDate,
        string reason = "Medical pause",
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        string status = "active")
    {
        var freezeId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.freezes (
                id,
                client_id,
                membership_id,
                start_date,
                end_date,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id,
                status)
            values (
                @id,
                @client_id,
                @membership_id,
                @start_date,
                @end_date,
                @reason,
                @occurred_at,
                @recorded_at,
                @recorded_by_account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id,
                @status)
            """;
        command.Parameters.AddWithValue("id", freezeId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, startDate);
        command.Parameters.AddWithValue("end_date", NpgsqlDbType.Date, endDate);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("occurred_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue(
            "recorded_by_account_id",
            fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return freezeId;
    }

    private static async Task<Guid> InsertCancellationAsync(
        string connectionString,
        FreezeFixture fixture,
        Guid freezeId,
        string reason = "Mistaken freeze range",
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        DateTimeOffset? recordedAt = null)
    {
        var cancellationId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = CreateCancellationCommand(
            connection,
            transaction: null,
            fixture,
            cancellationId,
            freezeId,
            reason,
            entryOrigin,
            entryBatchId,
            recordedAt ?? TestNow.AddMinutes(1));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return cancellationId;
    }

    private static async Task<Guid> CancelFreezeAsync(
        string connectionString,
        FreezeFixture fixture,
        Guid freezeId,
        string entryOrigin,
        Guid? entryBatchId)
    {
        var cancellationId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var cancellationCommand = CreateCancellationCommand(
            connection,
            transaction,
            fixture,
            cancellationId,
            freezeId,
            "Mistaken freeze range",
            entryOrigin,
            entryBatchId,
            TestNow.AddMinutes(1));
        Assert.Equal(1, await cancellationCommand.ExecuteNonQueryAsync());
        await using var statusCommand = connection.CreateCommand();
        statusCommand.Transaction = transaction;
        statusCommand.CommandText =
            "update bodylife.freezes set status = 'canceled' where id = @id";
        statusCommand.Parameters.AddWithValue("id", freezeId);
        Assert.Equal(1, await statusCommand.ExecuteNonQueryAsync());
        await transaction.CommitAsync();

        return cancellationId;
    }

    private static NpgsqlCommand CreateCancellationCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        FreezeFixture fixture,
        Guid cancellationId,
        Guid freezeId,
        string reason,
        string entryOrigin,
        Guid? entryBatchId,
        DateTimeOffset recordedAt)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into bodylife.freeze_cancellations (
                id,
                freeze_id,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id)
            values (
                @id,
                @freeze_id,
                @reason,
                @occurred_at,
                @recorded_at,
                @recorded_by_account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id)
            """;
        command.Parameters.AddWithValue("id", cancellationId);
        command.Parameters.AddWithValue("freeze_id", freezeId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("occurred_at", TestNow);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue(
            "recorded_by_account_id",
            fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        return command;
    }

    private static async Task<IReadOnlyList<string>> ReadColumnNamesAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select column_name
            from information_schema.columns
            where table_schema = 'bodylife'
                and table_name = @table_name
            order by ordinal_position
            """;
        command.Parameters.AddWithValue("table_name", tableName);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<bool> ConstraintExistsAsync(
        PostgreSqlTestDatabase database,
        string constraintName)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select exists (
                select 1
                from pg_constraint
                where conname = @constraint_name)
            """;
        command.Parameters.AddWithValue("constraint_name", constraintName);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadIndexDefinitionAsync(
        PostgreSqlTestDatabase database,
        string indexName)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select indexdef
            from pg_indexes
            where schemaname = 'bodylife'
                and indexname = @index_name
            """;
        command.Parameters.AddWithValue("index_name", indexName);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<FreezeCancellationSnapshot> ReadCancellationAsync(
        string connectionString,
        Guid cancellationId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select freeze_id, reason, entry_origin, entry_batch_id
            from bodylife.freeze_cancellations
            where id = @id
            """;
        command.Parameters.AddWithValue("id", cancellationId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new FreezeCancellationSnapshot(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3));
    }

    private static async Task<string> ReadFreezeStatusAsync(
        string connectionString,
        Guid freezeId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select status from bodylife.freezes where id = @id";
        command.Parameters.AddWithValue("id", freezeId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return (await database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{tableName}"))!;
    }

    private static async Task DeleteByIdAsync(
        string connectionString,
        string tableName,
        Guid id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"delete from bodylife.{tableName} where id = @id";
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PostgresException> AssertFreezeUpdateBlockedAsync(
        string connectionString,
        Guid freezeId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            set lock_timeout = '250ms';
            update bodylife.freezes
            set reason = 'Concurrent update'
            where id = @id
            """;
        command.Parameters.AddWithValue("id", freezeId);
        return await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
    }

    private static async Task UpdateFreezeReasonAsync(
        string connectionString,
        Guid freezeId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "update bodylife.freezes set reason = 'Unlocked update' where id = @id";
        command.Parameters.AddWithValue("id", freezeId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertPostgresViolationAsync(
        Func<Task> action,
        string sqlState,
        string? constraintName = null)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(sqlState, exception.SqlState);
        if (constraintName is not null)
        {
            Assert.Equal(constraintName, exception.ConstraintName);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record FreezeFixture(
        Guid ActorAccountId,
        Guid SessionId,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId);

    private sealed record FreezeCancellationSnapshot(
        Guid FreezeId,
        string Reason,
        string EntryOrigin,
        Guid? EntryBatchId);
}
