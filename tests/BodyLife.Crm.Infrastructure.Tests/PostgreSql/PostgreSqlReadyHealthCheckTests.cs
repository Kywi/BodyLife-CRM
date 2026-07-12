using System.Net;
using BodyLife.Crm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlReadyHealthCheckTests
{
    [PostgreSqlFact]
    public async Task ReadyHealthEndpointIsHealthyWhenPostgreSqlIsMigrated()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        using var factory = CreateFactory(database.ConnectionString);

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });

        using var response = await client.GetAsync("/health/ready");
        var content = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected /health/ready to return OK, got {response.StatusCode}. Body: {content}");
        Assert.Contains("\"status\":\"Healthy\"", content, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"postgresql\"", content, StringComparison.Ordinal);
        Assert.Contains(
            "\"description\":\"PostgreSQL connection is available and the schema is current.\"",
            content,
            StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task ReadyHealthEndpointIsUnhealthyWhenMigrationsArePending()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        var migrator = dbContext.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260709204232_AddBusinessAuditEntries");

        using var factory = CreateFactory(database.ConnectionString);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });

        using var response = await client.GetAsync("/health/ready");
        var content = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"Expected /health/ready to return ServiceUnavailable, got {response.StatusCode}. Body: {content}");
        Assert.Contains("\"status\":\"Unhealthy\"", content, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"postgresql\"", content, StringComparison.Ordinal);
        Assert.Contains(
            "\"description\":\"PostgreSQL schema has pending EF Core migrations.\"",
            content,
            StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<BodyLifeDbContext>();
                    services.RemoveAll<DbContextOptions<BodyLifeDbContext>>();
                    services.AddDbContext<BodyLifeDbContext>(
                        options => BodyLifeDbContextOptions.Configure(options, connectionString));
                });
            });
    }
}
