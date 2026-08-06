using System.Data.Common;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed partial class PostgreSqlGetClientNegativeVisitCoverageQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task EmptyClientReturnsNoNegativeVisitsAndOnlyActiveOneOffCatalogInStableOrder()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedReadFixtureAsync(database);
        await using var dbContext = database.CreateDbContext();

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(fixture.Owner, fixture.ClientId), CancellationToken.None);

        Assert.Equal(GetClientNegativeVisitCoverageStatus.Success, result.Status);
        var coverage = result.Coverage!;
        Assert.Equal(0, coverage.TotalNegativeBalance);
        Assert.Equal(0, coverage.UnknownNegativeBalance);
        Assert.Null(coverage.FirstNegativeVisitDate);
        Assert.Empty(coverage.OpenConcreteVisits);
        Assert.Empty(coverage.ActiveClosures);
        Assert.Collection(coverage.ActiveOneOffTypes,
            item => Assert.Equal("Alpha", item.Name),
            item => Assert.Equal("Zulu", item.Name));
        Assert.All(coverage.ActiveOneOffTypes, item => Assert.Equal(1, item.VisitsLimit));
        Assert.False(result.AllowedActions.IsAllowed(MembershipActionKeys.CloseNegativeVisitsOneOff));
        Assert.False(result.AllowedActions.IsAllowed(MembershipActionKeys.CorrectNegativeVisitCoverage));
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task OwnerNamedAdminAndSharedReceptionAdminAreAuthorizedWhileInvalidInputAndInactiveActorFailClosed()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedReadFixtureAsync(database);
        await using var dbContext = database.CreateDbContext();
        var handler = CreateHandler(dbContext);

        foreach (var actor in new[] { fixture.Owner, fixture.NamedAdmin, fixture.SharedAdmin })
        {
            Assert.Equal(GetClientNegativeVisitCoverageStatus.Success, (await handler.ExecuteAsync(
                new GetClientNegativeVisitCoverageQuery(actor, fixture.ClientId), CancellationToken.None)).Status);
        }

        var invalid = await handler.ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(fixture.Owner, Guid.Empty), CancellationToken.None);
        var denied = await handler.ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(fixture.InactiveAdmin, fixture.ClientId), CancellationToken.None);
        Assert.Equal(GetClientNegativeVisitCoverageStatus.ValidationFailed, invalid.Status);
        Assert.Equal("validation_failed", invalid.ErrorCode);
        Assert.Equal(GetClientNegativeVisitCoverageStatus.PermissionDenied, denied.Status);
        Assert.Equal("permission_denied", denied.ErrorCode);
    }

    [PostgreSqlFact]
    public async Task ActiveOneOffAndNewMembershipClosuresProjectImmutableSnapshotsAndExactSourceFacts()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        await using var dbContext = database.CreateDbContext();

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(fixture.Owner, fixture.ClientId),
            CancellationToken.None);

        Assert.Equal(GetClientNegativeVisitCoverageStatus.Success, result.Status);
        var coverage = result.Coverage!;
        Assert.Equal(0, coverage.TotalNegativeBalance);
        Assert.Equal(0, coverage.UnknownNegativeBalance);
        Assert.Empty(coverage.OpenConcreteVisits);
        var catalogType = Assert.Single(coverage.ActiveOneOffTypes);
        Assert.Equal("Zulu", catalogType.Name);
        Assert.False(result.AllowedActions.IsAllowed(MembershipActionKeys.CloseNegativeVisitsOneOff));
        Assert.True(result.AllowedActions.IsAllowed(MembershipActionKeys.CorrectNegativeVisitCoverage));

        Assert.Collection(
            coverage.ActiveClosures,
            oneOff =>
            {
                Assert.Equal(fixture.OneOffClosureId, oneOff.ClosureId);
                Assert.Equal("one_off", oneOff.ClosureType);
                Assert.Null(oneOff.CoveringMembershipId);
                Assert.Null(oneOff.CoveringMembershipSnapshot);
                Assert.Equal(fixture.VisitIds[1], oneOff.OldestOpenNegativeVisitId);
                var line = Assert.Single(oneOff.Lines);
                Assert.Equal("Alpha at closure", line.TypeName);
                Assert.Equal(new Money(50m, "UAH"), line.UnitPrice);
                Assert.Equal(new Money(50m, "UAH"), line.LineTotal);
                var item = Assert.Single(oneOff.Items);
                Assert.Equal(fixture.VisitIds[1], item.VisitId);
                Assert.Equal(fixture.SourceMembershipId, item.SourceMembershipId);
                Assert.Equal(fixture.ConsumptionIds[1], item.OldConsumptionId);
                Assert.Null(item.NewConsumptionId);
                var payment = Assert.IsType<NegativeVisitCoveragePaymentReadModel>(oneOff.Payment);
                Assert.Equal(fixture.OneOffPaymentId, payment.PaymentId);
                Assert.Equal(new Money(50m, "UAH"), payment.Amount);
            },
            newMembership =>
            {
                Assert.Equal(fixture.NewMembershipClosureId, newMembership.ClosureId);
                Assert.Equal("new_membership", newMembership.ClosureType);
                Assert.Equal(fixture.CoveringMembershipId, newMembership.CoveringMembershipId);
                Assert.Empty(newMembership.Lines);
                Assert.Null(newMembership.Payment);
                var snapshot = Assert.IsType<IssuedMembershipCoverageSnapshotReadModel>(
                    newMembership.CoveringMembershipSnapshot);
                Assert.Equal("Coverage membership", snapshot.TypeName);
                Assert.Equal(new Money(300m, "UAH"), snapshot.Price);
                Assert.Collection(
                    newMembership.Items,
                    item =>
                    {
                        Assert.Equal(fixture.VisitIds[2], item.VisitId);
                        Assert.Equal(fixture.NewConsumptionIds[0], item.NewConsumptionId);
                        Assert.Equal(fixture.CoveringMembershipId, item.CoveringMembershipId);
                    },
                    item =>
                    {
                        Assert.Equal(fixture.VisitIds[3], item.VisitId);
                        Assert.Equal(fixture.NewConsumptionIds[1], item.NewConsumptionId);
                        Assert.Equal(fixture.CoveringMembershipId, item.CoveringMembershipId);
                    });
            });
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task MalformedActiveClosureFailsClosedInsteadOfReturningPartialHistory()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        await database.ExecutePrivilegedConstraintCorruptionAsync<int>(
            $"""
            update bodylife.membership_negative_closure_items
            set status = 'canceled'
            where negative_closure_id = '{fixture.OneOffClosureId}';
            select 1;
            """);
        await using var dbContext = database.CreateDbContext();

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(fixture.Owner, fixture.ClientId),
            CancellationToken.None);

        Assert.Equal(GetClientNegativeVisitCoverageStatus.CanonicalStateInvalid, result.Status);
        Assert.Equal("canonical_state_invalid", result.ErrorCode);
        Assert.Null(result.Coverage);
        Assert.Empty(result.AllowedActions.Items);
    }

    [PostgreSqlFact]
    public async Task MissingCanonicalCacheReturnsRecalculationFailureWithoutPartialCoverage()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        await database.ExecuteScalarAsync<int>(
            $"delete from bodylife.membership_state_cache where membership_id = '{fixture.SourceMembershipId}'; select 1;");
        await using var dbContext = database.CreateDbContext();

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(fixture.Owner, fixture.ClientId),
            CancellationToken.None);

        Assert.Equal(GetClientNegativeVisitCoverageStatus.RecalculationFailed, result.Status);
        Assert.Equal("recalculation_failed", result.ErrorCode);
        Assert.Null(result.Coverage);
        Assert.Empty(result.AllowedActions.Items);
    }

    [PostgreSqlFact]
    public async Task SelectorAndClosureProjectionUseOneRepeatableReadSnapshot()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        var interceptor = new PauseBeforeClosureProjectionInterceptor();
        await using var queryContext = CreateDbContext(database.ConnectionString, interceptor);
        var queryTask = CreateHandler(queryContext).ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(fixture.Owner, fixture.ClientId),
            CancellationToken.None);

        await interceptor.ProjectionReadReached.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var correctionContext = database.CreateDbContext();
            await using var correctionTransaction = await correctionContext.Database
                .BeginTransactionAsync();
            await correctionContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                update bodylife.payments
                set status = 'canceled'
                where id = {fixture.OneOffPaymentId};

                update bodylife.membership_negative_closure_items
                set status = 'canceled'
                where id = {fixture.OneOffItemId};

                update bodylife.membership_negative_closures
                set status = 'canceled'
                where id = {fixture.OneOffClosureId};

                insert into bodylife.membership_negative_closure_corrections (
                    id, original_closure_id, replacement_closure_id, mode, reason,
                    occurred_at, recorded_at, recorded_by_account_id, session_id,
                    entry_origin, entry_batch_id, idempotency_key)
                values (
                    {Guid.NewGuid()}, {fixture.OneOffClosureId}, null, 'cancel',
                    'Concurrent snapshot test', {Now}, {Now},
                    {fixture.Owner.AccountId.Value}, {fixture.Owner.SessionId.Value},
                    'normal', null, 'query-concurrent-cancel');
                """);
            var rebuild = await new MembershipStateCacheRebuilder(
                    correctionContext,
                    new FixedTimeProvider(Now))
                .RebuildAsync(fixture.SourceMembershipId);
            Assert.True(rebuild.Succeeded);
            Assert.Equal(1, rebuild.State!.NegativeBalance);
            await correctionTransaction.CommitAsync();
        }
        finally
        {
            interceptor.ReleaseProjectionRead();
        }

        var snapshotResult = await queryTask;
        Assert.Equal(GetClientNegativeVisitCoverageStatus.Success, snapshotResult.Status);
        Assert.Equal(0, snapshotResult.Coverage!.TotalNegativeBalance);
        Assert.Equal(2, snapshotResult.Coverage.ActiveClosures.Count);

        await using var freshContext = database.CreateDbContext();
        var freshResult = await CreateHandler(freshContext).ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(fixture.Owner, fixture.ClientId),
            CancellationToken.None);
        Assert.Equal(GetClientNegativeVisitCoverageStatus.Success, freshResult.Status);
        Assert.Equal(1, freshResult.Coverage!.TotalNegativeBalance);
        Assert.Equal(fixture.VisitIds[1], Assert.Single(freshResult.Coverage.OpenConcreteVisits).VisitId);
        Assert.Equal(
            fixture.NewMembershipClosureId,
            Assert.Single(freshResult.Coverage.ActiveClosures).ClosureId);
    }

    [Fact]
    public void PersistenceRegistrationExposesScopedNegativeCoverageQueryHandler()
    {
        var services = new ServiceCollection();
        services.AddBodyLifePersistence(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:BodyLife"] = "Host=localhost;Database=bodylife" }).Build());

        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(
            IBodyLifeQueryHandler<GetClientNegativeVisitCoverageQuery, GetClientNegativeVisitCoverageResult>));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(GetClientNegativeVisitCoverageQueryHandler), descriptor.ImplementationType);
    }

    private static GetClientNegativeVisitCoverageQueryHandler CreateHandler(BodyLife.Crm.Infrastructure.Persistence.BodyLifeDbContext dbContext) =>
        new(dbContext, new MembershipNegativeVisitSelector(dbContext), new FixedTimeProvider(Now));

    private static BodyLifeDbContext CreateDbContext(
        string connectionString,
        IDbCommandInterceptor interceptor)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BodyLifeDbContext>();
        BodyLifeDbContextOptions.Configure(optionsBuilder, connectionString);
        optionsBuilder.AddInterceptors(interceptor);
        return new BodyLifeDbContext(optionsBuilder.Options);
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();
        return database;
    }

    private static async Task<Fixture> SeedReadFixtureAsync(PostgreSqlTestDatabase database)
    {
        var clientId = Guid.NewGuid();
        var owner = Actor(ActorRole.Owner, AccountKind.Owner);
        var namedAdmin = Actor(ActorRole.Admin, AccountKind.NamedAdmin);
        var sharedAdmin = Actor(ActorRole.Admin, AccountKind.SharedReceptionAdmin);
        var inactiveAdmin = Actor(ActorRole.Admin, AccountKind.NamedAdmin);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        foreach (var actor in new[] { owner, namedAdmin, sharedAdmin, inactiveAdmin })
        {
            await SeedActorAsync(connection, """
                insert into bodylife.accounts (
                    id, display_name, account_type, role, is_active, created_at, deactivated_at)
                values (
                    @account, 'Reader', @kind, @role, @active, @now,
                    case when @active then null else @now end);
                insert into bodylife.sessions (id, account_id, device_label, started_at, expires_at, last_seen_at)
                values (@session, @account, 'test', @now, @expires, @now);
                """, actor.AccountId.Value, actor.SessionId.Value,
                actor.AccountKind == AccountKind.Owner ? "owner" : actor.AccountKind == AccountKind.NamedAdmin ? "named_admin" : "shared_reception_admin",
                actor.Role == ActorRole.Owner ? "owner" : "admin", actor != inactiveAdmin);
        }
        await SeedClientAndCatalogAsync(connection, """
            insert into bodylife.clients (id, surname, name, normalized_full_name, operational_status, created_at, created_by_account_id, updated_at)
            values (@client, 'Coverage', 'Client', 'COVERAGE CLIENT', 'active', @now, @owner, @now);
            insert into bodylife.membership_types (
                id, name, kind, duration_days, visits_limit, price_amount,
                price_currency, is_active, created_at, updated_at, deactivated_at)
            values
              (gen_random_uuid(), 'Zulu', 'one_off', 1, 1, 70, 'UAH', true, @now, @now, null),
              (gen_random_uuid(), 'Alpha', 'one_off', 1, 1, 50, 'UAH', true, @now, @now, null),
              (gen_random_uuid(), 'Inactive', 'one_off', 1, 1, 20, 'UAH', false, @now, @now, @now);
            """, clientId, owner.AccountId.Value);
        return new Fixture(clientId, owner, namedAdmin, sharedAdmin, inactiveAdmin);
    }

    private static async Task<ClosureProjectionFixture> SeedClosureProjectionFixtureAsync(
        PostgreSqlTestDatabase database)
    {
        var fixture = new ClosureProjectionFixture(
            ClientId: Guid.NewGuid(),
            Owner: Actor(ActorRole.Owner, AccountKind.Owner),
            SourceTypeId: Guid.NewGuid(),
            OneOffTypeId: Guid.NewGuid(),
            OtherOneOffTypeId: Guid.NewGuid(),
            CoveringTypeId: Guid.NewGuid(),
            SourceMembershipId: Guid.NewGuid(),
            CoveringMembershipId: Guid.NewGuid(),
            VisitIds: Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray(),
            ConsumptionIds: Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray(),
            OneOffClosureId: Guid.NewGuid(),
            NewMembershipClosureId: Guid.NewGuid(),
            OneOffLineId: Guid.NewGuid(),
            OneOffItemId: Guid.NewGuid(),
            NewMembershipItemIds: Enumerable.Range(0, 2)
                .Select(_ => Guid.NewGuid())
                .ToArray(),
            NewConsumptionIds: Enumerable.Range(0, 2)
                .Select(_ => Guid.NewGuid())
                .ToArray(),
            OneOffPaymentId: Guid.NewGuid());
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            insert into bodylife.accounts (
                id, display_name, account_type, role, is_active, created_at)
            values (@account, 'Coverage owner', 'owner', 'owner', true, @now);

            insert into bodylife.sessions (
                id, account_id, device_label, started_at, expires_at, last_seen_at)
            values (@session, @account, 'coverage query', @now, @expires, @now);

            insert into bodylife.clients (
                id, surname, name, normalized_full_name, operational_status,
                created_at, created_by_account_id, updated_at)
            values (
                @client, 'Coverage', 'Projection', 'COVERAGE PROJECTION', 'active',
                @now, @account, @now);

            insert into bodylife.membership_types (
                id, name, kind, duration_days, visits_limit, price_amount,
                price_currency, is_active, created_at, updated_at, deactivated_at)
            values
                (@source_type, 'Source membership', 'ordinary', 30, 1, 100, 'UAH', true, @now, @now, null),
                (@one_off_type, 'Changed after closure', 'one_off', 1, 1, 999, 'UAH', false, @now, @now, @now),
                (@other_one_off_type, 'Zulu', 'one_off', 1, 1, 75, 'UAH', true, @now, @now, null),
                (@covering_type, 'Coverage membership', 'ordinary', 30, 3, 300, 'UAH', true, @now, @now, null);

            insert into bodylife.issued_memberships (
                id, client_id, membership_type_id, issuance_mode, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot,
                price_currency_snapshot, start_date, base_end_date, issued_at,
                issued_by_account_id, status, entry_origin)
            values
                (@source_membership, @client, @source_type, 'sale', 'Source membership',
                 30, 1, 100, 'UAH', date '2026-07-01', date '2026-07-30', @now - interval '1 hour',
                 @account, 'active', 'normal'),
                (@covering_membership, @client, @covering_type, 'sale', 'Coverage membership',
                 30, 3, 300, 'UAH', date '2026-07-30', date '2026-08-28', @now - interval '10 minutes',
                 @account, 'active', 'normal');

            insert into bodylife.payments (
                id, client_id, membership_id, amount, currency, method, payment_context,
                occurred_at, recorded_at, recorded_by_account_id, session_id,
                entry_origin, status)
            values
                (gen_random_uuid(), @client, @source_membership, 100, 'UAH', 'cash', 'membership_sale',
                 @now - interval '1 hour', @now - interval '1 hour', @account, @session,
                 'normal', 'active'),
                (gen_random_uuid(), @client, @covering_membership, 300, 'UAH', 'cash', 'membership_sale',
                 @now - interval '10 minutes', @now - interval '10 minutes', @account, @session,
                 'normal', 'active');

            insert into bodylife.visits (
                id, client_id, occurred_at, recorded_at, recorded_by_account_id,
                session_id, visit_kind, entry_origin, status)
            values
                (@visit_1, @client, @now - interval '3 days', @now - interval '3 days', @account, @session, 'membership', 'normal', 'active'),
                (@visit_2, @client, @now - interval '2 days', @now - interval '2 days', @account, @session, 'membership', 'normal', 'active'),
                (@visit_3, @client, @now - interval '1 day', @now - interval '1 day', @account, @session, 'membership', 'normal', 'active'),
                (@visit_4, @client, @now - interval '12 hours', @now - interval '12 hours', @account, @session, 'membership', 'normal', 'active');

            insert into bodylife.visit_consumptions (
                id, visit_id, client_id, visit_kind, membership_id, consumption_type,
                source_fact_type, source_fact_id, recorded_at, recorded_by_account_id,
                recorded_session_id, status)
            values
                (@consumption_1, @visit_1, @client, 'membership', @source_membership, 'counted', 'visit', @visit_1, @now - interval '3 days', @account, @session, 'active'),
                (@consumption_2, @visit_2, @client, 'membership', @source_membership, 'counted', 'visit', @visit_2, @now - interval '2 days', @account, @session, 'active'),
                (@consumption_3, @visit_3, @client, 'membership', @source_membership, 'counted', 'visit', @visit_3, @now - interval '1 day', @account, @session, 'active'),
                (@consumption_4, @visit_4, @client, 'membership', @source_membership, 'counted', 'visit', @visit_4, @now - interval '12 hours', @account, @session, 'active');

            insert into bodylife.membership_negative_closures (
                id, client_id, closure_type, covering_membership_id,
                oldest_open_negative_visit_id, visits_count, occurred_at, recorded_at,
                recorded_by_account_id, session_id, entry_origin, idempotency_key, status)
            values
                (@one_off_closure, @client, 'one_off', null, @visit_2, 1,
                 @now - interval '20 minutes', @now - interval '20 minutes', @account, @session,
                 'normal', 'query-one-off', 'active'),
                (@new_membership_closure, @client, 'new_membership', @covering_membership, @visit_3, 2,
                 @now - interval '10 minutes', @now - interval '10 minutes', @account, @session,
                 'normal', 'query-new-membership', 'active');

            insert into bodylife.membership_negative_closure_lines (
                id, negative_closure_id, membership_type_id, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot, quantity,
                unit_price_amount_snapshot, currency_snapshot, line_total, sequence)
            values (
                @one_off_line, @one_off_closure, @one_off_type, 'Alpha at closure',
                1, 1, 1, 50, 'UAH', 50, 1);

            insert into bodylife.visit_consumptions (
                id, visit_id, client_id, visit_kind, membership_id, consumption_type,
                source_fact_type, source_fact_id, recorded_at, recorded_by_account_id,
                recorded_session_id, status)
            values
                (@new_consumption_1, @visit_3, @client, 'membership', @covering_membership,
                 'negative_coverage', 'negative_closure_item', @new_membership_item_1,
                 @now - interval '10 minutes', @account, @session, 'active'),
                (@new_consumption_2, @visit_4, @client, 'membership', @covering_membership,
                 'negative_coverage', 'negative_closure_item', @new_membership_item_2,
                 @now - interval '10 minutes', @account, @session, 'active');

            insert into bodylife.membership_negative_closure_items (
                id, negative_closure_id, client_id, closure_line_id, sequence, visit_id,
                source_membership_id, old_consumption_id, covering_membership_id,
                new_consumption_id, status)
            values
                (@one_off_item, @one_off_closure, @client, @one_off_line, 1, @visit_2,
                 @source_membership, @consumption_2, null, null, 'active'),
                (@new_membership_item_1, @new_membership_closure, @client, null, 1, @visit_3,
                 @source_membership, @consumption_3, @covering_membership, @new_consumption_1, 'active'),
                (@new_membership_item_2, @new_membership_closure, @client, null, 2, @visit_4,
                 @source_membership, @consumption_4, @covering_membership, @new_consumption_2, 'active');

            insert into bodylife.payments (
                id, client_id, negative_closure_id, amount, currency, method, payment_context,
                occurred_at, recorded_at, recorded_by_account_id, session_id,
                entry_origin, status)
            values (
                @one_off_payment, @client, @one_off_closure, 50, 'UAH', 'cash', 'negative_closure',
                @now - interval '20 minutes', @now - interval '20 minutes', @account, @session,
                'normal', 'active');
            """,
            connection);
        command.Parameters.AddWithValue("account", fixture.Owner.AccountId.Value);
        command.Parameters.AddWithValue("session", fixture.Owner.SessionId.Value);
        command.Parameters.AddWithValue("client", fixture.ClientId);
        command.Parameters.AddWithValue("source_type", fixture.SourceTypeId);
        command.Parameters.AddWithValue("one_off_type", fixture.OneOffTypeId);
        command.Parameters.AddWithValue("other_one_off_type", fixture.OtherOneOffTypeId);
        command.Parameters.AddWithValue("covering_type", fixture.CoveringTypeId);
        command.Parameters.AddWithValue("source_membership", fixture.SourceMembershipId);
        command.Parameters.AddWithValue("covering_membership", fixture.CoveringMembershipId);
        command.Parameters.AddWithValue("visit_1", fixture.VisitIds[0]);
        command.Parameters.AddWithValue("visit_2", fixture.VisitIds[1]);
        command.Parameters.AddWithValue("visit_3", fixture.VisitIds[2]);
        command.Parameters.AddWithValue("visit_4", fixture.VisitIds[3]);
        command.Parameters.AddWithValue("consumption_1", fixture.ConsumptionIds[0]);
        command.Parameters.AddWithValue("consumption_2", fixture.ConsumptionIds[1]);
        command.Parameters.AddWithValue("consumption_3", fixture.ConsumptionIds[2]);
        command.Parameters.AddWithValue("consumption_4", fixture.ConsumptionIds[3]);
        command.Parameters.AddWithValue("one_off_closure", fixture.OneOffClosureId);
        command.Parameters.AddWithValue("new_membership_closure", fixture.NewMembershipClosureId);
        command.Parameters.AddWithValue("one_off_line", fixture.OneOffLineId);
        command.Parameters.AddWithValue("one_off_item", fixture.OneOffItemId);
        command.Parameters.AddWithValue("new_membership_item_1", fixture.NewMembershipItemIds[0]);
        command.Parameters.AddWithValue("new_membership_item_2", fixture.NewMembershipItemIds[1]);
        command.Parameters.AddWithValue("new_consumption_1", fixture.NewConsumptionIds[0]);
        command.Parameters.AddWithValue("new_consumption_2", fixture.NewConsumptionIds[1]);
        command.Parameters.AddWithValue("one_off_payment", fixture.OneOffPaymentId);
        command.Parameters.AddWithValue("now", Now);
        command.Parameters.AddWithValue("expires", Now.AddHours(1));
        await command.ExecuteNonQueryAsync();

        await using var dbContext = database.CreateDbContext();
        var rebuilder = new MembershipStateCacheRebuilder(dbContext, new FixedTimeProvider(Now));
        Assert.True((await rebuilder.RebuildAsync(fixture.SourceMembershipId)).Succeeded);
        Assert.True((await rebuilder.RebuildAsync(fixture.CoveringMembershipId)).Succeeded);
        return fixture;
    }

    private static ActorContext Actor(ActorRole role, AccountKind kind) => new(AccountId.New(), role, kind, SessionId.New(), "test");

    private static async Task SeedActorAsync(NpgsqlConnection connection, string sql, Guid account, Guid session, string kind, string role, bool active)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("account", account);
        command.Parameters.AddWithValue("session", session);
        command.Parameters.AddWithValue("kind", kind!);
        command.Parameters.AddWithValue("role", role!);
        command.Parameters.AddWithValue("active", active);
        command.Parameters.AddWithValue("now", Now);
        command.Parameters.AddWithValue("expires", Now.AddHours(1));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedClientAndCatalogAsync(NpgsqlConnection connection, string sql, Guid client, Guid owner)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("client", client);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("now", Now);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record Fixture(Guid ClientId, ActorContext Owner, ActorContext NamedAdmin, ActorContext SharedAdmin, ActorContext InactiveAdmin);
    private sealed record ClosureProjectionFixture(
        Guid ClientId,
        ActorContext Owner,
        Guid SourceTypeId,
        Guid OneOffTypeId,
        Guid OtherOneOffTypeId,
        Guid CoveringTypeId,
        Guid SourceMembershipId,
        Guid CoveringMembershipId,
        Guid[] VisitIds,
        Guid[] ConsumptionIds,
        Guid OneOffClosureId,
        Guid NewMembershipClosureId,
        Guid OneOffLineId,
        Guid OneOffItemId,
        Guid[] NewMembershipItemIds,
        Guid[] NewConsumptionIds,
        Guid OneOffPaymentId);

    private sealed class PauseBeforeClosureProjectionInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> projectionReadReached = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> projectionReadReleased = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int negativeClosureReadCount;

        public Task ProjectionReadReached => projectionReadReached.Task;

        public void ReleaseProjectionRead() => projectionReadReleased.TrySetResult(true);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "membership_negative_closures",
                    StringComparison.Ordinal)
                && Interlocked.Increment(ref negativeClosureReadCount) == 2)
            {
                projectionReadReached.TrySetResult(true);
                await projectionReadReleased.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
