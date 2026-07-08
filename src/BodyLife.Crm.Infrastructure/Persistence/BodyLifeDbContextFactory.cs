using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BodyLife.Crm.Infrastructure.Persistence;

public sealed class BodyLifeDbContextFactory : IDesignTimeDbContextFactory<BodyLifeDbContext>
{
    public BodyLifeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(BodyLifeDbContextOptions.ConnectionStringEnvironmentVariable)
            ?? BodyLifeDbContextOptions.LocalDevelopmentConnectionString;

        var builder = new DbContextOptionsBuilder<BodyLifeDbContext>();
        BodyLifeDbContextOptions.Configure(builder, connectionString);

        return new BodyLifeDbContext(builder.Options);
    }
}
