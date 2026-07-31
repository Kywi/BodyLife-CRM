using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlIssueMembershipNegativeCoverageCommandTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        20,
        12,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset CatalogUpdatedAt = TestNow.AddDays(-1);

    [PostgreSqlFact]
    public async Task PartialCoverageBackdatesIssueConsumesLimitAndRebuildsBothSides()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(database, sourceVisitCount: 5);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId, expectedNegative: 3);
        var preview = await new PreviewIssueMembershipQueryHandler(
                dbContext,
                new MembershipNegativeVisitSelector(dbContext),
                new FixedTimeProvider(TestNow))
            .ExecuteAsync(
                new PreviewIssueMembershipQuery(
                    fixture.Actor,
                    fixture.ClientId,
                    fixture.CoveringTypeId,
                    new DateOnly(2026, 7, 20),
                    MembershipNegativeHandlingDecision.CoverWithNewMembership,
                    NegativeCoverageCount: 2),
                CancellationToken.None);
        Assert.Equal(PreviewIssueMembershipStatus.Success, preview.Status);
        Assert.True(preview.Preview!.CanProceedToIssue);
        Assert.Equal(fixture.VisitIds[2], preview.Preview.ExpectedOldestOpenNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 3), preview.Preview.ProposedStartDate);
        Assert.Equal(2, preview.Preview.CoveredNegativeVisitCount);
        Assert.Equal(1, preview.Preview.RemainingExistingNegativeBalance);
        Assert.Equal(0, preview.Preview.ExpectedInitialRemainingVisits);
        var command = CreateCommand(
            fixture,
            "cover-two",
            coverageCount: 2,
            fixture.VisitIds[2]);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);

        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Equal(new EntityId("client", fixture.ClientId), result.RereadTargetId);
        Assert.Contains(MembershipWarningCodes.NegativeBalance, result.Warnings);
        Assert.DoesNotContain(MembershipWarningCodes.ExpiredByDate, result.Warnings);
        var coveringMembershipId = result.PrimaryEntityId!.Value.Value;
        var closureId = await database.ExecuteScalarAsync<Guid>(
            $"select id from bodylife.membership_negative_closures where covering_membership_id = '{coveringMembershipId}'");

        Assert.Equal(
            1,
            await ReadCacheValueAsync(
                database,
                fixture.SourceMembershipId,
                "negative_balance"));
        Assert.Equal(
            2,
            await ReadCacheValueAsync(database, coveringMembershipId, "counted_visits"));
        Assert.Equal(
            0,
            await ReadCacheValueAsync(database, coveringMembershipId, "remaining_visits"));
        Assert.Equal(
            new DateOnly(2026, 7, 3),
            await database.ExecuteScalarAsync<DateOnly>(
                $"select start_date from bodylife.issued_memberships where id = '{coveringMembershipId}'"));
        Assert.Equal(
            new DateOnly(2026, 8, 1),
            await database.ExecuteScalarAsync<DateOnly>(
                $"select base_end_date from bodylife.issued_memberships where id = '{coveringMembershipId}'"));
        Assert.Equal(
            1200m,
            await database.ExecuteScalarAsync<decimal>(
                $"select amount from bodylife.payments where membership_id = '{coveringMembershipId}' and status = 'active'"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.payments where negative_closure_id = '{closureId}'"));
        Assert.Equal(
            2L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.membership_negative_closure_items where negative_closure_id = '{closureId}' and status = 'active'"));
        Assert.Equal(
            fixture.VisitIds[2],
            await database.ExecuteScalarAsync<Guid>(
                $"select visit_id from bodylife.membership_negative_closure_items where negative_closure_id = '{closureId}' and sequence = 1"));
        Assert.Equal(
            fixture.VisitIds[3],
            await database.ExecuteScalarAsync<Guid>(
                $"select visit_id from bodylife.membership_negative_closure_items where negative_closure_id = '{closureId}' and sequence = 2"));
        Assert.Equal(
            2L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.visit_consumptions where membership_id = '{coveringMembershipId}' and consumption_type = 'negative_coverage' and status = 'active'"));
        Assert.Equal(
            5L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.visit_consumptions where membership_id = '{fixture.SourceMembershipId}' and consumption_type = 'counted' and status = 'active'"));
        Assert.Equal(
            3L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.business_audit_entries where entity_id = '{closureId}' and action_type = 'membership_negative_closure.created'"));
    }

    [PostgreSqlFact]
    public async Task OneVisitCoverageCanFullyClearNegativeAndLeaveUnusedLimit()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(database, sourceVisitCount: 3);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId, expectedNegative: 1);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                fixture,
                "cover-one",
                coverageCount: 1,
                fixture.VisitIds[2]),
            CancellationToken.None);

        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Empty(result.Warnings);
        var coveringMembershipId = result.PrimaryEntityId!.Value.Value;
        Assert.Equal(
            0,
            await ReadCacheValueAsync(
                database,
                fixture.SourceMembershipId,
                "negative_balance"));
        Assert.Equal(
            1,
            await ReadCacheValueAsync(database, coveringMembershipId, "counted_visits"));
        Assert.Equal(
            1,
            await ReadCacheValueAsync(database, coveringMembershipId, "remaining_visits"));
    }

    [PostgreSqlFact]
    public async Task InvalidCountsStaleOldestAndExpiredCoverageHaveStableOutcomes()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            sourceVisitCount: 3,
            coveringDurationDays: 1);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId, expectedNegative: 1);
        var handler = CreateHandler(dbContext);

        var zero = await handler.ExecuteAsync(
            CreateCommand(fixture, "cover-zero", 0, fixture.VisitIds[2]),
            CancellationToken.None);
        var overLimit = await handler.ExecuteAsync(
            CreateCommand(fixture, "cover-over-limit", 3, fixture.VisitIds[2]),
            CancellationToken.None);
        var stale = await handler.ExecuteAsync(
            CreateCommand(fixture, "cover-stale", 1, Guid.NewGuid()),
            CancellationToken.None);

        AssertError(zero, CommandErrorCode.ValidationFailed, "negativeCoverageCount");
        AssertError(
            overLimit,
            CommandErrorCode.ValidationFailed,
            "negativeCoverageCount");
        AssertError(
            stale,
            CommandErrorCode.StaleState,
            "expectedOldestOpenNegativeVisitId");
        await AssertNoNewIssueAsync(database);

        var expired = await handler.ExecuteAsync(
            CreateCommand(fixture, "cover-expired", 1, fixture.VisitIds[2]),
            CancellationToken.None);

        Assert.Equal(CommandStatus.Success, expired.Status);
        Assert.Contains(MembershipWarningCodes.ExpiredByDate, expired.Warnings);
        Assert.DoesNotContain(MembershipWarningCodes.NegativeBalance, expired.Warnings);
        var coveringMembershipId = expired.PrimaryEntityId!.Value.Value;
        Assert.Equal(
            new DateOnly(2026, 7, 3),
            await database.ExecuteScalarAsync<DateOnly>(
                $"select effective_end_date from bodylife.membership_state_cache where membership_id = '{coveringMembershipId}'"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentRequestsCannotCoverTheSameOldestVisitTwice()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(database, sourceVisitCount: 5);
        await using (var rebuildContext = database.CreateDbContext())
        {
            await RebuildSourceAsync(
                rebuildContext,
                fixture.SourceMembershipId,
                expectedNegative: 3);
        }

        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(
                CreateCommand(
                    fixture,
                    "concurrent-cover-a",
                    1,
                    fixture.VisitIds[2]),
                CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(
                CreateCommand(
                    fixture,
                    "concurrent-cover-b",
                    1,
                    fixture.VisitIds[2]),
                CancellationToken.None));

        Assert.Single(results, result => result.Status == CommandStatus.Success);
        var stale = Assert.Single(results, result => result.Status == CommandStatus.Error);
        AssertError(
            stale,
            CommandErrorCode.StaleState,
            "expectedOldestOpenNegativeVisitId");
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_negative_closures"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.membership_negative_closure_items where visit_id = '{fixture.VisitIds[2]}' and status = 'active'"));
        Assert.Equal(
            2,
            await ReadCacheValueAsync(
                database,
                fixture.SourceMembershipId,
                "negative_balance"));
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        return database;
    }

    private static async Task<CoverageFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        int sourceVisitCount,
        int coveringDurationDays = 30)
    {
        var fixture = new CoverageFixture(
            new ActorContext(
                AccountId.New(),
                ActorRole.Admin,
                AccountKind.NamedAdmin,
                SessionId.New(),
                "coverage test"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Enumerable.Range(0, sourceVisitCount)
                .Select(_ => Guid.NewGuid())
                .ToArray(),
            Enumerable.Range(0, sourceVisitCount)
                .Select(_ => Guid.NewGuid())
                .ToArray());
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into bodylife.accounts (
                id, display_name, account_type, role, is_active, created_at, deactivated_at)
            values (
                @account_id, 'Coverage actor', 'named_admin', 'admin', true, @now, null);

            insert into bodylife.sessions (
                id, account_id, device_label, started_at, expires_at, ended_at, last_seen_at)
            values (
                @session_id, @account_id, 'coverage test', @now,
                @expires_at, null, @now);

            insert into bodylife.clients (
                id, surname, name, patronymic, normalized_full_name,
                phone_raw, phone_normalized, phone_last4, comment,
                operational_status, created_at, created_by_account_id, updated_at)
            values (
                @client_id, 'Coverage', 'Client', null, 'COVERAGE CLIENT',
                null, null, null, null, 'active', @now, @account_id, @now);

            insert into bodylife.membership_types (
                id, name, kind, duration_days, visits_limit, price_amount,
                price_currency, is_active, comment, created_at, updated_at, deactivated_at)
            values
                (@source_type_id, 'Two visits', 'ordinary', 30, 2, 900,
                    'UAH', true, null, @created_at, @catalog_updated_at, null),
                (@covering_type_id, 'Cover plan', 'ordinary', @covering_duration_days,
                    2, 1200, 'UAH', true, null, @created_at, @catalog_updated_at, null);

            insert into bodylife.issued_memberships (
                id, client_id, membership_type_id, issuance_mode, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot,
                price_currency_snapshot, start_date, base_end_date, issued_at,
                issued_by_account_id, status, entry_origin, entry_batch_id, comment)
            values (
                @source_membership_id, @client_id, @source_type_id, 'sale',
                'Two visits', 30, 2, 900, 'UAH', date '2026-07-01',
                date '2026-07-30', @created_at, @account_id, 'active',
                'normal', null, null);

            insert into bodylife.payments (
                id, client_id, membership_id, negative_closure_id, amount, currency,
                method, payment_context, occurred_at, recorded_at,
                recorded_by_account_id, session_id, entry_origin, entry_batch_id,
                comment, status)
            values (
                gen_random_uuid(), @client_id, @source_membership_id, null, 900, 'UAH',
                'cash', 'membership_sale', @created_at, @created_at,
                @account_id, @session_id, 'normal', null, null, 'active');
            """,
            ("account_id", fixture.Actor.AccountId.Value),
            ("session_id", fixture.Actor.SessionId.Value),
            ("client_id", fixture.ClientId),
            ("source_type_id", fixture.SourceTypeId),
            ("covering_type_id", fixture.CoveringTypeId),
            ("source_membership_id", fixture.SourceMembershipId),
            ("covering_duration_days", coveringDurationDays),
            ("now", TestNow.AddHours(-2)),
            ("expires_at", TestNow.AddHours(8)),
            ("created_at", TestNow.AddDays(-10)),
            ("catalog_updated_at", CatalogUpdatedAt));

        for (var index = 0; index < fixture.VisitIds.Length; index++)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                insert into bodylife.visits (
                    id, client_id, occurred_at, recorded_at, recorded_by_account_id,
                    session_id, visit_kind, entry_origin, entry_batch_id, comment, status)
                values (
                    @visit_id, @client_id, @occurred_at, @recorded_at, @account_id,
                    @session_id, 'membership', 'normal', null, null, 'active');

                insert into bodylife.visit_consumptions (
                    id, visit_id, client_id, visit_kind, membership_id,
                    consumption_type, source_fact_type, source_fact_id, recorded_at,
                    recorded_by_account_id, recorded_session_id, status)
                values (
                    @consumption_id, @visit_id, @client_id, 'membership',
                    @source_membership_id, 'counted', 'visit', @visit_id, @recorded_at,
                    @account_id, @session_id, 'active');
                """,
                ("visit_id", fixture.VisitIds[index]),
                ("consumption_id", fixture.ConsumptionIds[index]),
                ("client_id", fixture.ClientId),
                ("source_membership_id", fixture.SourceMembershipId),
                ("occurred_at", new DateTimeOffset(
                    2026,
                    7,
                    index + 1,
                    10,
                    0,
                    0,
                    TimeSpan.Zero)),
                ("recorded_at", TestNow.AddMinutes(index + 1)),
                ("account_id", fixture.Actor.AccountId.Value),
                ("session_id", fixture.Actor.SessionId.Value));
        }

        await transaction.CommitAsync();
        return fixture;
    }

    private static IssueMembershipCommand CreateCommand(
        CoverageFixture fixture,
        string idempotencyKey,
        int coverageCount,
        Guid expectedOldestVisitId)
    {
        return new IssueMembershipCommand(
            new CommandEnvelope(
                fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                TestNow,
                idempotencyKey,
                Reason: null,
                Comment: "Cover oldest negative Visits"),
            fixture.ClientId,
            fixture.CoveringTypeId,
            CatalogUpdatedAt,
            new DateOnly(2026, 7, 20),
            MembershipNegativeHandlingDecision.CoverWithNewMembership,
            EntryBatchId: null,
            coverageCount,
            expectedOldestVisitId);
    }

    private static IssueMembershipCommandHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        var auditAppender = new BusinessAuditAppender(dbContext);
        return new IssueMembershipCommandHandler(
            dbContext,
            auditAppender,
            new MembershipIssuePaymentWriter(dbContext, auditAppender),
            new MembershipNegativeVisitSelector(dbContext),
            new MembershipStateCacheRebuilder(dbContext, timeProvider),
            timeProvider);
    }

    private static async Task RebuildSourceAsync(
        BodyLifeDbContext dbContext,
        Guid membershipId,
        int expectedNegative)
    {
        var rebuild = await new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(TestNow))
            .RebuildAsync(membershipId);
        Assert.True(rebuild.Succeeded);
        Assert.Equal(expectedNegative, rebuild.State!.NegativeBalance);
    }

    private static async Task<int> ReadCacheValueAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        string column)
    {
        return await database.ExecuteScalarAsync<int>(
            $"select {column} from bodylife.membership_state_cache where membership_id = '{membershipId}'");
    }

    private static async Task AssertNoNewIssueAsync(PostgreSqlTestDatabase database)
    {
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.issued_memberships"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.payments"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_negative_closures"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.command_idempotency_keys"));
    }

    private static void AssertError(
        CommandResult result,
        CommandErrorCode code,
        string field)
    {
        Assert.Equal(CommandStatus.Error, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(field, error.Field);
    }

    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    private sealed record CoverageFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid SourceTypeId,
        Guid CoveringTypeId,
        Guid SourceMembershipId,
        Guid[] VisitIds,
        Guid[] ConsumptionIds);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
