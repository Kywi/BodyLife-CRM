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
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlAddFreezeCommandTests
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
    private static readonly DateOnly MembershipStartDate = new(2026, 7, 1);
    private static readonly DateOnly MembershipBaseEndDate = new(2026, 7, 30);

    [PostgreSqlFact]
    public async Task SuccessfulFreezeCommitsUnionStateAuditAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var range = new DateRange(
            new DateOnly(2026, 7, 10),
            new DateOnly(2026, 7, 12));

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(fixture, "freeze-success", range),
            CancellationToken.None);

        AssertSuccessfulResult(result, fixture);
        var freezeId = result.PrimaryEntityId!.Value.Value;
        var freeze = await ReadFreezeAsync(database, freezeId);
        Assert.Equal(fixture.ClientId, freeze.ClientId);
        Assert.Equal(fixture.MembershipId, freeze.MembershipId);
        Assert.Equal(range.StartDate, freeze.StartDate);
        Assert.Equal(range.EndDate, freeze.EndDate);
        Assert.Equal("Medical pause", freeze.Reason);
        Assert.Equal(FreezeOccurredAt, freeze.OccurredAt);
        Assert.Equal(TestNow, freeze.RecordedAt);
        Assert.Equal(fixture.Actor.AccountId.Value, freeze.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId.Value, freeze.SessionId);
        Assert.Equal("normal", freeze.EntryOrigin);
        Assert.Null(freeze.EntryBatchId);
        Assert.Equal("active", freeze.Status);

        var state = await ReadMembershipStateAsync(database, fixture.MembershipId);
        Assert.Equal(0, state.CountedVisits);
        Assert.Equal(8, state.RemainingVisits);
        Assert.Equal(0, state.NegativeBalance);
        Assert.Equal(3, state.ExtensionDays);
        Assert.Equal(MembershipBaseEndDate.AddDays(3), state.EffectiveEndDate);
        Assert.Equal(TestNow, state.RecalculatedAt);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            state.RecalculationVersion);

        var extensionRows = await ReadExtensionRowsAsync(
            database,
            fixture.MembershipId);
        Assert.Equal(3, extensionRows.Count);
        Assert.Equal(
            [
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 11),
                new DateOnly(2026, 7, 12),
            ],
            extensionRows.Select(row => row.ExtensionDate));
        Assert.All(extensionRows, row =>
        {
            Assert.Equal("freeze", row.SourceType);
            Assert.Equal(freezeId, row.SourceId);
            Assert.Equal(
                "Freeze 2026-07-10..2026-07-12: Medical pause",
                row.SourceLabel);
            Assert.True(row.IsActive);
            Assert.Equal(TestNow, row.RecalculatedAt);
        });

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(FreezeAuditActions.Added, audit.ActionType);
        Assert.Equal(FreezeAuditActions.FreezeEntityType, audit.EntityType);
        Assert.Equal(freezeId, audit.EntityId);
        Assert.Equal(fixture.Actor.AccountId.Value, audit.ActorAccountId);
        Assert.Equal("owner", audit.ActorAccountType);
        Assert.Equal("owner", audit.ActorRole);
        Assert.Equal(fixture.Actor.SessionId.Value, audit.SessionId);
        Assert.Equal("Reception tablet", audit.DeviceLabel);
        Assert.Equal(FreezeOccurredAt, audit.OccurredAt);
        Assert.Equal(TestNow, audit.RecordedAt);
        Assert.Equal("Medical pause", audit.Reason);
        Assert.Equal("Front desk Freeze", audit.Comment);
        Assert.Equal("correlation-freeze-success", audit.RequestCorrelationId);
        Assert.Equal("normal", audit.EntryOrigin);
        Assert.Equal("freeze-success", audit.IdempotencyKey);
        using (var related = JsonDocument.Parse(audit.RelatedEntityRefs))
        {
            Assert.Equal(2, related.RootElement.EnumerateObject().Count());
            Assert.Equal(
                fixture.ClientId,
                related.RootElement.GetProperty("clientId").GetGuid());
            Assert.Equal(
                fixture.MembershipId,
                related.RootElement.GetProperty("membershipId").GetGuid());
        }

        using (var before = JsonDocument.Parse(audit.BeforeSummary))
        {
            Assert.Single(before.RootElement.EnumerateObject());
            var membershipState = before.RootElement.GetProperty("membershipState");
            Assert.Equal(7, membershipState.EnumerateObject().Count());
            Assert.Equal(
                fixture.MembershipId,
                membershipState.GetProperty("membershipId").GetGuid());
            Assert.Equal(
                fixture.ClientId,
                membershipState.GetProperty("clientId").GetGuid());
            Assert.Equal(8, membershipState.GetProperty("remainingVisits").GetInt32());
            Assert.Equal(0, membershipState.GetProperty("negativeBalance").GetInt32());
            Assert.Equal(0, membershipState.GetProperty("extensionDays").GetInt32());
            Assert.Equal(
                "2026-07-30",
                membershipState.GetProperty("effectiveEndDate").GetString());
            Assert.Equal(JsonValueKind.Array, membershipState.GetProperty("warnings").ValueKind);
        }

        using (var after = JsonDocument.Parse(audit.AfterSummary))
        {
            Assert.Equal(2, after.RootElement.EnumerateObject().Count());
            var freezeSummary = after.RootElement.GetProperty("freeze");
            Assert.Equal(12, freezeSummary.EnumerateObject().Count());
            Assert.Equal(freezeId, freezeSummary.GetProperty("freezeId").GetGuid());
            Assert.Equal(
                fixture.ClientId,
                freezeSummary.GetProperty("clientId").GetGuid());
            Assert.Equal(
                fixture.MembershipId,
                freezeSummary.GetProperty("membershipId").GetGuid());
            Assert.Equal("2026-07-10", freezeSummary.GetProperty("startDate").GetString());
            Assert.Equal("2026-07-12", freezeSummary.GetProperty("endDate").GetString());
            Assert.Equal(3, freezeSummary.GetProperty("inclusiveDays").GetInt32());
            Assert.Equal("Medical pause", freezeSummary.GetProperty("reason").GetString());
            Assert.Equal(
                FreezeOccurredAt,
                freezeSummary.GetProperty("occurredAt").GetDateTimeOffset());
            Assert.Equal(
                TestNow,
                freezeSummary.GetProperty("recordedAt").GetDateTimeOffset());
            Assert.Equal("normal", freezeSummary.GetProperty("entryOrigin").GetString());
            Assert.Equal(JsonValueKind.Null, freezeSummary.GetProperty("entryBatchId").ValueKind);
            Assert.Equal("active", freezeSummary.GetProperty("status").GetString());
            var membershipState = after.RootElement.GetProperty("membershipState");
            Assert.Equal(7, membershipState.EnumerateObject().Count());
            Assert.Equal(
                fixture.MembershipId,
                membershipState.GetProperty("membershipId").GetGuid());
            Assert.Equal(
                fixture.ClientId,
                membershipState.GetProperty("clientId").GetGuid());
            Assert.Equal(8, membershipState.GetProperty("remainingVisits").GetInt32());
            Assert.Equal(0, membershipState.GetProperty("negativeBalance").GetInt32());
            Assert.Equal(3, membershipState.GetProperty("extensionDays").GetInt32());
            Assert.Equal(
                "2026-08-02",
                membershipState.GetProperty("effectiveEndDate").GetString());
            Assert.Equal(JsonValueKind.Array, membershipState.GetProperty("warnings").ValueKind);
        }

        var idempotency = await ReadIdempotencyAsync(database, "freeze-success");
        Assert.Equal("AddFreeze", idempotency.CommandName);
        Assert.Equal(fixture.Actor.AccountId.Value, idempotency.AccountId);
        Assert.Equal(fixture.Actor.SessionId.Value, idempotency.SessionId);
        Assert.Equal(freezeId, idempotency.PrimaryEntityId);
        Assert.Equal(fixture.ClientId, idempotency.RereadTargetId);
        Assert.Equal(result.AuditEntryId.Value.Value, idempotency.AuditEntryId);
        Assert.Equal("succeeded", idempotency.Status);
        Assert.False(string.IsNullOrWhiteSpace(idempotency.ResultFingerprint));
        Assert.Equal(1L, await CountRowsAsync(database, "freezes"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task OverlapUsesUnionAndPriorExtensionExpandsNextEligibleStart()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var namedAdmin = await InsertAdminActorAsync(
            database,
            AccountKind.NamedAdmin,
            "Named Admin");
        var handler = CreateHandler(dbContext);

        var first = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "cross-end",
                new DateRange(
                    new DateOnly(2026, 7, 29),
                    new DateOnly(2026, 8, 1))),
            CancellationToken.None);
        var second = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "overlap-from-extension",
                new DateRange(
                    new DateOnly(2026, 7, 31),
                    new DateOnly(2026, 8, 5)),
                actor: namedAdmin,
                reason: "Extended recovery"),
            CancellationToken.None);

        AssertSuccessfulResult(first, fixture);
        AssertSuccessfulResult(second, fixture);
        var state = await ReadMembershipStateAsync(database, fixture.MembershipId);
        Assert.Equal(8, state.ExtensionDays);
        Assert.Equal(MembershipBaseEndDate.AddDays(8), state.EffectiveEndDate);
        Assert.Equal(
            8L,
            await CountDistinctActiveExtensionDatesAsync(
                database,
                fixture.MembershipId));
        Assert.Equal(10L, await CountRowsAsync(database, "membership_extension_days"));
        Assert.Equal(2L, await CountRowsAsync(database, "freezes"));
        Assert.Equal(2L, await CountRowsAsync(database, "business_audit_entries"));
        var secondFreeze = await ReadFreezeAsync(
            database,
            second.PrimaryEntityId!.Value.Value);
        Assert.Equal(namedAdmin.AccountId.Value, secondFreeze.RecordedByAccountId);
    }

    [PostgreSqlFact]
    public async Task SharedAdminPaperFallbackPreservesSourceAndAuditMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var sharedAdmin = await InsertAdminActorAsync(
            database,
            AccountKind.SharedReceptionAdmin,
            "Shared Reception");
        var entryBatchId = Guid.NewGuid();
        var occurredAt = FreezeOccurredAt.AddDays(-3);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                fixture,
                "paper-freeze",
                new DateRange(
                    new DateOnly(2026, 7, 20),
                    new DateOnly(2026, 7, 21)),
                actor: sharedAdmin,
                origin: EntryOrigin.PaperFallback,
                occurredAt: occurredAt,
                reason: "Recovered medical pause",
                entryBatchId: entryBatchId),
            CancellationToken.None);

        AssertSuccessfulResult(result, fixture);
        var freeze = await ReadFreezeAsync(
            database,
            result.PrimaryEntityId!.Value.Value);
        Assert.Equal(occurredAt, freeze.OccurredAt);
        Assert.Equal(TestNow, freeze.RecordedAt);
        Assert.Equal("paper_fallback", freeze.EntryOrigin);
        Assert.Equal(entryBatchId, freeze.EntryBatchId);
        Assert.Equal(sharedAdmin.AccountId.Value, freeze.RecordedByAccountId);
        Assert.Equal(sharedAdmin.SessionId.Value, freeze.SessionId);
        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal("shared_reception_admin", audit.ActorAccountType);
        Assert.Equal("admin", audit.ActorRole);
        Assert.Equal("paper_fallback", audit.EntryOrigin);
        Assert.Equal("Recovered medical pause", audit.Reason);
        Assert.Equal(occurredAt, audit.OccurredAt);
    }

    [PostgreSqlFact]
    public async Task EligibilityAndVisitConflictsFailWithoutFreezeMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);

        var beforeStart = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "before-start",
                new DateRange(
                    MembershipStartDate.AddDays(-1),
                    MembershipStartDate)),
            CancellationToken.None);
        var afterEnd = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "after-end",
                new DateRange(
                    MembershipBaseEndDate.AddDays(1),
                    MembershipBaseEndDate.AddDays(2))),
            CancellationToken.None);
        var wrongClient = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "wrong-client",
                new DateRange(MembershipStartDate, MembershipStartDate),
                clientId: fixture.OtherClientId),
            CancellationToken.None);
        await InsertMembershipVisitAsync(
            database,
            fixture,
            new DateTimeOffset(2026, 7, 12, 9, 0, 0, TimeSpan.Zero));
        var visitConflict = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "visit-conflict",
                new DateRange(
                    new DateOnly(2026, 7, 11),
                    new DateOnly(2026, 7, 12))),
            CancellationToken.None);
        await UpdateMembershipStatusAsync(database, fixture.MembershipId, "canceled");
        var inactive = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "inactive-membership",
                new DateRange(MembershipStartDate, MembershipStartDate)),
            CancellationToken.None);

        AssertError(beforeStart, CommandErrorCode.MembershipNotEligible, "range");
        AssertError(afterEnd, CommandErrorCode.MembershipNotEligible, "range");
        AssertError(wrongClient, CommandErrorCode.NotFound, "membershipId");
        AssertError(
            visitConflict,
            CommandErrorCode.FreezeConflictsWithVisit,
            "range");
        AssertError(inactive, CommandErrorCode.MembershipNotEligible, "range");
        await AssertNoFreezeMutationAsync(database);
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(0L, await CountRowsAsync(database, "membership_extension_days"));
    }

    [PostgreSqlFact]
    public async Task InvalidInputsAndEndedSessionFailWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);
        var valid = CreateCommand(
            fixture,
            "valid-freeze",
            new DateRange(MembershipStartDate, MembershipStartDate));

        var emptyClient = await handler.ExecuteAsync(
            valid with { ClientId = Guid.Empty },
            CancellationToken.None);
        var emptyMembership = await handler.ExecuteAsync(
            valid with { MembershipId = Guid.Empty },
            CancellationToken.None);
        var missingRange = await handler.ExecuteAsync(
            valid with { Range = default },
            CancellationToken.None);
        var missingOccurredAt = await handler.ExecuteAsync(
            valid with { Envelope = valid.Envelope with { OccurredAt = null } },
            CancellationToken.None);
        var unsupportedOccurredAt = await handler.ExecuteAsync(
            valid with { Envelope = valid.Envelope with { OccurredAt = DateTimeOffset.MaxValue } },
            CancellationToken.None);
        var unsupportedRange = await handler.ExecuteAsync(
            valid with { Range = new DateRange(DateOnly.MaxValue, DateOnly.MaxValue) },
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

        AssertError(emptyClient, CommandErrorCode.ValidationFailed, "clientId");
        AssertError(emptyMembership, CommandErrorCode.ValidationFailed, "membershipId");
        AssertError(missingRange, CommandErrorCode.ValidationFailed, "range");
        AssertError(missingOccurredAt, CommandErrorCode.ValidationFailed, "occurredAt");
        AssertError(unsupportedOccurredAt, CommandErrorCode.ValidationFailed, "occurredAt");
        AssertError(unsupportedRange, CommandErrorCode.ValidationFailed, "range");
        AssertError(missingKey, CommandErrorCode.ValidationFailed, "idempotencyKey");
        AssertError(missingReason, CommandErrorCode.ReasonRequired, "reason");
        AssertError(normalWithBatch, CommandErrorCode.ValidationFailed, "entryBatchId");
        AssertError(invalidActorShape, CommandErrorCode.PermissionDenied);
        AssertError(endedSession, CommandErrorCode.PermissionDenied);
        await AssertNoFreezeMutationAsync(database);
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));
    }

    [PostgreSqlFact]
    public async Task IdempotentReplayReturnsOriginalAndChangedPayloadIsDuplicate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);
        var command = CreateCommand(
            fixture,
            "freeze-replay",
            new DateRange(
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 11)));

        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var changed = await handler.ExecuteAsync(
            command with
            {
                Range = new DateRange(
                    new DateOnly(2026, 7, 10),
                    new DateOnly(2026, 7, 12)),
            },
            CancellationToken.None);

        AssertSuccessfulResult(first, fixture);
        AssertSuccessfulResult(replay, fixture);
        Assert.Equal(first.PrimaryEntityId, replay.PrimaryEntityId);
        Assert.Equal(first.RereadTargetId, replay.RereadTargetId);
        Assert.Equal(first.RelatedEntityIds, replay.RelatedEntityIds);
        Assert.Equal(first.AuditEntryId, replay.AuditEntryId);
        AssertError(changed, CommandErrorCode.DuplicateSubmission, "idempotencyKey");
        Assert.Equal(1L, await CountRowsAsync(database, "freezes"));
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
            fixture = await SeedFixtureAsync(database, setupContext);
        }

        var command = CreateCommand(
            fixture,
            "concurrent-freeze",
            new DateRange(
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 12)));
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(command, CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(command, CancellationToken.None));

        Assert.All(results, result => AssertSuccessfulResult(result, fixture));
        Assert.Equal(results[0].PrimaryEntityId, results[1].PrimaryEntityId);
        Assert.Equal(results[0].AuditEntryId, results[1].AuditEntryId);
        Assert.Equal(1L, await CountRowsAsync(database, "freezes"));
        Assert.Equal(3L, await CountRowsAsync(database, "membership_extension_days"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task RecalculationFailureRollsBackSourceAndDerivedState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);

        var result = await CreateHandler(
                dbContext,
                new FailingMembershipStateRecalculator())
            .ExecuteAsync(
                CreateCommand(
                    fixture,
                    "recalculation-failure",
                    new DateRange(MembershipStartDate, MembershipStartDate)),
                CancellationToken.None);

        AssertError(result, CommandErrorCode.RecalculationFailed);
        await AssertNoFreezeMutationAsync(database);
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(0L, await CountRowsAsync(database, "membership_extension_days"));
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackEntireFreezeWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_freeze_added_audit
            check (action_type <> 'freeze.added')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext).ExecuteAsync(
                CreateCommand(
                    fixture,
                    "audit-failure",
                    new DateRange(MembershipStartDate, MembershipStartDate)),
                CancellationToken.None));

        await AssertNoFreezeMutationAsync(database);
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(0L, await CountRowsAsync(database, "membership_extension_days"));
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task CompetingMembershipLockReturnsConcurrencyConflict()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
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
            Assert.Equal(fixture.MembershipId, await lockCommand.ExecuteScalarAsync());
        }

        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("set lock_timeout = '250ms'");
        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                fixture,
                "membership-lock-conflict",
                new DateRange(MembershipStartDate, MembershipStartDate)),
            CancellationToken.None);

        await lockTransaction.RollbackAsync();
        AssertError(result, CommandErrorCode.ConcurrencyConflict);
        await AssertNoFreezeMutationAsync(database);
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));
    }

    [Fact]
    public void PersistenceRegistrationResolvesAddFreezeWorkflow()
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

        Assert.IsType<AddFreezeCommandHandler>(
            scope.ServiceProvider.GetRequiredService<
                IBodyLifeCommandHandler<AddFreezeCommand>>());
        Assert.IsType<MembershipFreezeEligibilityPreparer>(
            scope.ServiceProvider.GetRequiredService<
                MembershipFreezeEligibilityPreparer>());
        var extensionSourceProviders = scope.ServiceProvider.GetServices<
            IMembershipExtensionSourceProvider>().ToArray();
        Assert.Collection(
            extensionSourceProviders,
            source => Assert.IsType<MembershipFreezeExtensionSourceReader>(source),
            source => Assert.IsType<MembershipNonWorkingDayExtensionSourceReader>(source));
    }

    private static AddFreezeCommandHandler CreateHandler(
        BodyLifeDbContext dbContext,
        IMembershipStateRecalculator? membershipStateRecalculator = null)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        var extensionSourceReader = new MembershipFreezeExtensionSourceReader(dbContext);
        var cacheRebuilder = new MembershipStateCacheRebuilder(
            dbContext,
            timeProvider,
            [extensionSourceReader]);
        return new AddFreezeCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new MembershipFreezeEligibilityPreparer(dbContext, cacheRebuilder),
            membershipStateRecalculator
                ?? new MembershipStateRecalculator(cacheRebuilder),
            new GetMembershipStateQueryHandler(dbContext, timeProvider),
            timeProvider);
    }

    private static AddFreezeCommand CreateCommand(
        FreezeFixture fixture,
        string idempotencyKey,
        DateRange range,
        ActorContext? actor = null,
        EntryOrigin origin = EntryOrigin.Normal,
        DateTimeOffset? occurredAt = null,
        string? reason = "Medical pause",
        Guid? entryBatchId = null,
        Guid? clientId = null,
        Guid? membershipId = null)
    {
        return new AddFreezeCommand(
            new CommandEnvelope(
                actor ?? fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                origin,
                occurredAt ?? FreezeOccurredAt,
                idempotencyKey,
                reason,
                "  Front desk Freeze  "),
            clientId ?? fixture.ClientId,
            membershipId ?? fixture.MembershipId,
            range,
            entryBatchId);
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
                    'Client',
                    null,
                    'FREEZE CLIENT',
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
                'Add Freeze fixture',
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
                'Add Freeze fixture',
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
        command.Parameters.AddWithValue("other_client_id", otherClientId);
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
        Assert.Equal(5, await command.ExecuteNonQueryAsync());

        return new FreezeFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "  Reception tablet  "),
            clientId,
            otherClientId,
            membershipId);
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

    private static async Task InsertMembershipVisitAsync(
        PostgreSqlTestDatabase database,
        FreezeFixture fixture,
        DateTimeOffset occurredAt)
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
                @recorded_at,
                @account_id,
                @session_id,
                'active')
            """;
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("consumption_id", Guid.NewGuid());
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddMinutes(-1));
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
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

    private static async Task UpdateMembershipStatusAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        string status)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "update bodylife.issued_memberships set status = @status where id = @id";
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("id", membershipId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<FreezeRow> ReadFreezeAsync(
        PostgreSqlTestDatabase database,
        Guid freezeId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select client_id,
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
                   status
            from bodylife.freezes
            where id = @id
            """;
        command.Parameters.AddWithValue("id", freezeId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new FreezeRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetFieldValue<DateOnly>(2),
            reader.GetFieldValue<DateOnly>(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetGuid(7),
            reader.GetGuid(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            reader.GetString(11));
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
            select counted_visits,
                   remaining_visits,
                   negative_balance,
                   extension_days,
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
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetFieldValue<DateOnly>(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetInt32(6));
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
                   source_label,
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
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
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
                   related_entity_refs::text,
                   before_summary::text,
                   after_summary::text,
                   request_correlation_id,
                   entry_origin,
                   idempotency_key
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
            reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17));
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

    private static async Task AssertNoFreezeMutationAsync(
        PostgreSqlTestDatabase database)
    {
        Assert.Equal(0L, await CountRowsAsync(database, "freezes"));
        Assert.Equal(0L, await CountRowsAsync(database, "freeze_cancellations"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static void AssertSuccessfulResult(
        CommandResult result,
        FreezeFixture fixture)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.True(result.PrimaryEntityId.HasValue);
        Assert.Equal("freeze", result.PrimaryEntityId.Value.Type);
        Assert.NotEqual(Guid.Empty, result.PrimaryEntityId.Value.Value);
        Assert.Equal(new EntityId("client", fixture.ClientId), result.RereadTargetId);
        Assert.Equal(
            [new EntityId("membership", fixture.MembershipId)],
            result.RelatedEntityIds);
        Assert.True(result.AuditEntryId.HasValue);
        Assert.Empty(result.Warnings);
        Assert.False(result.ChangedAfterClose);
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

    private sealed class FailingMembershipStateRecalculator
        : IMembershipStateRecalculator
    {
        public Task<MembershipStateRecalculationResult> RecalculateAsync(
            Guid membershipId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MembershipStateRecalculationResult(
                membershipId,
                MembershipStateRecalculationStatus.InvalidSourceState));
        }
    }

    private sealed record FreezeFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId);

    private sealed record FreezeRow(
        Guid ClientId,
        Guid MembershipId,
        DateOnly StartDate,
        DateOnly EndDate,
        string Reason,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId,
        string Status);

    private sealed record MembershipStateRow(
        int CountedVisits,
        int RemainingVisits,
        int NegativeBalance,
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        DateTimeOffset RecalculatedAt,
        int RecalculationVersion);

    private sealed record ExtensionRow(
        DateOnly ExtensionDate,
        string SourceType,
        Guid SourceId,
        string SourceLabel,
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
        string RelatedEntityRefs,
        string BeforeSummary,
        string AfterSummary,
        string RequestCorrelationId,
        string EntryOrigin,
        string? IdempotencyKey);

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
