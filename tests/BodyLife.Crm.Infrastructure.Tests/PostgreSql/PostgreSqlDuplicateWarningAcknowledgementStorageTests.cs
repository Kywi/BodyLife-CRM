using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlDuplicateWarningAcknowledgementStorageTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 10, 11, 45, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task MigrationCreatesAcknowledgementTableConstraintsAndIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        Assert.True(await TableExistsAsync(database));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_duplicate_warning_acknowledgements_warning_type"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_duplicate_warning_acknowledgements_distinct_clients"));
        Assert.True(await ConstraintExistsAsync(
            database,
            "ck_duplicate_warning_acknowledgements_reason_not_empty"));
        Assert.True(await ConstraintExistsAsync(database, "fk_duplicate_warning_acks_client"));
        Assert.True(await ConstraintExistsAsync(database, "fk_duplicate_warning_acks_matched_client"));
        Assert.True(await ConstraintExistsAsync(database, "fk_duplicate_warning_acks_actor"));
        Assert.Equal(3L, await RestrictedForeignKeyCountAsync(database));
        Assert.True(await IndexExistsAsync(database, "ix_duplicate_warning_acks_client_timeline"));
        Assert.True(await IndexExistsAsync(database, "ix_duplicate_warning_acks_match_timeline"));
        Assert.True(await IndexExistsAsync(database, "ix_duplicate_warning_acks_actor"));
    }

    [PostgreSqlFact]
    public async Task SupportedWarningTypesAndRepeatedAcknowledgementsCanBePersisted()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedClientsAsync(dbContext);

        await InsertAcknowledgementAsync(
            dbContext,
            fixture.ClientId,
            fixture.MatchedClientId,
            fixture.ActorAccountId,
            "duplicate_phone",
            TestNow,
            "Confirmed that these are different people");
        await InsertAcknowledgementAsync(
            dbContext,
            fixture.ClientId,
            fixture.MatchedClientId,
            fixture.ActorAccountId,
            "similar_name",
            TestNow.AddMinutes(1),
            "Identity details were checked");
        await InsertAcknowledgementAsync(
            dbContext,
            fixture.ClientId,
            fixture.MatchedClientId,
            fixture.ActorAccountId,
            "duplicate_phone",
            TestNow.AddMinutes(2),
            "Phone ownership was checked again during update");

        var acknowledgementCount = await database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.duplicate_warning_acknowledgements");
        Assert.Equal(3L, acknowledgementCount);
    }

    [PostgreSqlFact]
    public async Task UnsupportedWarningTypeIsRejected()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedClientsAsync(dbContext);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => InsertAcknowledgementAsync(
                dbContext,
                fixture.ClientId,
                fixture.MatchedClientId,
                fixture.ActorAccountId,
                "duplicate_card",
                TestNow,
                "Unsupported warning"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_duplicate_warning_acknowledgements_warning_type", exception.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task AcknowledgementMustReferenceDifferentClients()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedClientsAsync(dbContext);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => InsertAcknowledgementAsync(
                dbContext,
                fixture.ClientId,
                fixture.ClientId,
                fixture.ActorAccountId,
                "similar_name",
                TestNow,
                "Self match"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_duplicate_warning_acknowledgements_distinct_clients", exception.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task AcknowledgementReasonMustNotBeBlank()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedClientsAsync(dbContext);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => InsertAcknowledgementAsync(
                dbContext,
                fixture.ClientId,
                fixture.MatchedClientId,
                fixture.ActorAccountId,
                "duplicate_phone",
                TestNow,
                "   "));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_duplicate_warning_acknowledgements_reason_not_empty", exception.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task AcknowledgementPreventsMatchedClientDeletion()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedClientsAsync(dbContext);
        await InsertAcknowledgementAsync(
            dbContext,
            fixture.ClientId,
            fixture.MatchedClientId,
            fixture.ActorAccountId,
            "duplicate_phone",
            TestNow,
            "Confirmed that these are different people");

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"delete from bodylife.clients where id = {fixture.MatchedClientId}"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal("fk_duplicate_warning_acks_matched_client", exception.ConstraintName);
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.duplicate_warning_acknowledgements"));
    }

    private static async Task<ClientsFixture> SeedClientsAsync(BodyLifeDbContext dbContext)
    {
        var bootstrapResult = await new OwnerBootstrapper(dbContext, new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrapResult.Status);

        var fixture = new ClientsFixture(
            bootstrapResult.AccountId!.Value,
            Guid.NewGuid(),
            Guid.NewGuid());
        await InsertClientAsync(dbContext, fixture.ClientId, fixture.ActorAccountId, "Ivanenko");
        await InsertClientAsync(dbContext, fixture.MatchedClientId, fixture.ActorAccountId, "Ivanova");
        return fixture;
    }

    private static Task<int> InsertClientAsync(
        BodyLifeDbContext dbContext,
        Guid clientId,
        Guid actorAccountId,
        string surname)
    {
        var normalizedFullName = $"{surname.ToUpperInvariant()} IVAN";
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into bodylife.clients (
                id,
                surname,
                name,
                normalized_full_name,
                operational_status,
                created_at,
                created_by_account_id,
                updated_at)
            values (
                {clientId},
                {surname},
                'Ivan',
                {normalizedFullName},
                'active',
                {TestNow},
                {actorAccountId},
                {TestNow})
            """);
    }

    private static Task<int> InsertAcknowledgementAsync(
        BodyLifeDbContext dbContext,
        Guid clientId,
        Guid matchedClientId,
        Guid actorAccountId,
        string warningType,
        DateTimeOffset acknowledgedAt,
        string reason)
    {
        var acknowledgementId = Guid.NewGuid();
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into bodylife.duplicate_warning_acknowledgements (
                id,
                client_id,
                warning_type,
                matched_client_id,
                acknowledged_by_account_id,
                acknowledged_at,
                reason)
            values (
                {acknowledgementId},
                {clientId},
                {warningType},
                {matchedClientId},
                {actorAccountId},
                {acknowledgedAt},
                {reason})
            """);
    }

    private static Task<bool> TableExistsAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<bool>(
            """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'bodylife'
                  and table_name = 'duplicate_warning_acknowledgements'
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
                  and table_name = 'duplicate_warning_acknowledgements'
                  and constraint_name = '{constraintName}'
            )
            """);
    }

    private static Task<long> RestrictedForeignKeyCountAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>(
            """
            select count(*)
            from information_schema.referential_constraints
            where constraint_schema = 'bodylife'
              and constraint_name in (
                  'fk_duplicate_warning_acks_client',
                  'fk_duplicate_warning_acks_matched_client',
                  'fk_duplicate_warning_acks_actor')
              and delete_rule = 'RESTRICT'
            """);
    }

    private static Task<bool> IndexExistsAsync(
        PostgreSqlTestDatabase database,
        string indexName)
    {
        return database.ExecuteScalarAsync<bool>(
            $"""
            select exists (
                select 1
                from pg_indexes
                where schemaname = 'bodylife'
                  and tablename = 'duplicate_warning_acknowledgements'
                  and indexname = '{indexName}'
            )
            """);
    }

    private sealed record ClientsFixture(
        Guid ActorAccountId,
        Guid ClientId,
        Guid MatchedClientId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
