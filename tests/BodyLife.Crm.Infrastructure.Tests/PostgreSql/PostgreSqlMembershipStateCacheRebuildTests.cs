using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMembershipStateCacheRebuildTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 13, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TestStartDate = new(2026, 7, 1);
    private static readonly DateOnly TestBaseEndDate = new(2026, 7, 30);

    [PostgreSqlFact]
    public async Task MissingSourceReturnsNotFoundWithoutCreatingCache()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = Guid.NewGuid();

        var result = await CreateRebuilder(dbContext).RebuildAsync(membershipId);

        Assert.Equal(MembershipStateCacheRebuildStatus.MissingSource, result.Status);
        Assert.Equal(membershipId, result.MembershipId);
        Assert.False(result.Succeeded);
        Assert.False(result.DriftDetected);
        Assert.Null(result.State);
        Assert.Null(result.RecalculatedAt);
        Assert.Null(result.RecalculationVersion);
        Assert.Equal(0L, await CountCacheRowsAsync(database));
    }

    [PostgreSqlFact]
    public async Task RebuildCreatesInitialCacheFromIssuedSnapshotAfterCatalogEdit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var seeded = await SeedIssuedMembershipAsync(database, dbContext);
        await EditMembershipTypeAsync(database.ConnectionString, seeded.MembershipTypeId);

        var result = await CreateRebuilder(dbContext).RebuildAsync(seeded.MembershipId);

        Assert.Equal(MembershipStateCacheRebuildStatus.Created, result.Status);
        Assert.True(result.Succeeded);
        Assert.True(result.DriftDetected);
        Assert.NotNull(result.State);
        Assert.Equal(0, result.State.CountedVisits);
        Assert.Equal(2, result.State.RemainingVisits);
        Assert.Equal(0, result.State.NegativeBalance);
        Assert.Equal(TestBaseEndDate, result.State.EffectiveEndDate);
        Assert.Equal(TestNow, result.RecalculatedAt);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            result.RecalculationVersion);

        var persisted = await ReadStateCacheAsync(database.ConnectionString, seeded.MembershipId);
        AssertInitialState(persisted);
        Assert.Equal(TestNow, persisted.RecalculatedAt);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            persisted.RecalculationVersion);
    }

    [PostgreSqlFact]
    public async Task ActiveOpeningStateCreatesCacheFromDeclaredBaseline()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var seeded = await SeedIssuedMembershipAsync(database, dbContext);
        await InsertOpeningStateAsync(
            database.ConnectionString,
            seeded,
            declaredRemainingVisits: 1,
            declaredNegativeBalance: 0);

        var result = await CreateRebuilder(dbContext).RebuildAsync(seeded.MembershipId);

        Assert.Equal(MembershipStateCacheRebuildStatus.Created, result.Status);
        Assert.NotNull(result.State);
        Assert.Equal(0, result.State.CountedVisits);
        Assert.Equal(1, result.State.RemainingVisits);
        Assert.Equal(0, result.State.NegativeBalance);
        Assert.Null(result.State.FirstNegativeVisitId);
        Assert.Null(result.State.FirstNegativeVisitDate);
        Assert.Equal(0, result.State.ExtensionDays);
        Assert.Equal(TestBaseEndDate, result.State.EffectiveEndDate);
        Assert.Null(result.State.LastCountedVisitAt);

        AssertOpeningBaseline(
            await ReadStateCacheAsync(database.ConnectionString, seeded.MembershipId),
            remainingVisits: 1,
            negativeBalance: 0,
            extensionDays: 0,
            effectiveEndDate: TestBaseEndDate);
    }

    [PostgreSqlFact]
    public async Task NegativeOpeningStateRepairsCacheWithoutSyntheticVisitMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var seeded = await SeedIssuedMembershipAsync(database, dbContext);
        await InsertOpeningStateAsync(
            database.ConnectionString,
            seeded,
            declaredRemainingVisits: -2,
            declaredNegativeBalance: 2,
            knownEffectiveEndDate: new DateOnly(2026, 8, 3),
            knownExtensionDays: 4);
        await InsertStateCacheAsync(database.ConnectionString, seeded.MembershipId);

        var result = await CreateRebuilder(dbContext).RebuildAsync(seeded.MembershipId);

        Assert.Equal(MembershipStateCacheRebuildStatus.Repaired, result.Status);
        Assert.True(result.DriftDetected);
        AssertOpeningBaseline(
            await ReadStateCacheAsync(database.ConnectionString, seeded.MembershipId),
            remainingVisits: -2,
            negativeBalance: 2,
            extensionDays: 4,
            effectiveEndDate: new DateOnly(2026, 8, 3));
    }

    [PostgreSqlFact]
    public async Task HistoricalOpeningStatesAreIgnoredWithoutActiveSource()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var seeded = await SeedIssuedMembershipAsync(database, dbContext);
        await InsertOpeningStateAsync(
            database.ConnectionString,
            seeded,
            declaredRemainingVisits: -2,
            declaredNegativeBalance: 2,
            knownEffectiveEndDate: new DateOnly(2026, 8, 3),
            knownExtensionDays: 4,
            status: "canceled");
        await InsertOpeningStateAsync(
            database.ConnectionString,
            seeded,
            declaredRemainingVisits: 1,
            declaredNegativeBalance: 0,
            status: "corrected");

        var result = await CreateRebuilder(dbContext).RebuildAsync(seeded.MembershipId);

        Assert.Equal(MembershipStateCacheRebuildStatus.Created, result.Status);
        AssertInitialState(await ReadStateCacheAsync(
            database.ConnectionString,
            seeded.MembershipId));
    }

    [PostgreSqlFact]
    public async Task DomainInvalidActiveOpeningStateRollsBackWithoutCacheMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var seeded = await SeedIssuedMembershipAsync(database, dbContext);
        await InsertOpeningStateAsync(
            database.ConnectionString,
            seeded,
            declaredRemainingVisits: 1,
            declaredNegativeBalance: 0,
            knownEffectiveEndDate: new DateOnly(2026, 7, 20));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateRebuilder(dbContext).RebuildAsync(seeded.MembershipId));

        Assert.Equal("openingState", exception.ParamName);
        Assert.Equal(0L, await CountCacheRowsAsync(database));
    }

    [PostgreSqlFact]
    public async Task RetiredOpeningStateRepairsCacheBackToIssuedInitialState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var seeded = await SeedIssuedMembershipAsync(database, dbContext);
        var openingStateId = await InsertOpeningStateAsync(
            database.ConnectionString,
            seeded,
            declaredRemainingVisits: 1,
            declaredNegativeBalance: 0);

        var created = await CreateRebuilder(dbContext).RebuildAsync(seeded.MembershipId);
        await UpdateOpeningStateStatusAsync(
            database.ConnectionString,
            openingStateId,
            "corrected");
        var repaired = await CreateRebuilder(dbContext).RebuildAsync(seeded.MembershipId);

        Assert.Equal(MembershipStateCacheRebuildStatus.Created, created.Status);
        Assert.Equal(MembershipStateCacheRebuildStatus.Repaired, repaired.Status);
        AssertInitialState(await ReadStateCacheAsync(
            database.ConnectionString,
            seeded.MembershipId));
    }

    [PostgreSqlFact]
    public async Task RebuildRepairsDriftAcrossEveryStableCacheField()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var seeded = await SeedIssuedMembershipAsync(database, dbContext);
        await InsertStateCacheAsync(
            database.ConnectionString,
            seeded.MembershipId,
            countedVisits: 3,
            remainingVisits: -1,
            negativeBalance: 1,
            firstNegativeVisitId: Guid.NewGuid(),
            firstNegativeVisitDate: new DateOnly(2026, 7, 3),
            extensionDays: 2,
            effectiveEndDate: new DateOnly(2026, 8, 1),
            lastCountedVisitAt: new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero),
            recalculatedAt: TestNow.AddHours(-1),
            recalculationVersion: 2);

        var result = await CreateRebuilder(dbContext).RebuildAsync(seeded.MembershipId);

        Assert.Equal(MembershipStateCacheRebuildStatus.Repaired, result.Status);
        Assert.True(result.Succeeded);
        Assert.True(result.DriftDetected);
        Assert.NotNull(result.State);
        AssertInitialState(await ReadStateCacheAsync(
            database.ConnectionString,
            seeded.MembershipId));
    }

    [PostgreSqlFact]
    public async Task MatchingCacheIsVerifiedAndRefreshesRecalculationMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var seeded = await SeedIssuedMembershipAsync(database, dbContext);
        await InsertStateCacheAsync(
            database.ConnectionString,
            seeded.MembershipId,
            recalculatedAt: TestNow.AddHours(-1));

        var result = await CreateRebuilder(dbContext).RebuildAsync(seeded.MembershipId);

        Assert.Equal(MembershipStateCacheRebuildStatus.Verified, result.Status);
        Assert.True(result.Succeeded);
        Assert.False(result.DriftDetected);
        Assert.NotNull(result.State);

        var persisted = await ReadStateCacheAsync(database.ConnectionString, seeded.MembershipId);
        AssertInitialState(persisted);
        Assert.Equal(TestNow, persisted.RecalculatedAt);
        Assert.Equal(1L, await CountCacheRowsAsync(database));
    }

    [PostgreSqlFact]
    public async Task RecalculationVersionMismatchIsReportedAndRepaired()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var seeded = await SeedIssuedMembershipAsync(database, dbContext);
        await InsertStateCacheAsync(
            database.ConnectionString,
            seeded.MembershipId,
            recalculationVersion: MembershipStateCacheRebuilder.CurrentRecalculationVersion - 1);

        var result = await CreateRebuilder(dbContext).RebuildAsync(seeded.MembershipId);

        Assert.Equal(MembershipStateCacheRebuildStatus.Repaired, result.Status);
        Assert.True(result.DriftDetected);
        var persisted = await ReadStateCacheAsync(database.ConnectionString, seeded.MembershipId);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            persisted.RecalculationVersion);
    }

    [PostgreSqlFact]
    public async Task ConcurrentRebuildsSerializeAndKeepOneCacheRow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid membershipId;

        await using (var seedContext = database.CreateDbContext())
        {
            await seedContext.Database.MigrateAsync();
            var seeded = await SeedIssuedMembershipAsync(database, seedContext);
            membershipId = seeded.MembershipId;
            await InsertOpeningStateAsync(
                database.ConnectionString,
                seeded,
                declaredRemainingVisits: 1,
                declaredNegativeBalance: 0);
        }

        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var firstRebuild = CreateRebuilder(firstContext).RebuildAsync(membershipId);
        var secondRebuild = CreateRebuilder(secondContext).RebuildAsync(membershipId);

        var results = await Task.WhenAll(firstRebuild, secondRebuild);

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Single(
            results,
            result => result.Status == MembershipStateCacheRebuildStatus.Created);
        Assert.Single(
            results,
            result => result.Status == MembershipStateCacheRebuildStatus.Verified);
        Assert.Equal(1L, await CountCacheRowsAsync(database));
        AssertOpeningBaseline(
            await ReadStateCacheAsync(database.ConnectionString, membershipId),
            remainingVisits: 1,
            negativeBalance: 0,
            extensionDays: 0,
            effectiveEndDate: TestBaseEndDate);
    }

    [PostgreSqlFact]
    public async Task ExistingTransactionOwnsCommitAndCanRollBackRebuild()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var seeded = await SeedIssuedMembershipAsync(database, dbContext);
        await InsertOpeningStateAsync(
            database.ConnectionString,
            seeded,
            declaredRemainingVisits: 1,
            declaredNegativeBalance: 0);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var result = await CreateRebuilder(dbContext).RebuildAsync(seeded.MembershipId);

        Assert.Equal(MembershipStateCacheRebuildStatus.Created, result.Status);
        Assert.NotNull(result.State);
        Assert.Equal(1, result.State.RemainingVisits);
        await transaction.RollbackAsync();
        dbContext.ChangeTracker.Clear();
        Assert.Equal(0L, await CountCacheRowsAsync(database));
    }

    private static MembershipStateCacheRebuilder CreateRebuilder(BodyLifeDbContext dbContext)
    {
        return new MembershipStateCacheRebuilder(
            dbContext,
            new FixedTimeProvider(TestNow));
    }

    private static async Task<SeededMembership> SeedIssuedMembershipAsync(
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
        var sessionId = Guid.NewGuid();

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
                null)
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("actor_account_id", actorAccountId);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("session_started_at", TestNow.AddMinutes(-1));
        command.Parameters.AddWithValue("session_expires_at", TestNow.AddHours(12));
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, TestStartDate);
        command.Parameters.AddWithValue("base_end_date", NpgsqlDbType.Date, TestBaseEndDate);
        Assert.Equal(4, await command.ExecuteNonQueryAsync());

        return new SeededMembership(
            membershipId,
            membershipTypeId,
            actorAccountId,
            sessionId);
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
                updated_at = @updated_at
            where id = @id
            """;
        command.Parameters.AddWithValue("id", membershipTypeId);
        command.Parameters.AddWithValue("updated_at", TestNow.AddMinutes(1));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<Guid> InsertOpeningStateAsync(
        string connectionString,
        SeededMembership seeded,
        int declaredRemainingVisits,
        int declaredNegativeBalance,
        DateOnly? knownEffectiveEndDate = null,
        int? knownExtensionDays = null,
        string status = "active")
    {
        var openingStateId = Guid.NewGuid();
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
                'Paper register 2026, page 12',
                'Active membership history before launch is incomplete',
                @recorded_at,
                @recorded_by_account_id,
                @recorded_session_id,
                'manual_backfill',
                null,
                @status)
            """;
        command.Parameters.AddWithValue("id", openingStateId);
        command.Parameters.AddWithValue("membership_id", seeded.MembershipId);
        command.Parameters.AddWithValue(
            "opening_as_of_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 13));
        command.Parameters.AddWithValue("declared_remaining_visits", declaredRemainingVisits);
        command.Parameters.AddWithValue("declared_negative_balance", declaredNegativeBalance);
        command.Parameters.Add("known_effective_end_date", NpgsqlDbType.Date).Value =
            knownEffectiveEndDate ?? (object)DBNull.Value;
        command.Parameters.Add("known_extension_days", NpgsqlDbType.Integer).Value =
            knownExtensionDays ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("recorded_by_account_id", seeded.ActorAccountId);
        command.Parameters.AddWithValue("recorded_session_id", seeded.SessionId);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return openingStateId;
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
        int recalculationVersion = MembershipStateCacheRebuilder.CurrentRecalculationVersion)
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
        command.Parameters.AddWithValue("recalculated_at", recalculatedAt ?? TestNow.AddHours(-1));
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

    private static Task<long> CountCacheRowsAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.membership_state_cache");
    }

    private static void AssertOpeningBaseline(
        PersistedMembershipState state,
        int remainingVisits,
        int negativeBalance,
        int extensionDays,
        DateOnly effectiveEndDate)
    {
        Assert.Equal(0, state.CountedVisits);
        Assert.Equal(remainingVisits, state.RemainingVisits);
        Assert.Equal(negativeBalance, state.NegativeBalance);
        Assert.Null(state.FirstNegativeVisitId);
        Assert.Null(state.FirstNegativeVisitDate);
        Assert.Equal(extensionDays, state.ExtensionDays);
        Assert.Equal(effectiveEndDate, state.EffectiveEndDate);
        Assert.Null(state.LastCountedVisitAt);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            state.RecalculationVersion);
    }

    private static void AssertInitialState(PersistedMembershipState state)
    {
        Assert.Equal(0, state.CountedVisits);
        Assert.Equal(2, state.RemainingVisits);
        Assert.Equal(0, state.NegativeBalance);
        Assert.Null(state.FirstNegativeVisitId);
        Assert.Null(state.FirstNegativeVisitDate);
        Assert.Equal(0, state.ExtensionDays);
        Assert.Equal(TestBaseEndDate, state.EffectiveEndDate);
        Assert.Null(state.LastCountedVisitAt);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            state.RecalculationVersion);
    }

    private sealed record SeededMembership(
        Guid MembershipId,
        Guid MembershipTypeId,
        Guid ActorAccountId,
        Guid SessionId);

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
