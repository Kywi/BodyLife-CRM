using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.Reports;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlReceptionActivityQueryTests
{
    private static readonly DateOnly BusinessDate = new(2026, 7, 20);
    private static readonly DateTimeOffset TestNow = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task ActivityUsesPostgreSqlKeysetOrderAndDoesNotAppendAuditRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedAsync(database);
        var oldest = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var tiedLower = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var tiedHigher = Guid.Parse("00000000-0000-0000-0000-000000000003");
        await InsertAuditAsync(database, fixture, oldest, "client.created", "client", fixture.ClientId, new { }, TestNow.AddHours(-2));
        await InsertAuditAsync(database, fixture, tiedLower, "client.updated", "client", fixture.ClientId, new { }, TestNow.AddHours(-1));
        await InsertAuditAsync(database, fixture, tiedHigher, "client.created", "client", fixture.ClientId, new { }, TestNow.AddHours(-1));

        var handler = CreateHandler(dbContext, new StatesStub(EmptyStates(fixture.ClientId)));
        var first = await handler.ExecuteAsync(new GetReceptionActivityQuery(fixture.Actor, BusinessDate, Limit: 2), CancellationToken.None);
        var firstPage = AssertSuccess(first);
        Assert.Equal([tiedHigher, tiedLower], firstPage.Items.Select(item => item.AuditEntryId));
        Assert.True(firstPage.HasMore);
        var second = await handler.ExecuteAsync(new GetReceptionActivityQuery(fixture.Actor, BusinessDate, Limit: 2, firstPage.NextCursor), CancellationToken.None);
        Assert.Equal([oldest], AssertSuccess(second).Items.Select(item => item.AuditEntryId));
        Assert.Equal(3L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.business_audit_entries"));
    }

    [PostgreSqlFact]
    public async Task ActivityFailsClosedForMalformedSourceAndRedactsUnallowlistedReferences()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedAsync(database);
        var paymentId = Guid.NewGuid();
        var hiddenId = Guid.NewGuid();
        await InsertAuditAsync(database, fixture, Guid.NewGuid(), "membership.issued", "membership", Guid.NewGuid(), new { ClientId = fixture.ClientId, PaymentId = paymentId, HiddenId = hiddenId }, TestNow);
        var handler = CreateHandler(dbContext, new StatesStub(EmptyStates(fixture.ClientId)));
        var redacted = AssertSuccess(await handler.ExecuteAsync(new GetReceptionActivityQuery(fixture.Actor, BusinessDate), CancellationToken.None));
        Assert.Equal([new ReceptionActivityRelatedEntity(ReceptionActivityRelatedEntityType.Payment, paymentId)], Assert.Single(redacted.Items).RelatedEntities);

        await InsertAuditAsync(database, fixture, Guid.NewGuid(), "visit.marked", "visit", Guid.NewGuid(), new { MembershipId = Guid.NewGuid() }, TestNow.AddMinutes(-1));
        var malformed = await handler.ExecuteAsync(new GetReceptionActivityQuery(fixture.Actor, BusinessDate), CancellationToken.None);
        Assert.Equal(GetReceptionActivityStatus.SourceInconsistent, malformed.Status);
        Assert.Null(malformed.Page);
        Assert.Equal(2L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.business_audit_entries"));
    }

    [Fact]
    public void HmacCursorRejectsTamperingAndBusinessDateMismatch()
    {
        var codec = CreateCursorProtector();
        var token = codec.Encode(BusinessDate, TestNow, Guid.NewGuid());
        Assert.True(codec.TryDecode(token, BusinessDate, out var cursor));
        Assert.NotNull(cursor);
        Assert.False(codec.TryDecode(token[..^1] + (token[^1] == 'A' ? "B" : "A"), BusinessDate, out _));
        Assert.False(codec.TryDecode(token, BusinessDate.AddDays(1), out _));
    }

    [PostgreSqlFact]
    public async Task ActivityPropagatesNoneSingleAndAmbiguousMembershipSelections()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedAsync(database);
        await InsertAuditAsync(database, fixture, Guid.NewGuid(), "client.created", "client", fixture.ClientId, new { }, TestNow);

        var none = AssertSuccess(await CreateHandler(dbContext, new StatesStub(EmptyStates(fixture.ClientId)))
            .ExecuteAsync(new GetReceptionActivityQuery(fixture.Actor, BusinessDate), CancellationToken.None));
        Assert.Equal(ReceptionActivityMembershipSelectionStatus.None, Assert.Single(none.Items).MembershipState.SelectionStatus);

        var singleStates = ClientMembershipStatesPolicy.Create(fixture.ClientId, BusinessDate, [CreateTimelineItem(fixture.ClientId, Guid.NewGuid())]);
        var single = AssertSuccess(await CreateHandler(dbContext, new StatesStub(singleStates))
            .ExecuteAsync(new GetReceptionActivityQuery(fixture.Actor, BusinessDate), CancellationToken.None));
        Assert.Equal(ReceptionActivityMembershipSelectionStatus.Single, Assert.Single(single.Items).MembershipState.SelectionStatus);
        Assert.NotEmpty(single.Items[0].MembershipState.TimelineState!.Warnings);

        var ambiguousStates = ClientMembershipStatesPolicy.Create(fixture.ClientId, BusinessDate, [CreateTimelineItem(fixture.ClientId, Guid.NewGuid()), CreateTimelineItem(fixture.ClientId, Guid.NewGuid())]);
        var ambiguous = AssertSuccess(await CreateHandler(dbContext, new StatesStub(ambiguousStates))
            .ExecuteAsync(new GetReceptionActivityQuery(fixture.Actor, BusinessDate), CancellationToken.None));
        Assert.Equal(ReceptionActivityMembershipSelectionStatus.Ambiguous, Assert.Single(ambiguous.Items).MembershipState.SelectionStatus);
        Assert.Equal(2, ambiguous.Items[0].MembershipState.Candidates.Count);
    }

    [PostgreSqlFact]
    public async Task AttentionUsesInclusiveEndBoundsDistinctNegativeClientsAndFailsClosedForStaleCache()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedAsync(database);
        var typeId = await InsertMembershipTypeAsync(database, fixture);
        var secondClient = await InsertClientAsync(database, fixture, "Second");
        await InsertMembershipAsync(database, fixture, typeId, fixture.ClientId, BusinessDate, -1);
        await InsertMembershipAsync(database, fixture, typeId, fixture.ClientId, BusinessDate.AddDays(7), -2);
        await InsertMembershipAsync(database, fixture, typeId, secondClient, BusinessDate.AddDays(8), -3);
        await InsertMembershipAsync(database, fixture, typeId, secondClient, BusinessDate.AddDays(-1), 1);
        var handler = new GetReceptionAttentionCountsQueryHandler(dbContext, new FixedTimeProvider(TestNow));

        var denied = await handler.ExecuteAsync(new GetReceptionAttentionCountsQuery(
            new ActorContext(AccountId.New(), ActorRole.Admin, AccountKind.NamedAdmin, SessionId.New(), "Unknown"), BusinessDate, 7), CancellationToken.None);
        Assert.Equal(GetReceptionAttentionCountsStatus.PermissionDenied, denied.Status);
        var result = await handler.ExecuteAsync(new GetReceptionAttentionCountsQuery(fixture.Actor, BusinessDate, 7), CancellationToken.None);
        Assert.Equal(GetReceptionAttentionCountsStatus.Success, result.Status);
        Assert.Equal(2, result.EndingSoonMembershipCount);
        Assert.Equal(2, result.NegativeClientCount);

        await MarkCacheStaleAsync(database);
        var stale = await handler.ExecuteAsync(new GetReceptionAttentionCountsQuery(fixture.Actor, BusinessDate, 7), CancellationToken.None);
        Assert.Equal(GetReceptionAttentionCountsStatus.RecalculationFailed, stale.Status);
    }

    private static GetReceptionActivityQueryHandler CreateHandler(BodyLifeDbContext dbContext, StatesStub states) => new(
        dbContext,
        new FixedTimeProvider(TestNow),
        CreateCursorProtector(),
        states);

    private static IReceptionActivityCursorProtector CreateCursorProtector()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BodyLife"] = "Host=localhost;Database=bodylife;Username=bodylife;Password=not-used",
            ["BodyLife:NonWorkingDayPreviewToken:SigningKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()),
        }).Build();
        var services = new ServiceCollection();
        services.AddBodyLifePersistence(configuration);
        var provider = services.BuildServiceProvider();
        return provider.CreateScope().ServiceProvider.GetRequiredService<IReceptionActivityCursorProtector>();
    }

    private static ClientMembershipStatesReadModel EmptyStates(Guid clientId) => ClientMembershipStatesPolicy.Create(clientId, BusinessDate, []);

    private static ClientMembershipStateTimelineItem CreateTimelineItem(Guid clientId, Guid membershipId)
    {
        var start = BusinessDate.AddDays(-24);
        var snapshot = new IssuedMembershipSnapshot("Eight visits", 30, 8, new Money(1000m, "UAH"));
        var terms = MembershipIssueTerms.FromIssuedSnapshot(Guid.NewGuid(), snapshot, start, MembershipDateRules.CalculateBaseEndDate(start, 30));
        return new ClientMembershipStateTimelineItem(new MembershipStateReadModel(membershipId, clientId, terms, MembershipStateCalculator.CalculateInitial(terms), BusinessDate), IssuedMembershipLifecycleStatus.Active, TestNow.AddDays(-24));
    }

    private static ReceptionActivityPage AssertSuccess(GetReceptionActivityResult result)
    {
        Assert.Equal(GetReceptionActivityStatus.Success, result.Status);
        return Assert.IsType<ReceptionActivityPage>(result.Page);
    }

    private static async Task<Fixture> SeedAsync(PostgreSqlTestDatabase database)
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into bodylife.accounts (id, display_name, account_type, role, is_active, created_at, deactivated_at)
            values (@account_id, 'Reception', 'shared_reception_admin', 'admin', true, @now, null);
            insert into bodylife.sessions (id, account_id, device_label, started_at, expires_at, ended_at, last_seen_at)
            values (@session_id, @account_id, 'Tablet', @now, @expires, null, @now);
            insert into bodylife.clients (id, surname, name, patronymic, normalized_full_name, phone_raw, phone_normalized, phone_last4, comment, operational_status, created_at, created_by_account_id, updated_at)
            values (@client_id, 'Activity', 'Client', null, 'ACTIVITY CLIENT', null, null, null, null, 'active', @now, @account_id, @now);
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("now", TestNow.AddHours(-3));
        command.Parameters.AddWithValue("expires", TestNow.AddHours(3));
        Assert.Equal(3, await command.ExecuteNonQueryAsync());
        return new Fixture(new ActorContext(new AccountId(accountId), ActorRole.Admin, AccountKind.SharedReceptionAdmin, new SessionId(sessionId), "Tablet"), clientId);
    }

    private static async Task InsertAuditAsync(PostgreSqlTestDatabase database, Fixture fixture, Guid id, string action, string entityType, Guid entityId, object related, DateTimeOffset recordedAt)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into bodylife.business_audit_entries (id, action_type, entity_type, entity_id, related_entity_refs, actor_account_id, actor_account_type, actor_role, session_id, device_label, occurred_at, recorded_at, reason, comment, before_summary, after_summary, request_correlation_id, entry_origin, idempotency_key, changed_after_close)
            values (@id, @action, @entity_type, @entity_id, @related, @account_id, 'shared_reception_admin', 'admin', @session_id, 'Tablet', @recorded_at, @recorded_at, null, null, '{}'::jsonb, '{}'::jsonb, @correlation, 'normal', @key, false)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.Add("related", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(
            related,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("correlation", $"activity-{id:N}");
        command.Parameters.AddWithValue("key", $"activity-{id:N}");
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<Guid> InsertMembershipTypeAsync(PostgreSqlTestDatabase database, Fixture fixture)
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into bodylife.membership_types (id, name, duration_days, visits_limit, price_amount, price_currency, is_active, comment, created_at, updated_at, deactivated_at)
            values (@id, 'Eight visits', 30, 8, 1000, 'UAH', true, null, @now, @now, null)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("now", TestNow);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return id;
    }

    private static async Task<Guid> InsertClientAsync(PostgreSqlTestDatabase database, Fixture fixture, string surname)
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into bodylife.clients (id, surname, name, patronymic, normalized_full_name, phone_raw, phone_normalized, phone_last4, comment, operational_status, created_at, created_by_account_id, updated_at)
            values (@id, @surname, 'Client', null, @name, null, null, null, null, 'active', @now, @account, @now)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("surname", surname);
        command.Parameters.AddWithValue("name", $"{surname.ToUpperInvariant()} CLIENT");
        command.Parameters.AddWithValue("now", TestNow);
        command.Parameters.AddWithValue("account", fixture.Actor.AccountId.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return id;
    }

    private static async Task InsertMembershipAsync(PostgreSqlTestDatabase database, Fixture fixture, Guid typeId, Guid clientId, DateOnly effectiveEndDate, int remainingVisits)
    {
        var id = Guid.NewGuid();
        var baseEnd = effectiveEndDate;
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into bodylife.issued_memberships (id, client_id, membership_type_id, type_name_snapshot, duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot, price_currency_snapshot, issuance_mode, start_date, base_end_date, issued_at, issued_by_account_id, status, entry_origin, entry_batch_id, comment)
            values (@id, @client, @type, 'Eight visits', 30, 8, 1000, 'UAH', 'opening_state', @start, @end, @now, @account, 'active', 'manual_backfill', null, null);
            insert into bodylife.membership_opening_states (id, membership_id, opening_as_of_date, declared_remaining_visits, declared_negative_balance, known_effective_end_date, known_extension_days, source_reference, reason, recorded_at, recorded_by_account_id, recorded_session_id, entry_origin, entry_batch_id, status)
            select gen_random_uuid(), id, start_date, visits_limit_snapshot, 0, base_end_date, 0, 'Reception fixture', 'Historical test state', issued_at, issued_by_account_id, (select id from bodylife.sessions where account_id = issued_by_account_id limit 1), 'manual_backfill', null, 'active'
            from bodylife.issued_memberships
            where id = @id;
            insert into bodylife.membership_state_cache (membership_id, counted_visits, remaining_visits, negative_balance, first_negative_visit_id, first_negative_visit_date, extension_days, effective_end_date, last_counted_visit_at, recalculated_at, recalculation_version)
            values (@id, @counted, @remaining, @negative, null, null, 0, @end, null, @now, @version)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("type", typeId);
        command.Parameters.AddWithValue("start", NpgsqlDbType.Date, baseEnd.AddDays(-29));
        command.Parameters.AddWithValue("end", NpgsqlDbType.Date, baseEnd);
        command.Parameters.AddWithValue("now", TestNow);
        command.Parameters.AddWithValue("account", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("counted", 8 - remainingVisits);
        command.Parameters.AddWithValue("remaining", remainingVisits);
        command.Parameters.AddWithValue("negative", Math.Max(0, -remainingVisits));
        command.Parameters.AddWithValue("version", MembershipStateCacheRebuilder.CurrentRecalculationVersion);
        Assert.Equal(3, await command.ExecuteNonQueryAsync());
    }

    private static async Task MarkCacheStaleAsync(PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "update bodylife.membership_state_cache set recalculation_version = @version where membership_id = (select membership_id from bodylife.membership_state_cache limit 1)";
        command.Parameters.AddWithValue("version", MembershipStateCacheRebuilder.CurrentRecalculationVersion - 1);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private sealed record Fixture(ActorContext Actor, Guid ClientId);

    private sealed class StatesStub(ClientMembershipStatesReadModel states) : IBodyLifeQueryHandler<GetClientMembershipStatesQuery, GetClientMembershipStatesResult>
    {
        public Task<GetClientMembershipStatesResult> ExecuteAsync(GetClientMembershipStatesQuery query, CancellationToken cancellationToken) => Task.FromResult(GetClientMembershipStatesResult.Succeeded(states, QueryPermissionSet.Empty));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
