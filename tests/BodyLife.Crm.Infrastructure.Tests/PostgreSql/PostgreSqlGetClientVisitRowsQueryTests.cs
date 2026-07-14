using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetClientVisitRowsQueryTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        14,
        16,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset FirstVisitOccurredAt = new(
        2026,
        7,
        14,
        9,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryReturnsOwnedActiveAndCanceledRowsWithCanonicalMetadataAndActions()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var oldestVisitId = await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "one_off",
            FirstVisitOccurredAt,
            TestNow.AddMinutes(-30),
            comment: "Drop-in session");
        var visitBatchId = Guid.NewGuid();
        var canceledVisitId = await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "membership",
            FirstVisitOccurredAt.AddHours(1),
            TestNow.AddMinutes(-20),
            entryOrigin: "paper_fallback",
            entryBatchId: visitBatchId,
            comment: "Recovered Visit",
            status: "canceled");
        var consumptionId = await InsertConsumptionAsync(
            database,
            fixture,
            canceledVisitId,
            status: "canceled");
        var cancellationBatchId = Guid.NewGuid();
        var cancellationId = await InsertCancellationAsync(
            database,
            fixture,
            canceledVisitId,
            "Mistaken paper entry",
            FirstVisitOccurredAt.AddHours(2),
            TestNow.AddMinutes(-10),
            "manual_backfill",
            cancellationBatchId);
        var latestVisitId = await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "trial",
            FirstVisitOccurredAt.AddHours(3),
            TestNow.AddMinutes(-5));
        await InsertVisitAsync(
            database,
            fixture,
            fixture.OtherClientId,
            "one_off",
            FirstVisitOccurredAt.AddHours(4),
            TestNow.AddMinutes(-1));
        var dayProvider = new RecordingVisitDayStatusProvider(
            VisitDayReconciliationStatus.Open);

        var result = await CreateHandler(dbContext, dayProvider).ExecuteAsync(
            new GetClientVisitRowsQuery(fixture.Actor, fixture.ClientId),
            CancellationToken.None);

        var page = AssertSuccess(result, fixture.ClientId);
        Assert.False(page.HasMore);
        Assert.Equal(
            [latestVisitId, canceledVisitId, oldestVisitId],
            page.Items.Select(row => row.VisitId).ToArray());

        var latest = page.Items[0];
        Assert.Equal(VisitKind.Trial, latest.VisitKind);
        Assert.Equal(ClientVisitRowStatus.Active, latest.Status);
        Assert.Null(latest.Consumption);
        Assert.Null(latest.Cancellation);
        Assert.True(latest.AllowedActions.IsAllowed(VisitActionKeys.Cancel));
        var latestPermission = Assert.Single(latest.AllowedActions.Items);
        Assert.Equal(VisitActionKeys.AdminOrOwnerPolicy, latestPermission.RequiredPolicy);

        var canceled = page.Items[1];
        Assert.Equal(fixture.ClientId, canceled.ClientId);
        Assert.Equal(VisitKind.Membership, canceled.VisitKind);
        Assert.Equal(EntryOrigin.PaperFallback, canceled.EntryOrigin);
        Assert.Equal(visitBatchId, canceled.EntryBatchId);
        Assert.Equal("Recovered Visit", canceled.Comment);
        Assert.Equal(ClientVisitRowStatus.Canceled, canceled.Status);
        Assert.Empty(canceled.AllowedActions.Items);
        var consumption = Assert.IsType<ClientVisitConsumption>(canceled.Consumption);
        Assert.Equal(consumptionId, consumption.ConsumptionId);
        Assert.Equal(fixture.MembershipId, consumption.MembershipId);
        Assert.Equal("Client visits fixture", consumption.MembershipTypeNameSnapshot);
        Assert.Equal(ClientVisitConsumptionStatus.Canceled, consumption.Status);
        var cancellation = Assert.IsType<ClientVisitCancellation>(
            canceled.Cancellation);
        Assert.Equal(cancellationId, cancellation.CancellationId);
        Assert.Equal("Mistaken paper entry", cancellation.Reason);
        Assert.Equal(FirstVisitOccurredAt.AddHours(2), cancellation.OccurredAt);
        Assert.Equal(TestNow.AddMinutes(-10), cancellation.RecordedAt);
        Assert.Equal(fixture.Actor.AccountId.Value, cancellation.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId.Value, cancellation.SessionId);
        Assert.Equal(EntryOrigin.ManualBackfill, cancellation.EntryOrigin);
        Assert.Equal(cancellationBatchId, cancellation.EntryBatchId);

        var oldest = page.Items[2];
        Assert.Equal(VisitKind.OneOff, oldest.VisitKind);
        Assert.Equal("Drop-in session", oldest.Comment);
        Assert.True(oldest.AllowedActions.IsAllowed(VisitActionKeys.Cancel));
        Assert.Equal(
            [DateOnly.FromDateTime(FirstVisitOccurredAt.DateTime)],
            dayProvider.RequestedDates);
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
    }

    [PostgreSqlFact]
    public async Task QueryUsesDeterministicLimitAndReportsMoreRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var thirdId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "one_off",
            FirstVisitOccurredAt,
            TestNow,
            visitId: firstId);
        await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "one_off",
            FirstVisitOccurredAt,
            TestNow,
            visitId: secondId);
        await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "one_off",
            FirstVisitOccurredAt,
            TestNow,
            visitId: thirdId);
        var dayProvider = new RecordingVisitDayStatusProvider(
            VisitDayReconciliationStatus.Open);

        var result = await CreateHandler(dbContext, dayProvider).ExecuteAsync(
            new GetClientVisitRowsQuery(fixture.Actor, fixture.ClientId, Limit: 2),
            CancellationToken.None);

        var page = AssertSuccess(result, fixture.ClientId);
        Assert.True(page.HasMore);
        Assert.Equal(
            [thirdId, secondId],
            page.Items.Select(row => row.VisitId).ToArray());
        Assert.All(
            page.Items,
            row => Assert.True(row.AllowedActions.IsAllowed(VisitActionKeys.Cancel)));
        Assert.Single(dayProvider.RequestedDates);
    }

    [PostgreSqlFact]
    public async Task ReconciledDayAllowsOwnerAndReturnsExplicitAdminDenial()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "one_off",
            FirstVisitOccurredAt,
            TestNow);
        var reconciledProvider = new RecordingVisitDayStatusProvider(
            VisitDayReconciliationStatus.Reconciled);

        var ownerResult = await CreateHandler(
            dbContext,
            reconciledProvider).ExecuteAsync(
                new GetClientVisitRowsQuery(fixture.Actor, fixture.ClientId),
                CancellationToken.None);

        var ownerRow = Assert.Single(AssertSuccess(
            ownerResult,
            fixture.ClientId).Items);
        var ownerPermission = Assert.Single(ownerRow.AllowedActions.Items);
        Assert.True(ownerPermission.IsAllowed);
        Assert.Equal(VisitActionKeys.OwnerPolicy, ownerPermission.RequiredPolicy);

        await UpdateActorIdentityAsync(
            database,
            fixture.Actor.AccountId.Value,
            "named_admin",
            "admin");
        var adminActor = fixture.Actor with
        {
            Role = ActorRole.Admin,
            AccountKind = AccountKind.NamedAdmin,
        };
        var adminResult = await CreateHandler(
            dbContext,
            reconciledProvider).ExecuteAsync(
                new GetClientVisitRowsQuery(adminActor, fixture.ClientId),
                CancellationToken.None);

        var adminRow = Assert.Single(AssertSuccess(
            adminResult,
            fixture.ClientId).Items);
        var deniedPermission = Assert.Single(adminRow.AllowedActions.Items);
        Assert.False(deniedPermission.IsAllowed);
        Assert.Equal(VisitActionKeys.OwnerPolicy, deniedPermission.RequiredPolicy);
        Assert.Equal("day_closed_requires_owner", deniedPermission.DeniedReasonCode);
        Assert.False(adminRow.AllowedActions.IsAllowed(VisitActionKeys.Cancel));

        var openAdminResult = await CreateHandler(
            dbContext,
            new RecordingVisitDayStatusProvider(
                VisitDayReconciliationStatus.Open)).ExecuteAsync(
                new GetClientVisitRowsQuery(adminActor, fixture.ClientId),
                CancellationToken.None);
        var openAdminPermission = Assert.Single(
            Assert.Single(AssertSuccess(
                openAdminResult,
                fixture.ClientId).Items).AllowedActions.Items);
        Assert.True(openAdminPermission.IsAllowed);
        Assert.Equal(
            VisitActionKeys.AdminOrOwnerPolicy,
            openAdminPermission.RequiredPolicy);
    }

    [PostgreSqlFact]
    public async Task ValidationMissingClientAndInactiveActorReturnNoRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(
            dbContext,
            new RecordingVisitDayStatusProvider(VisitDayReconciliationStatus.Open));

        var empty = await handler.ExecuteAsync(
            new GetClientVisitRowsQuery(fixture.Actor, fixture.ClientId),
            CancellationToken.None);
        var missingId = await handler.ExecuteAsync(
            new GetClientVisitRowsQuery(fixture.Actor, Guid.Empty),
            CancellationToken.None);
        var invalidLowLimit = await handler.ExecuteAsync(
            new GetClientVisitRowsQuery(fixture.Actor, fixture.ClientId, Limit: 0),
            CancellationToken.None);
        var invalidHighLimit = await handler.ExecuteAsync(
            new GetClientVisitRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: GetClientVisitRowsQuery.MaxLimit + 1),
            CancellationToken.None);
        var missingClient = await handler.ExecuteAsync(
            new GetClientVisitRowsQuery(fixture.Actor, Guid.NewGuid()),
            CancellationToken.None);
        await DeactivateActorAsync(database, fixture.Actor.AccountId.Value);
        var denied = await handler.ExecuteAsync(
            new GetClientVisitRowsQuery(fixture.Actor, fixture.ClientId),
            CancellationToken.None);

        Assert.Empty(AssertSuccess(empty, fixture.ClientId).Items);
        AssertFailure(
            missingId,
            GetClientVisitRowsStatus.ValidationFailed,
            "clientId");
        AssertFailure(
            invalidLowLimit,
            GetClientVisitRowsStatus.ValidationFailed,
            "limit");
        AssertFailure(
            invalidHighLimit,
            GetClientVisitRowsStatus.ValidationFailed,
            "limit");
        AssertFailure(
            missingClient,
            GetClientVisitRowsStatus.NotFound,
            "clientId");
        AssertFailure(denied, GetClientVisitRowsStatus.PermissionDenied);
    }

    [PostgreSqlFact]
    public async Task CanceledVisitWithoutRetainedCancellationFailsClosed()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "one_off",
            FirstVisitOccurredAt,
            TestNow,
            status: "canceled");

        var result = await CreateHandler(
            dbContext,
            new RecordingVisitDayStatusProvider(
                VisitDayReconciliationStatus.Open)).ExecuteAsync(
                new GetClientVisitRowsQuery(fixture.Actor, fixture.ClientId),
                CancellationToken.None);

        AssertFailure(result, GetClientVisitRowsStatus.SourceInconsistent);
        Assert.Equal("source_inconsistent", result.ErrorCode);
    }

    [Fact]
    public async Task PersistenceRegistrationComposesVisitRowsAndCancellationBoundaries()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] =
                    "Host=localhost;Database=bodylife;Username=bodylife;Password=not-used",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBodyLifePersistence(configuration);

        AssertScopedRegistration<
            IBodyLifeQueryHandler<GetClientVisitRowsQuery, GetClientVisitRowsResult>,
            GetClientVisitRowsQueryHandler>(services);
        AssertScopedRegistration<
            IBodyLifeCommandHandler<CancelVisitCommand>,
            CancelVisitCommandHandler>(services);
        AssertScopedRegistration<CancelVisitSourcePreparer, CancelVisitSourcePreparer>(
            services);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType
                == typeof(IVisitDayReconciliationStatusProvider)
                && descriptor.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetClientVisitRowsQuery,
                GetClientVisitRowsResult>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeCommandHandler<CancelVisitCommand>>());
        var dayStatusProvider = scope.ServiceProvider.GetRequiredService<
            IVisitDayReconciliationStatusProvider>();
        Assert.Equal(
            VisitDayReconciliationStatus.Open,
            await dayStatusProvider.GetStatusAsync(new DateOnly(2026, 7, 14)));
    }

    private static GetClientVisitRowsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext,
        IVisitDayReconciliationStatusProvider dayStatusProvider)
    {
        return new GetClientVisitRowsQueryHandler(
            dbContext,
            dayStatusProvider,
            new FixedTimeProvider(TestNow));
    }

    private static async Task<ClientVisitRowsFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var bootstrap = await new OwnerBootstrapper(
                dbContext,
                new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var accountId = bootstrap.AccountId!.Value;
        var sessionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.sessions (
                id,
                account_id,
                device_label,
                started_at,
                expires_at,
                ended_at,
                last_seen_at)
            values (
                @session_id,
                @account_id,
                'Reception tablet',
                @started_at,
                @expires_at,
                null,
                @last_seen_at);

            insert into bodylife.clients (
                id,
                surname,
                name,
                patronymic,
                normalized_full_name,
                phone_raw,
                phone_normalized,
                phone_last4,
                comment,
                operational_status,
                created_at,
                created_by_account_id,
                updated_at)
            values
                (
                    @client_id,
                    'Visit',
                    'Reader',
                    null,
                    'VISIT READER',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @created_at,
                    @account_id,
                    @created_at),
                (
                    @other_client_id,
                    'Other',
                    'Client',
                    null,
                    'OTHER CLIENT',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @created_at,
                    @account_id,
                    @created_at);

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
                @membership_type_id,
                'Client visits fixture',
                30,
                8,
                1000,
                'UAH',
                true,
                null,
                @created_at,
                @created_at,
                null);

            insert into bodylife.issued_memberships (
                id,
                client_id,
                membership_type_id,
                type_name_snapshot,
                duration_days_snapshot,
                visits_limit_snapshot,
                price_amount_snapshot,
                price_currency_snapshot,
                start_date,
                base_end_date,
                issued_at,
                issued_by_account_id,
                status,
                entry_origin,
                entry_batch_id,
                comment)
            values (
                @membership_id,
                @client_id,
                @membership_type_id,
                'Client visits fixture',
                30,
                8,
                1000,
                'UAH',
                @start_date,
                @base_end_date,
                @created_at,
                @account_id,
                'active',
                'normal',
                null,
                null)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-2));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("other_client_id", otherClientId);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-30));
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 1));
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 30));
        Assert.Equal(5, await command.ExecuteNonQueryAsync());

        return new ClientVisitRowsFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Reception tablet"),
            clientId,
            otherClientId,
            membershipId);
    }

    private static async Task<Guid> InsertVisitAsync(
        PostgreSqlTestDatabase database,
        ClientVisitRowsFixture fixture,
        Guid clientId,
        string visitKind,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        string? comment = null,
        string status = "active",
        Guid? visitId = null)
    {
        var id = visitId ?? Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.visits (
                id,
                client_id,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                visit_kind,
                entry_origin,
                entry_batch_id,
                comment,
                status)
            values (
                @id,
                @client_id,
                @occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                @visit_kind,
                @entry_origin,
                @entry_batch_id,
                @comment,
                @status)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("visit_kind", visitKind);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Varchar).Value =
            comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return id;
    }

    private static async Task<Guid> InsertConsumptionAsync(
        PostgreSqlTestDatabase database,
        ClientVisitRowsFixture fixture,
        Guid visitId,
        string status)
    {
        var consumptionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.visit_consumptions (
                id,
                visit_id,
                client_id,
                visit_kind,
                membership_id,
                consumption_type,
                source_fact_type,
                source_fact_id,
                recorded_at,
                recorded_by_account_id,
                recorded_session_id,
                status)
            values (
                @id,
                @visit_id,
                @client_id,
                'membership',
                @membership_id,
                'counted',
                'visit',
                @visit_id,
                @recorded_at,
                @account_id,
                @session_id,
                @status)
            """;
        command.Parameters.AddWithValue("id", consumptionId);
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddMinutes(-20));
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return consumptionId;
    }

    private static async Task<Guid> InsertCancellationAsync(
        PostgreSqlTestDatabase database,
        ClientVisitRowsFixture fixture,
        Guid visitId,
        string reason,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin,
        Guid? entryBatchId)
    {
        var cancellationId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.visit_cancellations (
                id,
                visit_id,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id)
            values (
                @id,
                @visit_id,
                @reason,
                @occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id)
            """;
        command.Parameters.AddWithValue("id", cancellationId);
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return cancellationId;
    }

    private static async Task UpdateActorIdentityAsync(
        PostgreSqlTestDatabase database,
        Guid accountId,
        string accountType,
        string role)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.accounts
            set account_type = @account_type,
                role = @role
            where id = @id
            """;
        command.Parameters.AddWithValue("account_type", accountType);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("id", accountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeactivateActorAsync(
        PostgreSqlTestDatabase database,
        Guid accountId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.accounts
            set is_active = false,
                deactivated_at = @deactivated_at
            where id = @id
            """;
        command.Parameters.AddWithValue("deactivated_at", TestNow);
        command.Parameters.AddWithValue("id", accountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static ClientVisitRowsPage AssertSuccess(
        GetClientVisitRowsResult result,
        Guid clientId)
    {
        Assert.Equal(GetClientVisitRowsStatus.Success, result.Status);
        var page = Assert.IsType<ClientVisitRowsPage>(result.Page);
        Assert.Equal(clientId, page.ClientId);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
        return page;
    }

    private static void AssertFailure(
        GetClientVisitRowsResult result,
        GetClientVisitRowsStatus status,
        string? field = null)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Page);
        Assert.NotNull(result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        if (field is not null)
        {
            Assert.Equal(field, result.ErrorField);
        }
    }

    private static void AssertScopedRegistration<TService, TImplementation>(
        IServiceCollection services)
    {
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(TService)
                && descriptor.ImplementationType == typeof(TImplementation)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private sealed record ClientVisitRowsFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId);

    private sealed class RecordingVisitDayStatusProvider(
        VisitDayReconciliationStatus status)
        : IVisitDayReconciliationStatusProvider
    {
        public List<DateOnly> RequestedDates { get; } = [];

        public Task<VisitDayReconciliationStatus> GetStatusAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
        {
            RequestedDates.Add(businessDate);
            return Task.FromResult(status);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
