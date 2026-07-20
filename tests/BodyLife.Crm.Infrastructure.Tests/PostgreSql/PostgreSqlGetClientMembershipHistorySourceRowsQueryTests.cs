using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetClientMembershipHistorySourceRowsQueryTests
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(
        JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        20,
        12,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryReturnsCanonicalMembershipSourcesInAuditChronology()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var source = await SeedHistoryAsync(database, fixture);
        var handler = CreateHandler(dbContext);

        var firstResult = await handler.ExecuteAsync(
            new GetClientMembershipHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 1),
            CancellationToken.None);
        var secondResult = await handler.ExecuteAsync(
            new GetClientMembershipHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 1,
                Offset: 1),
            CancellationToken.None);
        var rangedResult = await handler.ExecuteAsync(
            new GetClientMembershipHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                OccurredFromInclusive: TestNow.AddDays(-2).AddHours(-1),
                OccurredBeforeExclusive: TestNow),
            CancellationToken.None);

        var firstPage = AssertSuccess(firstResult, fixture.ClientId);
        Assert.True(firstPage.HasMore);
        Assert.Equal(1, firstPage.NextOffset);
        var openingRow = Assert.Single(firstPage.Items);
        Assert.Equal(ClientMembershipHistorySourceKind.OpeningState, openingRow.Kind);
        Assert.Equal(source.MembershipId, openingRow.MembershipId);
        Assert.Equal(TestNow.AddDays(-2), openingRow.OccurredAt);
        Assert.Equal(TestNow.AddDays(-1), openingRow.RecordedAt);
        Assert.Equal(EntryOrigin.ManualBackfill, openingRow.EntryOrigin);
        Assert.Null(openingRow.IssuedMembership);
        var opening = Assert.IsType<MembershipOpeningStateHistorySource>(
            openingRow.OpeningState);
        Assert.Equal(source.OpeningStateId, opening.OpeningStateId);
        Assert.Equal(new DateOnly(2026, 7, 15), opening.Declaration.OpeningAsOfDate);
        Assert.Equal(-2, opening.Declaration.DeclaredRemainingVisits);
        Assert.Equal(2, opening.Declaration.DeclaredNegativeBalance);
        Assert.Equal(new DateOnly(2026, 8, 5), opening.Declaration.KnownEffectiveEndDate);
        Assert.Equal(5, opening.Declaration.KnownExtensionDays);
        Assert.Equal("legacy-ledger-42", opening.SourceReference);
        Assert.Equal("Initial backfill", opening.Reason);
        Assert.Equal(fixture.Actor.AccountId, opening.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId, opening.RecordedSessionId);
        Assert.Equal(source.EntryBatchId, opening.EntryBatchId);
        Assert.Equal(MembershipOpeningStateSourceStatus.Active, opening.Status);
        Assert.Equal(
            MembershipAuditActions.OpeningStateCreated,
            openingRow.AuditEntry.ActionType);

        var secondPage = AssertSuccess(secondResult, fixture.ClientId);
        Assert.False(secondPage.HasMore);
        Assert.Null(secondPage.NextOffset);
        var issuedRow = Assert.Single(secondPage.Items);
        Assert.Equal(
            ClientMembershipHistorySourceKind.IssuedMembership,
            issuedRow.Kind);
        Assert.Equal(TestNow.AddDays(-3), issuedRow.OccurredAt);
        Assert.Equal(TestNow.AddDays(-3), issuedRow.RecordedAt);
        Assert.Equal(EntryOrigin.Normal, issuedRow.EntryOrigin);
        Assert.Null(issuedRow.OpeningState);
        var issued = Assert.IsType<IssuedMembershipHistorySource>(
            issuedRow.IssuedMembership);
        Assert.Equal(source.MembershipId, issued.MembershipId);
        Assert.Equal(fixture.MembershipTypeId, issued.MembershipTypeId);
        Assert.Equal("Legacy monthly", issued.Snapshot.TypeName);
        Assert.Equal(30, issued.Snapshot.DurationDays);
        Assert.Equal(12, issued.Snapshot.VisitsLimit);
        Assert.Equal(new Money(1250m, "UAH"), issued.Snapshot.Price);
        Assert.Equal(new DateOnly(2026, 7, 1), issued.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 30), issued.BaseEndDate);
        Assert.Equal(fixture.Actor.AccountId, issued.IssuedByAccountId);
        Assert.Equal(IssuedMembershipLifecycleStatus.Active, issued.Status);
        Assert.Equal("Imported contract", issued.Comment);
        Assert.Equal(MembershipAuditActions.Issued, issuedRow.AuditEntry.ActionType);

        var rangedPage = AssertSuccess(rangedResult, fixture.ClientId);
        Assert.Equal(
            source.OpeningStateId,
            Assert.Single(rangedPage.Items).AuditEntry.EntityId);
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenAuditHasNoCanonicalSource()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            MembershipAuditActions.Issued,
            MembershipAuditActions.MembershipEntityType,
            Guid.NewGuid(),
            fixture.ClientId,
            TestNow.AddDays(-1),
            TestNow.AddDays(-1),
            "normal");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientMembershipHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientMembershipHistorySourceRowsStatus.SourceInconsistent);
    }

    [PostgreSqlFact]
    public async Task ValidationMissingClientAndInactiveActorReturnNoRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);

        var missingId = await handler.ExecuteAsync(
            new GetClientMembershipHistorySourceRowsQuery(
                fixture.Actor,
                Guid.Empty),
            CancellationToken.None);
        var reversedRange = await handler.ExecuteAsync(
            new GetClientMembershipHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                TestNow,
                TestNow),
            CancellationToken.None);
        var invalidLimit = await handler.ExecuteAsync(
            new GetClientMembershipHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: GetClientMembershipHistorySourceRowsQuery.MaxLimit + 1),
            CancellationToken.None);
        var missingClient = await handler.ExecuteAsync(
            new GetClientMembershipHistorySourceRowsQuery(
                fixture.Actor,
                Guid.NewGuid()),
            CancellationToken.None);

        await DeactivateActorAsync(database, fixture.Actor.AccountId.Value);
        var denied = await handler.ExecuteAsync(
            new GetClientMembershipHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            missingId,
            GetClientMembershipHistorySourceRowsStatus.ValidationFailed,
            "clientId");
        AssertFailure(
            reversedRange,
            GetClientMembershipHistorySourceRowsStatus.ValidationFailed,
            "occurredBeforeExclusive");
        AssertFailure(
            invalidLimit,
            GetClientMembershipHistorySourceRowsStatus.ValidationFailed,
            "limit");
        AssertFailure(
            missingClient,
            GetClientMembershipHistorySourceRowsStatus.NotFound,
            "clientId");
        AssertFailure(
            denied,
            GetClientMembershipHistorySourceRowsStatus.PermissionDenied);
    }

    [Fact]
    public void PersistenceRegistrationResolvesMembershipHistorySourceQuery()
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

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(
                    IBodyLifeQueryHandler<
                        GetClientMembershipHistorySourceRowsQuery,
                        GetClientMembershipHistorySourceRowsResult>)
                && descriptor.ImplementationType
                    == typeof(GetClientMembershipHistorySourceRowsQueryHandler)
                && descriptor.Lifetime == ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetClientMembershipHistorySourceRowsQuery,
                GetClientMembershipHistorySourceRowsResult>>());
    }

    private static GetClientMembershipHistorySourceRowsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        return new GetClientMembershipHistorySourceRowsQueryHandler(
            dbContext,
            new GetClientAuditEntriesQueryHandler(dbContext, timeProvider));
    }

    private static async Task<HistoryFixture> SeedFixtureAsync(
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
                    'History',
                    'Client',
                    null,
                    'HISTORY CLIENT',
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
                'Current catalog name',
                45,
                20,
                1600,
                'UAH',
                true,
                null,
                @membership_type_created_at,
                @membership_type_updated_at,
                null);
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("other_client_id", otherClientId);
        command.Parameters.AddWithValue("created_at", TestNow.AddMonths(-1));
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue(
            "membership_type_created_at",
            TestNow.AddMonths(-2));
        command.Parameters.AddWithValue(
            "membership_type_updated_at",
            TestNow.AddMonths(-1));
        Assert.Equal(4, await command.ExecuteNonQueryAsync());

        return new HistoryFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Reception tablet"),
            clientId,
            otherClientId,
            membershipTypeId);
    }

    private static async Task<HistorySourceIds> SeedHistoryAsync(
        PostgreSqlTestDatabase database,
        HistoryFixture fixture)
    {
        var membershipId = Guid.NewGuid();
        var openingStateId = Guid.NewGuid();
        var entryBatchId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
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
                    'Legacy monthly',
                    30,
                    12,
                    1250,
                    'UAH',
                    @start_date,
                    @base_end_date,
                    @issued_at,
                    @account_id,
                    'active',
                    'normal',
                    null,
                    'Imported contract');

                insert into bodylife.membership_opening_states (
                    id,
                    membership_id,
                    opening_as_of_date,
                    declared_remaining_visits,
                    declared_negative_balance,
                    known_effective_end_date,
                    known_extension_days,
                    source_reference,
                    reason,
                    recorded_at,
                    recorded_by_account_id,
                    recorded_session_id,
                    entry_origin,
                    entry_batch_id,
                    status)
                values (
                    @opening_state_id,
                    @membership_id,
                    @opening_as_of_date,
                    -2,
                    2,
                    @known_effective_end_date,
                    5,
                    'legacy-ledger-42',
                    'Initial backfill',
                    @opening_recorded_at,
                    @account_id,
                    @session_id,
                    'manual_backfill',
                    @entry_batch_id,
                    'active');
                """;
            command.Parameters.AddWithValue("membership_id", membershipId);
            command.Parameters.AddWithValue("client_id", fixture.ClientId);
            command.Parameters.AddWithValue(
                "membership_type_id",
                fixture.MembershipTypeId);
            command.Parameters.AddWithValue("start_date", new DateOnly(2026, 7, 1));
            command.Parameters.AddWithValue("base_end_date", new DateOnly(2026, 7, 30));
            command.Parameters.AddWithValue("issued_at", TestNow.AddDays(-3));
            command.Parameters.AddWithValue(
                "account_id",
                fixture.Actor.AccountId.Value);
            command.Parameters.AddWithValue("opening_state_id", openingStateId);
            command.Parameters.AddWithValue(
                "opening_as_of_date",
                new DateOnly(2026, 7, 15));
            command.Parameters.AddWithValue(
                "known_effective_end_date",
                new DateOnly(2026, 8, 5));
            command.Parameters.AddWithValue(
                "opening_recorded_at",
                TestNow.AddDays(-1));
            command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
            command.Parameters.AddWithValue("entry_batch_id", entryBatchId);
            Assert.Equal(2, await command.ExecuteNonQueryAsync());
        }

        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            MembershipAuditActions.Issued,
            MembershipAuditActions.MembershipEntityType,
            membershipId,
            fixture.ClientId,
            TestNow.AddDays(-3),
            TestNow.AddDays(-3),
            "normal");
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            MembershipAuditActions.OpeningStateCreated,
            MembershipAuditActions.OpeningStateEntityType,
            openingStateId,
            fixture.ClientId,
            TestNow.AddDays(-2),
            TestNow.AddDays(-1),
            "manual_backfill",
            "Initial backfill");
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            "membership.corrected",
            MembershipAuditActions.MembershipEntityType,
            membershipId,
            fixture.ClientId,
            TestNow.AddHours(-1),
            TestNow.AddMinutes(-59),
            "normal");
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            MembershipAuditActions.Issued,
            MembershipAuditActions.MembershipEntityType,
            Guid.NewGuid(),
            fixture.OtherClientId,
            TestNow.AddMinutes(-30),
            TestNow.AddMinutes(-29),
            "normal");

        return new HistorySourceIds(membershipId, openingStateId, entryBatchId);
    }

    private static async Task InsertAuditAsync(
        PostgreSqlTestDatabase database,
        HistoryFixture fixture,
        Guid auditId,
        string actionType,
        string entityType,
        Guid entityId,
        Guid clientId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin,
        string? reason = null)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.business_audit_entries (
                id,
                action_type,
                entity_type,
                entity_id,
                related_entity_refs,
                actor_account_id,
                actor_account_type,
                actor_role,
                session_id,
                device_label,
                occurred_at,
                recorded_at,
                reason,
                comment,
                before_summary,
                after_summary,
                request_correlation_id,
                entry_origin,
                idempotency_key,
                changed_after_close)
            values (
                @id,
                @action_type,
                @entity_type,
                @entity_id,
                @related_entity_refs,
                @actor_account_id,
                'owner',
                'owner',
                @session_id,
                'Reception tablet',
                @occurred_at,
                @recorded_at,
                @reason,
                null,
                '{}'::jsonb,
                '{"state":"recorded"}'::jsonb,
                @request_correlation_id,
                @entry_origin,
                @idempotency_key,
                false)
            """;
        command.Parameters.AddWithValue("id", auditId);
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.Add(
            "related_entity_refs",
            NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(
                new { ClientId = clientId },
                AuditJsonOptions);
        command.Parameters.AddWithValue(
            "actor_account_id",
            fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.Add("reason", NpgsqlDbType.Varchar).Value =
            reason ?? (object)DBNull.Value;
        command.Parameters.AddWithValue(
            "request_correlation_id",
            $"history-{auditId:N}");
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.AddWithValue(
            "idempotency_key",
            $"history-idempotency-{auditId:N}");
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

    private static ClientMembershipHistorySourceRowsPage AssertSuccess(
        GetClientMembershipHistorySourceRowsResult result,
        Guid clientId)
    {
        Assert.Equal(GetClientMembershipHistorySourceRowsStatus.Success, result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        var page = Assert.IsType<ClientMembershipHistorySourceRowsPage>(result.Page);
        Assert.Equal(clientId, page.ClientId);
        return page;
    }

    private static void AssertFailure(
        GetClientMembershipHistorySourceRowsResult result,
        GetClientMembershipHistorySourceRowsStatus status,
        string? field = null)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Page);
        Assert.NotNull(result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(field, result.ErrorField);
    }

    private sealed record HistoryFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipTypeId);

    private sealed record HistorySourceIds(
        Guid MembershipId,
        Guid OpeningStateId,
        Guid EntryBatchId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
