using BodyLife.Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BodyLife.Crm.Web.Operations;

internal sealed class PostgreSqlHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BodyLifeDbContext>();

        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection is unavailable.");
        }

        var pendingMigrations = await dbContext.Database
            .GetPendingMigrationsAsync(cancellationToken);

        if (pendingMigrations.Any())
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL schema has pending EF Core migrations.");
        }

        return HealthCheckResult.Healthy(
            "PostgreSQL connection is available and the schema is current.");
    }
}
