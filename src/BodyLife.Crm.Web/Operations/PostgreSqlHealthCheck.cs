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

        if (await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            return HealthCheckResult.Healthy("PostgreSQL connection is available.");
        }

        return HealthCheckResult.Unhealthy("PostgreSQL connection is unavailable.");
    }
}
