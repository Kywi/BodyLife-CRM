using System.Data;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlNonWorkingDayAffectedScopePreparerTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        17,
        12,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateRange ProposedPeriod = new(
        new DateOnly(2026, 1, 30),
        new DateOnly(2026, 2, 2));

    [Fact]
    public void ServicesRegisterAffectedScopePreparer()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] =
                    BodyLifeDbContextOptions.LocalDevelopmentConnectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddBodyLifePersistence(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var concrete = Assert.IsType<MembershipNonWorkingDayAffectedScopePreparer>(
            scope.ServiceProvider.GetRequiredService<
                MembershipNonWorkingDayAffectedScopePreparer>());
        Assert.Same(
            concrete,
            scope.ServiceProvider.GetRequiredService<
                IMembershipNonWorkingDayAffectedScopePreparer>());
    }

    [PostgreSqlFact]
    public async Task PreparationRequiresCallerOwnedConsistentTransaction()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var preparer = CreatePreparer(dbContext);

        var missingTransaction = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preparer.PrepareAsync(ProposedPeriod));

        Assert.Contains(
            "caller-owned",
            missingTransaction.Message,
            StringComparison.OrdinalIgnoreCase);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted);
        var weakIsolation = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preparer.PrepareAsync(ProposedPeriod));

        Assert.Contains(
            "RepeatableRead",
            weakIsolation.Message,
            StringComparison.Ordinal);
        await transaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task PreparationReturnsExactCanonicalScopeWithoutWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedScopeFixtureAsync(database, dbContext);
        var before = await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead);
        var result = await CreatePreparer(dbContext).PrepareAsync(ProposedPeriod);

        Assert.Equal(ProposedPeriod, result.Period);
        Assert.Equal(3, result.AffectedCount);
        Assert.Equal(
            [
                fixture.EndBoundaryMembershipId,
                fixture.StartBoundaryMembershipId,
                fixture.ExtendedMembershipId,
            ],
            result.AffectedMemberships.Select(item => item.MembershipId));
        Assert.Equal(
            [
                fixture.EndBoundaryClientId,
                fixture.StartBoundaryClientId,
                fixture.ExtendedClientId,
            ],
            result.AffectedMemberships.Select(item => item.ClientId));
        Assert.All(
            result.AffectedMemberships,
            item =>
            {
                Assert.Equal(ProposedPeriod, item.AppliedRange);
                Assert.Equal(4, item.AppliedRange.InclusiveDays);
            });
        Assert.DoesNotContain(
            result.AffectedMemberships,
            item => item.MembershipId == fixture.NoOverlapMembershipId);
        Assert.DoesNotContain(
            result.AffectedMemberships,
            item => item.MembershipId == fixture.CanceledMembershipId);

        var during = await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId);
        Assert.Equal(before, during);
        Assert.False(dbContext.ChangeTracker.HasChanges());
        Assert.Empty(dbContext.ChangeTracker.Entries());

        var eligibleLock = await AssertMembershipUpdateBlockedAsync(
            database.ConnectionString,
            fixture.EndBoundaryMembershipId);
        var ineligibleCandidateLock = await AssertMembershipUpdateBlockedAsync(
            database.ConnectionString,
            fixture.NoOverlapMembershipId);
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, eligibleLock.SqlState);
        Assert.Equal(
            PostgresErrorCodes.LockNotAvailable,
            ineligibleCandidateLock.SqlState);
        Assert.Equal(
            1,
            await UpdateMembershipCommentAsync(
                database.ConnectionString,
                fixture.CanceledMembershipId,
                "Canceled Membership remains unlocked"));

        await transaction.RollbackAsync();
        Assert.Equal(
            1,
            await UpdateMembershipCommentAsync(
                database.ConnectionString,
                fixture.EndBoundaryMembershipId,
                "Active Membership lock released"));

        var after = await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId);
        Assert.Equal(before, after);
    }

    private static MembershipNonWorkingDayAffectedScopePreparer CreatePreparer(
        BodyLifeDbContext dbContext)
    {
        return new MembershipNonWorkingDayAffectedScopePreparer(
            dbContext,
            new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(TestNow),
                [
                    new MembershipFreezeExtensionSourceReader(dbContext),
                    new MembershipNonWorkingDayExtensionSourceReader(dbContext),
                ]));
    }

    private static async Task<ScopeFixture> SeedScopeFixtureAsync(
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
        var membershipTypeId = Guid.NewGuid();
        var fixture = ScopeFixture.Create(actorAccountId, sessionId, membershipTypeId);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await InsertSessionAndMembershipTypeAsync(connection, fixture);
        await InsertMembershipAsync(
            connection,
            fixture,
            fixture.EndBoundaryMembershipId,
            fixture.EndBoundaryClientId,
            "End boundary",
            new DateOnly(2026, 1, 22),
            "active",
            "active");
        await InsertMembershipAsync(
            connection,
            fixture,
            fixture.StartBoundaryMembershipId,
            fixture.StartBoundaryClientId,
            "Start boundary",
            new DateOnly(2026, 2, 2),
            "active",
            "inactive");
        await InsertMembershipAsync(
            connection,
            fixture,
            fixture.NoOverlapMembershipId,
            fixture.NoOverlapClientId,
            "No overlap",
            new DateOnly(2026, 2, 3),
            "active",
            "active");
        await InsertMembershipAsync(
            connection,
            fixture,
            fixture.ExtendedMembershipId,
            fixture.ExtendedClientId,
            "Accepted extension",
            new DateOnly(2026, 1, 20),
            "active",
            "active");
        await InsertMembershipAsync(
            connection,
            fixture,
            fixture.CanceledMembershipId,
            fixture.CanceledClientId,
            "Canceled lifecycle",
            new DateOnly(2026, 1, 22),
            "canceled",
            "active");
        await InsertFreezeAndStaleCacheAsync(connection, fixture);
        dbContext.ChangeTracker.Clear();

        return fixture;
    }

    private static async Task InsertSessionAndMembershipTypeAsync(
        NpgsqlConnection connection,
        ScopeFixture fixture)
    {
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
                @started_at,
                @expires_at,
                null,
                @started_at);

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
                'Affected scope fixture',
                10,
                8,
                1000,
                'UAH',
                true,
                null,
                @started_at,
                @started_at,
                null)
            """;
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("actor_account_id", fixture.ActorAccountId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("started_at", TestNow);
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(12));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertMembershipAsync(
        NpgsqlConnection connection,
        ScopeFixture fixture,
        Guid membershipId,
        Guid clientId,
        string label,
        DateOnly startDate,
        string membershipStatus,
        string clientStatus)
    {
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
                'Scope',
                @label,
                null,
                @normalized_name,
                null,
                null,
                null,
                null,
                @client_status,
                @recorded_at,
                @actor_account_id,
                @recorded_at);

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
                'Affected scope fixture',
                10,
                8,
                1000,
                'UAH',
                @start_date,
                @base_end_date,
                @recorded_at,
                @actor_account_id,
                @membership_status,
                'normal',
                null,
                null)
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("label", label);
        command.Parameters.AddWithValue("normalized_name", $"SCOPE {label.ToUpperInvariant()}");
        command.Parameters.AddWithValue("client_status", clientStatus);
        command.Parameters.AddWithValue("membership_status", membershipStatus);
        command.Parameters.AddWithValue("actor_account_id", fixture.ActorAccountId);
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, startDate);
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            startDate.AddDays(9));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertFreezeAndStaleCacheAsync(
        NpgsqlConnection connection,
        ScopeFixture fixture)
    {
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
                @freeze_id,
                @client_id,
                @membership_id,
                @freeze_start_date,
                @freeze_end_date,
                'Accepted extension source',
                @recorded_at,
                @recorded_at,
                @actor_account_id,
                @session_id,
                'normal',
                null,
                'active');

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
                0,
                8,
                0,
                null,
                null,
                0,
                @stale_effective_end_date,
                null,
                @stale_recalculated_at,
                1)
            """;
        command.Parameters.AddWithValue("freeze_id", Guid.NewGuid());
        command.Parameters.AddWithValue("client_id", fixture.ExtendedClientId);
        command.Parameters.AddWithValue("membership_id", fixture.ExtendedMembershipId);
        command.Parameters.AddWithValue("actor_account_id", fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue(
            "freeze_start_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 1, 25));
        command.Parameters.AddWithValue(
            "freeze_end_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 1, 29));
        command.Parameters.AddWithValue(
            "stale_effective_end_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 1, 29));
        command.Parameters.AddWithValue("stale_recalculated_at", TestNow.AddDays(-1));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task<DatabaseSnapshot> ReadSnapshotAsync(
        BodyLifeDbContext dbContext,
        Guid extendedMembershipId)
    {
        if (dbContext.Database.GetDbConnection().State != ConnectionState.Open)
        {
            await dbContext.Database.OpenConnectionAsync();
        }

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            """
            select
                (select count(*) from bodylife.non_working_periods),
                (select count(*) from bodylife.non_working_period_applications),
                (select count(*) from bodylife.non_working_period_cancellations),
                (select count(*) from bodylife.membership_state_cache),
                (select count(*) from bodylife.membership_extension_days),
                (select count(*) from bodylife.business_audit_entries),
                (select count(*) from bodylife.command_idempotency_keys),
                (select count(*) from bodylife.freezes),
                cache.extension_days,
                cache.effective_end_date,
                cache.recalculation_version
            from bodylife.membership_state_cache as cache
            where cache.membership_id = @membership_id
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "membership_id";
        parameter.DbType = DbType.Guid;
        parameter.Value = extendedMembershipId;
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new DatabaseSnapshot(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt32(8),
            reader.GetFieldValue<DateOnly>(9),
            reader.GetInt32(10));
    }

    private static async Task<PostgresException> AssertMembershipUpdateBlockedAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            set lock_timeout = '250ms';
            update bodylife.issued_memberships
            set comment = 'Concurrent edit'
            where id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        return await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
    }

    private static async Task<int> UpdateMembershipCommentAsync(
        string connectionString,
        Guid membershipId,
        string comment)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.issued_memberships
            set comment = @comment
            where id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("comment", comment);
        return await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record ScopeFixture(
        Guid ActorAccountId,
        Guid SessionId,
        Guid MembershipTypeId,
        Guid EndBoundaryMembershipId,
        Guid EndBoundaryClientId,
        Guid StartBoundaryMembershipId,
        Guid StartBoundaryClientId,
        Guid NoOverlapMembershipId,
        Guid NoOverlapClientId,
        Guid ExtendedMembershipId,
        Guid ExtendedClientId,
        Guid CanceledMembershipId,
        Guid CanceledClientId)
    {
        public static ScopeFixture Create(
            Guid actorAccountId,
            Guid sessionId,
            Guid membershipTypeId)
        {
            return new ScopeFixture(
                actorAccountId,
                sessionId,
                membershipTypeId,
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Guid.Parse("10000000-0000-0000-0000-000000000002"),
                Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Guid.Parse("10000000-0000-0000-0000-000000000003"),
                Guid.Parse("00000000-0000-0000-0000-000000000004"),
                Guid.Parse("10000000-0000-0000-0000-000000000004"),
                Guid.Parse("00000000-0000-0000-0000-000000000005"),
                Guid.Parse("10000000-0000-0000-0000-000000000005"));
        }
    }

    private sealed record DatabaseSnapshot(
        long NonWorkingPeriodCount,
        long NonWorkingApplicationCount,
        long NonWorkingCancellationCount,
        long MembershipCacheCount,
        long ExtensionDayCount,
        long AuditCount,
        long IdempotencyCount,
        long FreezeCount,
        int ExtendedMembershipCacheDays,
        DateOnly ExtendedMembershipCacheEndDate,
        int ExtendedMembershipCacheVersion);
}
