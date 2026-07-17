using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlNonWorkingDayExtensionSourceTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        17,
        11,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly MembershipStartDate = new(2026, 7, 1);
    private static readonly DateOnly MembershipBaseEndDate = new(2026, 7, 30);

    [PostgreSqlFact]
    public async Task ReaderMapsRetainedRangesAndLocksCanonicalSources()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var active = await InsertNonWorkingSourceAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var canceledPeriod = await InsertNonWorkingSourceAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 21), new DateOnly(2026, 7, 21)),
            periodStatus: "canceled");
        var correctedPeriod = await InsertNonWorkingSourceAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 22), new DateOnly(2026, 7, 22)),
            periodStatus: "corrected");
        var canceledApplication = await InsertNonWorkingSourceAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 23), new DateOnly(2026, 7, 23)),
            applicationStatus: "canceled");
        var correctedApplication = await InsertNonWorkingSourceAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 24), new DateOnly(2026, 7, 24)),
            applicationStatus: "corrected");
        var reader = new MembershipNonWorkingDayExtensionSourceReader(dbContext);

        var missingId = await Assert.ThrowsAsync<ArgumentException>(() =>
            reader.GetForMembershipAsync(Guid.Empty));
        var missingTransaction = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reader.GetForMembershipAsync(fixture.MembershipId));

        Assert.Equal("membershipId", missingId.ParamName);
        Assert.Contains(
            "caller-owned",
            missingTransaction.Message,
            StringComparison.OrdinalIgnoreCase);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var sources = await reader.GetForMembershipAsync(fixture.MembershipId);

        Assert.Equal(
            [
                active.ApplicationId,
                canceledPeriod.ApplicationId,
                correctedPeriod.ApplicationId,
                canceledApplication.ApplicationId,
                correctedApplication.ApplicationId,
            ],
            sources.Select(source => source.SourceId));
        Assert.Equal(
            [true, false, false, false, false],
            sources.Select(source => source.IsActive));
        Assert.All(
            sources,
            source => Assert.Equal("non_working_period", source.SourceType));
        Assert.Equal(active.Range, sources[0].Range);
        Assert.Contains("technical_day", sources[0].SourceLabel, StringComparison.Ordinal);
        Assert.Contains(
            "Scheduled maintenance",
            sources[0].SourceLabel,
            StringComparison.Ordinal);
        Assert.All(
            sources,
            source => Assert.InRange(
                source.SourceLabel.Length,
                1,
                MembershipExtensionSourceRange.MaxSourceLabelLength));

        var periodLock = await AssertPeriodUpdateBlockedAsync(
            database.ConnectionString,
            active.PeriodId);
        var applicationLock = await AssertApplicationUpdateBlockedAsync(
            database.ConnectionString,
            active.ApplicationId);
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, periodLock.SqlState);
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, applicationLock.SqlState);

        await transaction.RollbackAsync();
        await UpdatePeriodReasonAsync(database.ConnectionString, active.PeriodId);
        await UpdateApplicationConfirmationAsync(
            database.ConnectionString,
            active.ApplicationId);
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(0L, await CountRowsAsync(database, "membership_extension_days"));
    }

    [PostgreSqlFact]
    public async Task RebuildUnionsFreezeAndNonWorkingDaysAndRetainsInactiveExplanations()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var freezeId = await InsertFreezeAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22)));
        var activePeriod = await InsertNonWorkingSourceAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 22), new DateOnly(2026, 7, 24)),
            reasonCode: "repair",
            reasonComment: "Floor repair");
        var inactivePeriod = await InsertNonWorkingSourceAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26)),
            applicationStatus: "canceled",
            reasonCode: "mistake",
            reasonComment: null);
        var rebuilder = new MembershipStateCacheRebuilder(
            dbContext,
            new FixedTimeProvider(TestNow),
            [
                new MembershipFreezeExtensionSourceReader(dbContext),
                new MembershipNonWorkingDayExtensionSourceReader(dbContext),
            ]);

        var result = await rebuilder.RebuildAsync(fixture.MembershipId);

        Assert.Equal(MembershipStateCacheRebuildStatus.Created, result.Status);
        Assert.NotNull(result.State);
        Assert.Equal(5, result.State.ExtensionDays);
        Assert.Equal(MembershipBaseEndDate.AddDays(5), result.State.EffectiveEndDate);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            result.RecalculationVersion);

        var cache = await ReadCacheAsync(database.ConnectionString, fixture.MembershipId);
        Assert.Equal(5, cache.ExtensionDays);
        Assert.Equal(MembershipBaseEndDate.AddDays(5), cache.EffectiveEndDate);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            cache.RecalculationVersion);

        var rows = await ReadExtensionRowsAsync(
            database.ConnectionString,
            fixture.MembershipId);
        Assert.Equal(8, rows.Count);
        Assert.Equal(
            5,
            rows.Where(row => row.IsActive)
                .Select(row => row.ExtensionDate)
                .Distinct()
                .Count());
        Assert.Equal(
            2,
            rows.Count(row => row.IsActive
                && row.ExtensionDate == new DateOnly(2026, 7, 22)));
        Assert.Equal(
            3,
            rows.Count(row => row.SourceType == "freeze"
                && row.SourceId == freezeId
                && row.IsActive));
        Assert.Equal(
            3,
            rows.Count(row => row.SourceType == "non_working_period"
                && row.SourceId == activePeriod.ApplicationId
                && row.IsActive));
        Assert.Equal(
            2,
            rows.Count(row => row.SourceType == "non_working_period"
                && row.SourceId == inactivePeriod.ApplicationId
                && !row.IsActive));
        Assert.All(rows, row => Assert.Equal(TestNow, row.RecalculatedAt));
    }

    private static async Task<ExtensionFixture> SeedFixtureAsync(
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
                'Non-working extension fixture',
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
                'Non-working extension fixture',
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
        Assert.Equal(4, await command.ExecuteNonQueryAsync());

        return new ExtensionFixture(
            actorAccountId,
            sessionId,
            clientId,
            membershipId);
    }

    private static async Task<NonWorkingSource> InsertNonWorkingSourceAsync(
        string connectionString,
        ExtensionFixture fixture,
        DateRange range,
        string periodStatus = "active",
        string applicationStatus = "active",
        string reasonCode = "technical_day",
        string? reasonComment = "Scheduled maintenance")
    {
        var periodId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
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
                @period_id,
                @start_date,
                @end_date,
                @reason_code,
                @reason_comment,
                @created_at,
                @created_by_account_id,
                @session_id,
                @period_status);

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
                @application_id,
                @period_id,
                @membership_id,
                @client_id,
                @start_date,
                @end_date,
                @previewed_at,
                @confirmed_at,
                @application_status)
            """;
        command.Parameters.AddWithValue("period_id", periodId);
        command.Parameters.AddWithValue("application_id", applicationId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            range.StartDate);
        command.Parameters.AddWithValue(
            "end_date",
            NpgsqlDbType.Date,
            range.EndDate);
        command.Parameters.AddWithValue("reason_code", reasonCode);
        command.Parameters.Add("reason_comment", NpgsqlDbType.Varchar).Value =
            reasonComment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("created_at", TestNow);
        command.Parameters.AddWithValue(
            "created_by_account_id",
            fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("period_status", periodStatus);
        command.Parameters.AddWithValue("previewed_at", TestNow.AddMinutes(1));
        command.Parameters.AddWithValue("confirmed_at", TestNow.AddMinutes(2));
        command.Parameters.AddWithValue("application_status", applicationStatus);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        return new NonWorkingSource(periodId, applicationId, range);
    }

    private static async Task<Guid> InsertFreezeAsync(
        string connectionString,
        ExtensionFixture fixture,
        DateRange range)
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
                'Medical pause',
                @occurred_at,
                @recorded_at,
                @recorded_by_account_id,
                @session_id,
                'normal',
                null,
                'active')
            """;
        command.Parameters.AddWithValue("id", freezeId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, range.StartDate);
        command.Parameters.AddWithValue("end_date", NpgsqlDbType.Date, range.EndDate);
        command.Parameters.AddWithValue("occurred_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue(
            "recorded_by_account_id",
            fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return freezeId;
    }

    private static async Task<PostgresException> AssertPeriodUpdateBlockedAsync(
        string connectionString,
        Guid periodId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            set lock_timeout = '250ms';
            update bodylife.non_working_periods
            set reason_comment = 'Concurrent edit'
            where id = @id
            """;
        command.Parameters.AddWithValue("id", periodId);
        return await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
    }

    private static async Task<PostgresException> AssertApplicationUpdateBlockedAsync(
        string connectionString,
        Guid applicationId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            set lock_timeout = '250ms';
            update bodylife.non_working_period_applications
            set confirmed_at = confirmed_at
            where id = @id
            """;
        command.Parameters.AddWithValue("id", applicationId);
        return await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
    }

    private static async Task UpdatePeriodReasonAsync(
        string connectionString,
        Guid periodId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.non_working_periods
            set reason_comment = 'Unlocked edit'
            where id = @id
            """;
        command.Parameters.AddWithValue("id", periodId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task UpdateApplicationConfirmationAsync(
        string connectionString,
        Guid applicationId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.non_working_period_applications
            set confirmed_at = @confirmed_at
            where id = @id
            """;
        command.Parameters.AddWithValue("id", applicationId);
        command.Parameters.AddWithValue("confirmed_at", TestNow.AddMinutes(3));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<StateCacheSnapshot> ReadCacheAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select extension_days, effective_end_date, recalculation_version
            from bodylife.membership_state_cache
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new StateCacheSnapshot(
            reader.GetInt32(0),
            reader.GetFieldValue<DateOnly>(1),
            reader.GetInt32(2));
    }

    private static async Task<IReadOnlyList<ExtensionRowSnapshot>>
        ReadExtensionRowsAsync(
            string connectionString,
            Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                extension_date,
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
        var rows = new List<ExtensionRowSnapshot>();
        while (await reader.ReadAsync())
        {
            rows.Add(new ExtensionRowSnapshot(
                reader.GetFieldValue<DateOnly>(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return rows;
    }

    private static async Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return (await database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{tableName}"))!;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record ExtensionFixture(
        Guid ActorAccountId,
        Guid SessionId,
        Guid ClientId,
        Guid MembershipId);

    private sealed record NonWorkingSource(
        Guid PeriodId,
        Guid ApplicationId,
        DateRange Range);

    private sealed record StateCacheSnapshot(
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        int RecalculationVersion);

    private sealed record ExtensionRowSnapshot(
        DateOnly ExtensionDate,
        string SourceType,
        Guid SourceId,
        string SourceLabel,
        bool IsActive,
        DateTimeOffset RecalculatedAt);
}
