using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
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

public sealed class PostgreSqlCorrectNonWorkingDayCommandTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        17,
        14,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateRange OriginalPeriod = new(
        new DateOnly(2026, 1, 30),
        new DateOnly(2026, 2, 2));
    private static readonly DateRange ReplacementPeriod = new(
        new DateOnly(2026, 2, 3),
        new DateOnly(2026, 2, 4));
    private static readonly Guid FirstClientId = Guid.Parse(
        "10000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondClientId = Guid.Parse(
        "10000000-0000-0000-0000-000000000002");
    private static readonly Guid ThirdClientId = Guid.Parse(
        "10000000-0000-0000-0000-000000000003");
    private static readonly Guid FirstMembershipId = Guid.Parse(
        "20000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondMembershipId = Guid.Parse(
        "20000000-0000-0000-0000-000000000002");
    private static readonly Guid ThirdMembershipId = Guid.Parse(
        "20000000-0000-0000-0000-000000000003");
    private static readonly Guid FirstApplicationId = Guid.Parse(
        "30000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondApplicationId = Guid.Parse(
        "30000000-0000-0000-0000-000000000002");

    [PostgreSqlFact]
    public async Task ReplaceRangeCommitsNewScopeAndRecalculatesOldNewUnion()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(
            dbContext,
            fixture,
            NonWorkingDayCorrectionMode.ReplaceRange,
            tokenService,
            clock);
        Assert.Equal(2, preview.OriginalAffectedCount);
        Assert.Equal(3, preview.ConfirmedAffectedCount);

        var result = await CreateHandler(dbContext, tokenService, clock).ExecuteAsync(
            CreateCommand(fixture, preview, "correct-range"),
            CancellationToken.None);

        AssertSuccess(
            result,
            CorrectNonWorkingDayCommand.PeriodEntityType,
            fixture.PeriodId,
            FirstMembershipId,
            SecondMembershipId,
            ThirdMembershipId);
        var replacementPeriodId = result.PrimaryEntityId!.Value.Value;

        var periods = await ReadPeriodsAsync(database);
        Assert.Equal(2, periods.Length);
        var original = Assert.Single(
            periods,
            period => period.Id == fixture.PeriodId);
        var replacement = Assert.Single(
            periods,
            period => period.Id == replacementPeriodId);
        Assert.Equal("corrected", original.Status);
        Assert.Equal(ReplacementPeriod.StartDate, replacement.StartDate);
        Assert.Equal(ReplacementPeriod.EndDate, replacement.EndDate);
        Assert.Equal("maintenance", replacement.ReasonCode);
        Assert.Equal("Boiler replacement", replacement.ReasonComment);
        Assert.Equal(TestNow, replacement.CreatedAt);
        Assert.Equal("active", replacement.Status);

        var originalApplications = await ReadApplicationsAsync(
            database,
            fixture.PeriodId);
        var replacementApplications = await ReadApplicationsAsync(
            database,
            replacementPeriodId);
        Assert.Equal(2, originalApplications.Length);
        Assert.All(
            originalApplications,
            application => Assert.Equal("corrected", application.Status));
        Assert.Equal(
            [FirstMembershipId, SecondMembershipId, ThirdMembershipId],
            replacementApplications.Select(application => application.MembershipId));
        Assert.All(replacementApplications, application =>
        {
            Assert.Equal(ReplacementPeriod.StartDate, application.AppliedStartDate);
            Assert.Equal(ReplacementPeriod.EndDate, application.AppliedEndDate);
            Assert.Equal(preview.Confirmation.IssuedAt, application.PreviewedAt);
            Assert.Equal(TestNow, application.ConfirmedAt);
            Assert.Equal("active", application.Status);
        });

        var states = await ReadStatesAsync(database);
        Assert.Equal(3, states.Length);
        Assert.Equal(
            [2, 2, 2],
            states.Select(state => state.ExtensionDays));
        Assert.Equal(
            [
                new DateOnly(2026, 2, 10),
                new DateOnly(2026, 2, 15),
                new DateOnly(2026, 3, 6),
            ],
            states.Select(state => state.EffectiveEndDate));
        var extensionRows = await ReadExtensionRowsAsync(database);
        Assert.Equal(14, extensionRows.Length);
        var activeExtensionRows = extensionRows
            .Where(row => row.IsActive)
            .ToArray();
        var inactiveExtensionRows = extensionRows
            .Where(row => !row.IsActive)
            .ToArray();
        Assert.Equal(6, activeExtensionRows.Length);
        Assert.Equal(8, inactiveExtensionRows.Length);
        Assert.All(activeExtensionRows, row => Assert.Contains(
            row.SourceId,
            replacementApplications.Select(application => application.Id)));
        Assert.All(inactiveExtensionRows, row => Assert.Contains(
            row.SourceId,
            originalApplications.Select(application => application.Id)));

        await AssertAuditAndIdempotencyAsync(
            database,
            result,
            fixture.PeriodId,
            NonWorkingDayAuditActions.Corrected,
            "replace_range",
            oldAffectedCount: 2,
            newAffectedCount: 3,
            affectedUnionCount: 3);

        var outcome = await ReadCanonicalOutcomeAsync(
            dbContext,
            fixture,
            result,
            clock);
        Assert.Equal(NonWorkingDayCorrectionMode.ReplaceRange, outcome.Mode);
        Assert.Equal(fixture.PeriodId, outcome.OriginalPeriod.PeriodId);
        Assert.Equal(
            NonWorkingDayCorrectionSourceStatus.Corrected,
            outcome.OriginalPeriod.Status);
        Assert.Equal(replacementPeriodId, outcome.ReplacementPeriod?.PeriodId);
        Assert.Equal(
            NonWorkingDayCorrectionSourceStatus.Active,
            outcome.ReplacementPeriod?.Status);
        Assert.Null(outcome.Cancellation);
        Assert.Equal(2, outcome.OriginalAffectedCount);
        Assert.Equal(3, outcome.ReplacementAffectedCount);
        Assert.Equal(3, outcome.AffectedUnionCount);
        Assert.Equal(
            [FirstMembershipId, SecondMembershipId, ThirdMembershipId],
            outcome.AffectedMembershipIds);

        var denied = await new GetNonWorkingDayCorrectionOutcomeQueryHandler(
                dbContext,
                clock)
            .ExecuteAsync(
                new GetNonWorkingDayCorrectionOutcomeQuery(
                    fixture.Actor with
                    {
                        Role = ActorRole.Admin,
                        AccountKind = AccountKind.NamedAdmin,
                    },
                    fixture.PeriodId,
                    result.AuditEntryId!.Value.Value),
                CancellationToken.None);
        Assert.Equal(
            GetNonWorkingDayCorrectionOutcomeStatus.PermissionDenied,
            denied.Status);
    }

    [PostgreSqlFact]
    public async Task ReplaceReasonPreservesExactConfirmedMembershipSnapshot()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(
            dbContext,
            fixture,
            NonWorkingDayCorrectionMode.ReplaceReason,
            tokenService,
            clock);

        var result = await CreateHandler(dbContext, tokenService, clock).ExecuteAsync(
            CreateCommand(fixture, preview, "correct-reason"),
            CancellationToken.None);

        AssertSuccess(
            result,
            CorrectNonWorkingDayCommand.PeriodEntityType,
            fixture.PeriodId,
            FirstMembershipId,
            SecondMembershipId);
        var replacementPeriodId = result.PrimaryEntityId!.Value.Value;
        var periods = await ReadPeriodsAsync(database);
        var original = Assert.Single(
            periods,
            period => period.Id == fixture.PeriodId);
        var replacement = Assert.Single(
            periods,
            period => period.Id == replacementPeriodId);
        Assert.Equal("corrected", original.Status);
        Assert.Equal(original.StartDate, replacement.StartDate);
        Assert.Equal(original.EndDate, replacement.EndDate);
        Assert.Equal("corrected_weather", replacement.ReasonCode);
        Assert.Equal("Corrected explanation", replacement.ReasonComment);
        Assert.Equal("active", replacement.Status);

        var oldApplications = await ReadApplicationsAsync(
            database,
            fixture.PeriodId);
        var newApplications = await ReadApplicationsAsync(
            database,
            replacementPeriodId);
        Assert.Equal(2, oldApplications.Length);
        Assert.Equal(2, newApplications.Length);
        Assert.Equal(
            oldApplications.Select(application => new
            {
                application.MembershipId,
                application.ClientId,
                application.AppliedStartDate,
                application.AppliedEndDate,
            }),
            newApplications.Select(application => new
            {
                application.MembershipId,
                application.ClientId,
                application.AppliedStartDate,
                application.AppliedEndDate,
            }));
        Assert.All(
            oldApplications,
            application => Assert.Equal("corrected", application.Status));
        Assert.All(
            newApplications,
            application => Assert.Equal("active", application.Status));
        Assert.Empty(
            oldApplications.Select(application => application.Id)
                .Intersect(newApplications.Select(application => application.Id)));

        var states = await ReadStatesAsync(database);
        Assert.Equal(2, states.Length);
        Assert.All(states, state => Assert.Equal(4, state.ExtensionDays));
        var extensionRows = await ReadExtensionRowsAsync(database);
        Assert.Equal(16, extensionRows.Length);
        Assert.Equal(8, extensionRows.Count(row => row.IsActive));
        Assert.Equal(8, extensionRows.Count(row => !row.IsActive));

        await AssertAuditAndIdempotencyAsync(
            database,
            result,
            fixture.PeriodId,
            NonWorkingDayAuditActions.Corrected,
            "replace_reason",
            oldAffectedCount: 2,
            newAffectedCount: 2,
            affectedUnionCount: 2);

        var outcome = await ReadCanonicalOutcomeAsync(
            dbContext,
            fixture,
            result,
            clock);
        Assert.Equal(NonWorkingDayCorrectionMode.ReplaceReason, outcome.Mode);
        Assert.Equal(replacementPeriodId, outcome.ReplacementPeriod?.PeriodId);
        Assert.Equal(OriginalPeriod, outcome.ReplacementPeriod?.Period);
        Assert.Equal("corrected_weather", outcome.ReplacementPeriod?.ReasonCode);
        Assert.Equal(2, outcome.OriginalAffectedCount);
        Assert.Equal(2, outcome.ReplacementAffectedCount);
        Assert.Equal(2, outcome.AffectedUnionCount);
    }

    [PostgreSqlFact]
    public async Task CancelRetainsCanceledSourcesAndCreatesCancellationFact()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(
            dbContext,
            fixture,
            NonWorkingDayCorrectionMode.Cancel,
            tokenService,
            clock);

        var result = await CreateHandler(dbContext, tokenService, clock).ExecuteAsync(
            CreateCommand(fixture, preview, "cancel-period"),
            CancellationToken.None);

        AssertSuccess(
            result,
            CorrectNonWorkingDayCommand.CancellationEntityType,
            fixture.PeriodId,
            FirstMembershipId,
            SecondMembershipId);
        var cancellationId = result.PrimaryEntityId!.Value.Value;
        var original = Assert.Single(await ReadPeriodsAsync(database));
        Assert.Equal("canceled", original.Status);
        var applications = await ReadApplicationsAsync(
            database,
            fixture.PeriodId);
        Assert.All(
            applications,
            application => Assert.Equal("canceled", application.Status));
        var cancellation = Assert.Single(await ReadCancellationsAsync(database));
        Assert.Equal(cancellationId, cancellation.Id);
        Assert.Equal(fixture.PeriodId, cancellation.NonWorkingPeriodId);
        Assert.Equal("Owner corrected the closure schedule", cancellation.Reason);
        Assert.Equal(TestNow, cancellation.RecordedAt);
        Assert.Equal(fixture.Actor.AccountId.Value, cancellation.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId.Value, cancellation.SessionId);

        var states = await ReadStatesAsync(database);
        Assert.Equal(2, states.Length);
        Assert.All(states, state => Assert.Equal(0, state.ExtensionDays));
        var extensionRows = await ReadExtensionRowsAsync(database);
        Assert.Equal(8, extensionRows.Length);
        Assert.All(extensionRows, row => Assert.False(row.IsActive));

        await AssertAuditAndIdempotencyAsync(
            database,
            result,
            fixture.PeriodId,
            NonWorkingDayAuditActions.Canceled,
            "cancel",
            oldAffectedCount: 2,
            newAffectedCount: 0,
            affectedUnionCount: 2);

        var outcome = await ReadCanonicalOutcomeAsync(
            dbContext,
            fixture,
            result,
            clock);
        Assert.Equal(NonWorkingDayCorrectionMode.Cancel, outcome.Mode);
        Assert.Equal(
            NonWorkingDayCorrectionSourceStatus.Canceled,
            outcome.OriginalPeriod.Status);
        Assert.Null(outcome.ReplacementPeriod);
        Assert.Equal(cancellationId, outcome.Cancellation?.CancellationId);
        Assert.Equal(2, outcome.OriginalAffectedCount);
        Assert.Equal(0, outcome.ReplacementAffectedCount);
        Assert.Equal(2, outcome.AffectedUnionCount);
    }

    [PostgreSqlFact]
    public async Task ExactReplayReturnsOriginalResultAndChangedPayloadIsRejected()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(
            dbContext,
            fixture,
            NonWorkingDayCorrectionMode.Cancel,
            tokenService,
            clock);
        var command = CreateCommand(fixture, preview, "cancel-replay");
        var handler = CreateHandler(dbContext, tokenService, clock);

        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var changed = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with
                {
                    Comment = "Changed correction explanation",
                },
            },
            CancellationToken.None);

        AssertSuccess(
            first,
            CorrectNonWorkingDayCommand.CancellationEntityType,
            fixture.PeriodId,
            FirstMembershipId,
            SecondMembershipId);
        AssertEquivalentSuccess(first, replay);
        AssertError(
            changed,
            CommandErrorCode.DuplicateSubmission,
            "idempotencyKey");
        Assert.Equal(1, await CountRowsAsync(database, "non_working_periods"));
        Assert.Equal(
            2,
            await CountRowsAsync(database, "non_working_period_applications"));
        Assert.Equal(
            1,
            await CountRowsAsync(database, "non_working_period_cancellations"));
        Assert.Equal(
            1,
            await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(
            1,
            await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ExpiredOrChangedPreviewFailsWithoutCommandWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(
            dbContext,
            fixture,
            NonWorkingDayCorrectionMode.ReplaceRange,
            tokenService,
            clock);
        var command = CreateCommand(fixture, preview, "stale-preview");

        clock.UtcNow = preview.Confirmation.ExpiresAt;
        var expired = await CreateHandler(dbContext, tokenService, clock)
            .ExecuteAsync(command, CancellationToken.None);
        AssertError(expired, CommandErrorCode.PreviewExpired, "confirmationToken");

        clock.UtcNow = TestNow;
        await SetMembershipStatusAsync(database, ThirdMembershipId, "canceled");
        var changed = await CreateHandler(dbContext, tokenService, clock)
            .ExecuteAsync(command, CancellationToken.None);
        AssertError(
            changed,
            CommandErrorCode.AffectedScopeChanged,
            "confirmationToken");

        await AssertOriginalSourceUnchangedAsync(database);
    }

    [PostgreSqlFact]
    public async Task UnsupportedOccurredAtIsRejectedBeforeCorrectionWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(
            dbContext,
            fixture,
            NonWorkingDayCorrectionMode.Cancel,
            tokenService,
            clock);

        var command = CreateCommand(fixture, preview, "unsupported-occurred-at");
        var result = await CreateHandler(dbContext, tokenService, clock).ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with
                {
                    OccurredAt = new DateTimeOffset(9999, 12, 31, 12, 0, 0, TimeSpan.Zero),
                },
            },
            CancellationToken.None);

        AssertError(result, CommandErrorCode.ValidationFailed, "occurredAt");
        await AssertOriginalSourceUnchangedAsync(database);
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
        var preview = await IssuePreviewAsync(
            dbContext,
            fixture,
            NonWorkingDayCorrectionMode.ReplaceReason,
            tokenService,
            clock);

        var result = await CreateHandler(
                dbContext,
                tokenService,
                clock,
                recalculatorDecorator: inner =>
                    new FailAfterCanonicalWriteRecalculator(inner))
            .ExecuteAsync(
                CreateCommand(fixture, preview, "recalculation-rollback"),
                CancellationToken.None);

        AssertError(result, CommandErrorCode.RecalculationFailed);
        await AssertOriginalSourceUnchangedAsync(database);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackEntireCorrectionWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        var preview = await IssuePreviewAsync(
            dbContext,
            fixture,
            NonWorkingDayCorrectionMode.Cancel,
            tokenService,
            clock);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_non_working_day_canceled_audit
            check (action_type <> 'non_working_day.canceled')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext, tokenService, clock).ExecuteAsync(
                CreateCommand(fixture, preview, "audit-rollback"),
                CancellationToken.None));

        await AssertOriginalSourceUnchangedAsync(database);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task ConcurrentSameKeyCommitsOneCompleteCorrection()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var tokenService = CreateTokenService(clock);
        CorrectionFixture fixture;
        NonWorkingDayCorrectionPreview preview;
        await using (var setupContext = database.CreateDbContext())
        {
            await setupContext.Database.MigrateAsync();
            fixture = await SeedFixtureAsync(database, setupContext);
            preview = await IssuePreviewAsync(
                setupContext,
                fixture,
                NonWorkingDayCorrectionMode.Cancel,
                tokenService,
                clock);
        }

        var command = CreateCommand(fixture, preview, "concurrent-correction");
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var results = await Task.WhenAll(
            CreateHandler(firstContext, tokenService, clock).ExecuteAsync(
                command,
                CancellationToken.None),
            CreateHandler(secondContext, tokenService, clock).ExecuteAsync(
                command,
                CancellationToken.None));

        Assert.All(results, result => AssertSuccess(
            result,
            CorrectNonWorkingDayCommand.CancellationEntityType,
            fixture.PeriodId,
            FirstMembershipId,
            SecondMembershipId));
        AssertEquivalentSuccess(results[0], results[1]);
        Assert.Equal(
            1,
            await CountRowsAsync(database, "non_working_period_cancellations"));
        Assert.Equal(
            1,
            await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(
            1,
            await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [Fact]
    public void PersistenceRegistrationIncludesCorrectNonWorkingDayHandler()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] =
                    BodyLifeDbContextOptions.LocalDevelopmentConnectionString,
                [$"{NonWorkingDayPreviewTokenOptions.SectionName}:"
                    + NonWorkingDayPreviewTokenOptions.SigningKeyName] = SigningKey(),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddBodyLifePersistence(configuration);

        var descriptor = Assert.Single(
            services,
            service => service.ServiceType
                == typeof(IBodyLifeCommandHandler<CorrectNonWorkingDayCommand>));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(
            typeof(CorrectNonWorkingDayCommandHandler),
            descriptor.ImplementationType);

        var queryDescriptor = Assert.Single(
            services,
            service => service.ServiceType == typeof(IBodyLifeQueryHandler<
                GetNonWorkingDayCorrectionOutcomeQuery,
                GetNonWorkingDayCorrectionOutcomeResult>));
        Assert.Equal(ServiceLifetime.Scoped, queryDescriptor.Lifetime);
        Assert.Equal(
            typeof(GetNonWorkingDayCorrectionOutcomeQueryHandler),
            queryDescriptor.ImplementationType);
    }

    private static CorrectNonWorkingDayCommandHandler CreateHandler(
        BodyLifeDbContext dbContext,
        INonWorkingDayCorrectionTokenService tokenService,
        TimeProvider timeProvider,
        Func<IMembershipStateRecalculator, IMembershipStateRecalculator>?
            recalculatorDecorator = null)
    {
        var components = CreateMembershipComponents(dbContext, timeProvider);
        IMembershipStateRecalculator recalculator =
            new MembershipStateRecalculator(components.Rebuilder);
        if (recalculatorDecorator is not null)
        {
            recalculator = recalculatorDecorator(recalculator);
        }

        var revalidationPreparer =
            new CorrectNonWorkingDayCommandRevalidationPreparer(
                dbContext,
                components.ReplacementPreparer,
                new CorrectNonWorkingDaySourcePreparer(dbContext),
                tokenService,
                timeProvider);
        return new CorrectNonWorkingDayCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            revalidationPreparer,
            recalculator,
            timeProvider);
    }

    private static async Task<NonWorkingDayCorrectionPreview> IssuePreviewAsync(
        BodyLifeDbContext dbContext,
        CorrectionFixture fixture,
        NonWorkingDayCorrectionMode mode,
        INonWorkingDayCorrectionTokenService tokenService,
        TimeProvider timeProvider)
    {
        var components = CreateMembershipComponents(dbContext, timeProvider);
        var result = await new PreviewCorrectNonWorkingDayQueryHandler(
                dbContext,
                components.ReplacementPreparer,
                new CorrectNonWorkingDaySourcePreparer(dbContext),
                tokenService,
                timeProvider)
            .ExecuteAsync(
                new PreviewCorrectNonWorkingDayQuery(
                    fixture.Actor,
                    fixture.PeriodId,
                    mode,
                    mode == NonWorkingDayCorrectionMode.ReplaceRange
                        ? ReplacementPeriod.StartDate
                        : null,
                    mode == NonWorkingDayCorrectionMode.ReplaceRange
                        ? ReplacementPeriod.EndDate
                        : null,
                    mode switch
                    {
                        NonWorkingDayCorrectionMode.ReplaceRange => "maintenance",
                        NonWorkingDayCorrectionMode.ReplaceReason =>
                            "corrected_weather",
                        _ => null,
                    },
                    mode switch
                    {
                        NonWorkingDayCorrectionMode.ReplaceRange =>
                            "Boiler replacement",
                        NonWorkingDayCorrectionMode.ReplaceReason =>
                            "Corrected explanation",
                        _ => null,
                    }),
                CancellationToken.None);

        Assert.Equal(PreviewCorrectNonWorkingDayStatus.Success, result.Status);
        return Assert.IsType<NonWorkingDayCorrectionPreview>(result.Preview);
    }

    private static MembershipComponents CreateMembershipComponents(
        BodyLifeDbContext dbContext,
        TimeProvider timeProvider)
    {
        var sourceReader = new MembershipNonWorkingDayExtensionSourceReader(
            dbContext);
        var rebuilder = new MembershipStateCacheRebuilder(
            dbContext,
            timeProvider,
            [
                new MembershipFreezeExtensionSourceReader(dbContext),
                sourceReader,
            ]);
        var affectedScopePreparer =
            new MembershipNonWorkingDayAffectedScopePreparer(
                dbContext,
                rebuilder);
        return new MembershipComponents(
            rebuilder,
            new MembershipNonWorkingDayReplacementImpactPreparer(
                affectedScopePreparer,
                sourceReader));
    }

    private static CorrectNonWorkingDayCommand CreateCommand(
        CorrectionFixture fixture,
        NonWorkingDayCorrectionPreview preview,
        string idempotencyKey)
    {
        return new CorrectNonWorkingDayCommand(
            new CommandEnvelope(
                fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                TestNow,
                idempotencyKey,
                "  Owner corrected the closure schedule  ",
                "  Confirmed against the correction preview  "),
            fixture.PeriodId,
            preview.Mode,
            preview.Mode == NonWorkingDayCorrectionMode.ReplaceRange
                ? ReplacementPeriod.StartDate
                : null,
            preview.Mode == NonWorkingDayCorrectionMode.ReplaceRange
                ? ReplacementPeriod.EndDate
                : null,
            preview.ReplacementInput?.ReasonCode,
            preview.ReplacementInput?.ReasonComment,
            preview.Confirmation.ConfirmationToken);
    }

    private static HmacNonWorkingDayCorrectionTokenService CreateTokenService(
        TimeProvider timeProvider)
    {
        return new HmacNonWorkingDayCorrectionTokenService(
            new NonWorkingDayPreviewTokenOptions(
                SigningKey(),
                TimeSpan.FromMinutes(5)),
            timeProvider);
    }

    private static async Task<CorrectionFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var bootstrap = await new OwnerBootstrapper(dbContext, new FixedTimeProvider(
                TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var accountId = bootstrap.AccountId!.Value;
        var sessionId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
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
                'Correction command fixture',
                30,
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
                    'Correction',
                    'First',
                    null,
                    'CORRECTION FIRST',
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
                    'Correction',
                    'Second',
                    null,
                    'CORRECTION SECOND',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @recorded_at,
                    @account_id,
                    @recorded_at),
                (
                    @third_client_id,
                    'Correction',
                    'Third',
                    null,
                    'CORRECTION THIRD',
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
                    'Correction command fixture',
                    30,
                    8,
                    1000,
                    'UAH',
                    '2026-01-10'::date,
                    '2026-02-08'::date,
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
                    'Correction command fixture',
                    30,
                    8,
                    1000,
                    'UAH',
                    '2026-01-15'::date,
                    '2026-02-13'::date,
                    @recorded_at,
                    @account_id,
                    'active',
                    'normal',
                    null,
                    null),
                (
                    @third_membership_id,
                    @third_client_id,
                    @membership_type_id,
                    'Correction command fixture',
                    30,
                    8,
                    1000,
                    'UAH',
                    '2026-02-03'::date,
                    '2026-03-04'::date,
                    @recorded_at,
                    @account_id,
                    'active',
                    'normal',
                    null,
                    null);

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
                @original_start_date,
                @original_end_date,
                'weather_closure',
                'Severe weather',
                @recorded_at,
                @account_id,
                @session_id,
                'active');

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
                    @second_application_id,
                    @period_id,
                    @second_membership_id,
                    @second_client_id,
                    @original_start_date,
                    @original_end_date,
                    @previewed_at,
                    @recorded_at,
                    'active'),
                (
                    @first_application_id,
                    @period_id,
                    @first_membership_id,
                    @first_client_id,
                    @original_start_date,
                    @original_end_date,
                    @previewed_at,
                    @recorded_at,
                    'active');
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("first_client_id", FirstClientId);
        command.Parameters.AddWithValue("second_client_id", SecondClientId);
        command.Parameters.AddWithValue("third_client_id", ThirdClientId);
        command.Parameters.AddWithValue("first_membership_id", FirstMembershipId);
        command.Parameters.AddWithValue("second_membership_id", SecondMembershipId);
        command.Parameters.AddWithValue("third_membership_id", ThirdMembershipId);
        command.Parameters.AddWithValue("first_application_id", FirstApplicationId);
        command.Parameters.AddWithValue("second_application_id", SecondApplicationId);
        command.Parameters.AddWithValue("period_id", periodId);
        command.Parameters.AddWithValue(
            "original_start_date",
            OriginalPeriod.StartDate);
        command.Parameters.AddWithValue("original_end_date", OriginalPeriod.EndDate);
        command.Parameters.AddWithValue("previewed_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(12));
        Assert.Equal(11, await command.ExecuteNonQueryAsync());
        dbContext.ChangeTracker.Clear();

        return new CorrectionFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Owner laptop"),
            periodId);
    }

    private static async Task<PeriodRow[]> ReadPeriodsAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                id,
                start_date,
                end_date,
                reason_code,
                reason_comment,
                created_at,
                created_by_account_id,
                session_id,
                status
            from bodylife.non_working_periods
            order by id
            """;
        var rows = new List<PeriodRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new PeriodRow(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetGuid(6),
                reader.GetGuid(7),
                reader.GetString(8)));
        }

        return rows.ToArray();
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
                client_id,
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
                reader.GetGuid(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetString(7)));
        }

        return rows.ToArray();
    }

    private static async Task<CancellationRow[]> ReadCancellationsAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                id,
                non_working_period_id,
                reason,
                recorded_at,
                recorded_by_account_id,
                session_id
            from bodylife.non_working_period_cancellations
            order by id
            """;
        var rows = new List<CancellationRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new CancellationRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetGuid(4),
                reader.GetGuid(5)));
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
                extension_days,
                effective_end_date
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
                reader.GetFieldValue<DateOnly>(2)));
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
            select membership_id, extension_date, source_id, is_active
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
                reader.GetGuid(2),
                reader.GetBoolean(3)));
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
                after_summary::text
            from bodylife.business_audit_entries
            where id = @audit_entry_id
            """;
        command.Parameters.AddWithValue("audit_entry_id", auditEntryId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var row = new AuditRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetString(9));
        Assert.False(await reader.ReadAsync());
        return row;
    }

    private static async Task<IdempotencyRow> ReadIdempotencyAsync(
        PostgreSqlTestDatabase database)
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
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var row = new IdempotencyRow(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
        Assert.False(await reader.ReadAsync());
        return row;
    }

    private static async Task AssertAuditAndIdempotencyAsync(
        PostgreSqlTestDatabase database,
        CommandResult result,
        Guid originalPeriodId,
        string expectedAction,
        string expectedMode,
        int oldAffectedCount,
        int newAffectedCount,
        int affectedUnionCount)
    {
        var audit = await ReadAuditAsync(
            database,
            result.AuditEntryId!.Value.Value);
        Assert.Equal(expectedAction, audit.ActionType);
        Assert.Equal(NonWorkingDayAuditActions.PeriodEntityType, audit.EntityType);
        Assert.Equal(originalPeriodId, audit.EntityId);
        Assert.Equal("owner", audit.ActorAccountType);
        Assert.Equal("owner", audit.ActorRole);
        Assert.Equal(TestNow, audit.OccurredAt);
        Assert.Equal(TestNow, audit.RecordedAt);
        Assert.Equal("Owner corrected the closure schedule", audit.Reason);
        Assert.Equal(
            "Confirmed against the correction preview",
            audit.Comment);
        using (var after = JsonDocument.Parse(audit.AfterSummaryJson))
        {
            Assert.Equal(
                expectedMode,
                after.RootElement.GetProperty("mode").GetString());
            Assert.Equal(
                oldAffectedCount,
                after.RootElement.GetProperty("oldAffectedCount").GetInt32());
            Assert.Equal(
                newAffectedCount,
                after.RootElement.GetProperty("newAffectedCount").GetInt32());
            Assert.Equal(
                affectedUnionCount,
                after.RootElement.GetProperty("affectedUnionCount").GetInt32());
            var recalculation = after.RootElement.GetProperty("recalculation");
            Assert.Equal(
                affectedUnionCount,
                recalculation.GetProperty("requestedCount").GetInt32());
            Assert.Equal(
                affectedUnionCount,
                recalculation.GetProperty("succeededCount").GetInt32());
        }

        var idempotency = await ReadIdempotencyAsync(database);
        Assert.Equal("CorrectNonWorkingDay", idempotency.CommandName);
        Assert.Equal(result.PrimaryEntityId!.Value.Value, idempotency.PrimaryEntityId);
        Assert.Equal(originalPeriodId, idempotency.RereadTargetId);
        Assert.Equal(result.AuditEntryId.Value.Value, idempotency.AuditEntryId);
        Assert.Equal("succeeded", idempotency.Status);
        Assert.False(string.IsNullOrWhiteSpace(idempotency.ResultFingerprint));
    }

    private static async Task AssertOriginalSourceUnchangedAsync(
        PostgreSqlTestDatabase database)
    {
        var period = Assert.Single(await ReadPeriodsAsync(database));
        Assert.Equal("active", period.Status);
        var applications = await ReadApplicationsAsync(database, period.Id);
        Assert.Equal(2, applications.Length);
        Assert.All(
            applications,
            application => Assert.Equal("active", application.Status));
        Assert.Equal(
            0,
            await CountRowsAsync(database, "non_working_period_cancellations"));
        Assert.Equal(
            0,
            await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(
            0,
            await CountRowsAsync(database, "membership_extension_days"));
        Assert.Equal(
            0,
            await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(
            0,
            await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static async Task SetMembershipStatusAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        string status)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.issued_memberships
            set status = @status
            where id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{tableName}");
    }

    private static async Task<NonWorkingDayCanonicalCorrection>
        ReadCanonicalOutcomeAsync(
            BodyLifeDbContext dbContext,
            CorrectionFixture fixture,
            CommandResult commandResult,
            TimeProvider timeProvider)
    {
        dbContext.ChangeTracker.Clear();
        var queryResult = await new GetNonWorkingDayCorrectionOutcomeQueryHandler(
                dbContext,
                timeProvider)
            .ExecuteAsync(
                new GetNonWorkingDayCorrectionOutcomeQuery(
                    fixture.Actor,
                    fixture.PeriodId,
                    commandResult.AuditEntryId!.Value.Value),
                CancellationToken.None);
        Assert.Equal(
            GetNonWorkingDayCorrectionOutcomeStatus.Success,
            queryResult.Status);
        Assert.NotNull(queryResult.Correction);
        Assert.Equal(
            commandResult.PrimaryEntityId!.Value.Value,
            queryResult.Correction.PrimaryEntityId);
        Assert.Equal(
            commandResult.AuditEntryId.Value.Value,
            queryResult.Correction.AuditEntryId);
        Assert.Equal(
            "Owner corrected the closure schedule",
            queryResult.Correction.CorrectionReason);
        Assert.Equal(
            "Confirmed against the correction preview",
            queryResult.Correction.CorrectionComment);
        Assert.Equal(EntryOrigin.Normal, queryResult.Correction.EntryOrigin);
        return queryResult.Correction;
    }

    private static void AssertSuccess(
        CommandResult result,
        string primaryEntityType,
        Guid originalPeriodId,
        params Guid[] membershipIds)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Equal(primaryEntityType, result.PrimaryEntityId?.Type);
        Assert.Equal(
            new EntityId(
                CorrectNonWorkingDayCommand.CanonicalRereadEntityType,
                originalPeriodId),
            result.RereadTargetId);
        Assert.Equal(
            new EntityId(CorrectNonWorkingDayCommand.PeriodEntityType, originalPeriodId),
            result.RelatedEntityIds[0]);
        Assert.Equal(
            membershipIds.Select(membershipId => new EntityId(
                CorrectNonWorkingDayCommand.MembershipEntityType,
                membershipId)),
            result.RelatedEntityIds.Skip(1));
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

    private static void AssertEquivalentSuccess(
        CommandResult expected,
        CommandResult actual)
    {
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.PrimaryEntityId, actual.PrimaryEntityId);
        Assert.Equal(expected.RereadTargetId, actual.RereadTargetId);
        Assert.Equal(expected.RelatedEntityIds, actual.RelatedEntityIds);
        Assert.Equal(expected.Warnings, actual.Warnings);
        Assert.Equal(expected.AuditEntryId, actual.AuditEntryId);
        Assert.Equal(expected.ChangedAfterClose, actual.ChangedAfterClose);
        Assert.Equal(expected.Errors, actual.Errors);
    }

    private static string SigningKey()
    {
        return Convert.ToBase64String(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
    }

    private sealed record CorrectionFixture(ActorContext Actor, Guid PeriodId);

    private sealed record MembershipComponents(
        MembershipStateCacheRebuilder Rebuilder,
        MembershipNonWorkingDayReplacementImpactPreparer ReplacementPreparer);

    private sealed record PeriodRow(
        Guid Id,
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
        Guid ClientId,
        DateOnly AppliedStartDate,
        DateOnly AppliedEndDate,
        DateTimeOffset PreviewedAt,
        DateTimeOffset ConfirmedAt,
        string Status);

    private sealed record CancellationRow(
        Guid Id,
        Guid NonWorkingPeriodId,
        string Reason,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId);

    private sealed record StateRow(
        Guid MembershipId,
        int ExtensionDays,
        DateOnly EffectiveEndDate);

    private sealed record ExtensionRow(
        Guid MembershipId,
        DateOnly ExtensionDate,
        Guid SourceId,
        bool IsActive);

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
        string AfterSummaryJson);

    private sealed record IdempotencyRow(
        string CommandName,
        Guid? PrimaryEntityId,
        Guid? RereadTargetId,
        Guid? AuditEntryId,
        string Status,
        string? ResultFingerprint);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

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
            var completed = await inner.RecalculateAsync(
                membershipId,
                cancellationToken);
            Assert.True(completed.Succeeded);
            return new MembershipStateRecalculationResult(
                membershipId,
                MembershipStateRecalculationStatus.InvalidSourceState);
        }
    }
}
