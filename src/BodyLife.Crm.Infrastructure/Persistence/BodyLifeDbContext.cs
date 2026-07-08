using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence;

public sealed class BodyLifeDbContext(DbContextOptions<BodyLifeDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(BodyLifeDbContextOptions.DefaultSchema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BodyLifeDbContext).Assembly);
    }
}
