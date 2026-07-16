using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlCancelFreezeCommandTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        16,
        12,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset FreezeOccurredAt = new(
        2026,
        7,
        15,
        10,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset CancellationOccurredAt = new(
        2026,
        7,
        16,
        11,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly MembershipStartDate = new(2026, 7, 1);
    private static readonly DateOnly MembershipBaseEndDate = new(2026, 7, 30);
    private static readonly DateRange DefaultFreezeRange = new(
        new DateOnly(2026, 7, 10),
        new DateOnly(2026, 7, 12));

    [PostgreSqlFact]
    public async Task SuccessfulCancellationCommitsHistoryStateAuditAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureWithFreezeAsync(database, dbContext);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(fixture, "cancel-freeze-success"),
            CancellationToken.None);

        AssertSuccessfulResult(result, fixture);
        var cancellationId = result.PrimaryEntityId!.Value.Value;
        var cancellation = await ReadCancellationAsync(database, cancellationId);
        Assert.Equal(fixture.FreezeId, cancellation.FreezeId);
        Assert.Equal("Entered by mistake", cancellation.Reason);
        Assert.Equal(CancellationOccurredAt, cancellation.OccurredAt);
        Assert.Equal(TestNow, cancellation.RecordedAt);
        Assert.Equal(fixture.Actor.AccountId.Value, cancellation.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId.Value, cancellation.SessionId);
        Assert.Equal("normal", cancellation.EntryOrigin);
        Assert.Null(cancellation.EntryBatchId);
        Assert.Equal("canceled", await ReadFreezeStatusAsync(database, fixture.FreezeId));

        var state = await ReadMembershipStateAsync(database, fixture.MembershipId);
        Assert.Equal(0, state.ExtensionDays);
        Assert.Equal(MembershipBaseEndDate, state.EffectiveEndDate);
        Assert.Equal(TestNow, state.RecalculatedAt);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            state.RecalculationVersion);

        var extensionRows = await ReadExtensionRowsAsync(
            database,
            fixture.MembershipId);
        Assert.Equal(3, extensionRows.Count);
        Assert.All(extensionRows, row =>
        {
            Assert.Equal("freeze", row.SourceType);
            Assert.Equal(fixture.FreezeId, row.SourceId);
            Assert.False(row.IsActive);
            Assert.Equal(TestNow, row.RecalculatedAt);
        });

        await using (var projectionTransaction =
            await dbContext.Database.BeginTransactionAsync())
        {
            var sources = await new MembershipVisitFreezeSourceReader(dbContext)
                .GetForVisitAsync(
                    fixture.MembershipId,
                    DefaultFreezeRange.StartDate);
            Assert.False(Assert.Single(sources).IsActive);
            await projectionTransaction.RollbackAsync();
        }

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(FreezeAuditActions.Canceled, audit.ActionType);
        Assert.Equal(FreezeAuditActions.FreezeEntityType, audit.EntityType);
        Assert.Equal(fixture.FreezeId, audit.EntityId);
        Assert.Equal(fixture.Actor.AccountId.Value, audit.ActorAccountId);
        Assert.Equal("owner", audit.ActorAccountType);
        Assert.Equal("owner", audit.ActorRole);
        Assert.Equal(fixture.Actor.SessionId.Value, audit.SessionId);
        Assert.Equal("Reception tablet", audit.DeviceLabel);
        Assert.Equal(CancellationOccurredAt, audit.OccurredAt);
        Assert.Equal(TestNow, audit.RecordedAt);
        Assert.Equal("Entered by mistake", audit.Reason);
        Assert.Equal("Cancel front desk Freeze", audit.Comment);
        Assert.Equal("correlation-cancel-freeze-success", audit.RequestCorrelationId);
        Assert.Equal("normal", audit.EntryOrigin);
        Assert.Equal("cancel-freeze-success", audit.IdempotencyKey);
        Assert.False(audit.ChangedAfterClose);
        using (var before = JsonDocument.Parse(audit.BeforeSummary))
        {
            Assert.Equal(
                "active",
                before.RootElement.GetProperty("freeze").GetProperty("status").GetString());
            Assert.Equal(
                3,
                before.RootElement
                    .GetProperty("membershipState")
                    .GetProperty("extensionDays")
                    .GetInt32());
        }

        using (var after = JsonDocument.Parse(audit.AfterSummary))
        {
            var cancellationSummary = after.RootElement.GetProperty("cancellation");
            Assert.Equal(
                cancellationId,
                cancellationSummary.GetProperty("cancellationId").GetGuid());
            Assert.False(
                cancellationSummary.GetProperty("changedAfterClose").GetBoolean());
            Assert.Equal(
                "canceled",
                after.RootElement.GetProperty("freeze").GetProperty("status").GetString());
            Assert.Equal(
                0,
                after.RootElement
                    .GetProperty("membershipState")
                    .GetProperty("extensionDays")
                    .GetInt32());
        }

        var idempotency = await ReadIdempotencyAsync(
            database,
            "cancel-freeze-success");
        Assert.Equal("CancelFreeze", idempotency.CommandName);
        Assert.Equal(fixture.Actor.AccountId.Value, idempotency.AccountId);
        Assert.Equal(fixture.Actor.SessionId.Value, idempotency.SessionId);
        Assert.Equal(cancellationId, idempotency.PrimaryEntityId);
        Assert.Equal(fixture.ClientId, idempotency.RereadTargetId);
        Assert.Equal(result.AuditEntryId.Value.Value, idempotency.AuditEntryId);
        Assert.Equal("succeeded", idempotency.Status);
        Assert.False(string.IsNullOrWhiteSpace(idempotency.ResultFingerprint));
        Assert.Equal(1L, await CountRowsAsync(database, "freeze_cancellations"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task CancelingOverlappedFreezeKeepsRemainingActiveUnion()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureWithFreezeAsync(database, dbContext);
        var coveringFreezeId = await InsertFreezeAsync(
            dbContext,
            fixture,
            new DateRange(
                DefaultFreezeRange.StartDate,
                new DateOnly(2026, 7, 14)),
            "Covering recovery range");
        await RebuildMembershipStateAsync(dbContext, fixture.MembershipId);

        var before = await ReadMembershipStateAsync(database, fixture.MembershipId);
        Assert.Equal(5, before.ExtensionDays);
        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(fixture, "cancel-overlap"),
            CancellationToken.None);

        AssertSuccessfulResult(result, fixture);
        var after = await ReadMembershipStateAsync(database, fixture.MembershipId);
        Assert.Equal(5, after.ExtensionDays);
        Assert.Equal(MembershipBaseEndDate.AddDays(5), after.EffectiveEndDate);
        var rows = await ReadExtensionRowsAsync(database, fixture.MembershipId);
        Assert.Equal(8, rows.Count);
        Assert.All(
            rows.Where(row => row.SourceId == fixture.FreezeId),
            row => Assert.False(row.IsActive));
        Assert.All(
            rows.Where(row => row.SourceId == coveringFreezeId),
            row => Assert.True(row.IsActive));
        Assert.Equal(
            5L,
            await CountDistinctActiveExtensionDatesAsync(
                database,
                fixture.MembershipId));
    }

    [PostgreSqlFact]
    public async Task SharedAdminPaperFallbackPreservesCancellationMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureWithFreezeAsync(database, dbContext);
        var sharedAdmin = await InsertAdminActorAsync(
            database,
            AccountKind.SharedReceptionAdmin,
            "Shared Reception");
        var entryBatchId = Guid.NewGuid();
        var occurredAt = CancellationOccurredAt.AddDays(-2);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                fixture,
                "paper-cancel-freeze",
                actor: sharedAdmin,
                origin: EntryOrigin.PaperFallback,
                occurredAt: occurredAt,
                reason: "Recovered cancellation",
                entryBatchId: entryBatchId),
            CancellationToken.None);

        AssertSuccessfulResult(result, fixture);
        var cancellation = await ReadCancellationAsync(
            database,
            result.PrimaryEntityId!.Value.Value);
        Assert.Equal("Recovered cancellation", cancellation.Reason);
        Assert.Equal(occurredAt, cancellation.OccurredAt);
        Assert.Equal("paper_fallback", cancellation.EntryOrigin);
        Assert.Equal(entryBatchId, cancellation.EntryBatchId);
        Assert.Equal(sharedAdmin.AccountId.Value, cancellation.RecordedByAccountId);
        Assert.Equal(sharedAdmin.SessionId.Value, cancellation.SessionId);
        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal("shared_reception_admin", audit.ActorAccountType);
        Assert.Equal("admin", audit.ActorRole);
        Assert.Equal("paper_fallback", audit.EntryOrigin);
        Assert.Equal("Recovered cancellation", audit.Reason);
    }

    [PostgreSqlFact]
    public async Task ReconciledDayRequiresOwnerAndMarksOwnerSuccess()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureWithFreezeAsync(database, dbContext);
        var namedAdmin = await InsertAdminActorAsync(
            database,
            AccountKind.NamedAdmin,
            "Named Admin");
        var dayStatus = new StubFreezeDayReconciliationStatusProvider(
            FreezeDayReconciliationStatus.Reconciled);
        var handler = CreateHandler(dbContext, dayStatusProvider: dayStatus);

        var denied = await handler.ExecuteAsync(
            CreateCommand(fixture, "admin-reconciled", actor: namedAdmin),
            CancellationToken.None);
        var owner = await handler.ExecuteAsync(
            CreateCommand(fixture, "owner-reconciled"),
            CancellationToken.None);

        AssertError(denied, CommandErrorCode.DayClosedRequiresOwner, "freezeId");
        AssertSuccessfulResult(owner, fixture, changedAfterClose: true);
        Assert.All(
            dayStatus.RequestedDates,
            date => Assert.Equal(
                DateOnly.FromDateTime(FreezeOccurredAt.UtcDateTime),
                date));
        var audit = await ReadAuditAsync(database, owner.AuditEntryId!.Value.Value);
        Assert.True(audit.ChangedAfterClose);
        using var after = JsonDocument.Parse(audit.AfterSummary);
        Assert.True(
            after.RootElement
                .GetProperty("cancellation")
                .GetProperty("changedAfterClose")
                .GetBoolean());
        Assert.Equal(1L, await CountRowsAsync(database, "freeze_cancellations"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InvalidInputsAndEndedSessionFailWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureWithFreezeAsync(database, dbContext);
        var handler = CreateHandler(dbContext);
        var valid = CreateCommand(fixture, "valid-cancellation");

        var emptyFreeze = await handler.ExecuteAsync(
            valid with { FreezeId = Guid.Empty },
            CancellationToken.None);
        var missingOccurredAt = await handler.ExecuteAsync(
            valid with { Envelope = valid.Envelope with { OccurredAt = null } },
            CancellationToken.None);
        var missingKey = await handler.ExecuteAsync(
            valid with { Envelope = valid.Envelope with { IdempotencyKey = "  " } },
            CancellationToken.None);
        var missingReason = await handler.ExecuteAsync(
            valid with { Envelope = valid.Envelope with { Reason = "  " } },
            CancellationToken.None);
        var normalWithBatch = await handler.ExecuteAsync(
            valid with { EntryBatchId = Guid.NewGuid() },
            CancellationToken.None);
        var invalidActorShape = await handler.ExecuteAsync(
            valid with
            {
                Envelope = valid.Envelope with
                {
                    Actor = fixture.Actor with { AccountKind = AccountKind.NamedAdmin },
                },
            },
            CancellationToken.None);
        await EndSessionAsync(database, fixture.Actor.SessionId.Value);
        var endedSession = await handler.ExecuteAsync(valid, CancellationToken.None);

        AssertError(emptyFreeze, CommandErrorCode.ValidationFailed, "freezeId");
        AssertError(missingOccurredAt, CommandErrorCode.ValidationFailed, "occurredAt");
        AssertError(missingKey, CommandErrorCode.ValidationFailed, "idempotencyKey");
        AssertError(missingReason, CommandErrorCode.ReasonRequired, "reason");
        AssertError(normalWithBatch, CommandErrorCode.ValidationFailed, "entryBatchId");
        AssertError(invalidActorShape, CommandErrorCode.PermissionDenied);
        AssertError(endedSession, CommandErrorCode.PermissionDenied);
        await AssertNoCancellationMutationAsync(database, fixture, expectedExtensionDays: 3);
    }

    [PostgreSqlFact]
    public async Task MissingAndAlreadyCanceledSourcesFailWithoutDuplicateHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureWithFreezeAsync(database, dbContext);
        var handler = CreateHandler(dbContext);

        var missing = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "missing-freeze",
                freezeId: Guid.NewGuid()),
            CancellationToken.None);
        var first = await handler.ExecuteAsync(
            CreateCommand(fixture, "first-cancel"),
            CancellationToken.None);
        var alreadyCanceled = await handler.ExecuteAsync(
            CreateCommand(fixture, "second-cancel"),
            CancellationToken.None);

        AssertError(missing, CommandErrorCode.NotFound, "freezeId");
        AssertSuccessfulResult(first, fixture);
        AssertError(alreadyCanceled, CommandErrorCode.AlreadyCanceled, "freezeId");
        Assert.Equal(1L, await CountRowsAsync(database, "freeze_cancellations"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task IdempotentReplayReturnsOriginalAndChangedPayloadIsDuplicate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureWithFreezeAsync(database, dbContext);
        var handler = CreateHandler(dbContext);
        var command = CreateCommand(fixture, "cancel-freeze-replay");

        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var changed = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with { Reason = "Different reason" },
            },
            CancellationToken.None);

        AssertSuccessfulResult(first, fixture);
        AssertSuccessfulResult(replay, fixture);
        Assert.Equal(first.PrimaryEntityId, replay.PrimaryEntityId);
        Assert.Equal(first.RereadTargetId, replay.RereadTargetId);
        Assert.Equal(first.RelatedEntityIds, replay.RelatedEntityIds);
        Assert.Equal(first.AuditEntryId, replay.AuditEntryId);
        AssertError(changed, CommandErrorCode.DuplicateSubmission, "idempotencyKey");
        Assert.Equal(1L, await CountRowsAsync(database, "freeze_cancellations"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentSameKeySerializesToOneCompleteWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        FreezeFixture fixture;
        await using (var setupContext = database.CreateDbContext())
        {
            await setupContext.Database.MigrateAsync();
            fixture = await SeedFixtureWithFreezeAsync(database, setupContext);
        }

        var command = CreateCommand(fixture, "concurrent-cancel-freeze");
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(command, CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(command, CancellationToken.None));

        Assert.All(results, result => AssertSuccessfulResult(result, fixture));
        Assert.Equal(results[0].PrimaryEntityId, results[1].PrimaryEntityId);
        Assert.Equal(results[0].AuditEntryId, results[1].AuditEntryId);
        Assert.Equal(1L, await CountRowsAsync(database, "freeze_cancellations"));
        Assert.Equal(3L, await CountRowsAsync(database, "membership_extension_days"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task RecalculationFailureRollsBackSourceAndCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureWithFreezeAsync(database, dbContext);
        var realRecalculator = CreateRecalculator(dbContext);
        var failingRecalculator = new FailOnSecondMembershipStateRecalculator(
            realRecalculator);

        var result = await CreateHandler(
                dbContext,
                membershipStateRecalculator: failingRecalculator)
            .ExecuteAsync(
                CreateCommand(fixture, "cancel-recalculation-failure"),
                CancellationToken.None);

        AssertError(result, CommandErrorCode.RecalculationFailed);
        Assert.Equal(2, failingRecalculator.CallCount);
        await AssertNoCancellationMutationAsync(database, fixture, expectedExtensionDays: 3);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackEntireCancellationWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureWithFreezeAsync(database, dbContext);
        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_freeze_canceled_audit
            check (action_type <> 'freeze.canceled')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext).ExecuteAsync(
                CreateCommand(fixture, "cancel-audit-failure"),
                CancellationToken.None));

        await AssertNoCancellationMutationAsync(database, fixture, expectedExtensionDays: 3);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task CompetingMembershipLockReturnsConcurrencyConflict()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureWithFreezeAsync(database, dbContext);
        await using var lockConnection = new NpgsqlConnection(database.ConnectionString);
        await lockConnection.OpenAsync();
        await using var lockTransaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.Transaction = lockTransaction;
            lockCommand.CommandText =
                "select id from bodylife.issued_memberships where id = @id for update";
            lockCommand.Parameters.AddWithValue("id", fixture.MembershipId);
            Assert.Equal(fixture.MembershipId, await lockCommand.ExecuteScalarAsync());
        }

        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("set lock_timeout = '250ms'");
        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(fixture, "cancel-membership-lock-conflict"),
            CancellationToken.None);

        await lockTransaction.RollbackAsync();
        AssertError(result, CommandErrorCode.ConcurrencyConflict);
        await AssertNoCancellationMutationAsync(database, fixture, expectedExtensionDays: 3);
    }

    [PostgreSqlFact]
    public async Task SourcePreparationRequiresTransactionAndHoldsMembershipAndFreezeLocks()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureWithFreezeAsync(database, dbContext);
        var preparer = new CancelFreezeSourcePreparer(dbContext);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            preparer.PrepareAsync(Guid.Empty));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preparer.PrepareAsync(fixture.FreezeId));

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var preparation = await preparer.PrepareAsync(fixture.FreezeId);

        Assert.True(preparation.IsPrepared);
        Assert.NotNull(preparation.Source);
        Assert.Equal(fixture.MembershipId, preparation.Source.MembershipId);
        Assert.Equal(FreezeCancellationSourceStatus.Active, preparation.Source.Status);
        await AssertRowLockUnavailableAsync(
            database,
            "issued_memberships",
            fixture.MembershipId);
        await AssertRowLockUnavailableAsync(database, "freezes", fixture.FreezeId);
        await transaction.RollbackAsync();
        await AssertNoCancellationMutationAsync(database, fixture, expectedExtensionDays: 3);
    }

    [Fact]
    public void PersistenceRegistrationResolvesCancelFreezeWorkflow()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] =
                    "Host=localhost;Database=bodylife;Username=bodylife;Password=test",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddBodyLifePersistence(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<CancelFreezeCommandHandler>(
            scope.ServiceProvider.GetRequiredService<
                IBodyLifeCommandHandler<CancelFreezeCommand>>());
        Assert.IsType<CancelFreezeSourcePreparer>(
            scope.ServiceProvider.GetRequiredService<CancelFreezeSourcePreparer>());
        Assert.Equal(
            "OpenFreezeDayReconciliationStatusProvider",
            scope.ServiceProvider.GetRequiredService<
                IFreezeDayReconciliationStatusProvider>().GetType().Name);
    }

    private static CancelFreezeCommandHandler CreateHandler(
        BodyLifeDbContext dbContext,
        IMembershipStateRecalculator? membershipStateRecalculator = null,
        IFreezeDayReconciliationStatusProvider? dayStatusProvider = null)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        return new CancelFreezeCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new CancelFreezeSourcePreparer(dbContext),
            membershipStateRecalculator ?? CreateRecalculator(dbContext),
            new GetMembershipStateQueryHandler(dbContext, timeProvider),
            dayStatusProvider ?? new StubFreezeDayReconciliationStatusProvider(),
            timeProvider);
    }

    private static IMembershipStateRecalculator CreateRecalculator(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        var sourceReader = new MembershipFreezeExtensionSourceReader(dbContext);
        return new MembershipStateRecalculator(
            new MembershipStateCacheRebuilder(
                dbContext,
                timeProvider,
                [sourceReader]));
    }

    private static CancelFreezeCommand CreateCommand(
        FreezeFixture fixture,
        string idempotencyKey,
        ActorContext? actor = null,
        EntryOrigin origin = EntryOrigin.Normal,
        DateTimeOffset? occurredAt = null,
        string? reason = "Entered by mistake",
        Guid? entryBatchId = null,
        Guid? freezeId = null)
    {
        return new CancelFreezeCommand(
            new CommandEnvelope(
                actor ?? fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                origin,
                occurredAt ?? CancellationOccurredAt,
                idempotencyKey,
                reason,
                "  Cancel front desk Freeze  "),
            freezeId ?? fixture.FreezeId,
            entryBatchId);
    }

    private static async Task<FreezeFixture> SeedFixtureWithFreezeAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var fixture = await SeedFixtureAsync(database, dbContext);
        var freezeId = await InsertFreezeAsync(
            dbContext,
            fixture,
            DefaultFreezeRange,
            "Medical pause");
        fixture = fixture with { FreezeId = freezeId };
        await RebuildMembershipStateAsync(dbContext, fixture.MembershipId);
        return fixture;
    }

    private static async Task<FreezeFixture> SeedFixtureAsync(
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
            values (
                @client_id,
                'Cancel Freeze',
                'Client',
                null,
                'CANCEL FREEZE CLIENT',
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
                'Cancel Freeze fixture',
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
                'Cancel Freeze fixture',
                30,
                8,
                1000,
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
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-30));
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            MembershipStartDate);
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            MembershipBaseEndDate);
        command.Parameters.AddWithValue("issued_at", TestNow.AddDays(-15));
        Assert.Equal(4, await command.ExecuteNonQueryAsync());
        dbContext.ChangeTracker.Clear();

        return new FreezeFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "  Reception tablet  "),
            clientId,
            membershipId,
            Guid.Empty);
    }

    private static async Task<Guid> InsertFreezeAsync(
        BodyLifeDbContext dbContext,
        FreezeFixture fixture,
        DateRange range,
        string reason)
    {
        var freezeId = Guid.NewGuid();
        var insertedRows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
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
                {freezeId},
                {fixture.ClientId},
                {fixture.MembershipId},
                {range.StartDate},
                {range.EndDate},
                {reason},
                {FreezeOccurredAt},
                {TestNow.AddDays(-1)},
                {fixture.Actor.AccountId.Value},
                {fixture.Actor.SessionId.Value},
                'normal',
                null,
                'active')
            """);
        Assert.Equal(1, insertedRows);
        dbContext.ChangeTracker.Clear();
        return freezeId;
    }

    private static async Task RebuildMembershipStateAsync(
        BodyLifeDbContext dbContext,
        Guid membershipId)
    {
        var reader = new MembershipFreezeExtensionSourceReader(dbContext);
        var result = await new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(TestNow),
                [reader])
            .RebuildAsync(membershipId);
        Assert.True(result.Succeeded);
        dbContext.ChangeTracker.Clear();
    }

    private static async Task<ActorContext> InsertAdminActorAsync(
        PostgreSqlTestDatabase database,
        AccountKind accountKind,
        string displayName)
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var accountType = accountKind switch
        {
            AccountKind.NamedAdmin => "named_admin",
            AccountKind.SharedReceptionAdmin => "shared_reception_admin",
            _ => throw new ArgumentOutOfRangeException(nameof(accountKind)),
        };
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
                'admin',
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
                'Reception tablet',
                @started_at,
                @expires_at,
                null,
                @last_seen_at)
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("display_name", displayName);
        command.Parameters.AddWithValue("account_type", accountType);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-1));
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        return new ActorContext(
            new AccountId(accountId),
            ActorRole.Admin,
            accountKind,
            new SessionId(sessionId),
            "Reception tablet");
    }

    private static async Task EndSessionAsync(
        PostgreSqlTestDatabase database,
        Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "update bodylife.sessions set ended_at = @ended_at where id = @id";
        command.Parameters.AddWithValue("ended_at", TestNow);
        command.Parameters.AddWithValue("id", sessionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<FreezeCancellationRow> ReadCancellationAsync(
        PostgreSqlTestDatabase database,
        Guid cancellationId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select freeze_id,
                   reason,
                   occurred_at,
                   recorded_at,
                   recorded_by_account_id,
                   session_id,
                   entry_origin,
                   entry_batch_id
            from bodylife.freeze_cancellations
            where id = @id
            """;
        command.Parameters.AddWithValue("id", cancellationId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new FreezeCancellationRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetGuid(4),
            reader.GetGuid(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7));
    }

    private static async Task<string> ReadFreezeStatusAsync(
        PostgreSqlTestDatabase database,
        Guid freezeId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select status from bodylife.freezes where id = @id";
        command.Parameters.AddWithValue("id", freezeId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<MembershipStateRow> ReadMembershipStateAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select extension_days,
                   effective_end_date,
                   recalculated_at,
                   recalculation_version
            from bodylife.membership_state_cache
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new MembershipStateRow(
            reader.GetInt32(0),
            reader.GetFieldValue<DateOnly>(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetInt32(3));
    }

    private static async Task<IReadOnlyList<ExtensionRow>> ReadExtensionRowsAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select extension_date,
                   source_type,
                   source_id,
                   is_active,
                   recalculated_at
            from bodylife.membership_extension_days
            where membership_id = @membership_id
            order by extension_date, source_type, source_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ExtensionRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new ExtensionRow(
                reader.GetFieldValue<DateOnly>(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetBoolean(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return rows;
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
            select action_type,
                   entity_type,
                   entity_id,
                   actor_account_id,
                   actor_account_type,
                   actor_role,
                   session_id,
                   device_label,
                   occurred_at,
                   recorded_at,
                   reason,
                   comment,
                   before_summary::text,
                   after_summary::text,
                   request_correlation_id,
                   entry_origin,
                   idempotency_key,
                   changed_after_close
            from bodylife.business_audit_entries
            where id = @id
            """;
        command.Parameters.AddWithValue("id", auditEntryId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new AuditRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.GetBoolean(17));
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
            select command_name,
                   account_id,
                   session_id,
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
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetGuid(5),
            reader.GetString(6),
            reader.GetString(7));
    }

    private static async Task<long> CountDistinctActiveExtensionDatesAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select count(distinct extension_date)
            from bodylife.membership_extension_days
            where membership_id = @membership_id
                and is_active
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertRowLockUnavailableAsync(
        PostgreSqlTestDatabase database,
        string tableName,
        Guid id)
    {
        var commandText = tableName switch
        {
            "issued_memberships" =>
                "select id from bodylife.issued_memberships where id = @id for update",
            "freezes" => "select id from bodylife.freezes where id = @id for update",
            _ => throw new ArgumentOutOfRangeException(nameof(tableName)),
        };
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "set local lock_timeout = '250ms'; " + commandText;
        command.Parameters.AddWithValue("id", id);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
        await transaction.RollbackAsync();
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

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{tableName}");
    }

    private static async Task AssertNoCancellationMutationAsync(
        PostgreSqlTestDatabase database,
        FreezeFixture fixture,
        int expectedExtensionDays)
    {
        Assert.Equal("active", await ReadFreezeStatusAsync(database, fixture.FreezeId));
        Assert.Equal(0L, await CountRowsAsync(database, "freeze_cancellations"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
        var state = await ReadMembershipStateAsync(database, fixture.MembershipId);
        Assert.Equal(expectedExtensionDays, state.ExtensionDays);
        Assert.All(
            await ReadExtensionRowsAsync(database, fixture.MembershipId),
            row => Assert.True(row.IsActive));
    }

    private static void AssertSuccessfulResult(
        CommandResult result,
        FreezeFixture fixture,
        bool changedAfterClose = false)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.True(result.PrimaryEntityId.HasValue);
        Assert.Equal("freeze_cancellation", result.PrimaryEntityId.Value.Type);
        Assert.NotEqual(Guid.Empty, result.PrimaryEntityId.Value.Value);
        Assert.Equal(new EntityId("client", fixture.ClientId), result.RereadTargetId);
        Assert.Equal(
            [new EntityId("freeze", fixture.FreezeId)],
            result.RelatedEntityIds);
        Assert.True(result.AuditEntryId.HasValue);
        Assert.Empty(result.Warnings);
        Assert.Equal(changedAfterClose, result.ChangedAfterClose);
        Assert.Empty(result.Errors);
    }

    private static void AssertError(
        CommandResult result,
        CommandErrorCode code,
        string? field = null)
    {
        Assert.Equal(CommandStatus.Error, result.Status);
        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
        Assert.Null(result.AuditEntryId);
        Assert.Empty(result.RelatedEntityIds);
        Assert.Empty(result.Warnings);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(field, error.Field);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubFreezeDayReconciliationStatusProvider(
        FreezeDayReconciliationStatus status = FreezeDayReconciliationStatus.Open)
        : IFreezeDayReconciliationStatusProvider
    {
        public List<DateOnly> RequestedDates { get; } = [];

        public Task<FreezeDayReconciliationStatus> GetStatusAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
        {
            RequestedDates.Add(businessDate);
            return Task.FromResult(status);
        }
    }

    private sealed class FailOnSecondMembershipStateRecalculator(
        IMembershipStateRecalculator inner)
        : IMembershipStateRecalculator
    {
        public int CallCount { get; private set; }

        public Task<MembershipStateRecalculationResult> RecalculateAsync(
            Guid membershipId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return CallCount == 2
                ? Task.FromResult(new MembershipStateRecalculationResult(
                    membershipId,
                    MembershipStateRecalculationStatus.InvalidSourceState))
                : inner.RecalculateAsync(membershipId, cancellationToken);
        }
    }

    private sealed record FreezeFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid MembershipId,
        Guid FreezeId);

    private sealed record FreezeCancellationRow(
        Guid FreezeId,
        string Reason,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId);

    private sealed record MembershipStateRow(
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        DateTimeOffset RecalculatedAt,
        int RecalculationVersion);

    private sealed record ExtensionRow(
        DateOnly ExtensionDate,
        string SourceType,
        Guid SourceId,
        bool IsActive,
        DateTimeOffset RecalculatedAt);

    private sealed record AuditRow(
        string ActionType,
        string EntityType,
        Guid EntityId,
        Guid ActorAccountId,
        string ActorAccountType,
        string ActorRole,
        Guid SessionId,
        string? DeviceLabel,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string? Reason,
        string? Comment,
        string BeforeSummary,
        string AfterSummary,
        string RequestCorrelationId,
        string EntryOrigin,
        string? IdempotencyKey,
        bool ChangedAfterClose);

    private sealed record IdempotencyRow(
        string CommandName,
        Guid AccountId,
        Guid SessionId,
        Guid PrimaryEntityId,
        Guid RereadTargetId,
        Guid AuditEntryId,
        string Status,
        string ResultFingerprint);
}
