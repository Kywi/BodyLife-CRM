using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetAuditTimelineQueryTests
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
    public async Task GlobalTimelineUsesRecordedChronologyAndReturnsFullEnvelope()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database);
        var oldestId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var tiedLowerId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var tiedHigherId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var newestId = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var visitId = Guid.NewGuid();

        await InsertAuditAsync(
            database,
            fixture.SharedAdmin,
            oldestId,
            "client.created",
            "client",
            fixture.ClientId,
            new { ClientId = fixture.ClientId },
            occurredAt: TestNow.AddDays(-2),
            recordedAt: TestNow.AddHours(-3));
        await InsertAuditAsync(
            database,
            fixture.SharedAdmin,
            tiedLowerId,
            "visit.marked",
            "visit",
            visitId,
            new { ClientId = fixture.ClientId, MembershipId = Guid.NewGuid() },
            occurredAt: TestNow.AddDays(-3),
            recordedAt: TestNow.AddHours(-2),
            entryOrigin: "paper_fallback",
            reason: "Recovered reception sheet",
            comment: "Entered after connectivity returned",
            changedAfterClose: true);
        await InsertAuditAsync(
            database,
            fixture.SharedAdmin,
            tiedHigherId,
            "payment.created",
            "payment",
            Guid.NewGuid(),
            new { ClientId = fixture.OtherClientId },
            occurredAt: TestNow.AddHours(-1),
            recordedAt: TestNow.AddHours(-2));
        await InsertAuditAsync(
            database,
            fixture.SharedAdmin,
            newestId,
            "staff_account.updated",
            "staff_account",
            fixture.NamedAdmin.AccountId.Value,
            new { AccountId = fixture.NamedAdmin.AccountId.Value },
            occurredAt: TestNow.AddMinutes(-20),
            recordedAt: TestNow.AddMinutes(-10));

        var handler = CreateHandler(dbContext);
        var firstResult = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(fixture.Owner, Limit: 2),
            CancellationToken.None);
        var secondResult = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(fixture.NamedAdmin, Limit: 2, Offset: 2),
            CancellationToken.None);
        var sharedResult = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(
                fixture.SharedAdmin,
                EntityType: AuditTimelineEntityType.Visit),
            CancellationToken.None);

        var firstPage = AssertSuccess(firstResult);
        Assert.Equal(
            [newestId, tiedHigherId],
            firstPage.Items.Select(item => item.AuditEntryId.Value).ToArray());
        Assert.True(firstPage.HasMore);
        Assert.Equal(2, firstPage.NextOffset);

        var secondPage = AssertSuccess(secondResult);
        Assert.Equal(
            [tiedLowerId, oldestId],
            secondPage.Items.Select(item => item.AuditEntryId.Value).ToArray());
        Assert.False(secondPage.HasMore);
        Assert.Null(secondPage.NextOffset);

        var visit = Assert.Single(AssertSuccess(sharedResult).Items);
        Assert.Equal(tiedLowerId, visit.AuditEntryId.Value);
        Assert.Equal("visit.marked", visit.ActionType);
        Assert.Equal(AuditTimelineEntityType.Visit, visit.EntityType);
        Assert.Equal(visitId, visit.EntityId);
        Assert.Equal(fixture.SharedAdmin.AccountId, visit.ActorAccountId);
        Assert.Equal(AccountKind.SharedReceptionAdmin, visit.ActorAccountKind);
        Assert.Equal(ActorRole.Admin, visit.ActorRole);
        Assert.Equal(fixture.SharedAdmin.SessionId, visit.SessionId);
        Assert.Equal("Shared reception tablet", visit.DeviceLabel);
        Assert.Equal(TestNow.AddDays(-3), visit.OccurredAt);
        Assert.Equal(TestNow.AddHours(-2), visit.RecordedAt);
        Assert.Equal(EntryOrigin.PaperFallback, visit.EntryOrigin);
        Assert.Equal("Recovered reception sheet", visit.Reason);
        Assert.Equal("Entered after connectivity returned", visit.Comment);
        Assert.Contains(fixture.ClientId.ToString(), visit.RelatedEntityRefsJson);
        Assert.Equal("{}", visit.BeforeSummaryJson);
        Assert.Contains("recorded", visit.AfterSummaryJson, StringComparison.Ordinal);
        Assert.Equal($"audit-{tiedLowerId:N}", visit.RequestCorrelationId.Value);
        Assert.Equal($"key-{tiedLowerId:N}", visit.IdempotencyKey);
        Assert.True(visit.ChangedAfterClose);
    }

    [PostgreSqlFact]
    public async Task FiltersIntersectClientEntityRecordedRangeAndActions()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database);
        var directClientId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var affectedPeriodId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var paymentEntityId = Guid.NewGuid();

        await InsertAuditAsync(
            database,
            fixture.Owner,
            directClientId,
            "client.updated",
            "client",
            fixture.ClientId,
            new { },
            TestNow.AddDays(-5),
            TestNow.AddHours(-4));
        await InsertAuditAsync(
            database,
            fixture.Owner,
            visitId,
            "visit.marked",
            "visit",
            Guid.NewGuid(),
            new { ClientId = fixture.ClientId },
            TestNow.AddDays(-4),
            TestNow.AddHours(-2));
        await InsertAuditAsync(
            database,
            fixture.Owner,
            affectedPeriodId,
            "non_working_day.added",
            "non_working_period",
            Guid.NewGuid(),
            new
            {
                AffectedClientIds = new[] { fixture.OtherClientId, fixture.ClientId },
            },
            TestNow.AddDays(-3),
            TestNow.AddHours(-1));
        await InsertAuditAsync(
            database,
            fixture.Owner,
            paymentId,
            "payment.created",
            "payment",
            paymentEntityId,
            new { ClientId = fixture.OtherClientId },
            TestNow.AddDays(-2),
            TestNow.AddMinutes(-30));

        var handler = CreateHandler(dbContext);
        var clientResult = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(fixture.Owner, ClientId: fixture.ClientId),
            CancellationToken.None);
        var clientVisitResult = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(
                fixture.Owner,
                fixture.ClientId,
                AuditTimelineEntityType.Visit,
                RecordedFromInclusive: TestNow.AddHours(-3),
                RecordedBeforeExclusive: TestNow,
                ActionTypes: [" visit.marked ", "visit.marked"]),
            CancellationToken.None);
        var exactPaymentResult = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(
                fixture.Owner,
                EntityType: AuditTimelineEntityType.Payment,
                EntityId: paymentEntityId),
            CancellationToken.None);
        var beforeBoundaryResult = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(
                fixture.Owner,
                RecordedBeforeExclusive: TestNow.AddHours(-1)),
            CancellationToken.None);

        var clientPage = AssertSuccess(clientResult);
        Assert.Equal(fixture.ClientId, clientPage.ClientId);
        Assert.Equal(
            [affectedPeriodId, visitId, directClientId],
            clientPage.Items.Select(item => item.AuditEntryId.Value).ToArray());

        var clientVisitPage = AssertSuccess(clientVisitResult);
        Assert.Equal(AuditTimelineEntityType.Visit, clientVisitPage.EntityType);
        Assert.Null(clientVisitPage.EntityId);
        Assert.Equal(["visit.marked"], clientVisitPage.ActionTypes);
        Assert.Equal(visitId, Assert.Single(clientVisitPage.Items).AuditEntryId.Value);

        var exactPaymentPage = AssertSuccess(exactPaymentResult);
        Assert.Equal(paymentId, Assert.Single(exactPaymentPage.Items).AuditEntryId.Value);
        Assert.Equal(AuditTimelineEntityType.Payment, exactPaymentPage.EntityType);
        Assert.NotNull(exactPaymentPage.EntityId);

        var beforeBoundaryPage = AssertSuccess(beforeBoundaryResult);
        Assert.Equal(
            [visitId, directClientId],
            beforeBoundaryPage.Items.Select(item => item.AuditEntryId.Value).ToArray());
    }

    [PostgreSqlFact]
    public async Task InvalidRequestsMissingClientInactiveActorAndUnknownEntityFailClosed()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database);
        var handler = CreateHandler(dbContext);

        var emptyClient = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(fixture.Owner, ClientId: Guid.Empty),
            CancellationToken.None);
        var unpairedEntityId = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(fixture.Owner, EntityId: Guid.NewGuid()),
            CancellationToken.None);
        var emptyEntityId = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(
                fixture.Owner,
                EntityType: AuditTimelineEntityType.Visit,
                EntityId: Guid.Empty),
            CancellationToken.None);
        var invalidEntityType = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(
                fixture.Owner,
                EntityType: (AuditTimelineEntityType)999),
            CancellationToken.None);
        var reversedRange = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(
                fixture.Owner,
                RecordedFromInclusive: TestNow,
                RecordedBeforeExclusive: TestNow),
            CancellationToken.None);
        var blankAction = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(fixture.Owner, ActionTypes: [" "]),
            CancellationToken.None);
        var invalidLimit = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(
                fixture.Owner,
                Limit: GetAuditTimelineQuery.MaxLimit + 1),
            CancellationToken.None);
        var invalidOffset = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(fixture.Owner, Offset: -1),
            CancellationToken.None);
        var missingClient = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(fixture.Owner, ClientId: Guid.NewGuid()),
            CancellationToken.None);

        await DeactivateActorAsync(database, fixture.NamedAdmin.AccountId.Value);
        var denied = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(fixture.NamedAdmin),
            CancellationToken.None);

        await InsertAuditAsync(
            database,
            fixture.Owner,
            Guid.NewGuid(),
            "future_entity.changed",
            "future_entity",
            Guid.NewGuid(),
            new { },
            TestNow,
            TestNow);
        var unknownEntity = await handler.ExecuteAsync(
            new GetAuditTimelineQuery(fixture.Owner),
            CancellationToken.None);

        AssertFailure(emptyClient, GetAuditTimelineStatus.ValidationFailed, "clientId");
        AssertFailure(
            unpairedEntityId,
            GetAuditTimelineStatus.ValidationFailed,
            "entityId");
        AssertFailure(emptyEntityId, GetAuditTimelineStatus.ValidationFailed, "entityId");
        AssertFailure(
            invalidEntityType,
            GetAuditTimelineStatus.ValidationFailed,
            "entityType");
        AssertFailure(
            reversedRange,
            GetAuditTimelineStatus.ValidationFailed,
            "recordedBeforeExclusive");
        AssertFailure(blankAction, GetAuditTimelineStatus.ValidationFailed, "actionTypes");
        AssertFailure(invalidLimit, GetAuditTimelineStatus.ValidationFailed, "limit");
        AssertFailure(invalidOffset, GetAuditTimelineStatus.ValidationFailed, "offset");
        AssertFailure(missingClient, GetAuditTimelineStatus.NotFound, "clientId");
        AssertFailure(denied, GetAuditTimelineStatus.PermissionDenied);
        AssertFailure(unknownEntity, GetAuditTimelineStatus.SourceInconsistent);
    }

    [PostgreSqlFact]
    public async Task RecordedAndEntityQueriesUseAuditTimelineIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var recordedIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_business_audit_entries_recorded_timeline");
        var globalPlan = await ReadGlobalTimelinePlanAsync(database);
        var entityPlan = await ReadEntityTimelinePlanAsync(database, Guid.NewGuid());

        Assert.Contains("recorded_at DESC", recordedIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id DESC", recordedIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "ix_business_audit_entries_recorded_timeline",
            globalPlan,
            StringComparison.Ordinal);
        Assert.Contains(
            "ix_business_audit_entries_entity_timeline",
            entityPlan,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PersistenceRegistrationResolvesAuditTimelineQuery()
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
                    IBodyLifeQueryHandler<GetAuditTimelineQuery, GetAuditTimelineResult>)
                && descriptor.ImplementationType == typeof(GetAuditTimelineQueryHandler)
                && descriptor.Lifetime == ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<GetAuditTimelineQuery, GetAuditTimelineResult>>());
    }

    private static GetAuditTimelineQueryHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new GetAuditTimelineQueryHandler(
            dbContext,
            new FixedTimeProvider(TestNow));
    }

    private static async Task<AuditTimelineFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database)
    {
        var owner = await SeedActorAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner,
            "Owner workstation");
        var namedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            "Named admin laptop");
        var sharedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin,
            "Shared reception tablet");
        var clientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                    'Audit',
                    'Client',
                    null,
                    'AUDIT CLIENT',
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
                    @created_at)
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("other_client_id", otherClientId);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-10));
        command.Parameters.AddWithValue("account_id", owner.AccountId.Value);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        return new AuditTimelineFixture(
            owner,
            namedAdmin,
            sharedAdmin,
            clientId,
            otherClientId);
    }

    private static async Task<ActorContext> SeedActorAsync(
        PostgreSqlTestDatabase database,
        ActorRole role,
        AccountKind accountKind,
        string deviceLabel)
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.accounts (
                id,
                display_name,
                account_type,
                role,
                is_active,
                created_at,
                deactivated_at)
            values (
                @account_id,
                @display_name,
                @account_type,
                @role,
                true,
                @created_at,
                null);

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
                @device_label,
                @started_at,
                @expires_at,
                null,
                @last_seen_at)
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("display_name", $"{accountKind} audit actor");
        command.Parameters.AddWithValue("account_type", MapAccountKind(accountKind));
        command.Parameters.AddWithValue("role", MapRole(role));
        command.Parameters.AddWithValue("created_at", TestNow.AddHours(-2));
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("device_label", deviceLabel);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        return new ActorContext(
            new AccountId(accountId),
            role,
            accountKind,
            new SessionId(sessionId),
            deviceLabel);
    }

    private static async Task InsertAuditAsync(
        PostgreSqlTestDatabase database,
        ActorContext actor,
        Guid id,
        string actionType,
        string entityType,
        Guid entityId,
        object relatedEntityRefs,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin = "normal",
        string? reason = null,
        string? comment = null,
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
                @entity_type,
                @entity_id,
                @related_entity_refs,
                @actor_account_id,
                @actor_account_type,
                @actor_role,
                @session_id,
                @device_label,
                @occurred_at,
                @recorded_at,
                @reason,
                @comment,
                '{}'::jsonb,
                '{"state":"recorded"}'::jsonb,
                @request_correlation_id,
                @entry_origin,
                @idempotency_key,
                @changed_after_close)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.Add(
            "related_entity_refs",
            NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(
                relatedEntityRefs,
                AuditJsonOptions);
        command.Parameters.AddWithValue("actor_account_id", actor.AccountId.Value);
        command.Parameters.AddWithValue(
            "actor_account_type",
            MapAccountKind(actor.AccountKind));
        command.Parameters.AddWithValue("actor_role", MapRole(actor.Role));
        command.Parameters.AddWithValue("session_id", actor.SessionId.Value);
        command.Parameters.Add("device_label", NpgsqlDbType.Varchar).Value =
            actor.DeviceLabel ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.Add("reason", NpgsqlDbType.Varchar).Value =
            reason ?? (object)DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Varchar).Value =
            comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("request_correlation_id", $"audit-{id:N}");
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.AddWithValue("idempotency_key", $"key-{id:N}");
        command.Parameters.AddWithValue("changed_after_close", changedAfterClose);
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

    private static async Task<string> ReadIndexDefinitionAsync(
        PostgreSqlTestDatabase database,
        string indexName)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select indexdef
            from pg_indexes
            where schemaname = 'bodylife'
              and indexname = @index_name
            """;
        command.Parameters.AddWithValue("index_name", indexName);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadGlobalTimelinePlanAsync(
        PostgreSqlTestDatabase database)
    {
        return await ReadQueryPlanAsync(
            database,
            """
            explain (costs off)
            select id
            from bodylife.business_audit_entries
            where recorded_at >= @recorded_from
            order by recorded_at desc, id desc
            limit 51
            """,
            command => command.Parameters.AddWithValue(
                "recorded_from",
                TestNow.AddYears(-1)));
    }

    private static async Task<string> ReadEntityTimelinePlanAsync(
        PostgreSqlTestDatabase database,
        Guid entityId)
    {
        return await ReadQueryPlanAsync(
            database,
            """
            explain (costs off)
            select id
            from bodylife.business_audit_entries
            where entity_type = 'visit'
              and entity_id = @entity_id
            order by recorded_at desc, id desc
            limit 51
            """,
            command => command.Parameters.AddWithValue("entity_id", entityId));
    }

    private static async Task<string> ReadQueryPlanAsync(
        PostgreSqlTestDatabase database,
        string query,
        Action<NpgsqlCommand> configure)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var settingsCommand = connection.CreateCommand())
        {
            settingsCommand.CommandText = "set enable_seqscan = off";
            await settingsCommand.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = query;
        configure(command);
        await using var reader = await command.ExecuteReaderAsync();
        var plan = new List<string>();
        while (await reader.ReadAsync())
        {
            plan.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, plan);
    }

    private static AuditTimelinePage AssertSuccess(GetAuditTimelineResult result)
    {
        Assert.Equal(GetAuditTimelineStatus.Success, result.Status);
        var page = Assert.IsType<AuditTimelinePage>(result.Page);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
        return page;
    }

    private static void AssertFailure(
        GetAuditTimelineResult result,
        GetAuditTimelineStatus status,
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

    private static string MapAccountKind(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.Owner => "owner",
            AccountKind.NamedAdmin => "named_admin",
            AccountKind.SharedReceptionAdmin => "shared_reception_admin",
            _ => throw new ArgumentOutOfRangeException(nameof(accountKind)),
        };
    }

    private static string MapRole(ActorRole role)
    {
        return role switch
        {
            ActorRole.Owner => "owner",
            ActorRole.Admin => "admin",
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
    }

    private sealed record AuditTimelineFixture(
        ActorContext Owner,
        ActorContext NamedAdmin,
        ActorContext SharedAdmin,
        Guid ClientId,
        Guid OtherClientId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
