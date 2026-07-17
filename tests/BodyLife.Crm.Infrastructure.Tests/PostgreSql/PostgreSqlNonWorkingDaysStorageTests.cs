using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlNonWorkingDaysStorageTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        17,
        9,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly PeriodStartDate = new(2026, 7, 20);
    private static readonly DateOnly PeriodEndDate = new(2026, 7, 22);
    private static readonly DateOnly MembershipStartDate = new(2026, 7, 1);
    private static readonly DateOnly MembershipBaseEndDate = new(2026, 7, 30);

    [PostgreSqlFact]
    public async Task MigrationCreatesNonWorkingDaySourceTablesConstraintsAndIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        Assert.Equal(
            [
                "id",
                "start_date",
                "end_date",
                "reason_code",
                "reason_comment",
                "created_at",
                "created_by_account_id",
                "session_id",
                "status",
            ],
            await ReadColumnNamesAsync(database, "non_working_periods"));
        Assert.Equal(
            [
                "id",
                "non_working_period_id",
                "membership_id",
                "client_id",
                "applied_start_date",
                "applied_end_date",
                "previewed_at",
                "confirmed_at",
                "status",
            ],
            await ReadColumnNamesAsync(database, "non_working_period_applications"));
        Assert.Equal(
            [
                "id",
                "non_working_period_id",
                "reason",
                "recorded_at",
                "recorded_by_account_id",
                "session_id",
            ],
            await ReadColumnNamesAsync(database, "non_working_period_cancellations"));

        string[] expectedConstraints =
        [
            "AK_non_working_periods_id_range",
            "FK_non_working_period_applications_membership_client",
            "FK_non_working_period_applications_period_range",
            "FK_non_working_period_cancellations_period",
            "ck_non_working_period_applications_inclusive_range",
            "ck_non_working_period_applications_preview_order",
            "ck_non_working_period_applications_status",
            "ck_non_working_period_cancellations_reason_not_empty",
            "ck_non_working_periods_inclusive_range",
            "ck_non_working_periods_reason_code_not_empty",
            "ck_non_working_periods_reason_comment_not_empty",
            "ck_non_working_periods_status",
        ];
        foreach (var constraint in expectedConstraints)
        {
            Assert.True(
                await ConstraintExistsAsync(database, constraint),
                $"Expected constraint '{constraint}' was not found.");
        }

        Assert.Contains(
            "(status, start_date, end_date)",
            await ReadIndexDefinitionAsync(
                database,
                "ix_non_working_periods_status_range"),
            StringComparison.OrdinalIgnoreCase);

        var activeApplicationIndex = await ReadIndexDefinitionAsync(
            database,
            "ux_non_working_applications_active_period_membership");
        Assert.Contains("UNIQUE INDEX", activeApplicationIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "(non_working_period_id, membership_id)",
            activeApplicationIndex,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", activeApplicationIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active", activeApplicationIndex, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "UNIQUE INDEX",
            await ReadIndexDefinitionAsync(
                database,
                "ux_non_working_period_cancellations_period_id"),
            StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    public async Task ApplicationsRequireFullPeriodRangeAndMatchingMembershipClient()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);

        await AssertPostgresViolationAsync(
            () => InsertPeriodAsync(
                database.ConnectionString,
                fixture,
                PeriodEndDate,
                PeriodStartDate),
            PostgresErrorCodes.CheckViolation,
            "ck_non_working_periods_inclusive_range");
        await AssertPostgresViolationAsync(
            () => InsertPeriodAsync(
                database.ConnectionString,
                fixture,
                PeriodStartDate,
                PeriodEndDate,
                reasonCode: "   "),
            PostgresErrorCodes.CheckViolation,
            "ck_non_working_periods_reason_code_not_empty");
        await AssertPostgresViolationAsync(
            () => InsertPeriodAsync(
                database.ConnectionString,
                fixture,
                PeriodStartDate,
                PeriodEndDate,
                reasonComment: "  "),
            PostgresErrorCodes.CheckViolation,
            "ck_non_working_periods_reason_comment_not_empty");

        var periodId = await InsertPeriodAsync(
            database.ConnectionString,
            fixture,
            PeriodStartDate,
            PeriodEndDate);

        await AssertPostgresViolationAsync(
            () => InsertApplicationAsync(
                database.ConnectionString,
                fixture,
                periodId,
                fixture.ClientId,
                PeriodStartDate.AddDays(1),
                PeriodEndDate),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_non_working_period_applications_period_range");
        await AssertPostgresViolationAsync(
            () => InsertApplicationAsync(
                database.ConnectionString,
                fixture,
                periodId,
                fixture.OtherClientId,
                PeriodStartDate,
                PeriodEndDate),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_non_working_period_applications_membership_client");

        var applicationId = await InsertApplicationAsync(
            database.ConnectionString,
            fixture,
            periodId,
            fixture.ClientId,
            PeriodStartDate,
            PeriodEndDate);
        var application = await ReadApplicationAsync(
            database.ConnectionString,
            applicationId);

        Assert.Equal(periodId, application.NonWorkingPeriodId);
        Assert.Equal(fixture.MembershipId, application.MembershipId);
        Assert.Equal(fixture.ClientId, application.ClientId);
        Assert.Equal(PeriodStartDate, application.AppliedStartDate);
        Assert.Equal(PeriodEndDate, application.AppliedEndDate);
        Assert.Equal("active", application.Status);
    }

    [PostgreSqlFact]
    public async Task LifecycleChecksRetainInactiveApplicationsAndAllowOnlyOneActiveRow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var periodId = await InsertPeriodAsync(
            database.ConnectionString,
            fixture,
            PeriodStartDate,
            PeriodEndDate);

        await InsertApplicationAsync(
            database.ConnectionString,
            fixture,
            periodId,
            fixture.ClientId,
            PeriodStartDate,
            PeriodEndDate);
        await AssertPostgresViolationAsync(
            () => InsertApplicationAsync(
                database.ConnectionString,
                fixture,
                periodId,
                fixture.ClientId,
                PeriodStartDate,
                PeriodEndDate,
                confirmedAt: TestNow.AddMinutes(3)),
            PostgresErrorCodes.UniqueViolation,
            "ux_non_working_applications_active_period_membership");

        await InsertApplicationAsync(
            database.ConnectionString,
            fixture,
            periodId,
            fixture.ClientId,
            PeriodStartDate,
            PeriodEndDate,
            status: "canceled",
            confirmedAt: TestNow.AddMinutes(4));
        await InsertApplicationAsync(
            database.ConnectionString,
            fixture,
            periodId,
            fixture.ClientId,
            PeriodStartDate,
            PeriodEndDate,
            status: "corrected",
            confirmedAt: TestNow.AddMinutes(5));

        await AssertPostgresViolationAsync(
            () => InsertApplicationAsync(
                database.ConnectionString,
                fixture,
                periodId,
                fixture.ClientId,
                PeriodStartDate,
                PeriodEndDate,
                status: "deleted",
                confirmedAt: TestNow.AddMinutes(6)),
            PostgresErrorCodes.CheckViolation,
            "ck_non_working_period_applications_status");
        await AssertPostgresViolationAsync(
            () => InsertApplicationAsync(
                database.ConnectionString,
                fixture,
                periodId,
                fixture.ClientId,
                PeriodStartDate,
                PeriodEndDate,
                status: "canceled",
                previewedAt: TestNow.AddMinutes(8),
                confirmedAt: TestNow.AddMinutes(7)),
            PostgresErrorCodes.CheckViolation,
            "ck_non_working_period_applications_preview_order");
        await AssertPostgresViolationAsync(
            () => InsertPeriodAsync(
                database.ConnectionString,
                fixture,
                PeriodStartDate.AddDays(5),
                PeriodEndDate.AddDays(5),
                status: "deleted"),
            PostgresErrorCodes.CheckViolation,
            "ck_non_working_periods_status");

        Assert.Equal(
            3L,
            await CountRowsAsync(database, "non_working_period_applications"));
    }

    [PostgreSqlFact]
    public async Task CancellationIsUniqueAndRetainsPeriodAndApplicationHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var periodId = await InsertPeriodAsync(
            database.ConnectionString,
            fixture,
            PeriodStartDate,
            PeriodEndDate);
        var applicationId = await InsertApplicationAsync(
            database.ConnectionString,
            fixture,
            periodId,
            fixture.ClientId,
            PeriodStartDate,
            PeriodEndDate);

        await AssertPostgresViolationAsync(
            () => InsertCancellationAsync(
                database.ConnectionString,
                fixture,
                periodId,
                reason: "  "),
            PostgresErrorCodes.CheckViolation,
            "ck_non_working_period_cancellations_reason_not_empty");

        await CancelPeriodAsync(
            database.ConnectionString,
            fixture,
            periodId);

        await AssertPostgresViolationAsync(
            () => InsertCancellationAsync(
                database.ConnectionString,
                fixture,
                periodId,
                recordedAt: TestNow.AddMinutes(3)),
            PostgresErrorCodes.UniqueViolation,
            "ux_non_working_period_cancellations_period_id");
        await AssertPostgresViolationAsync(
            () => DeletePeriodAsync(database.ConnectionString, periodId),
            PostgresErrorCodes.ForeignKeyViolation);

        Assert.Equal(
            "canceled",
            await ReadStatusAsync(
                database.ConnectionString,
                "non_working_periods",
                periodId));
        Assert.Equal(
            "canceled",
            await ReadStatusAsync(
                database.ConnectionString,
                "non_working_period_applications",
                applicationId));
        Assert.Equal(
            "Unexpected closure entry",
            await ReadCancellationReasonAsync(database.ConnectionString, periodId));
        Assert.Equal(1L, await CountRowsAsync(database, "non_working_periods"));
        Assert.Equal(
            1L,
            await CountRowsAsync(database, "non_working_period_applications"));
        Assert.Equal(
            1L,
            await CountRowsAsync(database, "non_working_period_cancellations"));
    }

    private static async Task<NonWorkingDayFixture> SeedFixtureAsync(
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
                'Owner laptop',
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
                'Non-working day storage fixture',
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
                'Non-working day storage fixture',
                30,
                8,
                1000,
                'UAH',
                @membership_start_date,
                @membership_base_end_date,
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
        command.Parameters.AddWithValue(
            "membership_start_date",
            NpgsqlDbType.Date,
            MembershipStartDate);
        command.Parameters.AddWithValue(
            "membership_base_end_date",
            NpgsqlDbType.Date,
            MembershipBaseEndDate);
        Assert.Equal(5, await command.ExecuteNonQueryAsync());

        return new NonWorkingDayFixture(
            actorAccountId,
            sessionId,
            clientId,
            otherClientId,
            membershipId);
    }

    private static async Task<Guid> InsertPeriodAsync(
        string connectionString,
        NonWorkingDayFixture fixture,
        DateOnly startDate,
        DateOnly endDate,
        string reasonCode = "technical_day",
        string? reasonComment = "Scheduled maintenance",
        string status = "active")
    {
        var periodId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.non_working_periods (
                id,
                start_date,
                end_date,
                reason_code,
                reason_comment,
                created_at,
                created_by_account_id,
                session_id,
                status)
            values (
                @id,
                @start_date,
                @end_date,
                @reason_code,
                @reason_comment,
                @created_at,
                @created_by_account_id,
                @session_id,
                @status)
            """;
        command.Parameters.AddWithValue("id", periodId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, startDate);
        command.Parameters.AddWithValue("end_date", NpgsqlDbType.Date, endDate);
        command.Parameters.AddWithValue("reason_code", reasonCode);
        command.Parameters.Add("reason_comment", NpgsqlDbType.Varchar).Value =
            reasonComment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("created_at", TestNow);
        command.Parameters.AddWithValue(
            "created_by_account_id",
            fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return periodId;
    }

    private static async Task<Guid> InsertApplicationAsync(
        string connectionString,
        NonWorkingDayFixture fixture,
        Guid periodId,
        Guid clientId,
        DateOnly appliedStartDate,
        DateOnly appliedEndDate,
        string status = "active",
        DateTimeOffset? previewedAt = null,
        DateTimeOffset? confirmedAt = null)
    {
        var applicationId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.non_working_period_applications (
                id,
                non_working_period_id,
                membership_id,
                client_id,
                applied_start_date,
                applied_end_date,
                previewed_at,
                confirmed_at,
                status)
            values (
                @id,
                @non_working_period_id,
                @membership_id,
                @client_id,
                @applied_start_date,
                @applied_end_date,
                @previewed_at,
                @confirmed_at,
                @status)
            """;
        command.Parameters.AddWithValue("id", applicationId);
        command.Parameters.AddWithValue("non_working_period_id", periodId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue(
            "applied_start_date",
            NpgsqlDbType.Date,
            appliedStartDate);
        command.Parameters.AddWithValue(
            "applied_end_date",
            NpgsqlDbType.Date,
            appliedEndDate);
        command.Parameters.AddWithValue(
            "previewed_at",
            previewedAt ?? TestNow.AddMinutes(1));
        command.Parameters.AddWithValue(
            "confirmed_at",
            confirmedAt ?? TestNow.AddMinutes(2));
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return applicationId;
    }

    private static async Task<Guid> InsertCancellationAsync(
        string connectionString,
        NonWorkingDayFixture fixture,
        Guid periodId,
        string reason = "Unexpected closure entry",
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
            periodId,
            reason,
            recordedAt ?? TestNow.AddMinutes(2));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return cancellationId;
    }

    private static async Task CancelPeriodAsync(
        string connectionString,
        NonWorkingDayFixture fixture,
        Guid periodId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var applicationCommand = connection.CreateCommand())
        {
            applicationCommand.Transaction = transaction;
            applicationCommand.CommandText =
                """
                update bodylife.non_working_period_applications
                set status = 'canceled'
                where non_working_period_id = @period_id
                    and status = 'active'
                """;
            applicationCommand.Parameters.AddWithValue("period_id", periodId);
            Assert.Equal(1, await applicationCommand.ExecuteNonQueryAsync());
        }

        await using (var periodCommand = connection.CreateCommand())
        {
            periodCommand.Transaction = transaction;
            periodCommand.CommandText =
                """
                update bodylife.non_working_periods
                set status = 'canceled'
                where id = @period_id
                    and status = 'active'
                """;
            periodCommand.Parameters.AddWithValue("period_id", periodId);
            Assert.Equal(1, await periodCommand.ExecuteNonQueryAsync());
        }

        await using var cancellationCommand = CreateCancellationCommand(
            connection,
            transaction,
            fixture,
            Guid.NewGuid(),
            periodId,
            "Unexpected closure entry",
            TestNow.AddMinutes(2));
        Assert.Equal(1, await cancellationCommand.ExecuteNonQueryAsync());

        await transaction.CommitAsync();
    }

    private static NpgsqlCommand CreateCancellationCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        NonWorkingDayFixture fixture,
        Guid cancellationId,
        Guid periodId,
        string reason,
        DateTimeOffset recordedAt)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into bodylife.non_working_period_cancellations (
                id,
                non_working_period_id,
                reason,
                recorded_at,
                recorded_by_account_id,
                session_id)
            values (
                @id,
                @non_working_period_id,
                @reason,
                @recorded_at,
                @recorded_by_account_id,
                @session_id)
            """;
        command.Parameters.AddWithValue("id", cancellationId);
        command.Parameters.AddWithValue("non_working_period_id", periodId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue(
            "recorded_by_account_id",
            fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        return command;
    }

    private static async Task<ApplicationSnapshot> ReadApplicationAsync(
        string connectionString,
        Guid applicationId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                non_working_period_id,
                membership_id,
                client_id,
                applied_start_date,
                applied_end_date,
                status
            from bodylife.non_working_period_applications
            where id = @id
            """;
        command.Parameters.AddWithValue("id", applicationId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new ApplicationSnapshot(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetFieldValue<DateOnly>(3),
            reader.GetFieldValue<DateOnly>(4),
            reader.GetString(5));
    }

    private static async Task<string> ReadStatusAsync(
        string connectionString,
        string tableName,
        Guid id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"select status from bodylife.{tableName} where id = @id";
        command.Parameters.AddWithValue("id", id);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadCancellationReasonAsync(
        string connectionString,
        Guid periodId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select reason
            from bodylife.non_working_period_cancellations
            where non_working_period_id = @period_id
            """;
        command.Parameters.AddWithValue("period_id", periodId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task DeletePeriodAsync(
        string connectionString,
        Guid periodId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "delete from bodylife.non_working_periods where id = @id";
        command.Parameters.AddWithValue("id", periodId);
        await command.ExecuteNonQueryAsync();
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

    private static async Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return (await database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{tableName}"))!;
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

    private sealed record NonWorkingDayFixture(
        Guid ActorAccountId,
        Guid SessionId,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId);

    private sealed record ApplicationSnapshot(
        Guid NonWorkingPeriodId,
        Guid MembershipId,
        Guid ClientId,
        DateOnly AppliedStartDate,
        DateOnly AppliedEndDate,
        string Status);
}
