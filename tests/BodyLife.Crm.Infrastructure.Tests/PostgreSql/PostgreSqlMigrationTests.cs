using BodyLife.Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMigrationTests
{
    [PostgreSqlFact]
    public async Task InitialBaselineMigrationAppliesToCleanPostgreSqlDatabase()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
        var schemaExists = await database.ExecuteScalarAsync<bool>(
            "select exists (select 1 from information_schema.schemata where schema_name = 'bodylife')");
        var historyTableName = await database.ExecuteScalarAsync<string>(
            """
            select n.nspname || '.' || c.relname
            from pg_class c
            join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = 'bodylife'
              and c.relname = '__ef_migrations_history'
              and c.relkind = 'r'
            """);

        Assert.Contains("20260708140900_InitialBaseline", appliedMigrations);
        Assert.Contains("20260709204232_AddBusinessAuditEntries", appliedMigrations);
        Assert.Contains("20260710093311_AddSessionExpiry", appliedMigrations);
        Assert.Contains("20260710111409_AddClientsSearchStorage", appliedMigrations);
        Assert.Contains("20260710113814_AddDuplicateWarningAcknowledgements", appliedMigrations);
        Assert.Contains("20260720100603_HardenBusinessAuditAppendOnly", appliedMigrations);
        Assert.True(schemaExists);
        Assert.Equal($"bodylife.{BodyLifeDbContextOptions.MigrationsHistoryTable}", historyTableName);
    }
}
