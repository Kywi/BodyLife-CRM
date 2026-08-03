using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlPaperFallbackEntryBatchCommandTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        8,
        3,
        12,
        0,
        0,
        TimeSpan.Zero);

    private static readonly DateTimeOffset PaperOccurredAt = new(
        2026,
        8,
        2,
        9,
        30,
        0,
        TimeSpan.Zero);

    private static readonly DateOnly BusinessDateStart = new(2026, 8, 1);
    private static readonly DateOnly BusinessDateEnd = new(2026, 8, 3);

    [PostgreSqlFact]
    public async Task OwnerCreatesNormalizedBatchWithAuditAndIdempotentReplay()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var handler = CreateBatchHandler(dbContext);
        var command = CreateBatchCommand(
            owner,
            "paper-batch-1",
            " pf-2026-0042 ",
            note: " Front desk outage ");

        var result = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var mismatch = await handler.ExecuteAsync(
            command with
            {
                Note = "Different note",
            },
            CancellationToken.None);

        AssertBatchSuccess(result);
        AssertEquivalentSuccess(result, replay);
        AssertError(mismatch, CommandErrorCode.DuplicateSubmission, "idempotencyKey");

        var batch = Assert.Single(await ReadBatchesAsync(database));
        Assert.Equal(result.PrimaryEntityId!.Value.Value, batch.Id);
        Assert.Equal("paper_fallback", batch.BatchType);
        Assert.Equal("PF-2026-0042", batch.PaperSheetNumber);
        Assert.Equal(BusinessDateStart, batch.BusinessDateStart);
        Assert.Equal(BusinessDateEnd, batch.BusinessDateEnd);
        Assert.Equal(TestNow, batch.RecordedAt);
        Assert.Equal(owner.AccountId.Value, batch.RecordedByAccountId);
        Assert.Equal("Front desk outage", batch.Note);
        Assert.Null(batch.ReconciledAt);
        Assert.Null(batch.ReconciledByAccountId);

        var audit = Assert.Single(await ReadAuditsAsync(database));
        Assert.Equal(result.AuditEntryId!.Value.Value, audit.Id);
        Assert.Equal(PaperFallbackAuditActions.BatchCreated, audit.ActionType);
        Assert.Equal(PaperFallbackAuditActions.BatchEntityType, audit.EntityType);
        Assert.Equal(batch.Id, audit.EntityId);
        Assert.Equal("paper_fallback", audit.EntryOrigin);
        Assert.Equal(PaperOccurredAt, audit.OccurredAt);
        Assert.Equal(TestNow, audit.RecordedAt);
        Assert.Equal("Outage recovery", audit.Reason);
        Assert.Equal("Paper sheet registration", audit.Comment);
        Assert.Equal("paper-batch-1", audit.IdempotencyKey);
        Assert.Equal("{}", audit.RelatedEntityRefsJson);
        Assert.Equal("{}", audit.BeforeSummaryJson);
        using (var after = JsonDocument.Parse(audit.AfterSummaryJson))
        {
            var storedBatch = after.RootElement.GetProperty("entryBatch");
            Assert.Equal(batch.Id, storedBatch.GetProperty("id").GetGuid());
            Assert.Equal("PF-2026-0042", storedBatch.GetProperty("paperSheetNumber").GetString());
        }

        var idempotency = Assert.Single(await ReadIdempotencyAsync(database));
        Assert.Equal("CreatePaperFallbackBatch", idempotency.CommandName);
        Assert.Equal(batch.Id, idempotency.PrimaryEntityId);
        Assert.Equal(batch.Id, idempotency.RereadTargetId);
        Assert.Equal(audit.Id, idempotency.AuditEntryId);
        Assert.Equal("succeeded", idempotency.Status);
        Assert.False(string.IsNullOrWhiteSpace(idempotency.ResultFingerprint));

        var timelineResult = await new GetAuditTimelineQueryHandler(
                dbContext,
                new FixedTimeProvider(TestNow))
            .ExecuteAsync(
                new GetAuditTimelineQuery(
                    owner,
                    EntityType: AuditTimelineEntityType.EntryBatch,
                    EntityId: batch.Id,
                    ActionTypes: [PaperFallbackAuditActions.BatchCreated]),
                CancellationToken.None);
        Assert.Equal(GetAuditTimelineStatus.Success, timelineResult.Status);
        var timelineEntry = Assert.Single(timelineResult.Page!.Items);
        Assert.Equal(AuditTimelineEntityType.EntryBatch, timelineEntry.EntityType);
        Assert.Equal(batch.Id, timelineEntry.EntityId);
    }

    [PostgreSqlFact]
    public async Task AdminCreatesExplicitAndAutomaticRowsWithStableProvenance()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var admin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin);
        var batchId = await CreateBatchAsync(
            dbContext,
            admin,
            "paper-row-batch",
            "PF-2026-0050");
        var handler = CreateRowHandler(dbContext);
        var explicitCommand = CreateRowCommand(
            admin,
            batchId,
            "paper-row-7",
            lineNumber: 7,
            PaperFallbackEventType.Visit,
            " Visit copied from sheet ");
        var automaticCommand = CreateRowCommand(
            admin,
            batchId,
            "paper-row-auto",
            lineNumber: null,
            PaperFallbackEventType.Payment,
            "Cash payment copied from sheet");

        var explicitResult = await handler.ExecuteAsync(
            explicitCommand,
            CancellationToken.None);
        var replay = await handler.ExecuteAsync(
            explicitCommand,
            CancellationToken.None);
        var automaticResult = await handler.ExecuteAsync(
            automaticCommand,
            CancellationToken.None);
        var mismatch = await handler.ExecuteAsync(
            automaticCommand with { Explanation = "Changed explanation" },
            CancellationToken.None);

        AssertRowSuccess(explicitResult, batchId);
        AssertEquivalentSuccess(explicitResult, replay);
        AssertRowSuccess(automaticResult, batchId);
        AssertError(mismatch, CommandErrorCode.DuplicateSubmission, "idempotencyKey");

        var rows = await ReadRowsAsync(database);
        Assert.Equal(2, rows.Length);
        Assert.Equal([7, 8], rows.Select(row => row.LineNumber).ToArray());
        Assert.Equal("visit", rows[0].EventType);
        Assert.Equal("Visit copied from sheet", rows[0].Explanation);
        Assert.Equal("payment", rows[1].EventType);
        Assert.Equal(PaperOccurredAt, rows[0].OccurredAt);
        Assert.Equal(TestNow, rows[0].RecordedAt);
        Assert.All(rows, row => Assert.Equal(admin.AccountId.Value, row.RecordedByAccountId));
        Assert.All(rows, row => Assert.Equal(admin.SessionId.Value, row.SessionId));

        var rowAudits = (await ReadAuditsAsync(database))
            .Where(audit => audit.ActionType == PaperFallbackAuditActions.RowCreated)
            .OrderBy(audit => audit.Id)
            .ToArray();
        Assert.Equal(2, rowAudits.Length);
        Assert.All(rowAudits, audit =>
        {
            Assert.Equal(PaperFallbackAuditActions.RowEntityType, audit.EntityType);
            Assert.Equal("paper_fallback", audit.EntryOrigin);
            using var related = JsonDocument.Parse(audit.RelatedEntityRefsJson);
            Assert.Equal(batchId, related.RootElement.GetProperty("entryBatchId").GetGuid());
        });
        Assert.Equal(3L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InvalidOrUnauthorizedRequestsLeavePaperMetadataUntouched()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var inactiveOwner = await SeedActorAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner,
            isActive: false);
        var mismatchedActor = inactiveOwner with
        {
            Role = ActorRole.Admin,
            AccountKind = AccountKind.Owner,
        };
        var batchHandler = CreateBatchHandler(dbContext);

        var invalidShape = await batchHandler.ExecuteAsync(
            CreateBatchCommand(mismatchedActor, "invalid-shape", "PF-INVALID-1"),
            CancellationToken.None);
        var inactive = await batchHandler.ExecuteAsync(
            CreateBatchCommand(inactiveOwner, "inactive-owner", "PF-INVALID-2"),
            CancellationToken.None);
        var wrongOrigin = await batchHandler.ExecuteAsync(
            CreateBatchCommand(inactiveOwner, "wrong-origin", "PF-INVALID-3") with
            {
                Envelope = CreateEnvelope(inactiveOwner, "wrong-origin") with
                {
                    EntryOrigin = EntryOrigin.Normal,
                },
            },
            CancellationToken.None);
        var missingReason = await batchHandler.ExecuteAsync(
            CreateBatchCommand(inactiveOwner, "missing-reason", "PF-INVALID-4") with
            {
                Envelope = CreateEnvelope(inactiveOwner, "missing-reason") with
                {
                    Reason = null,
                    Comment = null,
                },
            },
            CancellationToken.None);
        var reversedRange = await batchHandler.ExecuteAsync(
            CreateBatchCommand(inactiveOwner, "reversed-range", "PF-INVALID-5") with
            {
                BusinessDateStart = BusinessDateEnd,
                BusinessDateEnd = BusinessDateStart,
            },
            CancellationToken.None);

        AssertError(invalidShape, CommandErrorCode.PermissionDenied);
        AssertError(inactive, CommandErrorCode.PermissionDenied);
        AssertError(wrongOrigin, CommandErrorCode.ValidationFailed, "entryOrigin");
        AssertError(missingReason, CommandErrorCode.ReasonRequired, "reason");
        AssertError(reversedRange, CommandErrorCode.ValidationFailed, "businessDateEnd");
        Assert.Equal(0L, await CountRowsAsync(database, "entry_batches"));
        Assert.Equal(0L, await CountRowsAsync(database, "entry_batch_rows"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task DuplicateSheetAndLineRollbackAuditAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var batchHandler = CreateBatchHandler(dbContext);
        var firstBatch = await batchHandler.ExecuteAsync(
            CreateBatchCommand(owner, "batch-first", "PF-2026-0060"),
            CancellationToken.None);
        var duplicateBatch = await batchHandler.ExecuteAsync(
            CreateBatchCommand(owner, "batch-duplicate", " pf-2026-0060 "),
            CancellationToken.None);

        AssertBatchSuccess(firstBatch);
        AssertError(
            duplicateBatch,
            CommandErrorCode.DuplicateSubmission,
            "paperSheetNumber");
        Assert.Equal(1L, await CountRowsAsync(database, "entry_batches"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));

        var batchId = firstBatch.PrimaryEntityId!.Value.Value;
        var rowHandler = CreateRowHandler(dbContext);
        var firstRow = await rowHandler.ExecuteAsync(
            CreateRowCommand(
                owner,
                batchId,
                "row-first",
                3,
                PaperFallbackEventType.Freeze,
                "Freeze from line 3"),
            CancellationToken.None);
        var duplicateRow = await rowHandler.ExecuteAsync(
            CreateRowCommand(
                owner,
                batchId,
                "row-duplicate",
                3,
                PaperFallbackEventType.Freeze,
                "Second attempt for line 3"),
            CancellationToken.None);

        AssertRowSuccess(firstRow, batchId);
        AssertError(duplicateRow, CommandErrorCode.DuplicateSubmission, "lineNumber");
        Assert.Equal(1L, await CountRowsAsync(database, "entry_batch_rows"));
        Assert.Equal(2L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(2L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentExplicitLineCreatesExactlyOneRow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateDbContext())
        {
            await setupContext.Database.MigrateAsync();
        }

        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        Guid batchId;
        await using (var batchContext = database.CreateDbContext())
        {
            batchId = await CreateBatchAsync(
                batchContext,
                owner,
                "concurrent-batch",
                "PF-2026-0070");
        }

        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var firstHandler = CreateRowHandler(firstContext);
        var secondHandler = CreateRowHandler(secondContext);
        var firstCommand = CreateRowCommand(
            owner,
            batchId,
            "concurrent-row-a",
            11,
            PaperFallbackEventType.Visit,
            "First concurrent copy");
        var secondCommand = CreateRowCommand(
            owner,
            batchId,
            "concurrent-row-b",
            11,
            PaperFallbackEventType.Visit,
            "Second concurrent copy");

        var results = await Task.WhenAll(
            firstHandler.ExecuteAsync(firstCommand, CancellationToken.None),
            secondHandler.ExecuteAsync(secondCommand, CancellationToken.None));

        AssertRowSuccess(
            Assert.Single(results, result => result.Status == CommandStatus.Success),
            batchId);
        AssertError(
            Assert.Single(results, result => result.Status == CommandStatus.Error),
            CommandErrorCode.DuplicateSubmission,
            "lineNumber");
        Assert.Equal(1L, await CountRowsAsync(database, "entry_batch_rows"));
        Assert.Equal(2L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(2L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentIdenticalBatchRequestsReplayOneCommittedResult()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateDbContext())
        {
            await setupContext.Database.MigrateAsync();
        }

        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var command = CreateBatchCommand(
            owner,
            "same-batch-key",
            "PF-2026-0071");
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateBatchHandler(firstContext).ExecuteAsync(command, CancellationToken.None),
            CreateBatchHandler(secondContext).ExecuteAsync(command, CancellationToken.None));

        Assert.All(results, AssertBatchSuccess);
        Assert.Single(results.Select(result => result.PrimaryEntityId).Distinct());
        Assert.Single(results.Select(result => result.AuditEntryId).Distinct());
        Assert.Equal(1L, await CountRowsAsync(database, "entry_batches"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentIdenticalRowRequestsReplayOneCommittedResult()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateDbContext())
        {
            await setupContext.Database.MigrateAsync();
        }

        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        Guid batchId;
        await using (var batchContext = database.CreateDbContext())
        {
            batchId = await CreateBatchAsync(
                batchContext,
                owner,
                "same-row-batch",
                "PF-2026-0072");
        }

        var command = CreateRowCommand(
            owner,
            batchId,
            "same-row-key",
            4,
            PaperFallbackEventType.Payment,
            "One physical paper line");
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateRowHandler(firstContext).ExecuteAsync(command, CancellationToken.None),
            CreateRowHandler(secondContext).ExecuteAsync(command, CancellationToken.None));

        Assert.All(results, result => AssertRowSuccess(result, batchId));
        Assert.Single(results.Select(result => result.PrimaryEntityId).Distinct());
        Assert.Single(results.Select(result => result.AuditEntryId).Distinct());
        Assert.Equal(1L, await CountRowsAsync(database, "entry_batch_rows"));
        Assert.Equal(2L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(2L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task OutOfRangeAndReconciledBatchRejectRowsWithoutPartialState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var batchId = await CreateBatchAsync(
            dbContext,
            owner,
            "range-batch",
            "PF-2026-0080");
        var rowHandler = CreateRowHandler(dbContext);
        var outOfRange = await rowHandler.ExecuteAsync(
            CreateRowCommand(
                owner,
                batchId,
                "outside-range",
                1,
                PaperFallbackEventType.Payment,
                "Outside range") with
            {
                Envelope = CreateEnvelope(
                    owner,
                    "outside-range",
                    PaperOccurredAt.AddDays(5)),
            },
            CancellationToken.None);

        AssertError(outOfRange, CommandErrorCode.ValidationFailed, "occurredAt");
        await ReconcileBatchAsync(database, batchId, owner.AccountId.Value);

        var reconciled = await rowHandler.ExecuteAsync(
            CreateRowCommand(
                owner,
                batchId,
                "reconciled-row",
                1,
                PaperFallbackEventType.Payment,
                "Attempt after reconciliation"),
            CancellationToken.None);

        AssertError(reconciled, CommandErrorCode.StaleState, "entryBatchId");
        Assert.Equal(0L, await CountRowsAsync(database, "entry_batch_rows"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task PostgreSqlEnforcesParentRangeAndSingleEntityOwnership()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var batchId = await CreateBatchAsync(
            dbContext,
            owner,
            "constraint-batch",
            "PF-2026-0090");
        var rowHandler = CreateRowHandler(dbContext);
        var first = await rowHandler.ExecuteAsync(
            CreateRowCommand(
                owner,
                batchId,
                "constraint-row-1",
                1,
                PaperFallbackEventType.Visit,
                "First linked row"),
            CancellationToken.None);
        var second = await rowHandler.ExecuteAsync(
            CreateRowCommand(
                owner,
                batchId,
                "constraint-row-2",
                2,
                PaperFallbackEventType.Visit,
                "Second linked row"),
            CancellationToken.None);
        AssertRowSuccess(first, batchId);
        AssertRowSuccess(second, batchId);

        var outsideRange = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertRawRowAsync(
                database,
                owner,
                batchId,
                3,
                PaperOccurredAt.AddDays(5)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, outsideRange.SqlState);
        Assert.Equal("ck_entry_batch_rows_parent", outsideRange.ConstraintName);

        var entityId = Guid.NewGuid();
        await InsertEntityLinkAsync(
            database,
            first.PrimaryEntityId!.Value.Value,
            "visit",
            entityId);
        var duplicateOwnership = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertEntityLinkAsync(
                database,
                second.PrimaryEntityId!.Value.Value,
                "visit",
                entityId));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateOwnership.SqlState);
        Assert.Equal(
            "ux_entry_batch_row_entities_entity",
            duplicateOwnership.ConstraintName);
        Assert.Equal(2L, await CountRowsAsync(database, "entry_batch_rows"));
        Assert.Equal(1L, await CountRowsAsync(database, "entry_batch_row_entities"));
    }

    [PostgreSqlFact]
    public async Task PostgreSqlSupportsManualBatchTypeAndProtectsParentSourceContext()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var batchId = await InsertManualBatchAsync(database, owner);

        await InsertRawRowAsync(
            database,
            owner,
            batchId,
            1,
            PaperOccurredAt);
        var immutable = await Assert.ThrowsAsync<PostgresException>(
            () => UpdateBatchBusinessRangeAsync(database, batchId));

        Assert.Equal(PostgresErrorCodes.CheckViolation, immutable.SqlState);
        Assert.Equal("ck_entry_batches_immutable", immutable.ConstraintName);
        var batch = Assert.Single(await ReadBatchesAsync(database));
        Assert.Equal("manual_backfill", batch.BatchType);
        Assert.Equal(BusinessDateStart, batch.BusinessDateStart);
        Assert.Equal(BusinessDateEnd, batch.BusinessDateEnd);
        Assert.Equal(1L, await CountRowsAsync(database, "entry_batch_rows"));
    }

    private static CreatePaperFallbackBatchCommandHandler CreateBatchHandler(
        BodyLife.Crm.Infrastructure.Persistence.BodyLifeDbContext dbContext) =>
        new(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new FixedTimeProvider(TestNow));

    private static CreatePaperFallbackBatchRowCommandHandler CreateRowHandler(
        BodyLife.Crm.Infrastructure.Persistence.BodyLifeDbContext dbContext) =>
        new(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new FixedTimeProvider(TestNow));

    private static CreatePaperFallbackBatchCommand CreateBatchCommand(
        ActorContext actor,
        string idempotencyKey,
        string paperSheetNumber,
        string? note = "Recovered after outage") =>
        new(
            CreateEnvelope(actor, idempotencyKey),
            paperSheetNumber,
            BusinessDateStart,
            BusinessDateEnd,
            note);

    private static CreatePaperFallbackBatchRowCommand CreateRowCommand(
        ActorContext actor,
        Guid batchId,
        string idempotencyKey,
        int? lineNumber,
        PaperFallbackEventType eventType,
        string explanation) =>
        new(
            CreateEnvelope(actor, idempotencyKey),
            batchId,
            lineNumber,
            eventType,
            explanation);

    private static CommandEnvelope CreateEnvelope(
        ActorContext actor,
        string idempotencyKey,
        DateTimeOffset? occurredAt = null) =>
        new(
            actor,
            new RequestCorrelationId($"correlation-{idempotencyKey}"),
            EntryOrigin.PaperFallback,
            occurredAt ?? PaperOccurredAt,
            idempotencyKey,
            "Outage recovery",
            "Paper sheet registration");

    private static async Task<Guid> CreateBatchAsync(
        BodyLife.Crm.Infrastructure.Persistence.BodyLifeDbContext dbContext,
        ActorContext actor,
        string idempotencyKey,
        string paperSheetNumber)
    {
        var result = await CreateBatchHandler(dbContext).ExecuteAsync(
            CreateBatchCommand(actor, idempotencyKey, paperSheetNumber),
            CancellationToken.None);
        AssertBatchSuccess(result);
        return result.PrimaryEntityId!.Value.Value;
    }

    private static async Task<ActorContext> SeedActorAsync(
        PostgreSqlTestDatabase database,
        ActorRole role,
        AccountKind accountKind,
        bool isActive = true)
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
            accountCommand.Parameters.AddWithValue("display_name", $"{accountKind} paper test");
            accountCommand.Parameters.AddWithValue("account_type", MapAccountKind(accountKind));
            accountCommand.Parameters.AddWithValue("role", MapRole(role));
            accountCommand.Parameters.AddWithValue("is_active", isActive);
            accountCommand.Parameters.AddWithValue("created_at", TestNow.AddHours(-1));
            accountCommand.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value =
                isActive ? DBNull.Value : TestNow;
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
                    null,
                    @last_seen_at)
                """;
            sessionCommand.Parameters.AddWithValue("id", sessionId);
            sessionCommand.Parameters.AddWithValue("account_id", accountId);
            sessionCommand.Parameters.AddWithValue("device_label", "paper test tablet");
            sessionCommand.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
            sessionCommand.Parameters.AddWithValue("expires_at", TestNow.AddHours(12));
            sessionCommand.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-1));
            await sessionCommand.ExecuteNonQueryAsync();
        }

        return new ActorContext(
            new AccountId(accountId),
            role,
            accountKind,
            new SessionId(sessionId),
            "paper test tablet");
    }

    private static async Task<BatchRow[]> ReadBatchesAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                id,
                batch_type,
                paper_sheet_number,
                business_date_start,
                business_date_end,
                recorded_at,
                recorded_by_account_id,
                reconciled_at,
                reconciled_by_account_id,
                note
            from bodylife.entry_batches
            order by paper_sheet_number
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<BatchRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new BatchRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return [.. rows];
    }

    private static async Task<PaperRow[]> ReadRowsAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                id,
                entry_batch_id,
                line_number,
                event_type,
                occurred_at,
                explanation,
                recorded_at,
                recorded_by_account_id,
                session_id
            from bodylife.entry_batch_rows
            order by line_number
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<PaperRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new PaperRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetGuid(7),
                reader.GetGuid(8)));
        }

        return [.. rows];
    }

    private static async Task<AuditRow[]> ReadAuditsAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                id,
                action_type,
                entity_type,
                entity_id,
                entry_origin,
                occurred_at,
                recorded_at,
                reason,
                comment,
                idempotency_key,
                related_entity_refs::text,
                before_summary::text,
                after_summary::text
            from bodylife.business_audit_entries
            order by recorded_at, id
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<AuditRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new AuditRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12)));
        }

        return [.. rows];
    }

    private static async Task<IdempotencyRow[]> ReadIdempotencyAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                command_name,
                primary_entity_id,
                reread_target_id,
                audit_entry_id,
                status,
                result_fingerprint
            from bodylife.command_idempotency_keys
            order by command_name, idempotency_key
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<IdempotencyRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new IdempotencyRow(
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return [.. rows];
    }

    private static async Task ReconcileBatchAsync(
        PostgreSqlTestDatabase database,
        Guid batchId,
        Guid accountId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.entry_batches
            set reconciled_at = @reconciled_at,
                reconciled_by_account_id = @account_id
            where id = @batch_id
            """;
        command.Parameters.AddWithValue("reconciled_at", TestNow.AddMinutes(1));
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("batch_id", batchId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<Guid> InsertManualBatchAsync(
        PostgreSqlTestDatabase database,
        ActorContext actor)
    {
        var batchId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
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
            values (
                @id,
                'manual_backfill',
                'MANUAL-2026-0001',
                @business_date_start,
                @business_date_end,
                @recorded_at,
                @account_id,
                null,
                null,
                'Accepted data-contract probe')
            """;
        command.Parameters.AddWithValue("id", batchId);
        command.Parameters.AddWithValue("business_date_start", BusinessDateStart);
        command.Parameters.AddWithValue("business_date_end", BusinessDateEnd);
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("account_id", actor.AccountId.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return batchId;
    }

    private static async Task UpdateBatchBusinessRangeAsync(
        PostgreSqlTestDatabase database,
        Guid batchId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.entry_batches
            set business_date_end = @business_date_end
            where id = @batch_id
            """;
        command.Parameters.AddWithValue("business_date_end", BusinessDateStart);
        command.Parameters.AddWithValue("batch_id", batchId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertRawRowAsync(
        PostgreSqlTestDatabase database,
        ActorContext actor,
        Guid batchId,
        int lineNumber,
        DateTimeOffset occurredAt)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
            values (
                @id,
                @entry_batch_id,
                @line_number,
                'visit',
                @occurred_at,
                'Direct constraint probe',
                @recorded_at,
                @recorded_by_account_id,
                @session_id)
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("entry_batch_id", batchId);
        command.Parameters.AddWithValue("line_number", lineNumber);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("recorded_by_account_id", actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", actor.SessionId.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertEntityLinkAsync(
        PostgreSqlTestDatabase database,
        Guid rowId,
        string entityType,
        Guid entityId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.entry_batch_row_entities (
                entry_batch_row_id,
                entity_type,
                entity_id)
            values (@row_id, @entity_type, @entity_id)
            """;
        command.Parameters.AddWithValue("row_id", rowId);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        await command.ExecuteNonQueryAsync();
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName) =>
        database.ExecuteScalarAsync<long>($"select count(*) from bodylife.{tableName}");

    private static void AssertBatchSuccess(CommandResult result)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Equal(PaperFallbackAuditActions.BatchEntityType, result.PrimaryEntityId?.Type);
        Assert.Equal(result.PrimaryEntityId, result.RereadTargetId);
        Assert.NotNull(result.AuditEntryId);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    private static void AssertRowSuccess(CommandResult result, Guid batchId)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Equal(PaperFallbackAuditActions.RowEntityType, result.PrimaryEntityId?.Type);
        Assert.Equal(
            new EntityId(PaperFallbackAuditActions.BatchEntityType, batchId),
            result.RereadTargetId);
        Assert.Equal(
            [new EntityId(PaperFallbackAuditActions.BatchEntityType, batchId)],
            result.RelatedEntityIds);
        Assert.NotNull(result.AuditEntryId);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    private static void AssertEquivalentSuccess(CommandResult expected, CommandResult actual)
    {
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.PrimaryEntityId, actual.PrimaryEntityId);
        Assert.Equal(expected.RereadTargetId, actual.RereadTargetId);
        Assert.Equal(expected.RelatedEntityIds, actual.RelatedEntityIds);
        Assert.Equal(expected.AuditEntryId, actual.AuditEntryId);
        Assert.Empty(actual.Errors);
    }

    private static void AssertError(
        CommandResult result,
        CommandErrorCode errorCode,
        string? field = null)
    {
        Assert.Equal(CommandStatus.Error, result.Status);
        var error = Assert.Single(result.Errors, error => error.Code == errorCode);
        if (field is not null)
        {
            Assert.Equal(field, error.Field);
        }

        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
        Assert.Null(result.AuditEntryId);
    }

    private static string MapAccountKind(AccountKind accountKind) =>
        accountKind switch
        {
            AccountKind.Owner => "owner",
            AccountKind.NamedAdmin => "named_admin",
            AccountKind.SharedReceptionAdmin => "shared_reception_admin",
            _ => throw new ArgumentOutOfRangeException(nameof(accountKind), accountKind, null),
        };

    private static string MapRole(ActorRole role) =>
        role switch
        {
            ActorRole.Owner => "owner",
            ActorRole.Admin => "admin",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record BatchRow(
        Guid Id,
        string BatchType,
        string PaperSheetNumber,
        DateOnly BusinessDateStart,
        DateOnly BusinessDateEnd,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        DateTimeOffset? ReconciledAt,
        Guid? ReconciledByAccountId,
        string? Note);

    private sealed record PaperRow(
        Guid Id,
        Guid EntryBatchId,
        int LineNumber,
        string EventType,
        DateTimeOffset OccurredAt,
        string Explanation,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId);

    private sealed record AuditRow(
        Guid Id,
        string ActionType,
        string EntityType,
        Guid EntityId,
        string EntryOrigin,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string? Reason,
        string? Comment,
        string? IdempotencyKey,
        string RelatedEntityRefsJson,
        string BeforeSummaryJson,
        string AfterSummaryJson);

    private sealed record IdempotencyRow(
        string CommandName,
        Guid PrimaryEntityId,
        Guid RereadTargetId,
        Guid AuditEntryId,
        string Status,
        string? ResultFingerprint);
}
