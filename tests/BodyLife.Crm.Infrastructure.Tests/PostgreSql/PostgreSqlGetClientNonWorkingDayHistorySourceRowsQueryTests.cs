using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetClientNonWorkingDayHistorySourceRowsQueryTests
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(
        JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        24,
        12,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryKeepsAddedCorrectedAndCanceledSourcesInAuditChronology()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var source = await SeedHistoryAsync(database, fixture);
        var handler = CreateHandler(dbContext);

        var firstResult = await handler.ExecuteAsync(
            new GetClientNonWorkingDayHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 2),
            CancellationToken.None);
        var secondResult = await handler.ExecuteAsync(
            new GetClientNonWorkingDayHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 2,
                Offset: 2),
            CancellationToken.None);
        var rangedResult = await handler.ExecuteAsync(
            new GetClientNonWorkingDayHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                OccurredFromInclusive: TestNow.AddDays(-3).AddHours(-12),
                OccurredBeforeExclusive: TestNow.AddHours(-12)),
            CancellationToken.None);

        var firstPage = AssertSuccess(firstResult, fixture.ClientId);
        Assert.True(firstPage.HasMore);
        Assert.Equal(2, firstPage.NextOffset);
        Assert.Equal(
            [
                ClientNonWorkingDayHistorySourceKind.Canceled,
                ClientNonWorkingDayHistorySourceKind.Corrected,
            ],
            firstPage.Items.Select(row => row.Kind));

        var canceledRow = firstPage.Items[0];
        Assert.Equal(source.CanceledPeriodId, canceledRow.PeriodId);
        Assert.Equal(TestNow.AddDays(-1), canceledRow.OccurredAt);
        Assert.Equal(
            TestNow.AddDays(-1).AddMinutes(15),
            canceledRow.RecordedAt);
        Assert.Equal(EntryOrigin.ManualBackfill, canceledRow.EntryOrigin);
        Assert.Null(canceledRow.AddedPeriod);
        var canceled = Assert.IsType<NonWorkingDayCorrectionHistorySource>(
            canceledRow.Correction);
        Assert.Equal(NonWorkingDayCorrectionMode.Cancel, canceled.Mode);
        Assert.Null(canceled.ReplacementPeriod);
        Assert.Equal(
            NonWorkingDayCorrectionSourceStatus.Canceled,
            canceled.OriginalPeriod.CurrentStatus);
        Assert.Equal(source.CancellationId, canceled.OriginalPeriod.CurrentCancellationId);
        Assert.Equal(2, canceled.OriginalPeriod.Period.InclusiveDays);
        var canceledApplication = Assert.Single(
            canceled.OriginalPeriod.ClientApplications);
        Assert.Equal(fixture.MembershipId, canceledApplication.MembershipId);
        Assert.Equal(
            "Eight visits / 30 days",
            canceledApplication.MembershipTypeNameSnapshot);
        Assert.Equal(
            canceled.OriginalPeriod.Period,
            canceledApplication.AppliedRange);
        Assert.Equal(
            source.CancellationId,
            Assert.IsType<NonWorkingDayCancellationHistorySource>(
                canceled.Cancellation).CancellationId);
        Assert.Equal(
            "Closure entered by mistake",
            canceled.CorrectionReason);
        Assert.True(canceledRow.AuditEntry.ChangedAfterClose);

        var correctedRow = firstPage.Items[1];
        Assert.Equal(source.OriginalPeriodId, correctedRow.PeriodId);
        Assert.Equal(TestNow.AddDays(-3), correctedRow.OccurredAt);
        Assert.Equal(
            TestNow.AddDays(-3).AddMinutes(20),
            correctedRow.RecordedAt);
        Assert.Equal(EntryOrigin.PaperFallback, correctedRow.EntryOrigin);
        Assert.Null(correctedRow.AddedPeriod);
        var corrected = Assert.IsType<NonWorkingDayCorrectionHistorySource>(
            correctedRow.Correction);
        Assert.Equal(NonWorkingDayCorrectionMode.ReplaceRange, corrected.Mode);
        Assert.Equal(
            NonWorkingDayCorrectionSourceStatus.Corrected,
            corrected.OriginalPeriod.CurrentStatus);
        Assert.Equal(2, corrected.OriginalPeriod.ConfirmedAffectedMembershipCount);
        Assert.Equal(2, corrected.OriginalPeriod.ConfirmedAffectedClientCount);
        Assert.Single(corrected.OriginalPeriod.ClientApplications);
        var replacement = Assert.IsType<NonWorkingDayHistoryPeriodSource>(
            corrected.ReplacementPeriod);
        Assert.Equal(source.ReplacementPeriodId, replacement.PeriodId);
        Assert.Equal(
            NonWorkingDayCorrectionSourceStatus.Active,
            replacement.CurrentStatus);
        Assert.Equal(1, replacement.ConfirmedAffectedMembershipCount);
        Assert.Equal(1, replacement.ConfirmedAffectedClientCount);
        Assert.Equal(4, replacement.Period.InclusiveDays);
        Assert.Equal(
            new DateOnly(2026, 7, 15),
            replacement.Period.StartDate);
        Assert.Equal(
            new DateOnly(2026, 7, 18),
            replacement.Period.EndDate);
        var replacementApplication = Assert.Single(
            replacement.ClientApplications);
        Assert.Equal(fixture.MembershipId, replacementApplication.MembershipId);
        Assert.Equal(replacement.Period, replacementApplication.AppliedRange);
        Assert.Equal(
            new[] { fixture.MembershipId, fixture.OtherMembershipId }
                .Order()
                .ToArray(),
            corrected.AffectedMembershipIds);
        Assert.Equal(
            "Replace closure range",
            corrected.CorrectionReason);
        Assert.Equal(
            "Owner confirmed replacement scope",
            corrected.CorrectionComment);
        Assert.True(correctedRow.AuditEntry.ChangedAfterClose);

        var secondPage = AssertSuccess(secondResult, fixture.ClientId);
        Assert.False(secondPage.HasMore);
        Assert.Null(secondPage.NextOffset);
        Assert.Equal(
            [source.CanceledPeriodId, source.OriginalPeriodId],
            secondPage.Items.Select(row => row.PeriodId));
        Assert.All(secondPage.Items, row => Assert.Equal(
            ClientNonWorkingDayHistorySourceKind.Added,
            row.Kind));
        Assert.Equal(
            NonWorkingDayCorrectionSourceStatus.Canceled,
            secondPage.Items[0].AddedPeriod!.CurrentStatus);
        Assert.Equal(
            source.CancellationId,
            secondPage.Items[0].AddedPeriod!.CurrentCancellationId);
        Assert.Equal(
            NonWorkingDayCorrectionSourceStatus.Corrected,
            secondPage.Items[1].AddedPeriod!.CurrentStatus);
        Assert.Equal(3, secondPage.Items[1].AddedPeriod!.Period.InclusiveDays);

        var rangedPage = AssertSuccess(rangedResult, fixture.ClientId);
        Assert.Equal(
            [
                ClientNonWorkingDayHistorySourceKind.Canceled,
                ClientNonWorkingDayHistorySourceKind.Corrected,
            ],
            rangedPage.Items.Select(row => row.Kind));
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenAuditHasNoCanonicalPeriod()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var auditId = Guid.NewGuid();
        await InsertAuditAsync(
            database,
            fixture,
            auditId,
            NonWorkingDayAuditActions.Added,
            Guid.NewGuid(),
            TestNow.AddDays(-1),
            TestNow.AddDays(-1).AddMinutes(1),
            "normal",
            reason: null,
            comment: null,
            new
            {
                AffectedMembershipIds = new[] { fixture.MembershipId },
                AffectedClientIds = new[] { fixture.ClientId },
            },
            afterSummary: new { });

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientNonWorkingDayHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientNonWorkingDayHistorySourceRowsStatus.SourceInconsistent);
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenCorrectionLinksMissingReplacement()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await SeedHistoryAsync(
            database,
            fixture,
            correctionReplacementLink: Guid.NewGuid());

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientNonWorkingDayHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientNonWorkingDayHistorySourceRowsStatus.SourceInconsistent);
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
            TestNow.AddDays(-1).AddMinutes(16));

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientNonWorkingDayHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientNonWorkingDayHistorySourceRowsStatus.SourceInconsistent);
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
            new GetClientNonWorkingDayHistorySourceRowsQuery(
                fixture.Actor,
                Guid.Empty),
            CancellationToken.None);
        var reversedRange = await handler.ExecuteAsync(
            new GetClientNonWorkingDayHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                TestNow,
                TestNow),
            CancellationToken.None);
        var invalidLimit = await handler.ExecuteAsync(
            new GetClientNonWorkingDayHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit:
                    GetClientNonWorkingDayHistorySourceRowsQuery.MaxLimit + 1),
            CancellationToken.None);
        var invalidOffset = await handler.ExecuteAsync(
            new GetClientNonWorkingDayHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Offset:
                    GetClientNonWorkingDayHistorySourceRowsQuery.MaxOffset + 1),
            CancellationToken.None);
        var missingClient = await handler.ExecuteAsync(
            new GetClientNonWorkingDayHistorySourceRowsQuery(
                fixture.Actor,
                Guid.NewGuid()),
            CancellationToken.None);

        await DeactivateActorAsync(database, fixture.Actor.AccountId.Value);
        var denied = await handler.ExecuteAsync(
            new GetClientNonWorkingDayHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            missingId,
            GetClientNonWorkingDayHistorySourceRowsStatus.ValidationFailed,
            "clientId");
        AssertFailure(
            reversedRange,
            GetClientNonWorkingDayHistorySourceRowsStatus.ValidationFailed,
            "occurredBeforeExclusive");
        AssertFailure(
            invalidLimit,
            GetClientNonWorkingDayHistorySourceRowsStatus.ValidationFailed,
            "limit");
        AssertFailure(
            invalidOffset,
            GetClientNonWorkingDayHistorySourceRowsStatus.ValidationFailed,
            "offset");
        AssertFailure(
            missingClient,
            GetClientNonWorkingDayHistorySourceRowsStatus.NotFound,
            "clientId");
        AssertFailure(
            denied,
            GetClientNonWorkingDayHistorySourceRowsStatus.PermissionDenied);
    }

    [Fact]
    public void PersistenceRegistrationResolvesNonWorkingDayHistorySourceQuery()
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
                        GetClientNonWorkingDayHistorySourceRowsQuery,
                        GetClientNonWorkingDayHistorySourceRowsResult>)
                && descriptor.ImplementationType
                    == typeof(
                        GetClientNonWorkingDayHistorySourceRowsQueryHandler)
                && descriptor.Lifetime == ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetClientNonWorkingDayHistorySourceRowsQuery,
                GetClientNonWorkingDayHistorySourceRowsResult>>());
    }

    private static GetClientNonWorkingDayHistorySourceRowsQueryHandler
        CreateHandler(BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        return new GetClientNonWorkingDayHistorySourceRowsQueryHandler(
            dbContext,
            new GetClientAuditEntriesQueryHandler(dbContext, timeProvider));
    }

    private static async Task<NonWorkingDayHistoryFixture> SeedFixtureAsync(
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
        var otherMembershipId = Guid.NewGuid();
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
            values
                (
                    @client_id,
                    'NonWorking',
                    'History',
                    null,
                    'NONWORKING HISTORY',
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
            values
                (
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
                    null),
                (
                    @other_membership_id,
                    @other_client_id,
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
        command.Parameters.AddWithValue("other_membership_id", otherMembershipId);
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
        Assert.Equal(6, await command.ExecuteNonQueryAsync());

        return new NonWorkingDayHistoryFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Owner laptop"),
            clientId,
            otherClientId,
            membershipId,
            otherMembershipId);
    }

    private static async Task<NonWorkingDayHistorySourceIds> SeedHistoryAsync(
        PostgreSqlTestDatabase database,
        NonWorkingDayHistoryFixture fixture,
        Guid? correctionReplacementLink = null)
    {
        var originalPeriodId = Guid.NewGuid();
        var replacementPeriodId = Guid.NewGuid();
        var canceledPeriodId = Guid.NewGuid();
        var cancellationId = Guid.NewGuid();
        var originalApplications = new[]
        {
            new ApplicationSeed(
                Guid.NewGuid(),
                fixture.MembershipId,
                fixture.ClientId),
            new ApplicationSeed(
                Guid.NewGuid(),
                fixture.OtherMembershipId,
                fixture.OtherClientId),
        }
            .OrderBy(application => application.MembershipId)
            .ThenBy(application => application.ApplicationId)
            .ToArray();
        var replacementApplication = new ApplicationSeed(
            Guid.NewGuid(),
            fixture.MembershipId,
            fixture.ClientId);
        var canceledApplication = new ApplicationSeed(
            Guid.NewGuid(),
            fixture.MembershipId,
            fixture.ClientId);
        var originalCreatedAt = TestNow.AddDays(-6).AddMinutes(10);
        var replacementCreatedAt = TestNow.AddDays(-3).AddMinutes(20);
        var canceledCreatedAt = TestNow.AddDays(-5).AddMinutes(5);
        var cancellationRecordedAt = TestNow.AddDays(-1).AddMinutes(15);

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
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
                values
                    (
                        @original_period_id,
                        @original_start_date,
                        @original_end_date,
                        'maintenance',
                        'Original maintenance window',
                        @original_created_at,
                        @account_id,
                        @session_id,
                        'corrected'),
                    (
                        @replacement_period_id,
                        @replacement_start_date,
                        @replacement_end_date,
                        'maintenance',
                        'Replacement maintenance window',
                        @replacement_created_at,
                        @account_id,
                        @session_id,
                        'active'),
                    (
                        @canceled_period_id,
                        @canceled_start_date,
                        @canceled_end_date,
                        'emergency',
                        'Emergency closure',
                        @canceled_created_at,
                        @account_id,
                        @session_id,
                        'canceled');

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
                values
                    (
                        @original_application_1_id,
                        @original_period_id,
                        @original_membership_1_id,
                        @original_client_1_id,
                        @original_start_date,
                        @original_end_date,
                        @original_previewed_at,
                        @original_created_at,
                        'corrected'),
                    (
                        @original_application_2_id,
                        @original_period_id,
                        @original_membership_2_id,
                        @original_client_2_id,
                        @original_start_date,
                        @original_end_date,
                        @original_previewed_at,
                        @original_created_at,
                        'corrected'),
                    (
                        @replacement_application_id,
                        @replacement_period_id,
                        @membership_id,
                        @client_id,
                        @replacement_start_date,
                        @replacement_end_date,
                        @replacement_previewed_at,
                        @replacement_created_at,
                        'active'),
                    (
                        @canceled_application_id,
                        @canceled_period_id,
                        @membership_id,
                        @client_id,
                        @canceled_start_date,
                        @canceled_end_date,
                        @canceled_previewed_at,
                        @canceled_created_at,
                        'canceled');

                insert into bodylife.non_working_period_cancellations (
                    id,
                    non_working_period_id,
                    reason,
                    recorded_at,
                    recorded_by_account_id,
                    session_id)
                values (
                    @cancellation_id,
                    @canceled_period_id,
                    'Closure entered by mistake',
                    @cancellation_recorded_at,
                    @account_id,
                    @session_id)
                """;
            command.Parameters.AddWithValue("original_period_id", originalPeriodId);
            command.Parameters.AddWithValue(
                "replacement_period_id",
                replacementPeriodId);
            command.Parameters.AddWithValue("canceled_period_id", canceledPeriodId);
            command.Parameters.AddWithValue(
                "original_start_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 10));
            command.Parameters.AddWithValue(
                "original_end_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 12));
            command.Parameters.AddWithValue(
                "replacement_start_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 15));
            command.Parameters.AddWithValue(
                "replacement_end_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 18));
            command.Parameters.AddWithValue(
                "canceled_start_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 20));
            command.Parameters.AddWithValue(
                "canceled_end_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 21));
            command.Parameters.AddWithValue(
                "original_created_at",
                originalCreatedAt);
            command.Parameters.AddWithValue(
                "replacement_created_at",
                replacementCreatedAt);
            command.Parameters.AddWithValue(
                "canceled_created_at",
                canceledCreatedAt);
            command.Parameters.AddWithValue(
                "account_id",
                fixture.Actor.AccountId.Value);
            command.Parameters.AddWithValue(
                "session_id",
                fixture.Actor.SessionId.Value);
            command.Parameters.AddWithValue(
                "original_application_1_id",
                originalApplications[0].ApplicationId);
            command.Parameters.AddWithValue(
                "original_membership_1_id",
                originalApplications[0].MembershipId);
            command.Parameters.AddWithValue(
                "original_client_1_id",
                originalApplications[0].ClientId);
            command.Parameters.AddWithValue(
                "original_application_2_id",
                originalApplications[1].ApplicationId);
            command.Parameters.AddWithValue(
                "original_membership_2_id",
                originalApplications[1].MembershipId);
            command.Parameters.AddWithValue(
                "original_client_2_id",
                originalApplications[1].ClientId);
            command.Parameters.AddWithValue(
                "original_previewed_at",
                originalCreatedAt.AddMinutes(-30));
            command.Parameters.AddWithValue(
                "replacement_application_id",
                replacementApplication.ApplicationId);
            command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
            command.Parameters.AddWithValue("client_id", fixture.ClientId);
            command.Parameters.AddWithValue(
                "replacement_previewed_at",
                replacementCreatedAt.AddMinutes(-30));
            command.Parameters.AddWithValue(
                "canceled_application_id",
                canceledApplication.ApplicationId);
            command.Parameters.AddWithValue(
                "canceled_previewed_at",
                canceledCreatedAt.AddMinutes(-30));
            command.Parameters.AddWithValue("cancellation_id", cancellationId);
            command.Parameters.AddWithValue(
                "cancellation_recorded_at",
                cancellationRecordedAt);
            Assert.Equal(8, await command.ExecuteNonQueryAsync());
        }

        var originalMembershipIds = originalApplications
            .Select(application => application.MembershipId)
            .ToArray();
        var originalClientIds = originalApplications
            .Select(application => application.ClientId)
            .ToArray();
        var affectedMembershipIds = originalMembershipIds
            .Concat([fixture.MembershipId])
            .Distinct()
            .Order()
            .ToArray();
        var affectedClientIds = originalClientIds
            .Concat([fixture.ClientId])
            .Distinct()
            .Order()
            .ToArray();

        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            NonWorkingDayAuditActions.Added,
            originalPeriodId,
            TestNow.AddDays(-6),
            originalCreatedAt,
            "normal",
            reason: null,
            comment: null,
            new
            {
                AffectedMembershipIds = originalMembershipIds,
                AffectedClientIds = originalClientIds,
            },
            afterSummary: new { });
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            NonWorkingDayAuditActions.Added,
            canceledPeriodId,
            TestNow.AddDays(-5),
            canceledCreatedAt,
            "normal",
            reason: null,
            comment: null,
            new
            {
                AffectedMembershipIds = new[] { fixture.MembershipId },
                AffectedClientIds = new[] { fixture.ClientId },
            },
            afterSummary: new { });
        var correctionAuditId = Guid.NewGuid();
        await InsertAuditAsync(
            database,
            fixture,
            correctionAuditId,
            NonWorkingDayAuditActions.Corrected,
            originalPeriodId,
            TestNow.AddDays(-3),
            replacementCreatedAt,
            "paper_fallback",
            "Replace closure range",
            "Owner confirmed replacement scope",
            new
            {
                OriginalPeriodId = originalPeriodId,
                ReplacementPeriodId = (Guid?)(
                    correctionReplacementLink ?? replacementPeriodId),
                CancellationId = (Guid?)null,
                OldMembershipIds = originalMembershipIds,
                NewMembershipIds = new[] { fixture.MembershipId },
                AffectedMembershipIds = affectedMembershipIds,
                AffectedClientIds = affectedClientIds,
            },
            new
            {
                Mode = "replace_range",
                OldAffectedCount = originalMembershipIds.Length,
                NewAffectedCount = 1,
                AffectedUnionCount = affectedMembershipIds.Length,
            },
            changedAfterClose: true);
        var cancellationAuditId = Guid.NewGuid();
        await InsertAuditAsync(
            database,
            fixture,
            cancellationAuditId,
            NonWorkingDayAuditActions.Canceled,
            canceledPeriodId,
            TestNow.AddDays(-1),
            cancellationRecordedAt,
            "manual_backfill",
            "Closure entered by mistake",
            "Owner canceled the period",
            new
            {
                OriginalPeriodId = canceledPeriodId,
                ReplacementPeriodId = (Guid?)null,
                CancellationId = (Guid?)cancellationId,
                OldMembershipIds = new[] { fixture.MembershipId },
                NewMembershipIds = Array.Empty<Guid>(),
                AffectedMembershipIds = new[] { fixture.MembershipId },
                AffectedClientIds = new[] { fixture.ClientId },
            },
            new
            {
                Mode = "cancel",
                OldAffectedCount = 1,
                NewAffectedCount = 0,
                AffectedUnionCount = 1,
            },
            changedAfterClose: true);
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            "non_working_day.previewed",
            originalPeriodId,
            TestNow.AddMinutes(-20),
            TestNow.AddMinutes(-19),
            "normal",
            reason: null,
            comment: null,
            new
            {
                AffectedMembershipIds = new[] { fixture.MembershipId },
                AffectedClientIds = new[] { fixture.ClientId },
            },
            afterSummary: new { });
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            NonWorkingDayAuditActions.Added,
            Guid.NewGuid(),
            TestNow.AddMinutes(-10),
            TestNow.AddMinutes(-9),
            "normal",
            reason: null,
            comment: null,
            new
            {
                AffectedMembershipIds = new[] { fixture.OtherMembershipId },
                AffectedClientIds = new[] { fixture.OtherClientId },
            },
            afterSummary: new { });

        return new NonWorkingDayHistorySourceIds(
            originalPeriodId,
            replacementPeriodId,
            canceledPeriodId,
            cancellationId,
            correctionAuditId,
            cancellationAuditId);
    }

    private static async Task InsertAuditAsync(
        PostgreSqlTestDatabase database,
        NonWorkingDayHistoryFixture fixture,
        Guid auditId,
        string actionType,
        Guid periodId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin,
        string? reason,
        string? comment,
        object relatedEntityRefs,
        object afterSummary,
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
                'non_working_period',
                @period_id,
                @related_entity_refs,
                @actor_account_id,
                'owner',
                'owner',
                @session_id,
                'Owner laptop',
                @occurred_at,
                @recorded_at,
                @reason,
                @comment,
                '{"state":"before"}'::jsonb,
                @after_summary,
                @request_correlation_id,
                @entry_origin,
                @idempotency_key,
                @changed_after_close)
            """;
        command.Parameters.AddWithValue("id", auditId);
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.AddWithValue("period_id", periodId);
        command.Parameters.Add(
            "related_entity_refs",
            NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(
                relatedEntityRefs,
                AuditJsonOptions);
        command.Parameters.AddWithValue(
            "actor_account_id",
            fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue(
            "session_id",
            fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.Add("reason", NpgsqlDbType.Varchar).Value =
            reason ?? (object)DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Varchar).Value =
            comment ?? (object)DBNull.Value;
        command.Parameters.Add(
            "after_summary",
            NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(
                afterSummary,
                AuditJsonOptions);
        command.Parameters.AddWithValue(
            "request_correlation_id",
            $"non-working-history-{auditId:N}");
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.AddWithValue(
            "idempotency_key",
            $"non-working-history-idempotency-{auditId:N}");
        command.Parameters.AddWithValue("changed_after_close", changedAfterClose);
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
            update bodylife.non_working_period_cancellations
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

    private static ClientNonWorkingDayHistorySourceRowsPage AssertSuccess(
        GetClientNonWorkingDayHistorySourceRowsResult result,
        Guid clientId)
    {
        Assert.Equal(
            GetClientNonWorkingDayHistorySourceRowsStatus.Success,
            result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        var page = Assert.IsType<ClientNonWorkingDayHistorySourceRowsPage>(
            result.Page);
        Assert.Equal(clientId, page.ClientId);
        return page;
    }

    private static void AssertFailure(
        GetClientNonWorkingDayHistorySourceRowsResult result,
        GetClientNonWorkingDayHistorySourceRowsStatus status,
        string? field = null)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Page);
        Assert.NotNull(result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(field, result.ErrorField);
    }

    private sealed record NonWorkingDayHistoryFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId,
        Guid OtherMembershipId);

    private sealed record NonWorkingDayHistorySourceIds(
        Guid OriginalPeriodId,
        Guid ReplacementPeriodId,
        Guid CanceledPeriodId,
        Guid CancellationId,
        Guid CorrectionAuditId,
        Guid CancellationAuditId);

    private sealed record ApplicationSeed(
        Guid ApplicationId,
        Guid MembershipId,
        Guid ClientId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
