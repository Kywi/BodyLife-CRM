using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMembershipStatePersistenceCoordinatorTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        13,
        17,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly TestStartDate = new(2026, 7, 1);
    private static readonly DateOnly TestBaseEndDate = new(2026, 7, 30);
    private static readonly Guid FirstNegativeVisitId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");

    [PostgreSqlFact]
    public async Task MissingMembershipAndInvalidInputsDoNotWriteDerivedState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var coordinator = CreateCoordinator(dbContext);
        var emptyCalculation = Calculate();
        var state = CreateState(emptyCalculation);
        var mismatchedCalculation = Calculate(
            Source(
                "freeze",
                Guid.NewGuid(),
                "One-day freeze",
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 10)));

        var emptyId = await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.PersistAsync(Guid.Empty, state, emptyCalculation));
        var missingState = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            coordinator.PersistAsync(Guid.NewGuid(), null, emptyCalculation));
        var missingCalculation = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            coordinator.PersistAsync(Guid.NewGuid(), state, null));
        var mismatched = await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.PersistAsync(Guid.NewGuid(), state, mismatchedCalculation));
        var membershipId = Guid.NewGuid();

        var result = await coordinator.PersistAsync(
            membershipId,
            state,
            emptyCalculation);

        Assert.Equal("membershipId", emptyId.ParamName);
        Assert.Equal("calculatedState", missingState.ParamName);
        Assert.Equal("extensionCalculation", missingCalculation.ParamName);
        Assert.Equal("extensionCalculation", mismatched.ParamName);
        Assert.Equal(MembershipStatePersistenceStatus.MissingMembership, result.Status);
        Assert.Equal(membershipId, result.MembershipId);
        Assert.False(result.Succeeded);
        Assert.Null(result.State);
        Assert.Equal(0, result.PersistedExtensionRowCount);
        Assert.Null(result.RecalculatedAt);
        Assert.Null(result.RecalculationVersion);
        Assert.Equal(0L, await CountCacheRowsAsync(database));
        Assert.Equal(0L, await CountExtensionRowsAsync(database));
    }

    [PostgreSqlFact]
    public async Task PersistWritesMatchingCacheAndExplanationWithOneTimestamp()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);
        var calculation = Calculate(
            Source(
                "freeze",
                Guid.NewGuid(),
                "Summer freeze",
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 12)),
            Source(
                "non_working_period",
                Guid.NewGuid(),
                "Gym closure",
                new DateOnly(2026, 7, 11),
                new DateOnly(2026, 7, 12)),
            Source(
                "membership_adjustment",
                Guid.NewGuid(),
                "Canceled adjustment",
                new DateOnly(2026, 7, 13),
                new DateOnly(2026, 7, 13),
                isActive: false));
        var state = CreateState(calculation);

        var result = await CreateCoordinator(dbContext).PersistAsync(
            membershipId,
            state,
            calculation);
        var cache = await ReadCacheAsync(database.ConnectionString, membershipId);
        var rows = await ReadExtensionRowsAsync(database.ConnectionString, membershipId);

        Assert.Equal(MembershipStatePersistenceStatus.Persisted, result.Status);
        Assert.Equal(membershipId, result.MembershipId);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.State);
        Assert.Equal(10, result.State.CountedVisits);
        Assert.Equal(-2, result.State.RemainingVisits);
        Assert.Equal(2, result.State.NegativeBalance);
        Assert.Equal(FirstNegativeVisitId, result.State.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 12), result.State.FirstNegativeVisitDate);
        Assert.Equal(3, result.State.ExtensionDays);
        Assert.Equal(TestBaseEndDate.AddDays(3), result.State.EffectiveEndDate);
        Assert.Equal(TestNow.AddMinutes(-30), result.State.LastCountedVisitAt);
        Assert.Equal(6, result.PersistedExtensionRowCount);
        Assert.Equal(TestNow, result.RecalculatedAt);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            result.RecalculationVersion);
        Assert.Equal(10, cache.CountedVisits);
        Assert.Equal(-2, cache.RemainingVisits);
        Assert.Equal(2, cache.NegativeBalance);
        Assert.Equal(FirstNegativeVisitId, cache.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 12), cache.FirstNegativeVisitDate);
        Assert.Equal(3, cache.ExtensionDays);
        Assert.Equal(TestBaseEndDate.AddDays(3), cache.EffectiveEndDate);
        Assert.Equal(TestNow.AddMinutes(-30), cache.LastCountedVisitAt);
        Assert.Equal(TestNow, cache.RecalculatedAt);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            cache.RecalculationVersion);
        Assert.Equal(6, rows.Count);
        Assert.All(rows, row => Assert.Equal(cache.RecalculatedAt, row.RecalculatedAt));
        Assert.Equal(
            calculation.ExtensionDays,
            rows.Where(row => row.IsActive)
                .Select(row => row.ExtensionDate)
                .Distinct()
                .Count());
        var inactive = Assert.Single(rows, row => !row.IsActive);
        Assert.Equal("membership_adjustment", inactive.SourceType);
        Assert.Equal("Canceled adjustment", inactive.SourceLabel);
    }

    [PostgreSqlFact]
    public async Task StateFromDifferentIssuedTermsIsRejectedWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);
        var calculation = Calculate();
        var mismatchedState = CreateState(
            calculation,
            startDate: new DateOnly(2026, 8, 1));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateCoordinator(dbContext).PersistAsync(
                membershipId,
                mismatchedState,
                calculation));

        Assert.Equal("calculatedState", exception.ParamName);
        Assert.IsType<ArgumentException>(exception.InnerException);
        Assert.Equal(0L, await CountCacheRowsAsync(database));
        Assert.Equal(0L, await CountExtensionRowsAsync(database));
    }

    [PostgreSqlFact]
    public async Task RepeatedPersistenceCanClearCacheExtensionsAndExplanationRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var coordinator = CreateCoordinator(dbContext, clock);
        var initialCalculation = Calculate(
            Source(
                "freeze",
                Guid.NewGuid(),
                "Two-day freeze",
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 11)));

        var first = await coordinator.PersistAsync(
            membershipId,
            CreateState(initialCalculation),
            initialCalculation);
        clock.Advance(TimeSpan.FromMinutes(5));
        var emptyCalculation = Calculate();
        var second = await coordinator.PersistAsync(
            membershipId,
            CreateState(emptyCalculation),
            emptyCalculation);
        var cache = await ReadCacheAsync(database.ConnectionString, membershipId);

        Assert.True(first.Succeeded);
        Assert.Equal(2, first.PersistedExtensionRowCount);
        Assert.True(second.Succeeded);
        Assert.Equal(0, second.PersistedExtensionRowCount);
        Assert.Equal(0, cache.ExtensionDays);
        Assert.Equal(TestBaseEndDate, cache.EffectiveEndDate);
        Assert.Equal(TestNow.AddMinutes(5), cache.RecalculatedAt);
        Assert.Equal(1L, await CountCacheRowsAsync(database));
        Assert.Equal(0L, await CountExtensionRowsAsync(database));
    }

    [PostgreSqlFact]
    public async Task CallerTransactionRollbackRestoresCacheAndExplanationRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var coordinator = CreateCoordinator(dbContext, clock);
        var initialCalculation = Calculate(
            Source(
                "initial_batch",
                Guid.NewGuid(),
                "Committed extension",
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 10)));
        await coordinator.PersistAsync(
            membershipId,
            CreateState(initialCalculation),
            initialCalculation);
        var cacheBefore = await ReadCacheAsync(database.ConnectionString, membershipId);
        var rowsBefore = await ReadExtensionRowsAsync(
            database.ConnectionString,
            membershipId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        clock.Advance(TimeSpan.FromMinutes(10));
        var replacementCalculation = Calculate(
            Source(
                "replacement_batch",
                Guid.NewGuid(),
                "Rolled-back extension",
                new DateOnly(2026, 7, 20),
                new DateOnly(2026, 7, 21)));

        var result = await coordinator.PersistAsync(
            membershipId,
            CreateState(replacementCalculation),
            replacementCalculation);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.PersistedExtensionRowCount);
        await transaction.RollbackAsync();
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            cacheBefore,
            await ReadCacheAsync(database.ConnectionString, membershipId));
        Assert.Equal(
            rowsBefore,
            await ReadExtensionRowsAsync(database.ConnectionString, membershipId));
    }

    [PostgreSqlFact]
    public async Task ConcurrentPersistenceSerializesWithoutMixingDerivedBatches()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid membershipId;
        await using (var seedContext = database.CreateDbContext())
        {
            await seedContext.Database.MigrateAsync();
            membershipId = await SeedIssuedMembershipAsync(database, seedContext);
        }

        var firstCalculation = Calculate(
            Source(
                "first_batch",
                Guid.NewGuid(),
                "First batch",
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 11)));
        var secondCalculation = Calculate(
            Source(
                "second_batch",
                Guid.NewGuid(),
                "Second batch",
                new DateOnly(2026, 7, 20),
                new DateOnly(2026, 7, 22)));
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateCoordinator(firstContext, new FixedTimeProvider(TestNow)).PersistAsync(
                membershipId,
                CreateState(firstCalculation),
                firstCalculation),
            CreateCoordinator(
                secondContext,
                new FixedTimeProvider(TestNow.AddMinutes(5))).PersistAsync(
                membershipId,
                CreateState(secondCalculation),
                secondCalculation));
        var cache = await ReadCacheAsync(database.ConnectionString, membershipId);
        var rows = await ReadExtensionRowsAsync(database.ConnectionString, membershipId);

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Equal(1L, await CountCacheRowsAsync(database));
        var persistedBatch = Assert.Single(rows.Select(row => row.SourceType).Distinct());
        Assert.Contains(persistedBatch, new[] { "first_batch", "second_batch" });
        var expectedDays = persistedBatch == "first_batch" ? 2 : 3;
        var expectedTimestamp = persistedBatch == "first_batch"
            ? TestNow
            : TestNow.AddMinutes(5);
        Assert.Equal(expectedDays, rows.Count);
        Assert.Equal(expectedDays, cache.ExtensionDays);
        Assert.Equal(TestBaseEndDate.AddDays(expectedDays), cache.EffectiveEndDate);
        Assert.Equal(expectedTimestamp, cache.RecalculatedAt);
        Assert.All(rows, row => Assert.Equal(cache.RecalculatedAt, row.RecalculatedAt));
    }

    [Fact]
    public void PersistenceRegistrationExposesScopedStatePersistenceCoordinator()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] =
                    "Host=localhost;Database=bodylife;Username=bodylife;Password=not-used",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBodyLifePersistence(configuration);

        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == typeof(MembershipStatePersistenceCoordinator));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(
            typeof(MembershipStatePersistenceCoordinator),
            descriptor.ImplementationType);
    }

    private static MembershipStatePersistenceCoordinator CreateCoordinator(
        BodyLifeDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        return new MembershipStatePersistenceCoordinator(
            dbContext,
            timeProvider ?? new FixedTimeProvider(TestNow));
    }

    private static MembershipCalculatedState CreateState(
        MembershipExtensionCalculation calculation,
        DateOnly? startDate = null)
    {
        var issuedStartDate = startDate ?? TestStartDate;
        var snapshot = new IssuedMembershipSnapshot(
            "Persistence coordinator membership",
            durationDays: 30,
            visitsLimit: 8,
            new Money(1000m, "UAH"));
        var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
            Guid.NewGuid(),
            snapshot,
            issuedStartDate,
            issuedStartDate.AddDays(29));

        return MembershipCalculatedState.FromStoredCache(
            issueTerms,
            countedVisits: 10,
            remainingVisits: -2,
            negativeBalance: 2,
            FirstNegativeVisitId,
            firstNegativeVisitDate: new DateOnly(2026, 7, 12),
            calculation.ExtensionDays,
            issueTerms.BaseEndDate.AddDays(calculation.ExtensionDays),
            lastCountedVisitAt: TestNow.AddMinutes(-30));
    }

    private static MembershipExtensionCalculation Calculate(
        params MembershipExtensionSourceRange[] sources)
    {
        return MembershipExtensionCalculator.Calculate(sources);
    }

    private static MembershipExtensionSourceRange Source(
        string sourceType,
        Guid sourceId,
        string sourceLabel,
        DateOnly startDate,
        DateOnly endDate,
        bool isActive = true)
    {
        return new MembershipExtensionSourceRange(
            sourceType,
            sourceId,
            sourceLabel,
            new DateRange(startDate, endDate),
            isActive);
    }

    private static async Task<Guid> SeedIssuedMembershipAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var bootstrap = await new OwnerBootstrapper(dbContext, new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

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
                'Persistence',
                'Coordinator',
                null,
                'PERSISTENCE COORDINATOR',
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
                'Persistence coordinator membership',
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
                'Persistence coordinator membership',
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
        command.Parameters.AddWithValue("client_id", Guid.NewGuid());
        command.Parameters.AddWithValue("membership_type_id", Guid.NewGuid());
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("actor_account_id", bootstrap.AccountId!.Value);
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, TestStartDate);
        command.Parameters.AddWithValue("base_end_date", NpgsqlDbType.Date, TestBaseEndDate);
        Assert.Equal(3, await command.ExecuteNonQueryAsync());

        return membershipId;
    }

    private static async Task<PersistedCache> ReadCacheAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select counted_visits,
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
        return new PersistedCache(
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

    private static async Task<IReadOnlyList<PersistedExtensionRow>> ReadExtensionRowsAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select id,
                   extension_date,
                   source_type,
                   source_id,
                   source_label,
                   is_active,
                   recalculated_at
            from bodylife.membership_extension_days
            where membership_id = @membership_id
            order by extension_date, source_type, source_id, id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<PersistedExtensionRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new PersistedExtensionRow(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return rows;
    }

    private static Task<long> CountCacheRowsAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.membership_state_cache");
    }

    private static Task<long> CountExtensionRowsAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.membership_extension_days");
    }

    private sealed record PersistedCache(
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

    private sealed record PersistedExtensionRow(
        Guid Id,
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

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            utcNow = utcNow.Add(duration);
        }
    }
}
