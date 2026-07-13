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

public sealed class PostgreSqlMembershipExtensionDayWriterTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        13,
        16,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly TestStartDate = new(2026, 7, 1);
    private static readonly DateOnly TestBaseEndDate = new(2026, 7, 30);

    [PostgreSqlFact]
    public async Task MissingMembershipAndInvalidInputsDoNotWriteRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var writer = CreateWriter(dbContext);
        var calculation = Calculate();

        var missingId = await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.ReplaceAsync(Guid.Empty, calculation));
        var missingCalculation = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            writer.ReplaceAsync(Guid.NewGuid(), calculation: null));
        var membershipId = Guid.NewGuid();

        var result = await writer.ReplaceAsync(membershipId, calculation);

        Assert.Equal("membershipId", missingId.ParamName);
        Assert.Equal("calculation", missingCalculation.ParamName);
        Assert.Equal(MembershipExtensionDayWriteStatus.MissingMembership, result.Status);
        Assert.Equal(membershipId, result.MembershipId);
        Assert.False(result.Succeeded);
        Assert.Null(result.ExtensionDays);
        Assert.Equal(0, result.PersistedRowCount);
        Assert.Null(result.RecalculatedAt);
        Assert.Equal(0L, await CountAllExtensionRowsAsync(database));
    }

    [PostgreSqlFact]
    public async Task ReplacePersistsCanonicalUnionAndEverySourceAttribution()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);
        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            new DateOnly(2026, 7, 5),
            "stale_source",
            Guid.NewGuid(),
            "Row from the previous rebuild",
            recalculatedAt: TestNow.AddHours(-1));
        var freezeId = Guid.NewGuid();
        var nonWorkingPeriodId = Guid.NewGuid();
        var adjustmentId = Guid.NewGuid();
        var calculation = Calculate(
            Source(
                "freeze",
                freezeId,
                "Freeze 2026-07-10..2026-07-12",
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 12)),
            Source(
                "non_working_period",
                nonWorkingPeriodId,
                "Gym closure 2026-07-11..2026-07-12",
                new DateOnly(2026, 7, 11),
                new DateOnly(2026, 7, 12)),
            Source(
                "membership_adjustment",
                adjustmentId,
                "Canceled exceptional extension",
                new DateOnly(2026, 7, 13),
                new DateOnly(2026, 7, 13),
                isActive: false));

        var result = await CreateWriter(dbContext).ReplaceAsync(membershipId, calculation);
        var rows = await ReadExtensionDaysAsync(database.ConnectionString, membershipId);

        Assert.Equal(MembershipExtensionDayWriteStatus.Replaced, result.Status);
        Assert.Equal(membershipId, result.MembershipId);
        Assert.True(result.Succeeded);
        Assert.Equal(3, result.ExtensionDays);
        Assert.Equal(6, result.PersistedRowCount);
        Assert.Equal(TestNow, result.RecalculatedAt);
        Assert.Equal(6, rows.Count);
        Assert.DoesNotContain(rows, row => row.SourceType == "stale_source");
        Assert.All(rows, row => Assert.Equal(TestNow, row.RecalculatedAt));
        Assert.Equal(
            calculation.ExtensionDays,
            rows.Where(row => row.IsActive)
                .Select(row => row.ExtensionDate)
                .Distinct()
                .Count());
        Assert.Equal(
            2,
            rows.Count(row => row.IsActive
                && row.ExtensionDate == new DateOnly(2026, 7, 11)));
        var inactive = Assert.Single(rows, row => !row.IsActive);
        Assert.Equal("membership_adjustment", inactive.SourceType);
        Assert.Equal(adjustmentId, inactive.SourceId);
        Assert.Equal("Canceled exceptional extension", inactive.SourceLabel);
    }

    [PostgreSqlFact]
    public async Task EmptyCalculationClearsPreviouslyDerivedRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);
        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            new DateOnly(2026, 7, 10),
            "freeze",
            Guid.NewGuid(),
            "Previous freeze day");

        var result = await CreateWriter(dbContext).ReplaceAsync(
            membershipId,
            Calculate());

        Assert.Equal(MembershipExtensionDayWriteStatus.Replaced, result.Status);
        Assert.Equal(0, result.ExtensionDays);
        Assert.Equal(0, result.PersistedRowCount);
        Assert.Equal(TestNow, result.RecalculatedAt);
        Assert.Empty(await ReadExtensionDaysAsync(database.ConnectionString, membershipId));
    }

    [PostgreSqlFact]
    public async Task RepeatedReplacementInOneScopeDoesNotAccumulateRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var writer = new MembershipExtensionDayWriter(dbContext, clock);
        var calculation = Calculate(
            Source(
                "freeze",
                Guid.NewGuid(),
                "Two-day freeze",
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 11)));

        var first = await writer.ReplaceAsync(membershipId, calculation);
        var firstRows = await ReadExtensionDaysAsync(database.ConnectionString, membershipId);
        clock.Advance(TimeSpan.FromMinutes(5));
        var second = await writer.ReplaceAsync(membershipId, calculation);
        var secondRows = await ReadExtensionDaysAsync(database.ConnectionString, membershipId);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(first.ExtensionDays, second.ExtensionDays);
        Assert.Equal(first.PersistedRowCount, second.PersistedRowCount);
        Assert.Equal(2, secondRows.Count);
        Assert.All(
            secondRows,
            row => Assert.Equal(TestNow.AddMinutes(5), row.RecalculatedAt));
        Assert.Empty(
            firstRows.Select(row => row.Id)
                .Intersect(secondRows.Select(row => row.Id)));
        Assert.Equal(
            firstRows.Select(RowIdentity),
            secondRows.Select(RowIdentity));
    }

    [PostgreSqlFact]
    public async Task ExistingTransactionOwnsCommitAndRollbackRestoresPreviousRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipId = await SeedIssuedMembershipAsync(database, dbContext);
        await InsertExtensionDayAsync(
            database.ConnectionString,
            membershipId,
            new DateOnly(2026, 7, 5),
            "existing_source",
            Guid.NewGuid(),
            "Committed row before replacement",
            recalculatedAt: TestNow.AddHours(-1));
        var before = await ReadExtensionDaysAsync(database.ConnectionString, membershipId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var replacement = Calculate(
            Source(
                "freeze",
                Guid.NewGuid(),
                "Uncommitted replacement",
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 11)));

        var result = await CreateWriter(dbContext).ReplaceAsync(membershipId, replacement);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.PersistedRowCount);
        await transaction.RollbackAsync();
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            before,
            await ReadExtensionDaysAsync(database.ConnectionString, membershipId));
    }

    [PostgreSqlFact]
    public async Task ConcurrentReplacementsSerializeWithoutMixingBatches()
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
                "First replacement batch",
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 11)));
        var secondCalculation = Calculate(
            Source(
                "second_batch",
                Guid.NewGuid(),
                "Second replacement batch",
                new DateOnly(2026, 7, 20),
                new DateOnly(2026, 7, 22)));
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateWriter(firstContext).ReplaceAsync(membershipId, firstCalculation),
            CreateWriter(secondContext).ReplaceAsync(membershipId, secondCalculation));
        var rows = await ReadExtensionDaysAsync(database.ConnectionString, membershipId);

        Assert.All(results, result => Assert.True(result.Succeeded));
        var persistedBatch = Assert.Single(rows.Select(row => row.SourceType).Distinct());
        Assert.Contains(persistedBatch, new[] { "first_batch", "second_batch" });
        Assert.Equal(persistedBatch == "first_batch" ? 2 : 3, rows.Count);
    }

    [Fact]
    public void PersistenceRegistrationExposesScopedExtensionDayWriter()
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
            candidate => candidate.ServiceType == typeof(MembershipExtensionDayWriter));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(MembershipExtensionDayWriter), descriptor.ImplementationType);
    }

    private static MembershipExtensionDayWriter CreateWriter(BodyLifeDbContext dbContext)
    {
        return new MembershipExtensionDayWriter(
            dbContext,
            new MutableTimeProvider(TestNow));
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

    private static string RowIdentity(PersistedExtensionDay row)
    {
        return $"{row.ExtensionDate:yyyy-MM-dd}|{row.SourceType}|{row.SourceId}|{row.IsActive}";
    }

    private static async Task<Guid> SeedIssuedMembershipAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var bootstrap = await new OwnerBootstrapper(dbContext, new MutableTimeProvider(TestNow))
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
                'Writer',
                null,
                'EXTENSION WRITER',
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
                'Extension writer membership',
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
                'Extension writer membership',
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

    private static async Task<Guid> InsertExtensionDayAsync(
        string connectionString,
        Guid membershipId,
        DateOnly extensionDate,
        string sourceType,
        Guid sourceId,
        string sourceLabel,
        bool isActive = true,
        DateTimeOffset? recalculatedAt = null)
    {
        var id = Guid.NewGuid();
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
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("extension_date", NpgsqlDbType.Date, extensionDate);
        command.Parameters.AddWithValue("source_type", sourceType);
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("source_label", sourceLabel);
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.AddWithValue("recalculated_at", recalculatedAt ?? TestNow);
        await command.ExecuteNonQueryAsync();

        return id;
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
        var rows = new List<PersistedExtensionDay>();
        while (await reader.ReadAsync())
        {
            rows.Add(new PersistedExtensionDay(
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

    private static Task<long> CountAllExtensionRowsAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.membership_extension_days");
    }

    private sealed record PersistedExtensionDay(
        Guid Id,
        DateOnly ExtensionDate,
        string SourceType,
        Guid SourceId,
        string SourceLabel,
        bool IsActive,
        DateTimeOffset RecalculatedAt);

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
