using BodyLife.Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMigrationTests
{
    [PostgreSqlFact]
    public async Task InitialBaselineAppliesExactlyOnceToCleanPostgreSqlDatabase()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();
        await dbContext.Database.MigrateAsync();

        var appliedMigrations = (await dbContext.Database
                .GetAppliedMigrationsAsync())
            .ToArray();
        var pendingMigrations = await dbContext.Database
            .GetPendingMigrationsAsync();
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
        var criticalTriggerCount = await database.ExecuteScalarAsync<long>(
            """
            select count(*)
            from pg_trigger trigger
            join pg_class relation on relation.oid = trigger.tgrelid
            join pg_namespace schema on schema.oid = relation.relnamespace
            where schema.nspname = 'bodylife'
              and not trigger.tgisinternal
              and trigger.tgname in (
                'trg_business_audit_entries_append_only',
                'ck_issued_memberships_exact_sale_payment',
                'ck_negative_closures_consistent',
                'ck_issued_sale_corrections_lifecycle',
                'ck_entry_batch_row_entities_exact_shape',
                'ck_issued_memberships_lifecycle_closure',
                'ck_membership_lifecycle_closures_status',
                'ck_membership_lifecycle_closures_append_only',
                'ck_membership_lifecycle_closures_no_truncate',
                'ck_membership_lifecycle_closures_paper_link')
            """);

        var migration = Assert.Single(appliedMigrations);
        Assert.EndsWith("_InitialBaseline", migration, StringComparison.Ordinal);
        Assert.Empty(pendingMigrations);
        Assert.False(dbContext.Database.HasPendingModelChanges());
        Assert.True(schemaExists);
        Assert.Equal(
            $"bodylife.{BodyLifeDbContextOptions.MigrationsHistoryTable}",
            historyTableName);
        Assert.Equal(10L, criticalTriggerCount);
    }

    [PostgreSqlFact]
    public async Task InitialBaselineRollsBackAndReappliesCleanly()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();

        await migrator.MigrateAsync();
        await migrator.MigrateAsync(Migration.InitialDatabase);

        var accountsTableExistsAfterRollback = await database.ExecuteScalarAsync<bool>(
            """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'bodylife'
                  and table_name = 'accounts')
            """);
        Assert.False(accountsTableExistsAfterRollback);

        await migrator.MigrateAsync();

        var appliedMigrations = await dbContext.Database
            .GetAppliedMigrationsAsync();
        Assert.Single(appliedMigrations);
    }
}
