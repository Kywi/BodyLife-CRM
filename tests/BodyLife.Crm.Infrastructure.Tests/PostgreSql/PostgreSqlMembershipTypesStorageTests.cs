using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMembershipTypesStorageTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 12, 19, 30, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task MigrationCreatesMembershipTypesTableConstraintsAndActiveIssueIndex()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        Assert.True(await TableExistsAsync(database, "membership_types"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_membership_types_duration_positive"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_membership_types_visits_non_negative"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_membership_types_price_non_negative"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_membership_types_lifecycle"));

        var activeIssueIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_membership_types_active_issue_order");
        Assert.Contains("(name, id)", activeIssueIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE is_active", activeIssueIndex, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNIQUE INDEX", activeIssueIndex, StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    public async Task CatalogAcceptsActiveAndInactiveRowsIncludingDuplicateNames()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        await InsertMembershipTypeAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            name: "Morning eight",
            visitsLimit: 0,
            priceAmount: 0m);
        await InsertMembershipTypeAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            name: "Morning eight",
            isActive: false,
            updatedAt: TestNow.AddMinutes(1),
            deactivatedAt: TestNow.AddMinutes(1));

        var totalCount = await database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.membership_types where name = 'Morning eight'");
        var activeCount = await database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.membership_types where name = 'Morning eight' and is_active");

        Assert.Equal(2L, totalCount);
        Assert.Equal(1L, activeCount);
    }

    [PostgreSqlFact]
    public async Task CatalogRejectsInvalidDurationVisitsAndPrice()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        await AssertCheckViolationAsync(
            () => InsertMembershipTypeAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                durationDays: 0),
            "ck_membership_types_duration_positive");
        await AssertCheckViolationAsync(
            () => InsertMembershipTypeAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                visitsLimit: -1),
            "ck_membership_types_visits_non_negative");
        await AssertCheckViolationAsync(
            () => InsertMembershipTypeAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                priceAmount: -0.01m),
            "ck_membership_types_price_non_negative");
    }

    [PostgreSqlFact]
    public async Task CatalogRejectsNonCanonicalTextAndCurrency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        await AssertCheckViolationAsync(
            () => InsertMembershipTypeAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                name: "   "),
            "ck_membership_types_name_not_empty");
        await AssertCheckViolationAsync(
            () => InsertMembershipTypeAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                priceCurrency: "uah"),
            "ck_membership_types_currency_canonical");
        await AssertCheckViolationAsync(
            () => InsertMembershipTypeAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                comment: "   "),
            "ck_membership_types_comment_not_empty");
    }

    [PostgreSqlFact]
    public async Task CatalogEnforcesDeactivationLifecycleAndPreservesTheRow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        await AssertCheckViolationAsync(
            () => InsertMembershipTypeAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                isActive: true,
                deactivatedAt: TestNow),
            "ck_membership_types_lifecycle");
        await AssertCheckViolationAsync(
            () => InsertMembershipTypeAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                isActive: false),
            "ck_membership_types_lifecycle");
        await AssertCheckViolationAsync(
            () => InsertMembershipTypeAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                updatedAt: TestNow.AddMinutes(-1)),
            "ck_membership_types_updated_after_created");
        await AssertCheckViolationAsync(
            () => InsertMembershipTypeAsync(
                database.ConnectionString,
                Guid.NewGuid(),
                isActive: false,
                updatedAt: TestNow.AddMinutes(1),
                deactivatedAt: TestNow.AddMinutes(2)),
            "ck_membership_types_lifecycle");

        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database.ConnectionString, membershipTypeId);
        await DeactivateMembershipTypeAsync(database.ConnectionString, membershipTypeId);

        var rowCount = await database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.membership_types where id = '{membershipTypeId}' and not is_active and deactivated_at is not null");
        Assert.Equal(1L, rowCount);
    }

    private static async Task InsertMembershipTypeAsync(
        string connectionString,
        Guid membershipTypeId,
        string name = "Eight visits",
        int durationDays = 30,
        int visitsLimit = 8,
        decimal priceAmount = 1200m,
        string priceCurrency = "UAH",
        bool isActive = true,
        string? comment = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? deactivatedAt = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.membership_types (
                id,
                name,
                duration_days,
                visits_limit,
                price_amount,
                price_currency,
                is_active,
                comment,
                created_at,
                updated_at,
                deactivated_at)
            values (
                @id,
                @name,
                @duration_days,
                @visits_limit,
                @price_amount,
                @price_currency,
                @is_active,
                @comment,
                @created_at,
                @updated_at,
                @deactivated_at)
            """;
        command.Parameters.AddWithValue("id", membershipTypeId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("duration_days", durationDays);
        command.Parameters.AddWithValue("visits_limit", visitsLimit);
        command.Parameters.AddWithValue("price_amount", priceAmount);
        command.Parameters.AddWithValue("price_currency", priceCurrency);
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value = comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("created_at", createdAt ?? TestNow);
        command.Parameters.AddWithValue("updated_at", updatedAt ?? TestNow);
        command.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value =
            deactivatedAt ?? (object)DBNull.Value;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeactivateMembershipTypeAsync(
        string connectionString,
        Guid membershipTypeId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_types
            set is_active = false,
                updated_at = @updated_at,
                deactivated_at = @deactivated_at
            where id = @id
            """;
        command.Parameters.AddWithValue("id", membershipTypeId);
        command.Parameters.AddWithValue("updated_at", TestNow.AddMinutes(1));
        command.Parameters.AddWithValue("deactivated_at", TestNow.AddMinutes(1));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertCheckViolationAsync(
        Func<Task> action,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(constraintName, exception.ConstraintName);
    }

    private static Task<bool> TableExistsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<bool>(
            $"""
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'bodylife'
                  and table_name = '{tableName}'
            )
            """);
    }

    private static Task<bool> ConstraintExistsAsync(
        PostgreSqlTestDatabase database,
        string constraintName)
    {
        return database.ExecuteScalarAsync<bool>(
            $"""
            select exists (
                select 1
                from information_schema.table_constraints
                where constraint_schema = 'bodylife'
                  and table_name = 'membership_types'
                  and constraint_name = '{constraintName}'
            )
            """);
    }

    private static async Task<string> ReadIndexDefinitionAsync(
        PostgreSqlTestDatabase database,
        string indexName)
    {
        return await database.ExecuteScalarAsync<string>(
            $"""
            select indexdef
            from pg_indexes
            where schemaname = 'bodylife'
              and tablename = 'membership_types'
              and indexname = '{indexName}'
            """)
            ?? throw new InvalidOperationException($"Index '{indexName}' was not found.");
    }
}
