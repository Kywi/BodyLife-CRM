using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetClientMembershipExtensionExplanationsQueryTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        17,
        14,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryProjectsOrderedActiveAndRetainedInactiveCanonicalSources()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database.ConnectionString, dbContext);
        var activeFreeze = await InsertFreezeAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 12)),
            "Medical pause",
            "active");
        var canceledFreeze = await InsertFreezeAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 16)),
            "Entered by mistake",
            "canceled");
        var activeNonWorking = await InsertNonWorkingSourceAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)),
            "technical_day",
            "Scheduled maintenance",
            "active");
        var correctedNonWorking = await InsertNonWorkingSourceAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 25)),
            "repair",
            "Floor repair",
            "corrected");
        var handler = CreateHandler(dbContext);

        var activeResult = await handler.ExecuteAsync(
            new GetClientMembershipExtensionExplanationsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);
        var historyResult = await handler.ExecuteAsync(
            new GetClientMembershipExtensionExplanationsQuery(
                fixture.Actor,
                fixture.ClientId,
                IncludeInactiveSources: true),
            CancellationToken.None);

        AssertSuccessful(activeResult);
        Assert.Equal(
            [activeNonWorking.ApplicationId, activeFreeze.FreezeId],
            activeResult.Explanations!.Items.Select(item => item.SourceId));
        Assert.All(activeResult.Explanations.Items, item => Assert.True(item.IsActive));

        AssertSuccessful(historyResult);
        var history = historyResult.Explanations!;
        Assert.Equal(fixture.ClientId, history.ClientId);
        Assert.Equal(
            [
                activeNonWorking.ApplicationId,
                activeFreeze.FreezeId,
                correctedNonWorking.ApplicationId,
                canceledFreeze.FreezeId,
            ],
            history.Items.Select(item => item.SourceId));
        Assert.Collection(
            history.Items,
            item =>
            {
                Assert.Equal(fixture.MembershipId, item.MembershipId);
                Assert.Equal(MembershipExtensionSourceKind.NonWorkingDay, item.SourceKind);
                Assert.Equal(activeNonWorking.PeriodId, item.NonWorkingPeriodId);
                Assert.Equal(
                    new DateRange(
                        new DateOnly(2026, 7, 20),
                        new DateOnly(2026, 7, 21)),
                    item.Range);
                Assert.Equal(MembershipExtensionSourceStatus.Active, item.Status);
                Assert.Equal("technical_day - Scheduled maintenance", item.ReasonLabel);
            },
            item =>
            {
                Assert.Equal(MembershipExtensionSourceKind.Freeze, item.SourceKind);
                Assert.Null(item.NonWorkingPeriodId);
                Assert.Equal(MembershipExtensionSourceStatus.Active, item.Status);
                Assert.Equal("Medical pause", item.ReasonLabel);
            },
            item =>
            {
                Assert.Equal(MembershipExtensionSourceKind.NonWorkingDay, item.SourceKind);
                Assert.Equal(correctedNonWorking.PeriodId, item.NonWorkingPeriodId);
                Assert.Equal(MembershipExtensionSourceStatus.Corrected, item.Status);
                Assert.Equal("repair - Floor repair", item.ReasonLabel);
            },
            item =>
            {
                Assert.Equal(MembershipExtensionSourceKind.Freeze, item.SourceKind);
                Assert.Equal(MembershipExtensionSourceStatus.Canceled, item.Status);
                Assert.Equal("Entered by mistake", item.ReasonLabel);
            });
        var readOnlyItems = Assert.IsAssignableFrom<IList<MembershipExtensionExplanation>>(
            history.Items);
        Assert.True(readOnlyItems.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => readOnlyItems.Add(history.Items[0]));
    }

    [PostgreSqlFact]
    public async Task AnyInactiveNonWorkingComponentProjectsEffectiveRetainedStatus()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database.ConnectionString, dbContext);
        var canceledApplication = await InsertNonWorkingSourceAsync(
            database.ConnectionString,
            fixture,
            new DateRange(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)),
            "technical_day",
            reasonComment: null,
            periodStatus: "active",
            applicationStatus: "canceled");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientMembershipExtensionExplanationsQuery(
                fixture.Actor,
                fixture.ClientId,
                IncludeInactiveSources: true),
            CancellationToken.None);

        AssertSuccessful(result);
        var explanation = Assert.Single(result.Explanations!.Items);
        Assert.Equal(canceledApplication.ApplicationId, explanation.SourceId);
        Assert.Equal(MembershipExtensionSourceStatus.Canceled, explanation.Status);
        Assert.False(explanation.IsActive);
    }

    [PostgreSqlFact]
    public async Task QueryEnforcesCanonicalActorValidationAndClientExistence()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database.ConnectionString, dbContext);
        var handler = CreateHandler(dbContext);

        var denied = await handler.ExecuteAsync(
            new GetClientMembershipExtensionExplanationsQuery(
                fixture.Actor with { Role = ActorRole.Admin },
                fixture.ClientId),
            CancellationToken.None);
        var invalid = await handler.ExecuteAsync(
            new GetClientMembershipExtensionExplanationsQuery(
                fixture.Actor,
                Guid.Empty),
            CancellationToken.None);
        var missing = await handler.ExecuteAsync(
            new GetClientMembershipExtensionExplanationsQuery(
                fixture.Actor,
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(
            GetClientMembershipExtensionExplanationsStatus.PermissionDenied,
            denied.Status);
        Assert.Equal("permission_denied", denied.ErrorCode);
        Assert.Equal(
            GetClientMembershipExtensionExplanationsStatus.ValidationFailed,
            invalid.Status);
        Assert.Equal("clientId", invalid.ErrorField);
        Assert.Equal(
            GetClientMembershipExtensionExplanationsStatus.NotFound,
            missing.Status);
        Assert.Equal("clientId", missing.ErrorField);
        Assert.All(
            new[] { denied, invalid, missing },
            result => Assert.Null(result.Explanations));
    }

    [Fact]
    public void PersistenceRegistrationExposesScopedExtensionExplanationHandler()
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

        var serviceType = typeof(IBodyLifeQueryHandler<
            GetClientMembershipExtensionExplanationsQuery,
            GetClientMembershipExtensionExplanationsResult>);
        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == serviceType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(
            typeof(GetClientMembershipExtensionExplanationsQueryHandler),
            descriptor.ImplementationType);
    }

    private static GetClientMembershipExtensionExplanationsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        return new GetClientMembershipExtensionExplanationsQueryHandler(
            dbContext,
            new FixedTimeProvider(TestNow));
    }

    private static async Task<ExtensionFixture> SeedFixtureAsync(
        string connectionString,
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
        var membershipTypeId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
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
                'Owner laptop',
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
            values (
                @client_id,
                'Extension',
                'Client',
                null,
                'EXTENSION CLIENT',
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
                'Eight visits / 30 days',
                30,
                8,
                1200,
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
                'Eight visits / 30 days',
                30,
                8,
                1200,
                'UAH',
                @start_date,
                @base_end_date,
                @issued_at,
                @account_id,
                'active',
                'normal',
                null,
                null)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(12));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-1));
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-30));
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 1));
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 30));
        command.Parameters.AddWithValue("issued_at", TestNow.AddDays(-16));
        Assert.Equal(4, await command.ExecuteNonQueryAsync());

        return new ExtensionFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Owner laptop"),
            accountId,
            sessionId,
            clientId,
            membershipId);
    }

    private static async Task<FreezeSource> InsertFreezeAsync(
        string connectionString,
        ExtensionFixture fixture,
        DateRange range,
        string reason,
        string status)
    {
        var freezeId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.freezes (
                id,
                client_id,
                membership_id,
                start_date,
                end_date,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id,
                status)
            values (
                @id,
                @client_id,
                @membership_id,
                @start_date,
                @end_date,
                @reason,
                @occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                'normal',
                null,
                @status)
            """;
        command.Parameters.AddWithValue("id", freezeId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, range.StartDate);
        command.Parameters.AddWithValue("end_date", NpgsqlDbType.Date, range.EndDate);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("occurred_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("account_id", fixture.AccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return new FreezeSource(freezeId, range);
    }

    private static async Task<NonWorkingSource> InsertNonWorkingSourceAsync(
        string connectionString,
        ExtensionFixture fixture,
        DateRange range,
        string reasonCode,
        string? reasonComment,
        string periodStatus,
        string? applicationStatus = null)
    {
        var periodId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.non_working_periods (
                id,
                start_date,
                end_date,
                reason_code,
                reason_comment,
                created_at,
                created_by_account_id,
                session_id,
                status)
            values (
                @period_id,
                @start_date,
                @end_date,
                @reason_code,
                @reason_comment,
                @created_at,
                @account_id,
                @session_id,
                @period_status);

            insert into bodylife.non_working_period_applications (
                id,
                non_working_period_id,
                membership_id,
                client_id,
                applied_start_date,
                applied_end_date,
                previewed_at,
                confirmed_at,
                status)
            values (
                @application_id,
                @period_id,
                @membership_id,
                @client_id,
                @start_date,
                @end_date,
                @previewed_at,
                @confirmed_at,
                @application_status)
            """;
        command.Parameters.AddWithValue("period_id", periodId);
        command.Parameters.AddWithValue("application_id", applicationId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, range.StartDate);
        command.Parameters.AddWithValue("end_date", NpgsqlDbType.Date, range.EndDate);
        command.Parameters.AddWithValue("reason_code", reasonCode);
        command.Parameters.Add("reason_comment", NpgsqlDbType.Varchar).Value =
            reasonComment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("created_at", TestNow);
        command.Parameters.AddWithValue("account_id", fixture.AccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("period_status", periodStatus);
        command.Parameters.AddWithValue("previewed_at", TestNow.AddMinutes(-2));
        command.Parameters.AddWithValue("confirmed_at", TestNow.AddMinutes(-1));
        command.Parameters.AddWithValue(
            "application_status",
            applicationStatus ?? periodStatus);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        return new NonWorkingSource(periodId, applicationId, range);
    }

    private static void AssertSuccessful(
        GetClientMembershipExtensionExplanationsResult result)
    {
        Assert.Equal(GetClientMembershipExtensionExplanationsStatus.Success, result.Status);
        Assert.NotNull(result.Explanations);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
    }

    private sealed record ExtensionFixture(
        ActorContext Actor,
        Guid AccountId,
        Guid SessionId,
        Guid ClientId,
        Guid MembershipId);

    private sealed record FreezeSource(Guid FreezeId, DateRange Range);

    private sealed record NonWorkingSource(
        Guid PeriodId,
        Guid ApplicationId,
        DateRange Range);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
