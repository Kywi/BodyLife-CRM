using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetClientFreezeHistorySourceRowsQueryTests
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(
        JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        23,
        12,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryKeepsAddedAndCanceledSourcesInAuditChronology()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var source = await SeedHistoryAsync(database, fixture);
        var handler = CreateHandler(dbContext);

        var firstResult = await handler.ExecuteAsync(
            new GetClientFreezeHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 2),
            CancellationToken.None);
        var secondResult = await handler.ExecuteAsync(
            new GetClientFreezeHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 2,
                Offset: 2),
            CancellationToken.None);
        var rangedResult = await handler.ExecuteAsync(
            new GetClientFreezeHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                OccurredFromInclusive: TestNow.AddDays(-2).AddHours(-12),
                OccurredBeforeExclusive: TestNow.AddHours(-12)),
            CancellationToken.None);

        var firstPage = AssertSuccess(firstResult, fixture.ClientId);
        Assert.True(firstPage.HasMore);
        Assert.Equal(2, firstPage.NextOffset);
        Assert.Equal(
            [
                ClientFreezeHistorySourceKind.CanceledFreeze,
                ClientFreezeHistorySourceKind.AddedFreeze,
            ],
            firstPage.Items.Select(row => row.Kind));

        var canceledRow = firstPage.Items[0];
        Assert.Equal(source.CanceledFreezeId, canceledRow.FreezeId);
        Assert.Equal(TestNow.AddDays(-1), canceledRow.OccurredAt);
        Assert.Equal(TestNow.AddDays(-1).AddMinutes(10), canceledRow.RecordedAt);
        Assert.Equal(EntryOrigin.ManualBackfill, canceledRow.EntryOrigin);
        Assert.Null(canceledRow.AddedFreeze);
        var cancellation = Assert.IsType<FreezeCancellationHistorySource>(
            canceledRow.Cancellation);
        Assert.Equal(source.CancellationId, cancellation.CancellationId);
        Assert.Equal("Mistaken freeze range", cancellation.Reason);
        Assert.Equal(source.CancellationBatchId, cancellation.EntryBatchId);
        Assert.Equal(fixture.Actor.AccountId, cancellation.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId, cancellation.RecordedSessionId);
        Assert.Equal(fixture.MembershipId, cancellation.MembershipId);
        Assert.Equal("Eight visits / 30 days", cancellation.Freeze.MembershipTypeNameSnapshot);
        Assert.Equal(new DateOnly(2026, 7, 10), cancellation.Freeze.Range.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 12), cancellation.Freeze.Range.EndDate);
        Assert.Equal(3, cancellation.Freeze.Range.InclusiveDays);
        Assert.Equal("Medical pause", cancellation.Freeze.Reason);
        Assert.Equal(
            FreezeCancellationSourceStatus.Canceled,
            cancellation.Freeze.CurrentStatus);
        Assert.Equal(
            source.CancellationId,
            cancellation.Freeze.CurrentCancellationId);
        Assert.Equal(FreezeAuditActions.Canceled, canceledRow.AuditEntry.ActionType);
        Assert.Equal("Mistaken freeze range", canceledRow.AuditEntry.Reason);
        Assert.True(canceledRow.AuditEntry.ChangedAfterClose);

        var activeRow = firstPage.Items[1];
        Assert.Equal(source.ActiveFreezeId, activeRow.FreezeId);
        Assert.Equal(TestNow.AddDays(-2), activeRow.OccurredAt);
        Assert.Equal(
            TestNow.AddDays(-1).AddHours(-12),
            activeRow.RecordedAt);
        Assert.Equal(EntryOrigin.PaperFallback, activeRow.EntryOrigin);
        Assert.Null(activeRow.Cancellation);
        var activeFreeze = Assert.IsType<FreezeHistorySource>(
            activeRow.AddedFreeze);
        Assert.Equal("Travel pause", activeFreeze.Reason);
        Assert.Equal(5, activeFreeze.Range.InclusiveDays);
        Assert.Equal(source.ActiveFreezeBatchId, activeFreeze.EntryBatchId);
        Assert.Equal(
            FreezeCancellationSourceStatus.Active,
            activeFreeze.CurrentStatus);
        Assert.Null(activeFreeze.CurrentCancellationId);
        Assert.Equal(FreezeAuditActions.Added, activeRow.AuditEntry.ActionType);

        var secondPage = AssertSuccess(secondResult, fixture.ClientId);
        Assert.False(secondPage.HasMore);
        Assert.Null(secondPage.NextOffset);
        var originalAddedRow = Assert.Single(secondPage.Items);
        Assert.Equal(
            ClientFreezeHistorySourceKind.AddedFreeze,
            originalAddedRow.Kind);
        Assert.Equal(source.CanceledFreezeId, originalAddedRow.FreezeId);
        Assert.Equal(
            FreezeCancellationSourceStatus.Canceled,
            originalAddedRow.AddedFreeze!.CurrentStatus);
        Assert.Equal(
            source.CancellationId,
            originalAddedRow.AddedFreeze.CurrentCancellationId);
        Assert.Equal(3, originalAddedRow.AddedFreeze.Range.InclusiveDays);

        var rangedPage = AssertSuccess(rangedResult, fixture.ClientId);
        Assert.Equal(
            [
                ClientFreezeHistorySourceKind.CanceledFreeze,
                ClientFreezeHistorySourceKind.AddedFreeze,
            ],
            rangedPage.Items.Select(row => row.Kind));
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenAuditHasNoCanonicalFreeze()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            FreezeAuditActions.Added,
            Guid.NewGuid(),
            fixture.ClientId,
            TestNow.AddDays(-1),
            TestNow.AddDays(-1),
            "normal",
            "Medical pause");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientFreezeHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientFreezeHistorySourceRowsStatus.SourceInconsistent);
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenCanceledFreezeHasNoCancellationFact()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var source = await SeedHistoryAsync(database, fixture);
        await DeleteCancellationAsync(database, source.CancellationId);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientFreezeHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientFreezeHistorySourceRowsStatus.SourceInconsistent);
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenCancellationAndAuditEnvelopesDisagree()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var source = await SeedHistoryAsync(database, fixture);
        await ChangeCancellationRecordedAtAsync(
            database,
            source.CancellationId,
            TestNow.AddDays(-1).AddMinutes(11));

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientFreezeHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientFreezeHistorySourceRowsStatus.SourceInconsistent);
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
            new GetClientFreezeHistorySourceRowsQuery(
                fixture.Actor,
                Guid.Empty),
            CancellationToken.None);
        var reversedRange = await handler.ExecuteAsync(
            new GetClientFreezeHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                TestNow,
                TestNow),
            CancellationToken.None);
        var invalidLimit = await handler.ExecuteAsync(
            new GetClientFreezeHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: GetClientFreezeHistorySourceRowsQuery.MaxLimit + 1),
            CancellationToken.None);
        var invalidOffset = await handler.ExecuteAsync(
            new GetClientFreezeHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Offset: GetClientFreezeHistorySourceRowsQuery.MaxOffset + 1),
            CancellationToken.None);
        var missingClient = await handler.ExecuteAsync(
            new GetClientFreezeHistorySourceRowsQuery(
                fixture.Actor,
                Guid.NewGuid()),
            CancellationToken.None);

        await DeactivateActorAsync(database, fixture.Actor.AccountId.Value);
        var denied = await handler.ExecuteAsync(
            new GetClientFreezeHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            missingId,
            GetClientFreezeHistorySourceRowsStatus.ValidationFailed,
            "clientId");
        AssertFailure(
            reversedRange,
            GetClientFreezeHistorySourceRowsStatus.ValidationFailed,
            "occurredBeforeExclusive");
        AssertFailure(
            invalidLimit,
            GetClientFreezeHistorySourceRowsStatus.ValidationFailed,
            "limit");
        AssertFailure(
            invalidOffset,
            GetClientFreezeHistorySourceRowsStatus.ValidationFailed,
            "offset");
        AssertFailure(
            missingClient,
            GetClientFreezeHistorySourceRowsStatus.NotFound,
            "clientId");
        AssertFailure(
            denied,
            GetClientFreezeHistorySourceRowsStatus.PermissionDenied);
    }

    [Fact]
    public void PersistenceRegistrationResolvesFreezeHistorySourceQuery()
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
                        GetClientFreezeHistorySourceRowsQuery,
                        GetClientFreezeHistorySourceRowsResult>)
                && descriptor.ImplementationType
                    == typeof(GetClientFreezeHistorySourceRowsQueryHandler)
                && descriptor.Lifetime == ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetClientFreezeHistorySourceRowsQuery,
                GetClientFreezeHistorySourceRowsResult>>());
    }

    private static GetClientFreezeHistorySourceRowsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        return new GetClientFreezeHistorySourceRowsQueryHandler(
            dbContext,
            new GetClientAuditEntriesQueryHandler(dbContext, timeProvider));
    }

    private static async Task<FreezeHistoryFixture> SeedFixtureAsync(
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
                    'Freeze',
                    'History',
                    null,
                    'FREEZE HISTORY',
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
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-2));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("other_client_id", otherClientId);
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
        command.Parameters.AddWithValue("issued_at", TestNow.AddDays(-20));
        Assert.Equal(5, await command.ExecuteNonQueryAsync());

        return new FreezeHistoryFixture(
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

    private static async Task<FreezeHistorySourceIds> SeedHistoryAsync(
        PostgreSqlTestDatabase database,
        FreezeHistoryFixture fixture)
    {
        var canceledFreezeId = Guid.NewGuid();
        var activeFreezeId = Guid.NewGuid();
        var activeFreezeBatchId = Guid.NewGuid();
        var cancellationId = Guid.NewGuid();
        var cancellationBatchId = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
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
                values
                    (
                        @canceled_freeze_id,
                        @client_id,
                        @membership_id,
                        @canceled_start_date,
                        @canceled_end_date,
                        'Medical pause',
                        @canceled_occurred_at,
                        @canceled_recorded_at,
                        @account_id,
                        @session_id,
                        'normal',
                        null,
                        'canceled'),
                    (
                        @active_freeze_id,
                        @client_id,
                        @membership_id,
                        @active_start_date,
                        @active_end_date,
                        'Travel pause',
                        @active_occurred_at,
                        @active_recorded_at,
                        @account_id,
                        @session_id,
                        'paper_fallback',
                        @active_freeze_batch_id,
                        'active');

                insert into bodylife.freeze_cancellations (
                    id,
                    freeze_id,
                    reason,
                    occurred_at,
                    recorded_at,
                    recorded_by_account_id,
                    session_id,
                    entry_origin,
                    entry_batch_id)
                values (
                    @cancellation_id,
                    @canceled_freeze_id,
                    'Mistaken freeze range',
                    @cancellation_occurred_at,
                    @cancellation_recorded_at,
                    @account_id,
                    @session_id,
                    'manual_backfill',
                    @cancellation_batch_id)
                """;
            command.Parameters.AddWithValue("canceled_freeze_id", canceledFreezeId);
            command.Parameters.AddWithValue("active_freeze_id", activeFreezeId);
            command.Parameters.AddWithValue("client_id", fixture.ClientId);
            command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
            command.Parameters.AddWithValue(
                "canceled_start_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 10));
            command.Parameters.AddWithValue(
                "canceled_end_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 12));
            command.Parameters.AddWithValue(
                "canceled_occurred_at",
                TestNow.AddDays(-4));
            command.Parameters.AddWithValue(
                "canceled_recorded_at",
                TestNow.AddDays(-4).AddMinutes(5));
            command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
            command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
            command.Parameters.AddWithValue(
                "active_start_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 15));
            command.Parameters.AddWithValue(
                "active_end_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 19));
            command.Parameters.AddWithValue(
                "active_occurred_at",
                TestNow.AddDays(-2));
            command.Parameters.AddWithValue(
                "active_recorded_at",
                TestNow.AddDays(-1).AddHours(-12));
            command.Parameters.AddWithValue(
                "active_freeze_batch_id",
                activeFreezeBatchId);
            command.Parameters.AddWithValue("cancellation_id", cancellationId);
            command.Parameters.AddWithValue(
                "cancellation_occurred_at",
                TestNow.AddDays(-1));
            command.Parameters.AddWithValue(
                "cancellation_recorded_at",
                TestNow.AddDays(-1).AddMinutes(10));
            command.Parameters.AddWithValue(
                "cancellation_batch_id",
                cancellationBatchId);
            Assert.Equal(3, await command.ExecuteNonQueryAsync());
        }

        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            FreezeAuditActions.Added,
            canceledFreezeId,
            fixture.ClientId,
            TestNow.AddDays(-4),
            TestNow.AddDays(-4).AddMinutes(5),
            "normal",
            "Medical pause");
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            FreezeAuditActions.Added,
            activeFreezeId,
            fixture.ClientId,
            TestNow.AddDays(-2),
            TestNow.AddDays(-1).AddHours(-12),
            "paper_fallback",
            "Travel pause");
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            FreezeAuditActions.Canceled,
            canceledFreezeId,
            fixture.ClientId,
            TestNow.AddDays(-1),
            TestNow.AddDays(-1).AddMinutes(10),
            "manual_backfill",
            "Mistaken freeze range",
            changedAfterClose: true);
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            "freeze.reviewed",
            canceledFreezeId,
            fixture.ClientId,
            TestNow.AddMinutes(-20),
            TestNow.AddMinutes(-19),
            "normal",
            "Ignored audit");
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            FreezeAuditActions.Added,
            Guid.NewGuid(),
            fixture.OtherClientId,
            TestNow.AddMinutes(-10),
            TestNow.AddMinutes(-9),
            "normal",
            "Other client pause");

        return new FreezeHistorySourceIds(
            canceledFreezeId,
            activeFreezeId,
            activeFreezeBatchId,
            cancellationId,
            cancellationBatchId);
    }

    private static async Task InsertAuditAsync(
        PostgreSqlTestDatabase database,
        FreezeHistoryFixture fixture,
        Guid auditId,
        string actionType,
        Guid freezeId,
        Guid clientId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin,
        string reason,
        bool changedAfterClose = false)
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
                'freeze',
                @freeze_id,
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
                '{"state":"before"}'::jsonb,
                '{"state":"after"}'::jsonb,
                @request_correlation_id,
                @entry_origin,
                @idempotency_key,
                @changed_after_close)
            """;
        command.Parameters.AddWithValue("id", auditId);
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.AddWithValue("freeze_id", freezeId);
        command.Parameters.Add(
            "related_entity_refs",
            NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(
                new { ClientId = clientId, fixture.MembershipId },
                AuditJsonOptions);
        command.Parameters.AddWithValue(
            "actor_account_id",
            fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue(
            "request_correlation_id",
            $"freeze-history-{auditId:N}");
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.AddWithValue(
            "idempotency_key",
            $"freeze-history-idempotency-{auditId:N}");
        command.Parameters.AddWithValue("changed_after_close", changedAfterClose);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeleteCancellationAsync(
        PostgreSqlTestDatabase database,
        Guid cancellationId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "delete from bodylife.freeze_cancellations where id = @id";
        command.Parameters.AddWithValue("id", cancellationId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task ChangeCancellationRecordedAtAsync(
        PostgreSqlTestDatabase database,
        Guid cancellationId,
        DateTimeOffset recordedAt)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.freeze_cancellations
            set recorded_at = @recorded_at
            where id = @id
            """;
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("id", cancellationId);
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

    private static ClientFreezeHistorySourceRowsPage AssertSuccess(
        GetClientFreezeHistorySourceRowsResult result,
        Guid clientId)
    {
        Assert.Equal(GetClientFreezeHistorySourceRowsStatus.Success, result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        var page = Assert.IsType<ClientFreezeHistorySourceRowsPage>(result.Page);
        Assert.Equal(clientId, page.ClientId);
        return page;
    }

    private static void AssertFailure(
        GetClientFreezeHistorySourceRowsResult result,
        GetClientFreezeHistorySourceRowsStatus status,
        string? field = null)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Page);
        Assert.NotNull(result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(field, result.ErrorField);
    }

    private sealed record FreezeHistoryFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId);

    private sealed record FreezeHistorySourceIds(
        Guid CanceledFreezeId,
        Guid ActiveFreezeId,
        Guid ActiveFreezeBatchId,
        Guid CancellationId,
        Guid CancellationBatchId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
