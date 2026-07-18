using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMarkVisitCommandTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        14,
        14,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset VisitOccurredAt = new(
        2026,
        7,
        14,
        9,
        30,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly MembershipStartDate = new(2026, 7, 1);

    [PostgreSqlFact]
    public async Task MembershipVisitPersistsSourceRecalculationAuditAndRereadAtomically()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext, visitsLimit: 1);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                fixture,
                "membership-success",
                VisitKind.Membership,
                fixture.MembershipId),
            CancellationToken.None);

        AssertSuccessfulResult(result, fixture.ClientId);
        Assert.Equal(
            [new EntityId("membership", fixture.MembershipId)],
            result.RelatedEntityIds);
        Assert.Contains(MembershipWarningCodes.ZeroRemaining, result.Warnings);

        var visitId = result.PrimaryEntityId!.Value.Value;
        var visit = await ReadVisitAsync(database, visitId);
        Assert.Equal(fixture.ClientId, visit.ClientId);
        Assert.Equal(VisitOccurredAt, visit.OccurredAt);
        Assert.Equal(TestNow, visit.RecordedAt);
        Assert.Equal(fixture.Actor.AccountId.Value, visit.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId.Value, visit.SessionId);
        Assert.Equal("membership", visit.VisitKind);
        Assert.Equal("normal", visit.EntryOrigin);
        Assert.Equal("Front desk Visit", visit.Comment);
        Assert.Equal("active", visit.Status);

        var consumption = await ReadConsumptionAsync(database, visitId);
        Assert.Equal(fixture.MembershipId, consumption.MembershipId);
        Assert.Equal(visitId, consumption.SourceFactId);
        Assert.Equal("active", consumption.Status);

        var cache = await ReadCacheAsync(database, fixture.MembershipId);
        Assert.Equal(1, cache.CountedVisits);
        Assert.Equal(0, cache.RemainingVisits);
        Assert.Equal(0, cache.NegativeBalance);
        Assert.Null(cache.FirstNegativeVisitId);
        Assert.Null(cache.FirstNegativeVisitDate);
        Assert.Equal(VisitOccurredAt, cache.LastCountedVisitAt);
        Assert.Equal(TestNow, cache.RecalculatedAt);

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(VisitAuditActions.Marked, audit.ActionType);
        Assert.Equal(VisitAuditActions.VisitEntityType, audit.EntityType);
        Assert.Equal(visitId, audit.EntityId);
        Assert.Equal(VisitOccurredAt, audit.OccurredAt);
        Assert.Equal(TestNow, audit.RecordedAt);
        Assert.Equal("normal", audit.EntryOrigin);
        Assert.Equal("membership-success", audit.IdempotencyKey);
        using (var before = JsonDocument.Parse(audit.BeforeSummary))
        {
            Assert.Equal(
                1,
                before.RootElement.GetProperty("remainingVisits").GetInt32());
        }

        using (var after = JsonDocument.Parse(audit.AfterSummary))
        {
            var visitSummary = after.RootElement.GetProperty("visit");
            Assert.Equal(visitId, visitSummary.GetProperty("visitId").GetGuid());
            Assert.Equal(
                consumption.Id,
                visitSummary.GetProperty("consumptionId").GetGuid());
            Assert.Empty(visitSummary.GetProperty("acknowledgements").EnumerateArray());
            Assert.Equal(
                0,
                after.RootElement
                    .GetProperty("membershipState")
                    .GetProperty("remainingVisits")
                    .GetInt32());
        }

        Assert.Equal(1L, await CountRowsAsync(database, "visits"));
        Assert.Equal(1L, await CountRowsAsync(database, "visit_consumptions"));
        Assert.Equal(1L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task CurrentExpiredAndZeroWarningsRequireTheExactAcknowledgementSet()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(
            database,
            dbContext,
            visitsLimit: 0,
            durationDays: 13);
        var handler = CreateHandler(dbContext);

        var missing = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "warning-missing",
                VisitKind.Membership,
                fixture.MembershipId),
            CancellationToken.None);
        var partial = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "warning-partial",
                VisitKind.Membership,
                fixture.MembershipId,
                [MembershipVisitAcknowledgement.Expired]),
            CancellationToken.None);
        var accepted = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "warning-exact",
                VisitKind.Membership,
                fixture.MembershipId,
                [
                    MembershipVisitAcknowledgement.ZeroRemaining,
                    MembershipVisitAcknowledgement.Expired,
                ]),
            CancellationToken.None);

        AssertError(
            missing,
            CommandErrorCode.WarningAcknowledgementRequired,
            "acknowledgements");
        AssertError(
            partial,
            CommandErrorCode.WarningAcknowledgementRequired,
            "acknowledgements");
        AssertSuccessfulResult(accepted, fixture.ClientId);
        Assert.Contains(MembershipWarningCodes.ExpiredByDate, accepted.Warnings);
        Assert.Contains(MembershipWarningCodes.NegativeBalance, accepted.Warnings);

        var visitId = accepted.PrimaryEntityId!.Value.Value;
        var cache = await ReadCacheAsync(database, fixture.MembershipId);
        Assert.Equal(1, cache.CountedVisits);
        Assert.Equal(-1, cache.RemainingVisits);
        Assert.Equal(1, cache.NegativeBalance);
        Assert.Equal(visitId, cache.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 14), cache.FirstNegativeVisitDate);

        var audit = await ReadAuditAsync(
            database,
            accepted.AuditEntryId!.Value.Value);
        using var after = JsonDocument.Parse(audit.AfterSummary);
        Assert.Equal(
            ["expired", "zero_remaining"],
            after.RootElement
                .GetProperty("visit")
                .GetProperty("acknowledgements")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
        Assert.Equal(1L, await CountRowsAsync(database, "visits"));
        Assert.Equal(1L, await CountRowsAsync(database, "visit_consumptions"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task OneOffAndTrialWriteOnlyVisitAndAuditFacts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);

        var oneOff = await handler.ExecuteAsync(
            CreateCommand(fixture, "one-off", VisitKind.OneOff),
            CancellationToken.None);
        var trial = await handler.ExecuteAsync(
            CreateCommand(fixture, "trial", VisitKind.Trial),
            CancellationToken.None);

        AssertSuccessfulResult(oneOff, fixture.ClientId);
        AssertSuccessfulResult(trial, fixture.ClientId);
        Assert.Empty(oneOff.RelatedEntityIds);
        Assert.Empty(trial.RelatedEntityIds);
        Assert.Empty(oneOff.Warnings);
        Assert.Empty(trial.Warnings);
        Assert.Equal(
            "one_off,trial",
            await database.ExecuteScalarAsync<string>(
                "select string_agg(visit_kind, ',' order by recorded_at, visit_kind) from bodylife.visits"));
        Assert.Equal(0L, await CountRowsAsync(database, "visit_consumptions"));
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(2L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(2L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task IdempotentReplayReturnsOriginalAndChangedPayloadIsDuplicate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);
        var command = CreateCommand(fixture, "visit-replay", VisitKind.OneOff);

        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var changed = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with { Comment = "Changed Visit context" },
            },
            CancellationToken.None);

        AssertSuccessfulResult(first, fixture.ClientId);
        AssertSuccessfulResult(replay, fixture.ClientId);
        Assert.Equal(first.PrimaryEntityId, replay.PrimaryEntityId);
        Assert.Equal(first.RereadTargetId, replay.RereadTargetId);
        Assert.Equal(first.AuditEntryId, replay.AuditEntryId);
        AssertError(changed, CommandErrorCode.DuplicateSubmission, "idempotencyKey");
        Assert.Equal(1L, await CountRowsAsync(database, "visits"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentSameKeySerializesToOneCompleteWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        MarkVisitFixture fixture;
        await using (var setupContext = database.CreateDbContext())
        {
            await setupContext.Database.MigrateAsync();
            fixture = await SeedFixtureAsync(database, setupContext);
        }

        var command = CreateCommand(
            fixture,
            "concurrent-same-key",
            VisitKind.Membership,
            fixture.MembershipId);
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(command, CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(command, CancellationToken.None));

        Assert.All(
            results,
            result => AssertSuccessfulResult(result, fixture.ClientId));
        Assert.Equal(results[0].PrimaryEntityId, results[1].PrimaryEntityId);
        Assert.Equal(results[0].AuditEntryId, results[1].AuditEntryId);
        Assert.Equal(1L, await CountRowsAsync(database, "visits"));
        Assert.Equal(1L, await CountRowsAsync(database, "visit_consumptions"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task MissingSelectionAndActiveFreezeFailWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);

        var missing = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "missing-membership",
                VisitKind.Membership,
                Guid.NewGuid()),
            CancellationToken.None);
        await InsertFreezeAsync(database, fixture);
        var frozen = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "frozen-membership",
                VisitKind.Membership,
                fixture.MembershipId),
            CancellationToken.None);

        AssertError(missing, CommandErrorCode.NotFound, "membershipId");
        AssertError(frozen, CommandErrorCode.VisitDuringFreeze, "membershipId");
        await AssertNoVisitMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task PaperFallbackPreservesOccurredRecordedBatchAndAuditMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var entryBatchId = Guid.NewGuid();
        var occurredAt = TestNow.AddDays(-2).AddMinutes(-15);
        var command = CreateCommand(
            fixture,
            "paper-fallback",
            VisitKind.OneOff,
            origin: EntryOrigin.PaperFallback,
            occurredAt: occurredAt,
            reason: "Recovered from signed reception sheet",
            entryBatchId: entryBatchId);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);

        AssertSuccessfulResult(result, fixture.ClientId);
        var visit = await ReadVisitAsync(database, result.PrimaryEntityId!.Value.Value);
        Assert.Equal(occurredAt, visit.OccurredAt);
        Assert.Equal(TestNow, visit.RecordedAt);
        Assert.Equal("paper_fallback", visit.EntryOrigin);
        Assert.Equal(entryBatchId, visit.EntryBatchId);

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(occurredAt, audit.OccurredAt);
        Assert.Equal(TestNow, audit.RecordedAt);
        Assert.Equal("paper_fallback", audit.EntryOrigin);
        Assert.Equal("Recovered from signed reception sheet", audit.Reason);
        Assert.Equal("paper-fallback", audit.IdempotencyKey);
    }

    [PostgreSqlFact]
    public async Task InvalidEnvelopeAndInactiveCanonicalActorAreDeniedWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);
        var valid = CreateCommand(fixture, "valid", VisitKind.OneOff);

        var missingOccurredAt = await handler.ExecuteAsync(
            valid with
            {
                Envelope = valid.Envelope with
                {
                    IdempotencyKey = "missing-occurred",
                    OccurredAt = null,
                },
            },
            CancellationToken.None);
        var fallbackWithoutContext = await handler.ExecuteAsync(
            valid with
            {
                Envelope = valid.Envelope with
                {
                    IdempotencyKey = "fallback-no-context",
                    EntryOrigin = EntryOrigin.PaperFallback,
                    Reason = null,
                    Comment = null,
                },
            },
            CancellationToken.None);

        await DeactivateAccountAsync(database, fixture.Actor.AccountId.Value);
        var inactiveActor = await handler.ExecuteAsync(
            valid with
            {
                Envelope = valid.Envelope with { IdempotencyKey = "inactive-actor" },
            },
            CancellationToken.None);

        AssertError(
            missingOccurredAt,
            CommandErrorCode.ValidationFailed,
            "occurredAt");
        AssertError(
            fallbackWithoutContext,
            CommandErrorCode.ValidationFailed,
            "entryOrigin");
        AssertError(inactiveActor, CommandErrorCode.PermissionDenied);
        await AssertNoVisitMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task CompetingMembershipLockReturnsConcurrencyConflictWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var migrationContext = database.CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var dbContext = database.CreateDbContext();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await using var lockConnection = new NpgsqlConnection(database.ConnectionString);
        await lockConnection.OpenAsync();
        await using var lockTransaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.Transaction = lockTransaction;
            lockCommand.CommandText =
                "select id from bodylife.issued_memberships where id = @id for update";
            lockCommand.Parameters.AddWithValue("id", fixture.MembershipId);
            Assert.Equal(
                fixture.MembershipId,
                await lockCommand.ExecuteScalarAsync());
        }

        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("set lock_timeout = '250ms'");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                fixture,
                "membership-lock-conflict",
                VisitKind.Membership,
                fixture.MembershipId),
            CancellationToken.None);

        await lockTransaction.RollbackAsync();
        AssertError(result, CommandErrorCode.ConcurrencyConflict);
        await AssertNoVisitMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task AddFreezeQueuedBeforeMarkVisitCommitsWithoutDeadlock()
    {
        await AssertConcurrentFreezeVisitOrderingAsync(freezeQueuedFirst: true);
    }

    [PostgreSqlFact]
    public async Task MarkVisitQueuedBeforeAddFreezeCommitsWithoutContradictoryFacts()
    {
        await AssertConcurrentFreezeVisitOrderingAsync(freezeQueuedFirst: false);
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackVisitConsumptionCacheAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_visit_marked_audit
            check (action_type <> 'visit.marked')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext).ExecuteAsync(
                CreateCommand(
                    fixture,
                    "audit-failure",
                    VisitKind.Membership,
                    fixture.MembershipId),
                CancellationToken.None));

        await AssertNoVisitMutationAsync(database);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    private static async Task AssertConcurrentFreezeVisitOrderingAsync(
        bool freezeQueuedFirst)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        MarkVisitFixture fixture;
        await using (var setupContext = database.CreateDbContext())
        {
            await setupContext.Database.MigrateAsync();
            fixture = await SeedFixtureAsync(database, setupContext);
        }

        await using var freezeContext = database.CreateDbContext();
        await using var visitContext = database.CreateDbContext();
        var freezeBackendPid = await GetBackendPidAsync(freezeContext);
        var visitBackendPid = await GetBackendPidAsync(visitContext);
        await using var lockConnection = new NpgsqlConnection(database.ConnectionString);
        await lockConnection.OpenAsync();
        await using var lockTransaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.Transaction = lockTransaction;
            lockCommand.CommandText =
                "select id from bodylife.issued_memberships where id = @id for update";
            lockCommand.Parameters.AddWithValue("id", fixture.MembershipId);
            Assert.Equal(
                fixture.MembershipId,
                await lockCommand.ExecuteScalarAsync());
        }

        var freezeCommand = CreateAddFreezeCommand(
            fixture,
            freezeQueuedFirst ? "freeze-first" : "freeze-second");
        var visitCommand = CreateCommand(
            fixture,
            freezeQueuedFirst ? "visit-second" : "visit-first",
            VisitKind.Membership,
            fixture.MembershipId);
        Task<CommandResult> freezeTask;
        Task<CommandResult> visitTask;

        if (freezeQueuedFirst)
        {
            freezeTask = CreateAddFreezeHandler(freezeContext).ExecuteAsync(
                freezeCommand,
                CancellationToken.None);
            await WaitForLockWaitAsync(database, freezeBackendPid);
            visitTask = CreateHandler(visitContext).ExecuteAsync(
                visitCommand,
                CancellationToken.None);
            await WaitForLockWaitAsync(database, visitBackendPid);
        }
        else
        {
            visitTask = CreateHandler(visitContext).ExecuteAsync(
                visitCommand,
                CancellationToken.None);
            await WaitForLockWaitAsync(database, visitBackendPid);
            freezeTask = CreateAddFreezeHandler(freezeContext).ExecuteAsync(
                freezeCommand,
                CancellationToken.None);
            await WaitForLockWaitAsync(database, freezeBackendPid);
        }

        await lockTransaction.CommitAsync();
        var results = await Task.WhenAll(freezeTask, visitTask)
            .WaitAsync(TimeSpan.FromSeconds(15));
        var freezeResult = results[0];
        var visitResult = results[1];

        var cache = await ReadCacheAsync(database, fixture.MembershipId);
        if (freezeQueuedFirst)
        {
            AssertSuccessfulFreezeResult(freezeResult, fixture);
            AssertError(
                visitResult,
                CommandErrorCode.VisitDuringFreeze,
                "membershipId");
            Assert.Equal(0, cache.CountedVisits);
            Assert.Equal(8, cache.RemainingVisits);
            Assert.Equal(1, cache.ExtensionDays);
            Assert.Null(cache.LastCountedVisitAt);
            Assert.Equal(0L, await CountRowsAsync(database, "visits"));
            Assert.Equal(0L, await CountRowsAsync(database, "visit_consumptions"));
            Assert.Equal(1L, await CountRowsAsync(database, "freezes"));
        }
        else
        {
            AssertSuccessfulResult(visitResult, fixture.ClientId);
            AssertError(
                freezeResult,
                CommandErrorCode.FreezeConflictsWithVisit,
                "range");
            Assert.Equal(1, cache.CountedVisits);
            Assert.Equal(7, cache.RemainingVisits);
            Assert.Equal(0, cache.ExtensionDays);
            Assert.Equal(VisitOccurredAt, cache.LastCountedVisitAt);
            Assert.Equal(1L, await CountRowsAsync(database, "visits"));
            Assert.Equal(1L, await CountRowsAsync(database, "visit_consumptions"));
            Assert.Equal(0L, await CountRowsAsync(database, "freezes"));
        }

        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static AddFreezeCommandHandler CreateAddFreezeHandler(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        var extensionSourceReader = new MembershipFreezeExtensionSourceReader(
            dbContext);
        var cacheRebuilder = new MembershipStateCacheRebuilder(
            dbContext,
            timeProvider,
            [extensionSourceReader]);

        return new AddFreezeCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new MembershipFreezeEligibilityPreparer(dbContext, cacheRebuilder),
            new MembershipStateRecalculator(cacheRebuilder),
            new GetMembershipStateQueryHandler(dbContext, timeProvider),
            timeProvider);
    }

    private static AddFreezeCommand CreateAddFreezeCommand(
        MarkVisitFixture fixture,
        string idempotencyKey)
    {
        var visitDate = DateOnly.FromDateTime(VisitOccurredAt.UtcDateTime);
        return new AddFreezeCommand(
            new CommandEnvelope(
                fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                VisitOccurredAt,
                idempotencyKey,
                "Concurrent medical pause",
                "Visit/Freeze concurrency gate"),
            fixture.ClientId,
            fixture.MembershipId,
            new DateRange(visitDate, visitDate),
            EntryBatchId: null);
    }

    private static async Task<int> GetBackendPidAsync(BodyLifeDbContext dbContext)
    {
        await dbContext.Database.OpenConnectionAsync();
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "select pg_backend_pid()";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task WaitForLockWaitAsync(
        PostgreSqlTestDatabase database,
        int backendPid)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "select wait_event_type = 'Lock' from pg_stat_activity where pid = @pid";
        command.Parameters.AddWithValue("pid", backendPid);

        for (var attempt = 0; attempt < 200; attempt++)
        {
            var waiting = await command.ExecuteScalarAsync();
            if (waiting is true)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException(
            $"PostgreSQL backend {backendPid} did not enter a lock wait.");
    }

    private static MarkVisitCommandHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        var cacheRebuilder = new MembershipStateCacheRebuilder(
            dbContext,
            timeProvider);
        var freezeReader = new MembershipVisitFreezeSourceReader(dbContext);
        var eligibilityPreparer = new MembershipVisitEligibilityPreparer(
            dbContext,
            cacheRebuilder,
            freezeReader);

        return new MarkVisitCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            eligibilityPreparer,
            new MembershipStateRecalculator(cacheRebuilder),
            new GetMembershipStateQueryHandler(dbContext, timeProvider),
            timeProvider);
    }

    private static MarkVisitCommand CreateCommand(
        MarkVisitFixture fixture,
        string idempotencyKey,
        VisitKind visitKind,
        Guid? membershipId = null,
        IReadOnlyList<MembershipVisitAcknowledgement>? acknowledgements = null,
        EntryOrigin origin = EntryOrigin.Normal,
        DateTimeOffset? occurredAt = null,
        string? reason = null,
        Guid? entryBatchId = null)
    {
        return new MarkVisitCommand(
            new CommandEnvelope(
                fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                origin,
                occurredAt ?? VisitOccurredAt,
                idempotencyKey,
                reason,
                "  Front desk Visit  "),
            fixture.ClientId,
            visitKind,
            membershipId,
            acknowledgements ?? [],
            entryBatchId);
    }

    private static async Task<MarkVisitFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext,
        int visitsLimit = 8,
        int durationDays = 30)
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
        var baseEndDate = MembershipStartDate.AddDays(durationDays - 1);

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
                'Visit',
                'Client',
                null,
                'VISIT CLIENT',
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
                'Mark Visit fixture',
                @duration_days,
                @visits_limit,
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
                'Mark Visit fixture',
                @duration_days,
                @visits_limit,
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
        command.Parameters.AddWithValue("duration_days", durationDays);
        command.Parameters.AddWithValue("visits_limit", visitsLimit);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            MembershipStartDate);
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            baseEndDate);
        command.Parameters.AddWithValue("issued_at", TestNow.AddDays(-14));
        Assert.Equal(4, await command.ExecuteNonQueryAsync());

        var actor = new ActorContext(
            new AccountId(accountId),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(sessionId),
            "Reception tablet");
        return new MarkVisitFixture(actor, clientId, membershipId);
    }

    private static async Task InsertFreezeAsync(
        PostgreSqlTestDatabase database,
        MarkVisitFixture fixture)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
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
            values (
                @id,
                @client_id,
                @membership_id,
                @visit_date,
                @visit_date,
                'Medical pause',
                @recorded_at,
                @recorded_at,
                @account_id,
                @session_id,
                'normal',
                null,
                'active')
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue(
            "visit_date",
            NpgsqlDbType.Date,
            DateOnly.FromDateTime(VisitOccurredAt.DateTime));
        command.Parameters.AddWithValue("recorded_at", TestNow.AddHours(-2));
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeactivateAccountAsync(
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

    private static async Task<VisitRow> ReadVisitAsync(
        PostgreSqlTestDatabase database,
        Guid visitId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select client_id,
                   occurred_at,
                   recorded_at,
                   recorded_by_account_id,
                   session_id,
                   visit_kind,
                   entry_origin,
                   entry_batch_id,
                   comment,
                   status
            from bodylife.visits
            where id = @id
            """;
        command.Parameters.AddWithValue("id", visitId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new VisitRow(
            reader.GetGuid(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetString(9));
    }

    private static async Task<ConsumptionRow> ReadConsumptionAsync(
        PostgreSqlTestDatabase database,
        Guid visitId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select id, membership_id, source_fact_id, status
            from bodylife.visit_consumptions
            where visit_id = @visit_id
            """;
        command.Parameters.AddWithValue("visit_id", visitId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new ConsumptionRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3));
    }

    private static async Task<CacheRow> ReadCacheAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select counted_visits,
                   remaining_visits,
                   negative_balance,
                   first_negative_visit_id,
                   first_negative_visit_date,
                   last_counted_visit_at,
                   extension_days,
                   recalculated_at
            from bodylife.membership_state_cache
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new CacheRow(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
            reader.IsDBNull(5)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetInt32(6),
            reader.GetFieldValue<DateTimeOffset>(7));
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
                   occurred_at,
                   recorded_at,
                   entry_origin,
                   reason,
                   idempotency_key,
                   before_summary::text,
                   after_summary::text
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
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9));
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

    private static async Task AssertNoVisitMutationAsync(
        PostgreSqlTestDatabase database)
    {
        Assert.Equal(0L, await CountRowsAsync(database, "visits"));
        Assert.Equal(0L, await CountRowsAsync(database, "visit_consumptions"));
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static void AssertSuccessfulResult(CommandResult result, Guid clientId)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.True(result.PrimaryEntityId.HasValue);
        Assert.Equal("visit", result.PrimaryEntityId.Value.Type);
        Assert.NotEqual(Guid.Empty, result.PrimaryEntityId.Value.Value);
        Assert.Equal(new EntityId("client", clientId), result.RereadTargetId);
        Assert.True(result.AuditEntryId.HasValue);
        Assert.Empty(result.Errors);
    }

    private static void AssertSuccessfulFreezeResult(
        CommandResult result,
        MarkVisitFixture fixture)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.True(result.PrimaryEntityId.HasValue);
        Assert.Equal("freeze", result.PrimaryEntityId.Value.Type);
        Assert.NotEqual(Guid.Empty, result.PrimaryEntityId.Value.Value);
        Assert.Equal(
            [new EntityId("membership", fixture.MembershipId)],
            result.RelatedEntityIds);
        Assert.Equal(
            new EntityId("client", fixture.ClientId),
            result.RereadTargetId);
        Assert.True(result.AuditEntryId.HasValue);
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
        if (field is not null)
        {
            Assert.Equal(field, error.Field);
        }

        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
        Assert.Null(result.AuditEntryId);
    }

    private sealed record MarkVisitFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid MembershipId);

    private sealed record VisitRow(
        Guid ClientId,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string VisitKind,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status);

    private sealed record ConsumptionRow(
        Guid Id,
        Guid MembershipId,
        Guid SourceFactId,
        string Status);

    private sealed record CacheRow(
        int CountedVisits,
        int RemainingVisits,
        int NegativeBalance,
        Guid? FirstNegativeVisitId,
        DateOnly? FirstNegativeVisitDate,
        DateTimeOffset? LastCountedVisitAt,
        int ExtensionDays,
        DateTimeOffset RecalculatedAt);

    private sealed record AuditRow(
        string ActionType,
        string EntityType,
        Guid EntityId,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string EntryOrigin,
        string? Reason,
        string? IdempotencyKey,
        string BeforeSummary,
        string AfterSummary);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
