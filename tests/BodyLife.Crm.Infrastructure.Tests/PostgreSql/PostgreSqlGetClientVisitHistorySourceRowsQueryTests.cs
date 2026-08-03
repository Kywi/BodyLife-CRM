using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetClientVisitHistorySourceRowsQueryTests
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(
        JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        21,
        12,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryKeepsMarkedAndCanceledSourcesInAuditChronology()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var source = await SeedHistoryAsync(database, fixture);
        var handler = CreateHandler(dbContext);

        var firstResult = await handler.ExecuteAsync(
            new GetClientVisitHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 2),
            CancellationToken.None);
        var secondResult = await handler.ExecuteAsync(
            new GetClientVisitHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 2,
                Offset: 2),
            CancellationToken.None);
        var rangedResult = await handler.ExecuteAsync(
            new GetClientVisitHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                OccurredFromInclusive: TestNow.AddDays(-1).AddHours(-12),
                OccurredBeforeExclusive: TestNow),
            CancellationToken.None);

        var firstPage = AssertSuccess(firstResult, fixture.ClientId);
        Assert.True(firstPage.HasMore);
        Assert.Equal(2, firstPage.NextOffset);
        Assert.Equal(
            [
                ClientVisitHistorySourceKind.CanceledVisit,
                ClientVisitHistorySourceKind.MarkedVisit,
            ],
            firstPage.Items.Select(row => row.Kind));

        var canceledRow = firstPage.Items[0];
        Assert.Equal(source.MembershipVisitId, canceledRow.VisitId);
        Assert.Equal(TestNow.AddDays(-1), canceledRow.OccurredAt);
        Assert.Equal(
            TestNow.AddDays(-1).AddMinutes(10),
            canceledRow.RecordedAt);
        Assert.Equal(EntryOrigin.PaperFallback, canceledRow.EntryOrigin);
        Assert.Null(canceledRow.MarkedVisit);
        var cancellation = Assert.IsType<VisitCancellationHistorySource>(
            canceledRow.Cancellation);
        Assert.Equal(source.CancellationId, cancellation.CancellationId);
        Assert.Equal("Duplicate check-in", cancellation.Reason);
        Assert.Equal(fixture.Actor.AccountId, cancellation.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId, cancellation.RecordedSessionId);
        Assert.Equal(source.CancellationBatchId, cancellation.EntryBatchId);
        var cancellationPaper = Assert.IsType<PaperFallbackEntryRowReference>(
            cancellation.PaperReference);
        Assert.Equal(source.CancellationBatchId, cancellationPaper.EntryBatchId);
        Assert.Equal(
            source.CancellationBatchRowId,
            cancellationPaper.EntryBatchRowId);
        Assert.Equal(
            source.CancellationPaperSheetNumber,
            cancellationPaper.PaperSheetNumber);
        Assert.Equal(source.CancellationLineNumber, cancellationPaper.LineNumber);
        Assert.Equal(
            PaperFallbackEventType.CorrectionOrCancellation,
            cancellationPaper.EventType);
        Assert.Equal(TestNow.AddDays(-1), cancellationPaper.OccurredAt);
        Assert.Equal(
            source.CancellationExplanation,
            cancellationPaper.Explanation);
        Assert.Equal(VisitAuditActions.Canceled, canceledRow.AuditEntry.ActionType);
        Assert.Equal("Duplicate check-in", canceledRow.AuditEntry.Reason);
        Assert.True(canceledRow.AuditEntry.ChangedAfterClose);

        var oneOffRow = firstPage.Items[1];
        Assert.Equal(source.OneOffVisitId, oneOffRow.VisitId);
        Assert.Equal(TestNow.AddDays(-2), oneOffRow.OccurredAt);
        Assert.Equal(
            TestNow.AddDays(-1).AddHours(-12),
            oneOffRow.RecordedAt);
        Assert.Equal(EntryOrigin.PaperFallback, oneOffRow.EntryOrigin);
        Assert.Null(oneOffRow.Cancellation);
        var oneOff = Assert.IsType<MarkedVisitHistorySource>(
            oneOffRow.MarkedVisit);
        Assert.Equal(VisitKind.OneOff, oneOff.VisitKind);
        Assert.Equal(ClientVisitRowStatus.Active, oneOff.CurrentStatus);
        Assert.Null(oneOff.CurrentConsumption);
        Assert.Null(oneOff.CurrentCancellationId);
        Assert.Equal(source.OneOffBatchId, oneOff.EntryBatchId);
        Assert.Equal("Recovered paper visit", oneOff.Comment);
        Assert.Equal(fixture.Actor.AccountId, oneOff.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId, oneOff.RecordedSessionId);
        Assert.Equal(VisitAuditActions.Marked, oneOffRow.AuditEntry.ActionType);
        Assert.Equal("Outage recovery", oneOffRow.AuditEntry.Reason);
        var paper = Assert.IsType<PaperFallbackEntryRowReference>(
            oneOff.PaperReference);
        Assert.Equal(source.OneOffBatchId, paper.EntryBatchId);
        Assert.Equal(source.OneOffBatchRowId, paper.EntryBatchRowId);
        Assert.Equal(source.OneOffPaperSheetNumber, paper.PaperSheetNumber);
        Assert.Equal(source.OneOffLineNumber, paper.LineNumber);
        Assert.Equal(PaperFallbackEventType.Visit, paper.EventType);
        Assert.Equal(TestNow.AddDays(-2), paper.OccurredAt);
        Assert.Equal(source.OneOffExplanation, paper.Explanation);

        var secondPage = AssertSuccess(secondResult, fixture.ClientId);
        Assert.False(secondPage.HasMore);
        Assert.Null(secondPage.NextOffset);
        var membershipRow = Assert.Single(secondPage.Items);
        Assert.Equal(ClientVisitHistorySourceKind.MarkedVisit, membershipRow.Kind);
        Assert.Equal(source.MembershipVisitId, membershipRow.VisitId);
        Assert.Null(membershipRow.Cancellation);
        var membershipVisit = Assert.IsType<MarkedVisitHistorySource>(
            membershipRow.MarkedVisit);
        Assert.Equal(VisitKind.Membership, membershipVisit.VisitKind);
        Assert.Equal(ClientVisitRowStatus.Canceled, membershipVisit.CurrentStatus);
        Assert.Equal(source.CancellationId, membershipVisit.CurrentCancellationId);
        var consumption = Assert.IsType<ClientVisitConsumption>(
            membershipVisit.CurrentConsumption);
        Assert.Equal(source.ConsumptionId, consumption.ConsumptionId);
        Assert.Equal(fixture.MembershipId, consumption.MembershipId);
        Assert.Equal("Visit history fixture", consumption.MembershipTypeNameSnapshot);
        Assert.Equal(ClientVisitConsumptionStatus.Canceled, consumption.Status);
        Assert.Equal("Member check-in", membershipVisit.Comment);
        Assert.Equal(VisitAuditActions.Marked, membershipRow.AuditEntry.ActionType);

        var rangedPage = AssertSuccess(rangedResult, fixture.ClientId);
        Assert.Equal(
            source.CancellationId,
            Assert.Single(rangedPage.Items).Cancellation!.CancellationId);
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenPaperLinkIsMissing()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var source = await SeedHistoryAsync(database, fixture);
        var handler = CreateHandler(dbContext);
        var query = new GetClientVisitHistorySourceRowsQuery(
            fixture.Actor,
            fixture.ClientId);

        await ExecuteNonQueryAsync(
            database,
            $"""
            delete from bodylife.entry_batch_row_entities
            where entry_batch_row_id = '{source.OneOffBatchRowId}'::uuid
            """);
        var missingLink = await handler.ExecuteAsync(query, CancellationToken.None);
        AssertFailure(
            missingLink,
            GetClientVisitHistorySourceRowsStatus.SourceInconsistent);
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenPaperCancellationLinkIsMissing()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var source = await SeedHistoryAsync(database, fixture);

        await ExecuteNonQueryAsync(
            database,
            $"""
            delete from bodylife.entry_batch_row_entities
            where entry_batch_row_id = '{source.CancellationBatchRowId}'::uuid
            """);
        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientVisitHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientVisitHistorySourceRowsStatus.SourceInconsistent);
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenPaperAuditProvenanceDoesNotMatchRow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await SeedHistoryAsync(database, fixture, paperAuditLineNumber: 999);

        var mismatchedAudit = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientVisitHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);
        AssertFailure(
            mismatchedAudit,
            GetClientVisitHistorySourceRowsStatus.SourceInconsistent);
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenPaperCancellationAuditDoesNotMatchRow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await SeedHistoryAsync(
            database,
            fixture,
            cancellationAuditLineNumber: 999);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientVisitHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientVisitHistorySourceRowsStatus.SourceInconsistent);
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenAuditHasNoCanonicalVisit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            VisitAuditActions.Marked,
            Guid.NewGuid(),
            fixture.ClientId,
            TestNow.AddDays(-1),
            TestNow.AddDays(-1),
            "normal");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientVisitHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientVisitHistorySourceRowsStatus.SourceInconsistent);
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenConsumptionEnvelopeDisagreesWithVisit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var visitId = await SeedMismatchedConsumptionAsync(database, fixture);
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            VisitAuditActions.Marked,
            visitId,
            fixture.ClientId,
            TestNow.AddDays(-1),
            TestNow.AddDays(-1).AddMinutes(5),
            "normal");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientVisitHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientVisitHistorySourceRowsStatus.SourceInconsistent);
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
            new GetClientVisitHistorySourceRowsQuery(fixture.Actor, Guid.Empty),
            CancellationToken.None);
        var reversedRange = await handler.ExecuteAsync(
            new GetClientVisitHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                TestNow,
                TestNow),
            CancellationToken.None);
        var invalidLimit = await handler.ExecuteAsync(
            new GetClientVisitHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: GetClientVisitHistorySourceRowsQuery.MaxLimit + 1),
            CancellationToken.None);
        var missingClient = await handler.ExecuteAsync(
            new GetClientVisitHistorySourceRowsQuery(
                fixture.Actor,
                Guid.NewGuid()),
            CancellationToken.None);

        await DeactivateActorAsync(database, fixture.Actor.AccountId.Value);
        var denied = await handler.ExecuteAsync(
            new GetClientVisitHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            missingId,
            GetClientVisitHistorySourceRowsStatus.ValidationFailed,
            "clientId");
        AssertFailure(
            reversedRange,
            GetClientVisitHistorySourceRowsStatus.ValidationFailed,
            "occurredBeforeExclusive");
        AssertFailure(
            invalidLimit,
            GetClientVisitHistorySourceRowsStatus.ValidationFailed,
            "limit");
        AssertFailure(
            missingClient,
            GetClientVisitHistorySourceRowsStatus.NotFound,
            "clientId");
        AssertFailure(denied, GetClientVisitHistorySourceRowsStatus.PermissionDenied);
    }

    [Fact]
    public void PersistenceRegistrationResolvesVisitHistorySourceQuery()
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
                        GetClientVisitHistorySourceRowsQuery,
                        GetClientVisitHistorySourceRowsResult>)
                && descriptor.ImplementationType
                    == typeof(GetClientVisitHistorySourceRowsQueryHandler)
                && descriptor.Lifetime == ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetClientVisitHistorySourceRowsQuery,
                GetClientVisitHistorySourceRowsResult>>());
    }

    private static GetClientVisitHistorySourceRowsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        return new GetClientVisitHistorySourceRowsQueryHandler(
            dbContext,
            new GetClientAuditEntriesQueryHandler(dbContext, timeProvider));
    }

    private static async Task<VisitHistoryFixture> SeedFixtureAsync(
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
                    'History',
                    null,
                    'VISIT HISTORY',
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
                'Visit history fixture',
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
                issuance_mode,
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
                'Visit history fixture',
                30,
                8,
                1000,
                'UAH',
                'opening_state',
                @start_date,
                @base_end_date,
                @created_at,
                @account_id,
                'active',
                'manual_backfill',
                null,
                null);

            insert into bodylife.membership_opening_states (
                id, membership_id, opening_as_of_date, declared_remaining_visits,
                declared_negative_balance, known_effective_end_date,
                known_extension_days, source_reference, reason, recorded_at,
                recorded_by_account_id, recorded_session_id, entry_origin,
                entry_batch_id, status)
            values (
                gen_random_uuid(), @membership_id, @start_date, 8, 0,
                @base_end_date, 0, 'Visit history fixture',
                'Historical state required by the query scenario', @created_at,
                @account_id, @session_id, 'manual_backfill', null, 'active')
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-2));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow);
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
        Assert.Equal(6, await command.ExecuteNonQueryAsync());

        return new VisitHistoryFixture(
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

    private static async Task<VisitHistorySourceIds> SeedHistoryAsync(
        PostgreSqlTestDatabase database,
        VisitHistoryFixture fixture,
        int? paperAuditLineNumber = null,
        int? cancellationAuditLineNumber = null)
    {
        var membershipVisitId = Guid.NewGuid();
        var consumptionId = Guid.NewGuid();
        var coverageConsumptionId = Guid.NewGuid();
        var coverageSourceFactId = Guid.NewGuid();
        var cancellationId = Guid.NewGuid();
        var cancellationBatchId = Guid.NewGuid();
        var cancellationBatchRowId = Guid.NewGuid();
        var oneOffVisitId = Guid.NewGuid();
        var oneOffBatchId = Guid.NewGuid();
        var oneOffBatchRowId = Guid.NewGuid();
        var oneOffAuditId = Guid.NewGuid();
        const string oneOffPaperSheetNumber = "VISIT-HISTORY-001";
        const int oneOffLineNumber = 7;
        const string oneOffExplanation = "Recovered one-off Visit from paper";
        const string cancellationPaperSheetNumber = "VISIT-CANCEL-001";
        const int cancellationLineNumber = 9;
        const string cancellationExplanation =
            "Recovered Visit cancellation from paper";
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                insert into bodylife.entry_batches (
                    id,
                    batch_type,
                    paper_sheet_number,
                    business_date_start,
                    business_date_end,
                    recorded_at,
                    recorded_by_account_id,
                    reconciled_at,
                    reconciled_by_account_id,
                    note)
                values
                    (
                        @one_off_batch_id,
                        'paper_fallback',
                        @one_off_paper_sheet_number,
                        @one_off_business_date,
                        @one_off_business_date,
                        @one_off_recorded_at,
                        @account_id,
                        null,
                        null,
                        'Visit history fixture'),
                    (
                        @cancellation_batch_id,
                        'paper_fallback',
                        @cancellation_paper_sheet_number,
                        @cancellation_business_date,
                        @cancellation_business_date,
                        @cancellation_recorded_at,
                        @account_id,
                        null,
                        null,
                        'Visit cancellation history fixture');

                insert into bodylife.entry_batch_rows (
                    id,
                    entry_batch_id,
                    line_number,
                    event_type,
                    occurred_at,
                    explanation,
                    recorded_at,
                    recorded_by_account_id,
                    session_id)
                values
                    (
                        @one_off_batch_row_id,
                        @one_off_batch_id,
                        @one_off_line_number,
                        'visit',
                        @one_off_occurred_at,
                        @one_off_explanation,
                        @one_off_recorded_at,
                        @account_id,
                        @session_id),
                    (
                        @cancellation_batch_row_id,
                        @cancellation_batch_id,
                        @cancellation_line_number,
                        'correction_or_cancellation',
                        @cancellation_occurred_at,
                        @cancellation_explanation,
                        @cancellation_recorded_at,
                        @account_id,
                        @session_id);

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
                    @membership_visit_id,
                    @client_id,
                    @membership_occurred_at,
                    @membership_recorded_at,
                    @account_id,
                    @session_id,
                    'membership',
                    'normal',
                    null,
                    'Member check-in',
                    'canceled');

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
                    @consumption_id,
                    @membership_visit_id,
                    @client_id,
                    'membership',
                    @membership_id,
                    'counted',
                    'visit',
                    @membership_visit_id,
                    @membership_recorded_at,
                    @account_id,
                    @session_id,
                    'canceled');

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
                    @coverage_consumption_id,
                    @membership_visit_id,
                    @client_id,
                    'membership',
                    @membership_id,
                    'negative_coverage',
                    'negative_closure_item',
                    @coverage_source_fact_id,
                    @coverage_recorded_at,
                    @account_id,
                    @session_id,
                    'canceled');

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
                    @cancellation_id,
                    @membership_visit_id,
                    'Duplicate check-in',
                    @cancellation_occurred_at,
                    @cancellation_recorded_at,
                    @account_id,
                    @session_id,
                    'paper_fallback',
                    @cancellation_batch_id);

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
                    @one_off_visit_id,
                    @client_id,
                    @one_off_occurred_at,
                    @one_off_recorded_at,
                    @account_id,
                    @session_id,
                    'one_off',
                    'paper_fallback',
                    @one_off_batch_id,
                    'Recovered paper visit',
                    'active');

                insert into bodylife.entry_batch_row_entities (
                    entry_batch_row_id,
                    entity_type,
                    entity_id)
                values
                    (
                        @one_off_batch_row_id,
                        'visit',
                        @one_off_visit_id),
                    (
                        @cancellation_batch_row_id,
                        'visit_cancellation',
                        @cancellation_id)
                """;
            command.Parameters.AddWithValue(
                "membership_visit_id",
                membershipVisitId);
            command.Parameters.AddWithValue("client_id", fixture.ClientId);
            command.Parameters.AddWithValue(
                "membership_occurred_at",
                TestNow.AddDays(-3));
            command.Parameters.AddWithValue(
                "membership_recorded_at",
                TestNow.AddDays(-3).AddMinutes(5));
            command.Parameters.AddWithValue(
                "account_id",
                fixture.Actor.AccountId.Value);
            command.Parameters.AddWithValue(
                "session_id",
                fixture.Actor.SessionId.Value);
            command.Parameters.AddWithValue("consumption_id", consumptionId);
            command.Parameters.AddWithValue(
                "coverage_consumption_id",
                coverageConsumptionId);
            command.Parameters.AddWithValue(
                "coverage_source_fact_id",
                coverageSourceFactId);
            command.Parameters.AddWithValue(
                "coverage_recorded_at",
                TestNow.AddDays(-2));
            command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
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
            command.Parameters.AddWithValue(
                "cancellation_batch_row_id",
                cancellationBatchRowId);
            command.Parameters.AddWithValue(
                "cancellation_paper_sheet_number",
                cancellationPaperSheetNumber);
            command.Parameters.AddWithValue(
                "cancellation_business_date",
                NpgsqlDbType.Date,
                BusinessTimeZone.GetBusinessDate(TestNow.AddDays(-1)));
            command.Parameters.AddWithValue(
                "cancellation_line_number",
                cancellationLineNumber);
            command.Parameters.AddWithValue(
                "cancellation_explanation",
                cancellationExplanation);
            command.Parameters.AddWithValue("one_off_visit_id", oneOffVisitId);
            command.Parameters.AddWithValue(
                "one_off_occurred_at",
                TestNow.AddDays(-2));
            command.Parameters.AddWithValue(
                "one_off_recorded_at",
                TestNow.AddDays(-1).AddHours(-12));
            command.Parameters.AddWithValue("one_off_batch_id", oneOffBatchId);
            command.Parameters.AddWithValue(
                "one_off_batch_row_id",
                oneOffBatchRowId);
            command.Parameters.AddWithValue(
                "one_off_paper_sheet_number",
                oneOffPaperSheetNumber);
            command.Parameters.AddWithValue(
                "one_off_business_date",
                NpgsqlDbType.Date,
                BusinessTimeZone.GetBusinessDate(TestNow.AddDays(-2)));
            command.Parameters.AddWithValue(
                "one_off_line_number",
                oneOffLineNumber);
            command.Parameters.AddWithValue(
                "one_off_explanation",
                oneOffExplanation);
            Assert.Equal(11, await command.ExecuteNonQueryAsync());
        }

        await InsertAuditAsync(
            database,
            fixture,
            oneOffAuditId,
            VisitAuditActions.Marked,
            membershipVisitId,
            fixture.ClientId,
            TestNow.AddDays(-3),
            TestNow.AddDays(-3).AddMinutes(5),
            "normal",
            comment: "Member check-in");
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            VisitAuditActions.Canceled,
            membershipVisitId,
            fixture.ClientId,
            TestNow.AddDays(-1),
            TestNow.AddDays(-1).AddMinutes(10),
            "paper_fallback",
            reason: "Duplicate check-in",
            changedAfterClose: true,
            entryBatchId: cancellationBatchId,
            entryBatchRowId: cancellationBatchRowId,
            paperSheetNumber: cancellationPaperSheetNumber,
            lineNumber: cancellationAuditLineNumber ?? cancellationLineNumber,
            paperExplanation: cancellationExplanation,
            cancellationId: cancellationId,
            membershipId: fixture.MembershipId,
            activeConsumptionId: consumptionId);
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            VisitAuditActions.Marked,
            oneOffVisitId,
            fixture.ClientId,
            TestNow.AddDays(-2),
            TestNow.AddDays(-1).AddHours(-12),
            "paper_fallback",
            reason: "Outage recovery",
            comment: "Recovered paper visit",
            entryBatchId: oneOffBatchId,
            entryBatchRowId: oneOffBatchRowId,
            paperSheetNumber: oneOffPaperSheetNumber,
            lineNumber: paperAuditLineNumber ?? oneOffLineNumber,
            paperExplanation: oneOffExplanation);
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            "visit.corrected",
            oneOffVisitId,
            fixture.ClientId,
            TestNow.AddHours(-1),
            TestNow.AddMinutes(-59),
            "normal");
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            VisitAuditActions.Marked,
            Guid.NewGuid(),
            fixture.OtherClientId,
            TestNow.AddMinutes(-30),
            TestNow.AddMinutes(-29),
            "normal");

        return new VisitHistorySourceIds(
            membershipVisitId,
            consumptionId,
            cancellationId,
            cancellationBatchId,
            cancellationBatchRowId,
            cancellationPaperSheetNumber,
            cancellationLineNumber,
            cancellationExplanation,
            oneOffVisitId,
            oneOffBatchId,
            oneOffBatchRowId,
            oneOffAuditId,
            oneOffPaperSheetNumber,
            oneOffLineNumber,
            oneOffExplanation);
    }

    private static async Task<Guid> SeedMismatchedConsumptionAsync(
        PostgreSqlTestDatabase database,
        VisitHistoryFixture fixture)
    {
        var visitId = Guid.NewGuid();
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
                @visit_id,
                @client_id,
                @occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                'membership',
                'normal',
                null,
                null,
                'active');

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
                @consumption_id,
                @visit_id,
                @client_id,
                'membership',
                @membership_id,
                'counted',
                'visit',
                @visit_id,
                @mismatched_recorded_at,
                @account_id,
                @session_id,
                'active')
            """;
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("occurred_at", TestNow.AddDays(-1));
        command.Parameters.AddWithValue(
            "recorded_at",
            TestNow.AddDays(-1).AddMinutes(5));
        command.Parameters.AddWithValue(
            "account_id",
            fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("consumption_id", Guid.NewGuid());
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue(
            "mismatched_recorded_at",
            TestNow.AddDays(-1).AddMinutes(6));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
        return visitId;
    }

    private static async Task InsertAuditAsync(
        PostgreSqlTestDatabase database,
        VisitHistoryFixture fixture,
        Guid auditId,
        string actionType,
        Guid visitId,
        Guid clientId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin,
        string? reason = null,
        string? comment = null,
        bool changedAfterClose = false,
        Guid? entryBatchId = null,
        Guid? entryBatchRowId = null,
        string? paperSheetNumber = null,
        int? lineNumber = null,
        string? paperExplanation = null,
        Guid? cancellationId = null,
        Guid? membershipId = null,
        Guid? activeConsumptionId = null)
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
                'visit',
                @visit_id,
                @related_entity_refs,
                @actor_account_id,
                'owner',
                'owner',
                @session_id,
                'Reception tablet',
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
        command.Parameters.AddWithValue("id", auditId);
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.AddWithValue("visit_id", visitId);
        object relatedEntityRefs;
        if (entryBatchRowId is null)
        {
            relatedEntityRefs = new { ClientId = clientId };
        }
        else if (actionType == VisitAuditActions.Canceled)
        {
            relatedEntityRefs = new
            {
                ClientId = clientId,
                MembershipId = membershipId,
                ActiveConsumptionId = activeConsumptionId,
                CancellationId = cancellationId,
                EntryBatchId = entryBatchId,
                EntryBatchRowId = entryBatchRowId,
                PaperSheetNumber = paperSheetNumber,
                LineNumber = lineNumber,
                PaperExplanation = paperExplanation,
            };
        }
        else
        {
            relatedEntityRefs = new
            {
                ClientId = clientId,
                MembershipId = (Guid?)null,
                ConsumptionId = (Guid?)null,
                EntryBatchId = entryBatchId,
                EntryBatchRowId = entryBatchRowId,
                PaperSheetNumber = paperSheetNumber,
                LineNumber = lineNumber,
                PaperExplanation = paperExplanation,
            };
        }
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
        command.Parameters.Add("comment", NpgsqlDbType.Varchar).Value =
            comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue(
            "request_correlation_id",
            $"visit-history-{auditId:N}");
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.AddWithValue(
            "idempotency_key",
            $"visit-history-idempotency-{auditId:N}");
        command.Parameters.AddWithValue("changed_after_close", changedAfterClose);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task ExecuteNonQueryAsync(
        PostgreSqlTestDatabase database,
        string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
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

    private static ClientVisitHistorySourceRowsPage AssertSuccess(
        GetClientVisitHistorySourceRowsResult result,
        Guid clientId)
    {
        Assert.Equal(GetClientVisitHistorySourceRowsStatus.Success, result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        var page = Assert.IsType<ClientVisitHistorySourceRowsPage>(result.Page);
        Assert.Equal(clientId, page.ClientId);
        return page;
    }

    private static void AssertFailure(
        GetClientVisitHistorySourceRowsResult result,
        GetClientVisitHistorySourceRowsStatus status,
        string? field = null)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Page);
        Assert.NotNull(result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(field, result.ErrorField);
    }

    private sealed record VisitHistoryFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId);

    private sealed record VisitHistorySourceIds(
        Guid MembershipVisitId,
        Guid ConsumptionId,
        Guid CancellationId,
        Guid CancellationBatchId,
        Guid CancellationBatchRowId,
        string CancellationPaperSheetNumber,
        int CancellationLineNumber,
        string CancellationExplanation,
        Guid OneOffVisitId,
        Guid OneOffBatchId,
        Guid OneOffBatchRowId,
        Guid OneOffAuditId,
        string OneOffPaperSheetNumber,
        int OneOffLineNumber,
        string OneOffExplanation);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
