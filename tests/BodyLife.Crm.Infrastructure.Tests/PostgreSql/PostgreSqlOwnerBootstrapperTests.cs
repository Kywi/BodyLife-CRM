using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlOwnerBootstrapperTests
{
    [PostgreSqlFact]
    public async Task OwnerBootstrapperCreatesOwnerAccount()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();
        var bootstrapper = new OwnerBootstrapper(dbContext, TimeProvider.System);

        var result = await bootstrapper.BootstrapOwnerAsync("  BodyLife Owner  ");

        var ownerCount = await CountOwnerAccountsAsync(database);
        var displayName = await database.ExecuteScalarAsync<string>(
            "select display_name from bodylife.accounts where account_type = 'owner'");
        var isActive = await database.ExecuteScalarAsync<bool>(
            "select is_active from bodylife.accounts where account_type = 'owner'");

        Assert.Equal(OwnerBootstrapStatus.Created, result.Status);
        Assert.NotNull(result.AccountId);
        Assert.Equal(1L, ownerCount);
        Assert.Equal("BodyLife Owner", displayName);
        Assert.True(isActive);
    }

    [PostgreSqlFact]
    public async Task OwnerBootstrapperIsIdempotentWhenOwnerExists()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();
        var bootstrapper = new OwnerBootstrapper(dbContext, TimeProvider.System);

        var firstResult = await bootstrapper.BootstrapOwnerAsync("Original Owner");
        var secondResult = await bootstrapper.BootstrapOwnerAsync("Replacement Owner");

        var ownerCount = await CountOwnerAccountsAsync(database);
        var displayName = await database.ExecuteScalarAsync<string>(
            "select display_name from bodylife.accounts where account_type = 'owner'");

        Assert.Equal(OwnerBootstrapStatus.Created, firstResult.Status);
        Assert.Equal(OwnerBootstrapStatus.AlreadyExists, secondResult.Status);
        Assert.Equal(firstResult.AccountId, secondResult.AccountId);
        Assert.Equal(1L, ownerCount);
        Assert.Equal("Original Owner", displayName);
    }

    [PostgreSqlFact]
    public async Task OwnerBootstrapperRejectsBlankDisplayNameWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();
        var bootstrapper = new OwnerBootstrapper(dbContext, TimeProvider.System);

        var result = await bootstrapper.BootstrapOwnerAsync(" ");

        var ownerCount = await CountOwnerAccountsAsync(database);

        Assert.Equal(OwnerBootstrapStatus.ValidationFailed, result.Status);
        Assert.Null(result.AccountId);
        Assert.Equal(0L, ownerCount);
    }

    private static Task<long> CountOwnerAccountsAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.accounts where account_type = 'owner'");
    }
}
