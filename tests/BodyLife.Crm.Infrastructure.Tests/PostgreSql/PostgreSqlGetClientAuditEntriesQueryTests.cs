using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetClientAuditEntriesQueryTests
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
    public async Task QueryUsesCanonicalClientLinksFiltersAndStablePagination()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var directId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var visitId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var nonWorkingDayId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var canceledVisitId = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var rangeStart = TestNow.AddHours(-4);
        var rangeEnd = TestNow;

        await InsertAuditAsync(
            database,
            fixture,
            directId,
            "client.created",
            "client",
            fixture.ClientId,
            new { MatchedClientIds = new[] { fixture.OtherClientId } },
            TestNow.AddHours(-3),
            TestNow.AddHours(-2).AddMinutes(-59));
        await InsertAuditAsync(
            database,
            fixture,
            visitId,
            "visit.marked",
            "visit",
            Guid.NewGuid(),
            new { ClientId = fixture.ClientId, MembershipId = Guid.NewGuid() },
            TestNow.AddHours(-1),
            TestNow.AddMinutes(-59),
            entryOrigin: "paper_fallback",
            reason: "Recovered reception sheet",
            changedAfterClose: true);
        await InsertAuditAsync(
            database,
            fixture,
            nonWorkingDayId,
            "non_working_day.added",
            "non_working_period",
            Guid.NewGuid(),
            new
            {
                AffectedClientIds = new[] { fixture.OtherClientId, fixture.ClientId },
            },
            TestNow.AddHours(-1),
            TestNow.AddMinutes(-59));
        await InsertAuditAsync(
            database,
            fixture,
            canceledVisitId,
            "visit.canceled",
            "visit",
            Guid.NewGuid(),
            new { ClientId = fixture.ClientId, MembershipId = Guid.NewGuid() },
            TestNow.AddHours(-2),
            TestNow.AddHours(-1).AddMinutes(-59));
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            "client.updated",
            "client",
            fixture.OtherClientId,
            new { MatchedClientIds = new[] { fixture.ClientId } },
            TestNow.AddMinutes(-30),
            TestNow.AddMinutes(-29));
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            "payment.created",
            "payment",
            Guid.NewGuid(),
            new { ClientId = fixture.OtherClientId },
            TestNow.AddMinutes(-20),
            TestNow.AddMinutes(-19));
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            "client.updated",
            "client",
            fixture.ClientId,
            relatedEntityRefs: new { },
            TestNow.AddHours(-5),
            TestNow.AddHours(-4).AddMinutes(1));

        var handler = CreateHandler(dbContext);
        var firstResult = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                rangeStart,
                rangeEnd,
                Limit: 2),
            CancellationToken.None);
        var secondResult = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                rangeStart,
                rangeEnd,
                Limit: 2,
                Offset: 2),
            CancellationToken.None);
        var visitResult = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                rangeStart,
                rangeEnd,
                [ClientAuditEntityFilter.Visit]),
            CancellationToken.None);
        var markedVisitResult = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                rangeStart,
                rangeEnd,
                [ClientAuditEntityFilter.Visit],
                [" visit.marked ", "visit.marked"]),
            CancellationToken.None);
        var exactVisitResult = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                rangeStart,
                rangeEnd,
                [ClientAuditEntityFilter.Visit],
                ["visit.marked", "visit.canceled"],
                Limit: 2,
                Offset: 0,
                AuditEntryIds:
                [
                    new AuditEntryId(canceledVisitId),
                    new AuditEntryId(visitId),
                    new AuditEntryId(canceledVisitId),
                ]),
            CancellationToken.None);

        var firstPage = AssertSuccess(firstResult, fixture.ClientId);
        Assert.Equal(
            [nonWorkingDayId, visitId],
            firstPage.Items.Select(item => item.AuditEntryId.Value).ToArray());
        Assert.True(firstPage.HasMore);
        Assert.Equal(2, firstPage.NextOffset);
        Assert.Equal(rangeStart, firstPage.OccurredFromInclusive);
        Assert.Equal(rangeEnd, firstPage.OccurredBeforeExclusive);

        var secondPage = AssertSuccess(secondResult, fixture.ClientId);
        Assert.Equal(
            [canceledVisitId, directId],
            secondPage.Items.Select(item => item.AuditEntryId.Value).ToArray());
        Assert.False(secondPage.HasMore);
        Assert.Null(secondPage.NextOffset);

        var visitPage = AssertSuccess(visitResult, fixture.ClientId);
        Assert.Equal([ClientAuditEntityFilter.Visit], visitPage.EntityFilters);
        Assert.Equal(
            [visitId, canceledVisitId],
            visitPage.Items.Select(item => item.AuditEntryId.Value).ToArray());
        var markedVisitPage = AssertSuccess(markedVisitResult, fixture.ClientId);
        Assert.Equal(["visit.marked"], markedVisitPage.ActionTypes);
        var visit = Assert.Single(markedVisitPage.Items);
        Assert.Equal(visitId, visit.AuditEntryId.Value);
        Assert.Equal("visit.marked", visit.ActionType);
        Assert.Equal(ClientAuditEntityFilter.Visit, visit.EntityType);
        Assert.Equal(fixture.Actor.AccountId, visit.ActorAccountId);
        Assert.Equal(AccountKind.Owner, visit.ActorAccountKind);
        Assert.Equal(ActorRole.Owner, visit.ActorRole);
        Assert.Equal(fixture.Actor.SessionId, visit.SessionId);
        Assert.Equal("Reception tablet", visit.DeviceLabel);
        Assert.Equal(EntryOrigin.PaperFallback, visit.EntryOrigin);
        Assert.Equal("Recovered reception sheet", visit.Reason);
        Assert.True(visit.ChangedAfterClose);
        Assert.Contains(fixture.ClientId.ToString(), visit.RelatedEntityRefsJson);
        Assert.Equal($"audit-{visitId:N}", visit.RequestCorrelationId.Value);

        var exactVisitPage = AssertSuccess(exactVisitResult, fixture.ClientId);
        Assert.Equal(
            [visitId, canceledVisitId],
            exactVisitPage.Items
                .Select(item => item.AuditEntryId.Value)
                .ToArray());
        Assert.False(exactVisitPage.HasMore);
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
            new GetClientAuditEntriesQuery(fixture.Actor, Guid.Empty),
            CancellationToken.None);
        var reversedRange = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                TestNow,
                TestNow),
            CancellationToken.None);
        var invalidFilter = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                EntityFilters: [(ClientAuditEntityFilter)999]),
            CancellationToken.None);
        var invalidLimit = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: GetClientAuditEntriesQuery.MaxLimit + 1),
            CancellationToken.None);
        var invalidActionType = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                ActionTypes: [" "]),
            CancellationToken.None);
        var invalidAuditEntryId = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                AuditEntryIds: [new AuditEntryId(Guid.Empty)]),
            CancellationToken.None);
        var invalidExactOffset = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                Offset: 1,
                AuditEntryIds: [AuditEntryId.New()]),
            CancellationToken.None);
        var unmatchedExactId = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                AuditEntryIds: [AuditEntryId.New()]),
            CancellationToken.None);
        var missingClient = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(fixture.Actor, Guid.NewGuid()),
            CancellationToken.None);
        await DeactivateActorAsync(database, fixture.Actor.AccountId.Value);
        var denied = await handler.ExecuteAsync(
            new GetClientAuditEntriesQuery(fixture.Actor, fixture.ClientId),
            CancellationToken.None);

        AssertFailure(missingId, GetClientAuditEntriesStatus.ValidationFailed, "clientId");
        AssertFailure(
            reversedRange,
            GetClientAuditEntriesStatus.ValidationFailed,
            "occurredBeforeExclusive");
        AssertFailure(
            invalidFilter,
            GetClientAuditEntriesStatus.ValidationFailed,
            "entityFilters");
        AssertFailure(invalidLimit, GetClientAuditEntriesStatus.ValidationFailed, "limit");
        AssertFailure(
            invalidActionType,
            GetClientAuditEntriesStatus.ValidationFailed,
            "actionTypes");
        AssertFailure(
            invalidAuditEntryId,
            GetClientAuditEntriesStatus.ValidationFailed,
            "auditEntryIds");
        AssertFailure(
            invalidExactOffset,
            GetClientAuditEntriesStatus.ValidationFailed,
            "auditEntryIds");
        AssertFailure(
            unmatchedExactId,
            GetClientAuditEntriesStatus.SourceInconsistent);
        AssertFailure(missingClient, GetClientAuditEntriesStatus.NotFound, "clientId");
        AssertFailure(denied, GetClientAuditEntriesStatus.PermissionDenied);
    }

    [PostgreSqlFact]
    public async Task ClientLookupUsesEntityAndJsonbContainmentIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var clientId = Guid.NewGuid();

        var indexDefinition = await ReadRelatedEntityIndexDefinitionAsync(database);
        var queryPlan = await ReadClientLookupPlanAsync(database, clientId);

        Assert.Contains("USING gin", indexDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("jsonb_path_ops", indexDefinition, StringComparison.Ordinal);
        Assert.Contains(
            "ix_business_audit_entries_entity_timeline",
            queryPlan,
            StringComparison.Ordinal);
        Assert.Contains(
            "ix_business_audit_entries_related_entity_refs",
            queryPlan,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PersistenceRegistrationResolvesClientAuditEntriesQuery()
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
                        GetClientAuditEntriesQuery,
                        GetClientAuditEntriesResult>)
                && descriptor.ImplementationType
                    == typeof(GetClientAuditEntriesQueryHandler)
                && descriptor.Lifetime == ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetClientAuditEntriesQuery,
                GetClientAuditEntriesResult>>());
    }

    private static GetClientAuditEntriesQueryHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        return new GetClientAuditEntriesQueryHandler(
            dbContext,
            new FixedTimeProvider(TestNow));
    }

    private static async Task<ClientAuditFixture> SeedFixtureAsync(
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
                    @created_at);
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("other_client_id", otherClientId);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-1));
        Assert.Equal(3, await command.ExecuteNonQueryAsync());

        return new ClientAuditFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Reception tablet"),
            clientId,
            otherClientId);
    }

    private static async Task InsertAuditAsync(
        PostgreSqlTestDatabase database,
        ClientAuditFixture fixture,
        Guid id,
        string actionType,
        string entityType,
        Guid entityId,
        object relatedEntityRefs,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin = "normal",
        string? reason = null,
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
        command.Parameters.AddWithValue(
            "actor_account_id",
            fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.Add("reason", NpgsqlDbType.Varchar).Value =
            reason ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("request_correlation_id", $"audit-{id:N}");
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.AddWithValue("idempotency_key", $"key-{id:N}");
        command.Parameters.AddWithValue("changed_after_close", changedAfterClose);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<string> ReadRelatedEntityIndexDefinitionAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select indexdef
            from pg_indexes
            where schemaname = 'bodylife'
              and indexname = 'ix_business_audit_entries_related_entity_refs'
            """;
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadClientLookupPlanAsync(
        PostgreSqlTestDatabase database,
        Guid clientId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var settingsCommand = connection.CreateCommand())
        {
            settingsCommand.CommandText = "set enable_seqscan = off";
            await settingsCommand.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            explain (costs off)
            select id
            from bodylife.business_audit_entries
            where (entity_type = 'client' and entity_id = @client_id)
               or related_entity_refs @> @scalar_client_reference
               or related_entity_refs @> @affected_client_reference
            order by occurred_at desc, recorded_at desc, id desc
            limit 51
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.Add(
            "scalar_client_reference",
            NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(
                new { ClientId = clientId },
                AuditJsonOptions);
        command.Parameters.Add(
            "affected_client_reference",
            NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(
                new { AffectedClientIds = new[] { clientId } },
                AuditJsonOptions);
        await using var reader = await command.ExecuteReaderAsync();
        var plan = new List<string>();
        while (await reader.ReadAsync())
        {
            plan.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, plan);
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

    private static ClientAuditEntriesPage AssertSuccess(
        GetClientAuditEntriesResult result,
        Guid clientId)
    {
        Assert.Equal(GetClientAuditEntriesStatus.Success, result.Status);
        var page = Assert.IsType<ClientAuditEntriesPage>(result.Page);
        Assert.Equal(clientId, page.ClientId);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
        return page;
    }

    private static void AssertFailure(
        GetClientAuditEntriesResult result,
        GetClientAuditEntriesStatus status,
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

    private sealed record ClientAuditFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
