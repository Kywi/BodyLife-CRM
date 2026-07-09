using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlStaffAccountLifecycleTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task StaffAccountLifecycleCreatesNamedAdminAndSharedReceptionAccounts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var service = Service(dbContext);

        var namedAdminResult = await service.CreateStaffAccountAsync(
            OwnerEnvelope(),
            AccountKind.NamedAdmin,
            "  Main Admin  ");
        var sharedReceptionResult = await service.CreateStaffAccountAsync(
            OwnerEnvelope(),
            AccountKind.SharedReceptionAdmin,
            "  Front desk shared  ");

        Assert.Equal(StaffAccountLifecycleStatus.Created, namedAdminResult.Status);
        Assert.True(namedAdminResult.Succeeded);
        Assert.Equal(StaffAccountLifecycleStatus.Created, sharedReceptionResult.Status);
        Assert.True(sharedReceptionResult.Succeeded);
        Assert.Equal(
            "Main Admin",
            await ReadAccountValueAsync<string>(database, namedAdminResult.AccountId!.Value, "display_name"));
        Assert.Equal(
            "named_admin",
            await ReadAccountValueAsync<string>(database, namedAdminResult.AccountId.Value, "account_type"));
        Assert.Equal(
            "admin",
            await ReadAccountValueAsync<string>(database, namedAdminResult.AccountId.Value, "role"));
        Assert.Equal(
            "shared_reception_admin",
            await ReadAccountValueAsync<string>(database, sharedReceptionResult.AccountId!.Value, "account_type"));
        Assert.Equal(0L, await CountCredentialsAsync(database));
    }

    [PostgreSqlFact]
    public async Task StaffAccountLifecycleRequiresOwnerActor()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var service = Service(dbContext);

        var result = await service.CreateStaffAccountAsync(
            AdminEnvelope(),
            AccountKind.NamedAdmin,
            "Main Admin");

        Assert.Equal(StaffAccountLifecycleStatus.PermissionDenied, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(0L, await CountStaffAccountsAsync(database));
    }

    [PostgreSqlFact]
    public async Task StaffAccountLifecycleRejectsOwnerAccountCreation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var service = Service(dbContext);

        var result = await service.CreateStaffAccountAsync(
            OwnerEnvelope(),
            AccountKind.Owner,
            "Second Owner");

        Assert.Equal(StaffAccountLifecycleStatus.ValidationFailed, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(0L, await CountStaffAccountsAsync(database));
    }

    [PostgreSqlFact]
    public async Task StaffAccountLifecycleUpdatesDisplayName()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var service = Service(dbContext);
        var createResult = await service.CreateStaffAccountAsync(
            OwnerEnvelope(),
            AccountKind.NamedAdmin,
            "Main Admin");

        var updateResult = await service.UpdateStaffAccountDisplayNameAsync(
            OwnerEnvelope(),
            createResult.AccountId!.Value,
            "  Updated Admin  ");

        Assert.Equal(StaffAccountLifecycleStatus.DisplayNameUpdated, updateResult.Status);
        Assert.Equal(
            "Updated Admin",
            await ReadAccountValueAsync<string>(database, createResult.AccountId.Value, "display_name"));
    }

    [PostgreSqlFact]
    public async Task StaffAccountLifecycleDeactivatesSessionsAndReactivatesAccount()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var service = Service(dbContext);
        var createResult = await service.CreateStaffAccountAsync(
            OwnerEnvelope(),
            AccountKind.SharedReceptionAdmin,
            "Front desk shared");
        var accountId = createResult.AccountId!.Value;
        await InsertActiveSessionAsync(database.ConnectionString, accountId);

        var deactivateResult = await service.SetStaffAccountActiveStateAsync(
            OwnerEnvelope(),
            accountId,
            isActive: false);
        var reactivateResult = await service.SetStaffAccountActiveStateAsync(
            OwnerEnvelope(),
            accountId,
            isActive: true);

        Assert.Equal(StaffAccountLifecycleStatus.Deactivated, deactivateResult.Status);
        Assert.Equal(StaffAccountLifecycleStatus.Activated, reactivateResult.Status);
        Assert.True(await ReadAccountValueAsync<bool>(database, accountId, "is_active"));
        Assert.Null(await ReadAccountValueAsync<DateTimeOffset?>(database, accountId, "deactivated_at"));
        Assert.Equal(0L, await CountSessionsAsync(database, accountId, ended: false));
        Assert.Equal(1L, await CountSessionsAsync(database, accountId, ended: true));
    }

    [PostgreSqlFact]
    public async Task StaffAccountLifecycleProtectsOwnerAccount()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var ownerBootstrapper = new OwnerBootstrapper(dbContext, FixedClock());
        var ownerResult = await ownerBootstrapper.BootstrapOwnerAsync("BodyLife Owner");
        var service = Service(dbContext);

        var updateResult = await service.UpdateStaffAccountDisplayNameAsync(
            OwnerEnvelope(),
            ownerResult.AccountId!.Value,
            "Renamed Owner");
        var deactivateResult = await service.SetStaffAccountActiveStateAsync(
            OwnerEnvelope(),
            ownerResult.AccountId.Value,
            isActive: false);

        Assert.Equal(StaffAccountLifecycleStatus.OwnerAccountProtected, updateResult.Status);
        Assert.Equal(StaffAccountLifecycleStatus.OwnerAccountProtected, deactivateResult.Status);
        Assert.Equal(
            "BodyLife Owner",
            await ReadAccountValueAsync<string>(database, ownerResult.AccountId.Value, "display_name"));
        Assert.True(await ReadAccountValueAsync<bool>(database, ownerResult.AccountId.Value, "is_active"));
    }

    private static StaffAccountLifecycleService Service(BodyLifeDbContext dbContext)
    {
        return new StaffAccountLifecycleService(
            dbContext,
            new BusinessAuditAppender(dbContext),
            FixedClock());
    }

    private static CommandEnvelope OwnerEnvelope()
    {
        return Envelope(ActorRole.Owner, AccountKind.Owner);
    }

    private static CommandEnvelope AdminEnvelope()
    {
        return Envelope(ActorRole.Admin, AccountKind.NamedAdmin);
    }

    private static CommandEnvelope Envelope(ActorRole role, AccountKind accountKind)
    {
        return new CommandEnvelope(
            new ActorContext(
                AccountId.New(),
                role,
                accountKind,
                SessionId.New(),
                "test device"),
            new RequestCorrelationId("test-correlation"),
            EntryOrigin.Normal,
            OccurredAt: null,
            IdempotencyKey: null,
            Reason: null,
            Comment: null);
    }

    private static TimeProvider FixedClock()
    {
        return new FixedTimeProvider(TestNow);
    }

    private static async Task<T?> ReadAccountValueAsync<T>(
        PostgreSqlTestDatabase database,
        Guid accountId,
        string columnName)
    {
        return await database.ExecuteScalarAsync<T>(
            $"""
            select {columnName}
            from bodylife.accounts
            where id = '{accountId}'::uuid
            """);
    }

    private static Task<long> CountStaffAccountsAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>(
            """
            select count(*)
            from bodylife.accounts
            where account_type in ('named_admin', 'shared_reception_admin')
            """);
    }

    private static Task<long> CountCredentialsAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>("select count(*) from bodylife.account_credentials");
    }

    private static Task<long> CountSessionsAsync(PostgreSqlTestDatabase database, Guid accountId, bool ended)
    {
        var predicate = ended
            ? "ended_at is not null"
            : "ended_at is null";

        return database.ExecuteScalarAsync<long>(
            $"""
            select count(*)
            from bodylife.sessions
            where account_id = '{accountId}'::uuid
              and {predicate}
            """);
    }

    private static async Task InsertActiveSessionAsync(string connectionString, Guid accountId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            insert into bodylife.sessions (
                id,
                account_id,
                device_label,
                started_at,
                ended_at,
                last_seen_at)
            values (
                @id,
                @account_id,
                @device_label,
                @started_at,
                @ended_at,
                @last_seen_at)
            """;

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("device_label", "front desk tablet");
        command.Parameters.AddWithValue("started_at", TestNow);
        command.Parameters.Add("ended_at", NpgsqlDbType.TimestampTz).Value = DBNull.Value;
        command.Parameters.AddWithValue("last_seen_at", TestNow);

        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
