using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence;

public static class BodyLifeDbContextOptions
{
    public const string ConnectionStringName = "BodyLife";
    public const string ConnectionStringEnvironmentVariable = "BODYLIFE_CONNECTION_STRING";
    public const string DefaultSchema = "bodylife";
    public const string MigrationsHistoryTable = "__ef_migrations_history";
    public const string LocalDevelopmentConnectionString =
        "Host=localhost;Port=55432;Database=bodylife_crm_dev;Username=bodylife;Password=bodylife_dev_password";

    public static void Configure(DbContextOptionsBuilder builder, string connectionString)
    {
        builder.UseNpgsql(
            connectionString,
            npgsqlOptions => npgsqlOptions
                .MigrationsAssembly(typeof(BodyLifeDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(MigrationsHistoryTable, DefaultSchema));
    }
}
