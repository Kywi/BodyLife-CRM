using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlAssignOrChangeCardCommandTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 10, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InitialAssignedAt = TestNow.AddDays(-2);

    [PostgreSqlFact]
    public async Task OwnerNamedAdminAndSharedReceptionCanAssignFirstCard()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actors = new[]
        {
            await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner),
            await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin),
            await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin),
        };
        var handler = CreateHandler(dbContext);
        var results = new List<CommandResult>();

        for (var index = 0; index < actors.Length; index++)
        {
            var clientId = Guid.NewGuid();
            await InsertClientAsync(database, clientId, actors[index].AccountId.Value, $"Allowed{index}");
            results.Add(await handler.ExecuteAsync(
                CardCommand(
                    actors[index],
                    $"allowed-card-{index}",
                    clientId,
                    newCardNumber: $"BL-ALLOWED-{index}"),
                CancellationToken.None));
        }

        Assert.All(results, AssertSuccessfulClientResult);
        Assert.Equal(3L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(3L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(3L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task AssignChangeAndClearPreserveHistoryAndWriteCanonicalAuditEvents()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin,
            deviceLabel: "front desk tablet");
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database, clientId, actor.AccountId.Value, "Lifecycle");
        var handler = CreateHandler(dbContext);

        var assignedResult = await handler.ExecuteAsync(
            CardCommand(
                actor,
                "card-assigned",
                clientId,
                newCardNumber: "  bl - 1001  "),
            CancellationToken.None);
        var firstAssignmentId = Assert.IsType<Guid>(await ReadCurrentCardIdAsync(database, clientId));
        var changedResult = await handler.ExecuteAsync(
            CardCommand(
                actor,
                "card-changed",
                clientId,
                expectedCurrentCardAssignmentId: firstAssignmentId,
                newCardNumber: "BL-2002",
                reason: "Damaged card"),
            CancellationToken.None);
        var secondAssignmentId = Assert.IsType<Guid>(await ReadCurrentCardIdAsync(database, clientId));
        var clearedResult = await handler.ExecuteAsync(
            CardCommand(
                actor,
                "card-cleared",
                clientId,
                expectedCurrentCardAssignmentId: secondAssignmentId,
                newCardNumber: null,
                clearCurrentCard: true,
                comment: "Client left card program"),
            CancellationToken.None);

        AssertSuccessfulClientResult(assignedResult);
        AssertSuccessfulClientResult(changedResult);
        AssertSuccessfulClientResult(clearedResult);
        Assert.Equal(clientId, assignedResult.PrimaryEntityId!.Value.Value);
        Assert.Null(await ReadCurrentCardIdAsync(database, clientId));

        var firstAssignment = await ReadCardAsync(database, firstAssignmentId);
        Assert.Equal("bl - 1001", firstAssignment.CardNumberRaw);
        Assert.Equal("BL-1001", firstAssignment.CardNumberNormalized);
        Assert.False(firstAssignment.IsCurrent);
        Assert.Equal(TestNow.UtcDateTime, firstAssignment.AssignedAt);
        Assert.Equal(TestNow.UtcDateTime, firstAssignment.EndedAt);
        Assert.Equal(actor.AccountId.Value, firstAssignment.EndedByAccountId);
        Assert.Equal("Damaged card", firstAssignment.EndReason);

        var secondAssignment = await ReadCardAsync(database, secondAssignmentId);
        Assert.Equal("BL-2002", secondAssignment.CardNumberRaw);
        Assert.Equal("BL-2002", secondAssignment.CardNumberNormalized);
        Assert.False(secondAssignment.IsCurrent);
        Assert.Equal(TestNow.UtcDateTime, secondAssignment.AssignedAt);
        Assert.Equal(TestNow.UtcDateTime, secondAssignment.EndedAt);
        Assert.Equal(actor.AccountId.Value, secondAssignment.EndedByAccountId);
        Assert.Equal("Client left card program", secondAssignment.EndReason);

        var assignedAudit = await ReadAuditAsync(database, assignedResult.AuditEntryId!.Value.Value);
        Assert.Equal(ClientAuditActions.CardAssigned, assignedAudit.ActionType);
        Assert.Equal("{}", assignedAudit.BeforeSummary);
        Assert.Contains("bl - 1001", assignedAudit.AfterSummary, StringComparison.Ordinal);
        Assert.Null(assignedAudit.Reason);
        Assert.Null(assignedAudit.Comment);

        var changedAudit = await ReadAuditAsync(database, changedResult.AuditEntryId!.Value.Value);
        Assert.Equal(ClientAuditActions.CardChanged, changedAudit.ActionType);
        Assert.Contains("bl - 1001", changedAudit.BeforeSummary, StringComparison.Ordinal);
        Assert.Contains("BL-2002", changedAudit.AfterSummary, StringComparison.Ordinal);
        Assert.Contains(firstAssignmentId.ToString(), changedAudit.RelatedEntityRefs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(secondAssignmentId.ToString(), changedAudit.RelatedEntityRefs, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Damaged card", changedAudit.Reason);

        var clearedAudit = await ReadAuditAsync(database, clearedResult.AuditEntryId!.Value.Value);
        Assert.Equal(ClientAuditActions.CardCleared, clearedAudit.ActionType);
        Assert.Contains("BL-2002", clearedAudit.BeforeSummary, StringComparison.Ordinal);
        Assert.Equal("{}", clearedAudit.AfterSummary);
        Assert.Equal("Client left card program", clearedAudit.Comment);
        Assert.Equal(actor.AccountId.Value, clearedAudit.ActorAccountId);
        Assert.Equal("shared_reception_admin", clearedAudit.ActorAccountType);
        Assert.Equal("admin", clearedAudit.ActorRole);
        Assert.Equal(actor.SessionId.Value, clearedAudit.SessionId);
        Assert.Equal("front desk tablet", clearedAudit.DeviceLabel);
        Assert.Equal(TestNow.UtcDateTime, clearedAudit.OccurredAt);
        Assert.Equal(TestNow.UtcDateTime, clearedAudit.RecordedAt);

        Assert.Equal(2L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(3L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(3L, await CountRowsAsync(database, "command_idempotency_keys"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(distinct command_name) from bodylife.command_idempotency_keys"));
        Assert.Equal(
            "AssignOrChangeCard",
            await database.ExecuteScalarAsync<string>(
                "select min(command_name) from bodylife.command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task SameNormalizedNumberCanBeExplicitlyReissuedWithHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database, clientId, actor.AccountId.Value, "Reissue");
        var oldAssignmentId = await InsertCurrentCardAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "BL-REISSUE",
            InitialAssignedAt);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CardCommand(
                actor,
                "same-number-reissue",
                clientId,
                expectedCurrentCardAssignmentId: oldAssignmentId,
                newCardNumber: " bl - reissue ",
                reason: "Physical card reissued"),
            CancellationToken.None);

        AssertSuccessfulClientResult(result);
        var currentAssignmentId = Assert.IsType<Guid>(await ReadCurrentCardIdAsync(database, clientId));
        Assert.NotEqual(oldAssignmentId, currentAssignmentId);
        Assert.Equal("BL-REISSUE", (await ReadCardAsync(database, currentAssignmentId)).CardNumberNormalized);
        var oldAssignment = await ReadCardAsync(database, oldAssignmentId);
        Assert.False(oldAssignment.IsCurrent);
        Assert.Equal("Physical card reissued", oldAssignment.EndReason);
        Assert.Equal(
            2L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.client_card_assignments where card_number_normalized = 'BL-REISSUE'"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.client_card_assignments where card_number_normalized = 'BL-REISSUE' and is_current"));
        Assert.Equal(
            ClientAuditActions.CardChanged,
            (await ReadAuditAsync(database, result.AuditEntryId!.Value.Value)).ActionType);
    }

    [PostgreSqlFact]
    public async Task InactiveExpiredAndMismatchedActorsAreDeniedWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var inactiveActor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            isActive: false);
        var expiredActor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            sessionExpiresAt: TestNow.AddMinutes(-1));
        var validActor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var mismatchedActor = validActor with { Role = ActorRole.Owner };
        var actors = new[] { inactiveActor, expiredActor, mismatchedActor };
        var handler = CreateHandler(dbContext);
        var results = new List<CommandResult>();

        for (var index = 0; index < actors.Length; index++)
        {
            var clientId = Guid.NewGuid();
            await InsertClientAsync(database, clientId, actors[index].AccountId.Value, $"Denied{index}");
            results.Add(await handler.ExecuteAsync(
                CardCommand(
                    actors[index],
                    $"denied-card-{index}",
                    clientId,
                    newCardNumber: $"BL-DENIED-{index}"),
                CancellationToken.None));
        }

        Assert.All(results, result => AssertError(result, CommandErrorCode.PermissionDenied));
        Assert.Equal(0L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InvalidMissingStaleAndReasonlessCommandsDoNotMutateCardState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var currentClientId = Guid.NewGuid();
        var noCardClientId = Guid.NewGuid();
        await InsertClientAsync(database, currentClientId, actor.AccountId.Value, "Current");
        await InsertClientAsync(database, noCardClientId, actor.AccountId.Value, "NoCard");
        var currentAssignmentId = await InsertCurrentCardAsync(
            database,
            currentClientId,
            actor.AccountId.Value,
            "BL-STABLE",
            InitialAssignedAt);
        var handler = CreateHandler(dbContext);

        var missingResult = await handler.ExecuteAsync(
            CardCommand(actor, "missing-client", Guid.NewGuid(), newCardNumber: "BL-MISSING"),
            CancellationToken.None);
        var emptyClientIdResult = await handler.ExecuteAsync(
            CardCommand(actor, "empty-client", Guid.Empty, newCardNumber: "BL-EMPTY"),
            CancellationToken.None);
        var emptyExpectedIdResult = await handler.ExecuteAsync(
            CardCommand(
                actor,
                "empty-expected",
                currentClientId,
                expectedCurrentCardAssignmentId: Guid.Empty,
                newCardNumber: "BL-EMPTY-EXPECTED",
                reason: "Invalid expected id"),
            CancellationToken.None);
        var ambiguousIntentResult = await handler.ExecuteAsync(
            CardCommand(
                actor,
                "ambiguous-intent",
                noCardClientId,
                newCardNumber: "BL-AMBIGUOUS",
                clearCurrentCard: true),
            CancellationToken.None);
        var missingIntentResult = await handler.ExecuteAsync(
            CardCommand(
                actor,
                "missing-intent",
                noCardClientId,
                newCardNumber: null),
            CancellationToken.None);
        var clearMissingResult = await handler.ExecuteAsync(
            CardCommand(
                actor,
                "clear-missing",
                noCardClientId,
                newCardNumber: null,
                clearCurrentCard: true),
            CancellationToken.None);
        var staleResult = await handler.ExecuteAsync(
            CardCommand(
                actor,
                "stale-card",
                currentClientId,
                expectedCurrentCardAssignmentId: Guid.NewGuid(),
                newCardNumber: "BL-STALE",
                reason: "Stale edit"),
            CancellationToken.None);
        var reasonlessResult = await handler.ExecuteAsync(
            CardCommand(
                actor,
                "reasonless-card",
                currentClientId,
                expectedCurrentCardAssignmentId: currentAssignmentId,
                newCardNumber: "BL-NO-REASON"),
            CancellationToken.None);
        var earlyOccurredResult = await handler.ExecuteAsync(
            CardCommand(
                actor,
                "early-occurred",
                currentClientId,
                expectedCurrentCardAssignmentId: currentAssignmentId,
                newCardNumber: "BL-EARLY",
                reason: "Backdated incorrectly",
                occurredAt: InitialAssignedAt.AddMinutes(-1)),
            CancellationToken.None);
        var missingIdempotencyCommand = CardCommand(
            actor,
            "temporary-key",
            noCardClientId,
            newCardNumber: "BL-NO-KEY") with
        {
            Envelope = CardCommand(
                actor,
                "temporary-key",
                noCardClientId,
                newCardNumber: "BL-NO-KEY").Envelope with
            {
                IdempotencyKey = null,
            },
        };
        var missingIdempotencyResult = await handler.ExecuteAsync(
            missingIdempotencyCommand,
            CancellationToken.None);

        AssertError(missingResult, CommandErrorCode.NotFound);
        AssertError(emptyClientIdResult, CommandErrorCode.ValidationFailed);
        AssertError(emptyExpectedIdResult, CommandErrorCode.ValidationFailed);
        AssertError(ambiguousIntentResult, CommandErrorCode.ValidationFailed);
        AssertError(missingIntentResult, CommandErrorCode.ValidationFailed);
        AssertError(clearMissingResult, CommandErrorCode.ValidationFailed);
        AssertError(staleResult, CommandErrorCode.StaleState);
        AssertError(reasonlessResult, CommandErrorCode.ValidationFailed);
        Assert.Equal("reason", Assert.Single(reasonlessResult.Errors).Field);
        AssertError(earlyOccurredResult, CommandErrorCode.ValidationFailed);
        Assert.Equal("occurredAt", Assert.Single(earlyOccurredResult.Errors).Field);
        AssertError(missingIdempotencyResult, CommandErrorCode.ValidationFailed);
        Assert.Equal("idempotencyKey", Assert.Single(missingIdempotencyResult.Errors).Field);
        Assert.Equal(currentAssignmentId, await ReadCurrentCardIdAsync(database, currentClientId));
        Assert.Null(await ReadCurrentCardIdAsync(database, noCardClientId));
        Assert.Equal(1L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task CardCurrentOnAnotherClientBlocksChangeWithoutEndingTargetCard()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var targetClientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();
        await InsertClientAsync(database, targetClientId, actor.AccountId.Value, "Target");
        await InsertClientAsync(database, otherClientId, actor.AccountId.Value, "Other");
        var targetAssignmentId = await InsertCurrentCardAsync(
            database,
            targetClientId,
            actor.AccountId.Value,
            "BL-TARGET",
            InitialAssignedAt);
        await InsertCurrentCardAsync(
            database,
            otherClientId,
            actor.AccountId.Value,
            "BL-CONFLICT",
            InitialAssignedAt);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CardCommand(
                actor,
                "card-conflict",
                targetClientId,
                expectedCurrentCardAssignmentId: targetAssignmentId,
                newCardNumber: " bl-conflict ",
                reason: "Attempted reassignment"),
            CancellationToken.None);

        AssertError(result, CommandErrorCode.CardNumberAlreadyCurrent);
        Assert.Equal(targetAssignmentId, await ReadCurrentCardIdAsync(database, targetClientId));
        var targetAssignment = await ReadCardAsync(database, targetAssignmentId);
        Assert.True(targetAssignment.IsCurrent);
        Assert.Null(targetAssignment.EndedAt);
        Assert.Null(targetAssignment.EndReason);
        Assert.Equal(2L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentCardConflictRollsBackAnAlreadyEndedTargetAssignment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var migrationContext = database.CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var targetClientId = Guid.NewGuid();
        var competingClientId = Guid.NewGuid();
        await InsertClientAsync(database, targetClientId, actor.AccountId.Value, "RollbackTarget");
        await InsertClientAsync(database, competingClientId, actor.AccountId.Value, "RollbackCompetitor");
        var targetAssignmentId = await InsertCurrentCardAsync(
            database,
            targetClientId,
            actor.AccountId.Value,
            "BL-ROLLBACK-OLD",
            InitialAssignedAt);
        await using var competingConnection = new NpgsqlConnection(database.ConnectionString);
        await competingConnection.OpenAsync();
        await using var competingTransaction = await competingConnection.BeginTransactionAsync();
        await InsertCurrentCardAsync(
            competingConnection,
            competingTransaction,
            competingClientId,
            actor.AccountId.Value,
            "BL-ROLLBACK-RACE",
            TestNow);
        await using var commandContext = database.CreateDbContext();
        var commandTask = CreateHandler(commandContext).ExecuteAsync(
            CardCommand(
                actor,
                "rollback-race",
                targetClientId,
                expectedCurrentCardAssignmentId: targetAssignmentId,
                newCardNumber: "BL-ROLLBACK-RACE",
                reason: "Replace damaged card"),
            CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        await competingTransaction.CommitAsync();
        var result = await commandTask;

        Assert.Contains(
            Assert.Single(result.Errors).Code,
            new[] { CommandErrorCode.CardNumberAlreadyCurrent, CommandErrorCode.ConcurrencyConflict });
        Assert.Equal(targetAssignmentId, await ReadCurrentCardIdAsync(database, targetClientId));
        var targetAssignment = await ReadCardAsync(database, targetAssignmentId);
        Assert.True(targetAssignment.IsCurrent);
        Assert.Null(targetAssignment.EndedAt);
        Assert.Null(targetAssignment.EndReason);
        Assert.NotNull(await ReadCurrentCardIdAsync(database, competingClientId));
        Assert.Equal(2L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task IdempotentReplayReturnsOriginalResultAndRejectsChangedPayload()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database, clientId, actor.AccountId.Value, "Replay");
        var handler = CreateHandler(dbContext);
        var command = CardCommand(
            actor,
            "card-replay",
            clientId,
            newCardNumber: "BL-REPLAY");

        var firstResult = await handler.ExecuteAsync(command, CancellationToken.None);
        var replayResult = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with
                {
                    RequestCorrelationId = new RequestCorrelationId("card-replay-correlation-2"),
                },
                NewCardNumber = " bl - replay ",
            },
            CancellationToken.None);
        var changedPayloadResult = await handler.ExecuteAsync(
            command with { NewCardNumber = "BL-DIFFERENT" },
            CancellationToken.None);

        AssertSuccessfulClientResult(firstResult);
        AssertSuccessfulClientResult(replayResult);
        Assert.Equal(firstResult.PrimaryEntityId, replayResult.PrimaryEntityId);
        Assert.Equal(firstResult.AuditEntryId, replayResult.AuditEntryId);
        AssertError(changedPayloadResult, CommandErrorCode.DuplicateSubmission);
        Assert.Equal(1L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task PaperFallbackUsesOccurredTimeAndPreservesRecordedTimeAndOrigin()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database, clientId, actor.AccountId.Value, "Fallback");
        var oldAssignmentId = await InsertCurrentCardAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "BL-OLD-FALLBACK",
            TestNow.AddDays(-4));
        var occurredAt = TestNow.AddDays(-2);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CardCommand(
                actor,
                "paper-fallback-card",
                clientId,
                expectedCurrentCardAssignmentId: oldAssignmentId,
                newCardNumber: "BL-NEW-FALLBACK",
                reason: "Paper log line 7",
                entryOrigin: EntryOrigin.PaperFallback,
                occurredAt: occurredAt),
            CancellationToken.None);

        AssertSuccessfulClientResult(result);
        var oldAssignment = await ReadCardAsync(database, oldAssignmentId);
        Assert.Equal(occurredAt.UtcDateTime, oldAssignment.EndedAt);
        var newAssignmentId = Assert.IsType<Guid>(await ReadCurrentCardIdAsync(database, clientId));
        var newAssignment = await ReadCardAsync(database, newAssignmentId);
        Assert.Equal(occurredAt.UtcDateTime, newAssignment.AssignedAt);
        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal("paper_fallback", audit.EntryOrigin);
        Assert.Equal(occurredAt.UtcDateTime, audit.OccurredAt);
        Assert.Equal(TestNow.UtcDateTime, audit.RecordedAt);
        Assert.Equal("Paper log line 7", audit.Reason);
    }

    [PostgreSqlFact]
    public async Task ConcurrentCommandsForOneExpectedClientStateCommitOnlyOneWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var migrationContext = database.CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database, clientId, actor.AccountId.Value, "ConcurrentTarget");
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var firstTask = CreateHandler(firstContext).ExecuteAsync(
            CardCommand(actor, "same-client-1", clientId, newCardNumber: "BL-SAME-CLIENT-1"),
            CancellationToken.None);
        var secondTask = CreateHandler(secondContext).ExecuteAsync(
            CardCommand(actor, "same-client-2", clientId, newCardNumber: "BL-SAME-CLIENT-2"),
            CancellationToken.None);

        var results = await Task.WhenAll(firstTask, secondTask);

        AssertSuccessfulClientResult(Assert.Single(results, result => result.Status == CommandStatus.Success));
        var rejectedResult = Assert.Single(results, result => result.Status == CommandStatus.Error);
        Assert.Contains(
            Assert.Single(rejectedResult.Errors).Code,
            new[] { CommandErrorCode.StaleState, CommandErrorCode.ConcurrencyConflict });
        Assert.Equal(1L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentCommandsForOneCardAcrossClientsCommitOnlyOneWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var migrationContext = database.CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var firstClientId = Guid.NewGuid();
        var secondClientId = Guid.NewGuid();
        await InsertClientAsync(database, firstClientId, actor.AccountId.Value, "FirstConcurrent");
        await InsertClientAsync(database, secondClientId, actor.AccountId.Value, "SecondConcurrent");
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var firstTask = CreateHandler(firstContext).ExecuteAsync(
            CardCommand(actor, "same-card-1", firstClientId, newCardNumber: "BL-SHARED-CONCURRENT"),
            CancellationToken.None);
        var secondTask = CreateHandler(secondContext).ExecuteAsync(
            CardCommand(actor, "same-card-2", secondClientId, newCardNumber: "BL-SHARED-CONCURRENT"),
            CancellationToken.None);

        var results = await Task.WhenAll(firstTask, secondTask);

        AssertSuccessfulClientResult(Assert.Single(results, result => result.Status == CommandStatus.Success));
        var rejectedResult = Assert.Single(results, result => result.Status == CommandStatus.Error);
        Assert.Contains(
            Assert.Single(rejectedResult.Errors).Code,
            new[] { CommandErrorCode.CardNumberAlreadyCurrent, CommandErrorCode.ConcurrencyConflict });
        Assert.Equal(1L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static AssignOrChangeCardCommandHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new AssignOrChangeCardCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new FixedTimeProvider(TestNow));
    }

    private static AssignOrChangeCardCommand CardCommand(
        ActorContext actor,
        string idempotencyKey,
        Guid clientId,
        Guid? expectedCurrentCardAssignmentId = null,
        string? newCardNumber = null,
        bool clearCurrentCard = false,
        string? reason = null,
        string? comment = null,
        EntryOrigin entryOrigin = EntryOrigin.Normal,
        DateTimeOffset? occurredAt = null)
    {
        return new AssignOrChangeCardCommand(
            new CommandEnvelope(
                actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                entryOrigin,
                occurredAt ?? TestNow,
                idempotencyKey,
                reason,
                comment),
            clientId,
            expectedCurrentCardAssignmentId,
            newCardNumber,
            clearCurrentCard);
    }

    private static async Task<ActorContext> SeedActorAsync(
        PostgreSqlTestDatabase database,
        ActorRole role,
        AccountKind accountKind,
        bool isActive = true,
        DateTimeOffset? sessionExpiresAt = null,
        string? deviceLabel = "test device")
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var accountCommand = connection.CreateCommand())
        {
            accountCommand.CommandText =
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
                    @id,
                    @display_name,
                    @account_type,
                    @role,
                    @is_active,
                    @created_at,
                    @deactivated_at)
                """;
            accountCommand.Parameters.AddWithValue("id", accountId);
            accountCommand.Parameters.AddWithValue("display_name", $"{accountKind} card actor");
            accountCommand.Parameters.AddWithValue("account_type", MapAccountKind(accountKind));
            accountCommand.Parameters.AddWithValue("role", MapRole(role));
            accountCommand.Parameters.AddWithValue("is_active", isActive);
            accountCommand.Parameters.AddWithValue("created_at", TestNow.AddHours(-1));
            accountCommand.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value = isActive
                ? DBNull.Value
                : TestNow;
            await accountCommand.ExecuteNonQueryAsync();
        }

        await using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.CommandText =
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
                    @id,
                    @account_id,
                    @device_label,
                    @started_at,
                    @expires_at,
                    @ended_at,
                    @last_seen_at)
                """;
            sessionCommand.Parameters.AddWithValue("id", sessionId);
            sessionCommand.Parameters.AddWithValue("account_id", accountId);
            sessionCommand.Parameters.Add("device_label", NpgsqlDbType.Varchar).Value =
                deviceLabel ?? (object)DBNull.Value;
            sessionCommand.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
            sessionCommand.Parameters.AddWithValue(
                "expires_at",
                sessionExpiresAt ?? TestNow.AddHours(11));
            sessionCommand.Parameters.Add("ended_at", NpgsqlDbType.TimestampTz).Value = DBNull.Value;
            sessionCommand.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
            await sessionCommand.ExecuteNonQueryAsync();
        }

        return new ActorContext(
            new AccountId(accountId),
            role,
            accountKind,
            new SessionId(sessionId),
            deviceLabel);
    }

    private static async Task InsertClientAsync(
        PostgreSqlTestDatabase database,
        Guid clientId,
        Guid actorAccountId,
        string surname)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.clients (
                id,
                surname,
                name,
                normalized_full_name,
                operational_status,
                created_at,
                created_by_account_id,
                updated_at)
            values (
                @id,
                @surname,
                'Card',
                @normalized_full_name,
                'active',
                @created_at,
                @created_by_account_id,
                @updated_at)
            """;
        command.Parameters.AddWithValue("id", clientId);
        command.Parameters.AddWithValue("surname", surname);
        command.Parameters.AddWithValue(
            "normalized_full_name",
            ClientSearchNormalizer.NormalizeFullName(surname, "Card", patronymic: null));
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-5));
        command.Parameters.AddWithValue("created_by_account_id", actorAccountId);
        command.Parameters.AddWithValue("updated_at", TestNow.AddDays(-5));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> InsertCurrentCardAsync(
        PostgreSqlTestDatabase database,
        Guid clientId,
        Guid actorAccountId,
        string cardNumber,
        DateTimeOffset assignedAt)
    {
        var assignmentId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        return await InsertCurrentCardAsync(
            connection,
            transaction: null,
            clientId,
            actorAccountId,
            cardNumber,
            assignedAt,
            assignmentId);
    }

    private static async Task<Guid> InsertCurrentCardAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid clientId,
        Guid actorAccountId,
        string cardNumber,
        DateTimeOffset assignedAt,
        Guid? assignmentId = null)
    {
        var resolvedAssignmentId = assignmentId ?? Guid.NewGuid();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into bodylife.client_card_assignments (
                id,
                client_id,
                card_number_raw,
                card_number_normalized,
                assigned_at,
                assigned_by_account_id,
                is_current)
            values (
                @id,
                @client_id,
                @card_number_raw,
                @card_number_normalized,
                @assigned_at,
                @assigned_by_account_id,
                true)
            """;
        command.Parameters.AddWithValue("id", resolvedAssignmentId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("card_number_raw", cardNumber);
        command.Parameters.AddWithValue(
            "card_number_normalized",
            ClientSearchNormalizer.NormalizeCardNumber(cardNumber));
        command.Parameters.AddWithValue("assigned_at", assignedAt);
        command.Parameters.AddWithValue("assigned_by_account_id", actorAccountId);
        await command.ExecuteNonQueryAsync();
        return resolvedAssignmentId;
    }

    private static async Task<Guid?> ReadCurrentCardIdAsync(
        PostgreSqlTestDatabase database,
        Guid clientId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select id
            from bodylife.client_card_assignments
            where client_id = @client_id
              and is_current
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : (Guid)result;
    }

    private static async Task<CardRow> ReadCardAsync(
        PostgreSqlTestDatabase database,
        Guid assignmentId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select card_number_raw,
                   card_number_normalized,
                   assigned_at,
                   ended_at,
                   ended_by_account_id,
                   end_reason,
                   is_current
            from bodylife.client_card_assignments
            where id = @id
            """;
        command.Parameters.AddWithValue("id", assignmentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new CardRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetDateTime(2),
            reader.IsDBNull(3) ? null : reader.GetDateTime(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetBoolean(6));
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
                   request_correlation_id,
                   entry_origin,
                   idempotency_key,
                   related_entity_refs::text,
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
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetDateTime(8),
            reader.GetDateTime(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            reader.GetString(17));
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>($"select count(*) from bodylife.{tableName}");
    }

    private static void AssertSuccessfulClientResult(CommandResult result)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.NotNull(result.PrimaryEntityId);
        Assert.Equal("client", result.PrimaryEntityId.Value.Type);
        Assert.Equal(result.PrimaryEntityId, result.RereadTargetId);
        Assert.NotNull(result.AuditEntryId);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    private static void AssertError(CommandResult result, CommandErrorCode errorCode)
    {
        Assert.Equal(CommandStatus.Error, result.Status);
        Assert.Contains(result.Errors, error => error.Code == errorCode);
        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
        Assert.Null(result.AuditEntryId);
    }

    private static string MapAccountKind(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.Owner => "owner",
            AccountKind.NamedAdmin => "named_admin",
            AccountKind.SharedReceptionAdmin => "shared_reception_admin",
            _ => throw new ArgumentOutOfRangeException(nameof(accountKind), accountKind, null),
        };
    }

    private static string MapRole(ActorRole role)
    {
        return role switch
        {
            ActorRole.Owner => "owner",
            ActorRole.Admin => "admin",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private sealed record CardRow(
        string CardNumberRaw,
        string CardNumberNormalized,
        DateTime AssignedAt,
        DateTime? EndedAt,
        Guid? EndedByAccountId,
        string? EndReason,
        bool IsCurrent);

    private sealed record AuditRow(
        string ActionType,
        string EntityType,
        Guid EntityId,
        Guid ActorAccountId,
        string ActorAccountType,
        string ActorRole,
        Guid SessionId,
        string? DeviceLabel,
        DateTime OccurredAt,
        DateTime RecordedAt,
        string? Reason,
        string? Comment,
        string RequestCorrelationId,
        string EntryOrigin,
        string? IdempotencyKey,
        string RelatedEntityRefs,
        string BeforeSummary,
        string AfterSummary);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
