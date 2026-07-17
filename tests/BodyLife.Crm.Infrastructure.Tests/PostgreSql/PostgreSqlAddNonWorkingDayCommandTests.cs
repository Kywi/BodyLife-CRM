using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlAddNonWorkingDayCommandTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        17,
        12,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateRange ProposedPeriod = new(
        new DateOnly(2026, 1, 30),
        new DateOnly(2026, 2, 2));
    private static readonly Guid FirstMembershipId = Guid.Parse(
        "00000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondMembershipId = Guid.Parse(
        "00000000-0000-0000-0000-000000000002");
    private static readonly Guid OutsideMembershipId = Guid.Parse(
        "00000000-0000-0000-0000-000000000003");

    [PostgreSqlFact]
    public async Task SuccessfulCommandCommitsExactSnapshotStateAuditAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(dbContext, fixture.Actor, tokenService, clock);
        Assert.Equal(2, preview.AffectedCount);

        var result = await CreateHandler(dbContext, tokenService, clock).ExecuteAsync(
            CreateCommand(fixture, preview, "non-working-success"),
            CancellationToken.None);

        AssertSuccess(result, FirstMembershipId, SecondMembershipId);
        var periodId = result.PrimaryEntityId!.Value.Value;
        Assert.Equal(result.PrimaryEntityId, result.RereadTargetId);

        var period = await ReadPeriodAsync(database, periodId);
        Assert.Equal(ProposedPeriod.StartDate, period.StartDate);
        Assert.Equal(ProposedPeriod.EndDate, period.EndDate);
        Assert.Equal("weather_closure", period.ReasonCode);
        Assert.Equal("Severe weather", period.ReasonComment);
        Assert.Equal(TestNow, period.CreatedAt);
        Assert.Equal(fixture.Actor.AccountId.Value, period.CreatedByAccountId);
        Assert.Equal(fixture.Actor.SessionId.Value, period.SessionId);
        Assert.Equal("active", period.Status);

        var applications = await ReadApplicationsAsync(database, periodId);
        Assert.Equal(2, applications.Length);
        Assert.Equal(
            [FirstMembershipId, SecondMembershipId],
            applications.Select(application => application.MembershipId));
        Assert.All(applications, application =>
        {
            Assert.Equal(ProposedPeriod.StartDate, application.AppliedStartDate);
            Assert.Equal(ProposedPeriod.EndDate, application.AppliedEndDate);
            Assert.Equal(preview.Confirmation.IssuedAt, application.PreviewedAt);
            Assert.Equal(TestNow, application.ConfirmedAt);
            Assert.Equal("active", application.Status);
        });

        var states = await ReadStatesAsync(database);
        Assert.Equal(2, states.Length);
        Assert.Equal(FirstMembershipId, states[0].MembershipId);
        Assert.Equal(4, states[0].ExtensionDays);
        Assert.Equal(new DateOnly(2026, 2, 4), states[0].EffectiveEndDate);
        Assert.Equal(SecondMembershipId, states[1].MembershipId);
        Assert.Equal(4, states[1].ExtensionDays);
        Assert.Equal(new DateOnly(2026, 2, 15), states[1].EffectiveEndDate);
        Assert.All(states, state =>
        {
            Assert.Equal(0, state.CountedVisits);
            Assert.Equal(8, state.RemainingVisits);
            Assert.Equal(TestNow, state.RecalculatedAt);
            Assert.Equal(
                MembershipStateCacheRebuilder.CurrentRecalculationVersion,
                state.RecalculationVersion);
        });
        Assert.DoesNotContain(states, state => state.MembershipId == OutsideMembershipId);

        var extensionRows = await ReadExtensionRowsAsync(database);
        Assert.Equal(8, extensionRows.Length);
        Assert.All(extensionRows, row =>
        {
            Assert.Equal("non_working_period", row.SourceType);
            Assert.True(row.IsActive);
            Assert.Equal(TestNow, row.RecalculatedAt);
        });
        Assert.All(
            applications,
            application => Assert.Equal(
                4,
                extensionRows.Count(row => row.SourceId == application.Id)));

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(NonWorkingDayAuditActions.Added, audit.ActionType);
        Assert.Equal(NonWorkingDayAuditActions.PeriodEntityType, audit.EntityType);
        Assert.Equal(periodId, audit.EntityId);
        Assert.Equal("owner", audit.ActorAccountType);
        Assert.Equal("owner", audit.ActorRole);
        Assert.Equal(TestNow, audit.OccurredAt);
        Assert.Equal(TestNow, audit.RecordedAt);
        Assert.Equal("Owner confirmed closure", audit.Reason);
        Assert.Equal("Schedule source", audit.Comment);
        Assert.Equal("correlation-non-working-success", audit.RequestCorrelationId);
        Assert.Equal("non-working-success", audit.IdempotencyKey);
        using (var before = JsonDocument.Parse(audit.BeforeSummaryJson))
        {
            var previewSummary = before.RootElement.GetProperty("preview");
            Assert.Equal(2, previewSummary.GetProperty("affectedCount").GetInt32());
            Assert.Equal(
                preview.Confirmation.ScopeFingerprint,
                previewSummary.GetProperty("scopeFingerprint").GetString());
        }

        using (var after = JsonDocument.Parse(audit.AfterSummaryJson))
        {
            var periodSummary = after.RootElement.GetProperty("period");
            Assert.Equal(periodId, periodSummary.GetProperty("periodId").GetGuid());
            Assert.Equal(4, periodSummary.GetProperty("inclusiveDays").GetInt32());
            Assert.Equal(
                "weather_closure",
                periodSummary.GetProperty("reasonCode").GetString());
            Assert.Equal(
                2,
                after.RootElement.GetProperty("affectedMembershipCount").GetInt32());
            var recalculation = after.RootElement.GetProperty("recalculation");
            Assert.Equal(2, recalculation.GetProperty("requestedCount").GetInt32());
            Assert.Equal(2, recalculation.GetProperty("succeededCount").GetInt32());
        }

        var idempotency = await ReadIdempotencyAsync(
            database,
            "non-working-success");
        Assert.Equal("AddNonWorkingDay", idempotency.CommandName);
        Assert.Equal(periodId, idempotency.PrimaryEntityId);
        Assert.Equal(periodId, idempotency.RereadTargetId);
        Assert.Equal(result.AuditEntryId.Value.Value, idempotency.AuditEntryId);
        Assert.Equal("succeeded", idempotency.Status);
        Assert.False(string.IsNullOrWhiteSpace(idempotency.ResultFingerprint));
    }

    [PostgreSqlFact]
    public async Task OwnerInputAndTokenFailuresDoNotWriteCommandState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(dbContext, fixture.Actor, tokenService, clock);
        var valid = CreateCommand(fixture, preview, "validation-base");
        var handler = CreateHandler(dbContext, tokenService, clock);

        var adminShape = await handler.ExecuteAsync(
            valid with
            {
                Envelope = valid.Envelope with
                {
                    Actor = fixture.Actor with
                    {
                        Role = ActorRole.Admin,
                        AccountKind = AccountKind.NamedAdmin,
                    },
                },
            },
            CancellationToken.None);
        var missingReason = await handler.ExecuteAsync(
            valid with { ReasonCode = "  " },
            CancellationToken.None);
        var malformedToken = await handler.ExecuteAsync(
            valid with { ConfirmationToken = "not-a-preview-token" },
            CancellationToken.None);

        await EndSessionAsync(database, fixture.Actor.SessionId.Value);
        var endedSession = await handler.ExecuteAsync(valid, CancellationToken.None);

        AssertError(adminShape, CommandErrorCode.PermissionDenied);
        AssertError(missingReason, CommandErrorCode.ValidationFailed, "reasonCode");
        AssertError(malformedToken, CommandErrorCode.ValidationFailed, "confirmationToken");
        AssertError(endedSession, CommandErrorCode.PermissionDenied);
        await AssertNoCommandMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task ExpiredOrChangedPreviewFailsWithoutPartialWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var originalPreview = await IssuePreviewAsync(
            dbContext,
            fixture.Actor,
            tokenService,
            clock);

        await MoveOutsideMembershipIntoScopeAsync(database);

        var scopeChanged = await CreateHandler(dbContext, tokenService, clock)
            .ExecuteAsync(
                CreateCommand(fixture, originalPreview, "scope-changed"),
                CancellationToken.None);
        AssertError(
            scopeChanged,
            CommandErrorCode.AffectedScopeChanged,
            "confirmationToken");

        var currentPreview = await IssuePreviewAsync(
            dbContext,
            fixture.Actor,
            tokenService,
            clock);
        Assert.Equal(3, currentPreview.AffectedCount);
        clock.UtcNow = currentPreview.Confirmation.ExpiresAt;
        var expired = await CreateHandler(dbContext, tokenService, clock)
            .ExecuteAsync(
                CreateCommand(fixture, currentPreview, "preview-expired"),
                CancellationToken.None);

        AssertError(expired, CommandErrorCode.PreviewExpired, "confirmationToken");
        await AssertNoCommandMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task IdempotentReplayReturnsOriginalAndChangedPayloadIsRejected()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(dbContext, fixture.Actor, tokenService, clock);
        var command = CreateCommand(fixture, preview, "non-working-replay");
        var handler = CreateHandler(dbContext, tokenService, clock);

        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var changed = await handler.ExecuteAsync(
            command with { ReasonCode = "maintenance" },
            CancellationToken.None);

        AssertSuccess(first, FirstMembershipId, SecondMembershipId);
        AssertSuccess(replay, FirstMembershipId, SecondMembershipId);
        Assert.Equal(first.PrimaryEntityId, replay.PrimaryEntityId);
        Assert.Equal(first.RelatedEntityIds, replay.RelatedEntityIds);
        Assert.Equal(first.AuditEntryId, replay.AuditEntryId);
        AssertError(changed, CommandErrorCode.DuplicateSubmission, "idempotencyKey");
        await AssertCommandMutationCountsAsync(database, 1, 2, 2, 8, 1, 1);
    }

    [PostgreSqlFact]
    public async Task ConcurrentSameKeyCommitsOneCompleteWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        NonWorkingDayFixture fixture;
        NonWorkingDayImpactPreview preview;
        await using (var setupContext = database.CreateDbContext())
        {
            await setupContext.Database.MigrateAsync();
            fixture = await SeedFixtureAsync(database, setupContext);
            preview = await IssuePreviewAsync(
                setupContext,
                fixture.Actor,
                tokenService,
                clock);
        }

        var command = CreateCommand(fixture, preview, "concurrent-non-working");
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateHandler(firstContext, tokenService, clock).ExecuteAsync(
                command,
                CancellationToken.None),
            CreateHandler(secondContext, tokenService, clock).ExecuteAsync(
                command,
                CancellationToken.None));

        Assert.All(
            results,
            result => AssertSuccess(result, FirstMembershipId, SecondMembershipId));
        Assert.Equal(results[0].PrimaryEntityId, results[1].PrimaryEntityId);
        Assert.Equal(results[0].RelatedEntityIds, results[1].RelatedEntityIds);
        Assert.Equal(results[0].AuditEntryId, results[1].AuditEntryId);
        await AssertCommandMutationCountsAsync(database, 1, 2, 2, 8, 1, 1);
    }

    [PostgreSqlFact]
    public async Task RecalculationFailureRollsBackSourceAndDerivedState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(dbContext, fixture.Actor, tokenService, clock);

        var result = await CreateHandler(
                dbContext,
                tokenService,
                clock,
                recalculatorDecorator: inner =>
                    new FailAfterCanonicalWriteRecalculator(inner))
            .ExecuteAsync(
                CreateCommand(fixture, preview, "recalculation-failure"),
                CancellationToken.None);

        AssertError(result, CommandErrorCode.RecalculationFailed);
        await AssertNoCommandMutationAsync(database);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackEntireWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(dbContext, fixture.Actor, tokenService, clock);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_non_working_day_added_audit
            check (action_type <> 'non_working_day.added')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext, tokenService, clock).ExecuteAsync(
                CreateCommand(fixture, preview, "audit-failure"),
                CancellationToken.None));

        await AssertNoCommandMutationAsync(database);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task CompetingMembershipLockReturnsConcurrencyConflict()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(dbContext, fixture.Actor, tokenService, clock);
        await using var lockConnection = new NpgsqlConnection(database.ConnectionString);
        await lockConnection.OpenAsync();
        await using var lockTransaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.Transaction = lockTransaction;
            lockCommand.CommandText =
                "select id from bodylife.issued_memberships where id = @id for update";
            lockCommand.Parameters.AddWithValue("id", FirstMembershipId);
            Assert.Equal(FirstMembershipId, await lockCommand.ExecuteScalarAsync());
        }

        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("set lock_timeout = '250ms'");
        var result = await CreateHandler(dbContext, tokenService, clock).ExecuteAsync(
            CreateCommand(fixture, preview, "membership-lock-conflict"),
            CancellationToken.None);

        await lockTransaction.RollbackAsync();
        AssertError(result, CommandErrorCode.ConcurrencyConflict);
        await AssertNoCommandMutationAsync(database);
    }

    [Fact]
    public void PersistenceRegistrationIncludesAddNonWorkingDayCommandHandler()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] =
                    BodyLifeDbContextOptions.LocalDevelopmentConnectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddBodyLifePersistence(configuration);

        var descriptor = Assert.Single(
            services,
            service => service.ServiceType
                == typeof(IBodyLifeCommandHandler<AddNonWorkingDayCommand>));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(AddNonWorkingDayCommandHandler), descriptor.ImplementationType);
    }

    private static AddNonWorkingDayCommandHandler CreateHandler(
        BodyLifeDbContext dbContext,
        INonWorkingDayPreviewTokenService tokenService,
        TimeProvider timeProvider,
        Func<IMembershipStateRecalculator, IMembershipStateRecalculator>?
            recalculatorDecorator = null)
    {
        var rebuilder = CreateStateRebuilder(dbContext, timeProvider);
        IMembershipStateRecalculator recalculator =
            new MembershipStateRecalculator(rebuilder);
        if (recalculatorDecorator is not null)
        {
            recalculator = recalculatorDecorator(recalculator);
        }

        return new AddNonWorkingDayCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new MembershipNonWorkingDayAffectedScopePreparer(dbContext, rebuilder),
            recalculator,
            tokenService,
            timeProvider);
    }

    private static MembershipStateCacheRebuilder CreateStateRebuilder(
        BodyLifeDbContext dbContext,
        TimeProvider timeProvider)
    {
        return new MembershipStateCacheRebuilder(
            dbContext,
            timeProvider,
            [
                new MembershipFreezeExtensionSourceReader(dbContext),
                new MembershipNonWorkingDayExtensionSourceReader(dbContext),
            ]);
    }

    private static async Task<NonWorkingDayImpactPreview> IssuePreviewAsync(
        BodyLifeDbContext dbContext,
        ActorContext actor,
        INonWorkingDayPreviewTokenService tokenService,
        TimeProvider timeProvider)
    {
        var result = await new PreviewNonWorkingDayImpactQueryHandler(
                dbContext,
                new MembershipNonWorkingDayAffectedScopePreparer(
                    dbContext,
                    CreateStateRebuilder(dbContext, timeProvider)),
                tokenService,
                timeProvider)
            .ExecuteAsync(
                new PreviewNonWorkingDayImpactQuery(
                    actor,
                    ProposedPeriod.StartDate,
                    ProposedPeriod.EndDate,
                    "weather_closure",
                    "Severe weather"),
                CancellationToken.None);

        Assert.Equal(PreviewNonWorkingDayImpactStatus.Success, result.Status);
        return Assert.IsType<NonWorkingDayImpactPreview>(result.Preview);
    }

    private static AddNonWorkingDayCommand CreateCommand(
        NonWorkingDayFixture fixture,
        NonWorkingDayImpactPreview preview,
        string idempotencyKey)
    {
        return new AddNonWorkingDayCommand(
            new CommandEnvelope(
                fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                OccurredAt: null,
                idempotencyKey,
                "  Owner confirmed closure  ",
                "  Schedule source  "),
            preview.Period,
            preview.ReasonCode,
            preview.ReasonComment,
            preview.Confirmation.ConfirmationToken);
    }

    private static HmacNonWorkingDayPreviewTokenService CreateTokenService(
        TimeProvider timeProvider)
    {
        var signingKey = Convert.ToBase64String(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        return new HmacNonWorkingDayPreviewTokenService(
            new NonWorkingDayPreviewTokenOptions(
                signingKey,
                TimeSpan.FromMinutes(5)),
            timeProvider);
    }

    private static async Task<NonWorkingDayFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var bootstrap = await new OwnerBootstrapper(
                dbContext,
                new MutableTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var accountId = bootstrap.AccountId!.Value;
        var sessionId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var firstClientId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var secondClientId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var outsideClientId = Guid.Parse("10000000-0000-0000-0000-000000000003");
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
                @recorded_at,
                @expires_at,
                null,
                @recorded_at);

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
                'Non-working day fixture',
                10,
                8,
                1000,
                'UAH',
                true,
                null,
                @recorded_at,
                @recorded_at,
                null);

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
                    @first_client_id,
                    'NonWorkingDay',
                    'First',
                    null,
                    'NONWORKINGDAY FIRST',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @recorded_at,
                    @account_id,
                    @recorded_at),
                (
                    @second_client_id,
                    'NonWorkingDay',
                    'Second',
                    null,
                    'NONWORKINGDAY SECOND',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @recorded_at,
                    @account_id,
                    @recorded_at),
                (
                    @outside_client_id,
                    'NonWorkingDay',
                    'Outside',
                    null,
                    'NONWORKINGDAY OUTSIDE',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @recorded_at,
                    @account_id,
                    @recorded_at);

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
                    @first_membership_id,
                    @first_client_id,
                    @membership_type_id,
                    'Non-working day fixture',
                    10,
                    8,
                    1000,
                    'UAH',
                    '2026-01-22'::date,
                    '2026-01-31'::date,
                    @recorded_at,
                    @account_id,
                    'active',
                    'normal',
                    null,
                    null),
                (
                    @second_membership_id,
                    @second_client_id,
                    @membership_type_id,
                    'Non-working day fixture',
                    10,
                    8,
                    1000,
                    'UAH',
                    '2026-02-02'::date,
                    '2026-02-11'::date,
                    @recorded_at,
                    @account_id,
                    'active',
                    'normal',
                    null,
                    null),
                (
                    @outside_membership_id,
                    @outside_client_id,
                    @membership_type_id,
                    'Non-working day fixture',
                    10,
                    8,
                    1000,
                    'UAH',
                    '2026-02-03'::date,
                    '2026-02-12'::date,
                    @recorded_at,
                    @account_id,
                    'active',
                    'normal',
                    null,
                    null)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("first_client_id", firstClientId);
        command.Parameters.AddWithValue("second_client_id", secondClientId);
        command.Parameters.AddWithValue("outside_client_id", outsideClientId);
        command.Parameters.AddWithValue("first_membership_id", FirstMembershipId);
        command.Parameters.AddWithValue("second_membership_id", SecondMembershipId);
        command.Parameters.AddWithValue("outside_membership_id", OutsideMembershipId);
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(12));
        Assert.Equal(8, await command.ExecuteNonQueryAsync());
        dbContext.ChangeTracker.Clear();

        return new NonWorkingDayFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Owner laptop"));
    }

    private static async Task EndSessionAsync(
        PostgreSqlTestDatabase database,
        Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "update bodylife.sessions set ended_at = @ended_at where id = @session_id";
        command.Parameters.AddWithValue("ended_at", TestNow);
        command.Parameters.AddWithValue("session_id", sessionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task MoveOutsideMembershipIntoScopeAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.issued_memberships
            set
                start_date = '2026-02-02'::date,
                base_end_date = '2026-02-11'::date
            where id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", OutsideMembershipId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<PeriodRow> ReadPeriodAsync(
        PostgreSqlTestDatabase database,
        Guid periodId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                start_date,
                end_date,
                reason_code,
                reason_comment,
                created_at,
                created_by_account_id,
                session_id,
                status
            from bodylife.non_working_periods
            where id = @period_id
            """;
        command.Parameters.AddWithValue("period_id", periodId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new PeriodRow(
            reader.GetFieldValue<DateOnly>(0),
            reader.GetFieldValue<DateOnly>(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetGuid(5),
            reader.GetGuid(6),
            reader.GetString(7));
    }

    private static async Task<ApplicationRow[]> ReadApplicationsAsync(
        PostgreSqlTestDatabase database,
        Guid periodId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                id,
                membership_id,
                applied_start_date,
                applied_end_date,
                previewed_at,
                confirmed_at,
                status
            from bodylife.non_working_period_applications
            where non_working_period_id = @period_id
            order by membership_id
            """;
        command.Parameters.AddWithValue("period_id", periodId);
        var rows = new List<ApplicationRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new ApplicationRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetString(6)));
        }

        return rows.ToArray();
    }

    private static async Task<StateRow[]> ReadStatesAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                membership_id,
                counted_visits,
                remaining_visits,
                extension_days,
                effective_end_date,
                recalculated_at,
                recalculation_version
            from bodylife.membership_state_cache
            order by membership_id
            """;
        var rows = new List<StateRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new StateRow(
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetInt32(6)));
        }

        return rows.ToArray();
    }

    private static async Task<ExtensionRow[]> ReadExtensionRowsAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                membership_id,
                extension_date,
                source_type,
                source_id,
                is_active,
                recalculated_at
            from bodylife.membership_extension_days
            order by membership_id, extension_date
            """;
        var rows = new List<ExtensionRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new ExtensionRow(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.GetBoolean(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return rows.ToArray();
    }

    private static async Task<AuditRow> ReadAuditAsync(
        PostgreSqlTestDatabase database,
        Guid auditEntryId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                action_type,
                entity_type,
                entity_id,
                actor_account_type,
                actor_role,
                occurred_at,
                recorded_at,
                reason,
                comment,
                request_correlation_id,
                idempotency_key,
                before_summary::text,
                after_summary::text
            from bodylife.business_audit_entries
            where id = @audit_entry_id
            """;
        command.Parameters.AddWithValue("audit_entry_id", auditEntryId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new AuditRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12));
    }

    private static async Task<IdempotencyRow> ReadIdempotencyAsync(
        PostgreSqlTestDatabase database,
        string idempotencyKey)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                command_name,
                primary_entity_id,
                reread_target_id,
                audit_entry_id,
                status,
                result_fingerprint
            from bodylife.command_idempotency_keys
            where idempotency_key = @idempotency_key
            """;
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new IdempotencyRow(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    private static void AssertSuccess(
        CommandResult result,
        params Guid[] membershipIds)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Equal(AddNonWorkingDayCommand.PrimaryEntityType, result.PrimaryEntityId?.Type);
        Assert.Equal(result.PrimaryEntityId, result.RereadTargetId);
        Assert.Equal(
            membershipIds.Select(id =>
                new EntityId(AddNonWorkingDayCommand.MembershipEntityType, id)),
            result.RelatedEntityIds);
        Assert.NotNull(result.AuditEntryId);
        Assert.Empty(result.Errors);
    }

    private static void AssertError(
        CommandResult result,
        CommandErrorCode code,
        string? field = null)
    {
        Assert.Equal(CommandStatus.Error, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(field, error.Field);
        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
    }

    private static Task AssertNoCommandMutationAsync(PostgreSqlTestDatabase database)
    {
        return AssertCommandMutationCountsAsync(database, 0, 0, 0, 0, 0, 0);
    }

    private static async Task AssertCommandMutationCountsAsync(
        PostgreSqlTestDatabase database,
        int periodCount,
        int applicationCount,
        int stateCount,
        int extensionDayCount,
        int auditCount,
        int idempotencyCount)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                (select count(*) from bodylife.non_working_periods),
                (select count(*) from bodylife.non_working_period_applications),
                (select count(*) from bodylife.membership_state_cache),
                (select count(*) from bodylife.membership_extension_days),
                (select count(*) from bodylife.business_audit_entries),
                (select count(*) from bodylife.command_idempotency_keys)
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(periodCount, reader.GetInt64(0));
        Assert.Equal(applicationCount, reader.GetInt64(1));
        Assert.Equal(stateCount, reader.GetInt64(2));
        Assert.Equal(extensionDayCount, reader.GetInt64(3));
        Assert.Equal(auditCount, reader.GetInt64(4));
        Assert.Equal(idempotencyCount, reader.GetInt64(5));
    }

    private sealed record NonWorkingDayFixture(ActorContext Actor);

    private sealed record PeriodRow(
        DateOnly StartDate,
        DateOnly EndDate,
        string ReasonCode,
        string? ReasonComment,
        DateTimeOffset CreatedAt,
        Guid CreatedByAccountId,
        Guid SessionId,
        string Status);

    private sealed record ApplicationRow(
        Guid Id,
        Guid MembershipId,
        DateOnly AppliedStartDate,
        DateOnly AppliedEndDate,
        DateTimeOffset PreviewedAt,
        DateTimeOffset ConfirmedAt,
        string Status);

    private sealed record StateRow(
        Guid MembershipId,
        int CountedVisits,
        int RemainingVisits,
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        DateTimeOffset RecalculatedAt,
        int RecalculationVersion);

    private sealed record ExtensionRow(
        Guid MembershipId,
        DateOnly ExtensionDate,
        string SourceType,
        Guid SourceId,
        bool IsActive,
        DateTimeOffset RecalculatedAt);

    private sealed record AuditRow(
        string ActionType,
        string EntityType,
        Guid EntityId,
        string ActorAccountType,
        string ActorRole,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string? Reason,
        string? Comment,
        string RequestCorrelationId,
        string? IdempotencyKey,
        string BeforeSummaryJson,
        string AfterSummaryJson);

    private sealed record IdempotencyRow(
        string CommandName,
        Guid? PrimaryEntityId,
        Guid? RereadTargetId,
        Guid? AuditEntryId,
        string Status,
        string? ResultFingerprint);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class FailAfterCanonicalWriteRecalculator(
        IMembershipStateRecalculator inner)
        : IMembershipStateRecalculator
    {
        public async Task<MembershipStateRecalculationResult> RecalculateAsync(
            Guid membershipId,
            CancellationToken cancellationToken = default)
        {
            var completed = await inner.RecalculateAsync(membershipId, cancellationToken);
            Assert.True(completed.Succeeded);
            return new MembershipStateRecalculationResult(
                membershipId,
                MembershipStateRecalculationStatus.InvalidSourceState);
        }
    }
}
