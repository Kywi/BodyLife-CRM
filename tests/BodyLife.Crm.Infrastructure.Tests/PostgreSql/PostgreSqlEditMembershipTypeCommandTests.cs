using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlEditMembershipTypeCommandTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 12, 22, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SeedCreatedAt = TestNow.AddDays(-10);
    private static readonly DateTimeOffset SeedUpdatedAt = TestNow.AddDays(-1);

    [PostgreSqlFact]
    public async Task OwnerEditsCanonicalCatalogFieldsWithBeforeAfterAuditAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner,
            deviceLabel: "owner settings tablet");
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database, membershipTypeId);
        var command = EditCommand(
            actor,
            "edit-evening-twelve",
            membershipTypeId,
            SeedUpdatedAt,
            name: "  Evening   Twelve  ",
            durationDays: 45,
            visitsLimit: 12,
            price: new Money(1600.50m, "uah"),
            catalogComment: "  Future evening sales.  ",
            reason: "  Updated future offer.  ",
            envelopeComment: "  Owner approved.  ");

        var result = await CreateHandler(dbContext).ExecuteAsync(command, CancellationToken.None);

        AssertSuccessfulResult(result);
        Assert.Equal(membershipTypeId, result.PrimaryEntityId!.Value.Value);
        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.Equal("Evening Twelve", membershipType.Name);
        Assert.Equal(45, membershipType.DurationDays);
        Assert.Equal(12, membershipType.VisitsLimit);
        Assert.Equal(1600.50m, membershipType.PriceAmount);
        Assert.Equal("UAH", membershipType.PriceCurrency);
        Assert.True(membershipType.IsActive);
        Assert.Equal("Future evening sales.", membershipType.Comment);
        Assert.Equal(SeedCreatedAt.UtcDateTime, membershipType.CreatedAt);
        Assert.Equal(TestNow.UtcDateTime, membershipType.UpdatedAt);
        Assert.Null(membershipType.DeactivatedAt);

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(MembershipTypeAuditActions.Edited, audit.ActionType);
        Assert.Equal(MembershipTypeAuditActions.EntityType, audit.EntityType);
        Assert.Equal(membershipTypeId, audit.EntityId);
        Assert.Equal(actor.AccountId.Value, audit.ActorAccountId);
        Assert.Equal("owner", audit.ActorAccountType);
        Assert.Equal("owner", audit.ActorRole);
        Assert.Equal(actor.SessionId.Value, audit.SessionId);
        Assert.Equal("owner settings tablet", audit.DeviceLabel);
        Assert.Equal(command.Envelope.OccurredAt!.Value.UtcDateTime, audit.OccurredAt);
        Assert.Equal(TestNow.UtcDateTime, audit.RecordedAt);
        Assert.Equal("Updated future offer.", audit.Reason);
        Assert.Equal("Owner approved.", audit.Comment);
        Assert.Equal(command.Envelope.RequestCorrelationId.Value, audit.RequestCorrelationId);
        Assert.Equal("normal", audit.EntryOrigin);
        Assert.Equal(command.Envelope.IdempotencyKey, audit.IdempotencyKey);

        using var beforeSummary = JsonDocument.Parse(audit.BeforeSummary);
        AssertCatalogSummary(
            beforeSummary.RootElement,
            "Eight visits",
            30,
            8,
            1200m,
            "UAH",
            true,
            "Original catalog values");
        using var afterSummary = JsonDocument.Parse(audit.AfterSummary);
        AssertCatalogSummary(
            afterSummary.RootElement,
            "Evening Twelve",
            45,
            12,
            1600.50m,
            "UAH",
            true,
            "Future evening sales.");

        var idempotency = await ReadIdempotencyAsync(database);
        Assert.Equal("EditMembershipType", idempotency.CommandName);
        Assert.Equal("succeeded", idempotency.Status);
        Assert.Equal(membershipTypeId, idempotency.PrimaryEntityId);
        Assert.Equal(membershipTypeId, idempotency.RereadTargetId);
        Assert.Equal(result.AuditEntryId.Value.Value, idempotency.AuditEntryId);
        Assert.Equal(64, idempotency.FingerprintLength);
        Assert.Equal(1L, await CountRowsAsync(database, "membership_types"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task AdminSharedReceptionForgedAndExpiredOwnersAreDeniedWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database, membershipTypeId);
        var namedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var sharedReception = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin);
        var expiredOwner = await SeedActorAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner,
            sessionExpiresAt: TestNow.AddMinutes(-1));
        var forgedOwner = namedAdmin with
        {
            Role = ActorRole.Owner,
            AccountKind = AccountKind.Owner,
        };
        var handler = CreateHandler(dbContext);

        var results = new[]
        {
            await handler.ExecuteAsync(
                EditCommand(namedAdmin, "named-admin", membershipTypeId, SeedUpdatedAt),
                CancellationToken.None),
            await handler.ExecuteAsync(
                EditCommand(sharedReception, "shared-reception", membershipTypeId, SeedUpdatedAt),
                CancellationToken.None),
            await handler.ExecuteAsync(
                EditCommand(forgedOwner, "forged-owner", membershipTypeId, SeedUpdatedAt),
                CancellationToken.None),
            await handler.ExecuteAsync(
                EditCommand(expiredOwner, "expired-owner", membershipTypeId, SeedUpdatedAt),
                CancellationToken.None),
        };

        Assert.All(results, result => AssertError(result, CommandErrorCode.PermissionDenied));
        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.Equal("Eight visits", membershipType.Name);
        Assert.Equal(SeedUpdatedAt.UtcDateTime, membershipType.UpdatedAt);
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InvalidCatalogIdentityVersionReasonAndEnvelopeAreRejectedWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database, membershipTypeId);
        var handler = CreateHandler(dbContext);
        var validCommand = EditCommand(
            actor,
            "valid-edit",
            membershipTypeId,
            SeedUpdatedAt);

        var results = new[]
        {
            await handler.ExecuteAsync(
                validCommand with { MembershipTypeId = Guid.Empty },
                CancellationToken.None),
            await handler.ExecuteAsync(
                validCommand with { ExpectedUpdatedAt = default },
                CancellationToken.None),
            await handler.ExecuteAsync(
                validCommand with
                {
                    Envelope = validCommand.Envelope with { IdempotencyKey = "blank-name" },
                    Name = "   ",
                },
                CancellationToken.None),
            await handler.ExecuteAsync(
                validCommand with
                {
                    Envelope = validCommand.Envelope with { IdempotencyKey = "zero-duration" },
                    DurationDays = 0,
                },
                CancellationToken.None),
            await handler.ExecuteAsync(
                validCommand with
                {
                    Envelope = validCommand.Envelope with { IdempotencyKey = "negative-visits" },
                    VisitsLimit = -1,
                },
                CancellationToken.None),
            await handler.ExecuteAsync(
                validCommand with
                {
                    Envelope = validCommand.Envelope with { IdempotencyKey = "missing-currency" },
                    Price = default,
                },
                CancellationToken.None),
            await handler.ExecuteAsync(
                validCommand with
                {
                    Envelope = validCommand.Envelope with { IdempotencyKey = null },
                },
                CancellationToken.None),
            await handler.ExecuteAsync(
                validCommand with
                {
                    Envelope = validCommand.Envelope with
                    {
                        IdempotencyKey = "missing-reason",
                        Reason = null,
                        Comment = null,
                    },
                },
                CancellationToken.None),
        };

        Assert.All(results, result => AssertError(result, CommandErrorCode.ValidationFailed));
        Assert.Equal("membershipTypeId", Assert.Single(results[0].Errors).Field);
        Assert.Equal("expectedUpdatedAt", Assert.Single(results[1].Errors).Field);
        Assert.Equal("name", Assert.Single(results[2].Errors).Field);
        Assert.Equal("durationDays", Assert.Single(results[3].Errors).Field);
        Assert.Equal("visitsLimit", Assert.Single(results[4].Errors).Field);
        Assert.Equal("currency", Assert.Single(results[5].Errors).Field);
        Assert.Equal("idempotencyKey", Assert.Single(results[6].Errors).Field);
        Assert.Equal("reason", Assert.Single(results[7].Errors).Field);
        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.Equal("Eight visits", membershipType.Name);
        Assert.Equal(SeedUpdatedAt.UtcDateTime, membershipType.UpdatedAt);
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task MissingStaleAndNormalizedNoOpEditsAreRejectedWithoutSideEffects()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database, membershipTypeId);
        var handler = CreateHandler(dbContext);

        var missingResult = await handler.ExecuteAsync(
            EditCommand(
                actor,
                "missing-type",
                Guid.NewGuid(),
                SeedUpdatedAt),
            CancellationToken.None);
        var staleResult = await handler.ExecuteAsync(
            EditCommand(
                actor,
                "stale-type",
                membershipTypeId,
                SeedUpdatedAt.AddMinutes(-1)),
            CancellationToken.None);
        var noOpResult = await handler.ExecuteAsync(
            EditCommand(
                actor,
                "no-op-type",
                membershipTypeId,
                SeedUpdatedAt,
                name: "  Eight   visits  ",
                price: new Money(1200m, "uah"),
                catalogComment: "  Original catalog values  "),
            CancellationToken.None);

        AssertError(missingResult, CommandErrorCode.NotFound);
        AssertError(staleResult, CommandErrorCode.StaleState);
        AssertError(noOpResult, CommandErrorCode.ValidationFailed);
        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.Equal("Eight visits", membershipType.Name);
        Assert.Equal(SeedUpdatedAt.UtcDateTime, membershipType.UpdatedAt);
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task EditingInactiveTypePreservesItsLifecycleAndDoesNotReactivateIt()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(
            database,
            membershipTypeId,
            isActive: false,
            deactivatedAt: SeedUpdatedAt);
        var command = EditCommand(
            actor,
            "edit-inactive",
            membershipTypeId,
            SeedUpdatedAt,
            name: "Archived eight visits");

        var result = await CreateHandler(dbContext).ExecuteAsync(command, CancellationToken.None);

        AssertSuccessfulResult(result);
        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.Equal("Archived eight visits", membershipType.Name);
        Assert.False(membershipType.IsActive);
        Assert.Equal(SeedUpdatedAt.UtcDateTime, membershipType.DeactivatedAt);
        Assert.Equal(TestNow.UtcDateTime, membershipType.UpdatedAt);
        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        using var beforeSummary = JsonDocument.Parse(audit.BeforeSummary);
        using var afterSummary = JsonDocument.Parse(audit.AfterSummary);
        Assert.False(beforeSummary.RootElement.GetProperty("isActive").GetBoolean());
        Assert.False(afterSummary.RootElement.GetProperty("isActive").GetBoolean());
        Assert.Equal(
            beforeSummary.RootElement.GetProperty("deactivatedAt").GetString(),
            afterSummary.RootElement.GetProperty("deactivatedAt").GetString());
    }

    [PostgreSqlFact]
    public async Task IdempotentReplayReturnsOriginalResultAndRejectsChangedPayload()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database, membershipTypeId);
        var handler = CreateHandler(dbContext);
        var command = EditCommand(
            actor,
            "edit-replay",
            membershipTypeId,
            SeedUpdatedAt,
            name: "  Updated   Type  ",
            price: new Money(1300m, "uah"),
            catalogComment: "Future sales");

        var firstResult = await handler.ExecuteAsync(command, CancellationToken.None);
        var replayResult = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with
                {
                    RequestCorrelationId = new RequestCorrelationId("edit-replay-correlation-2"),
                    Reason = "  Catalog values updated  ",
                },
                Name = "Updated Type",
                Price = new Money(1300m, "UAH"),
                Comment = "  Future sales  ",
            },
            CancellationToken.None);
        var changedResult = await handler.ExecuteAsync(
            command with { VisitsLimit = 13 },
            CancellationToken.None);

        AssertSuccessfulResult(firstResult);
        AssertSuccessfulResult(replayResult);
        Assert.Equal(firstResult.PrimaryEntityId, replayResult.PrimaryEntityId);
        Assert.Equal(firstResult.RereadTargetId, replayResult.RereadTargetId);
        Assert.Equal(firstResult.AuditEntryId, replayResult.AuditEntryId);
        AssertError(changedResult, CommandErrorCode.DuplicateSubmission);
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentEditsFromOneExpectedVersionCommitOnlyOneWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var migrationContext = database.CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database, membershipTypeId);
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(
                EditCommand(
                    actor,
                    "concurrent-edit-1",
                    membershipTypeId,
                    SeedUpdatedAt,
                    name: "Concurrent first"),
                CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(
                EditCommand(
                    actor,
                    "concurrent-edit-2",
                    membershipTypeId,
                    SeedUpdatedAt,
                    name: "Concurrent second"),
                CancellationToken.None));

        AssertSuccessfulResult(Assert.Single(results, result => result.Status == CommandStatus.Success));
        AssertError(
            Assert.Single(results, result => result.Status == CommandStatus.Error),
            CommandErrorCode.StaleState);
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.Contains(
            membershipType.Name,
            new[] { "Concurrent first", "Concurrent second" });
        Assert.Equal(TestNow.UtcDateTime, membershipType.UpdatedAt);
    }

    [PostgreSqlFact]
    public async Task ConcurrentExactReplaysWithOneKeyReturnOneCommittedWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var migrationContext = database.CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database, membershipTypeId);
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var command = EditCommand(
            actor,
            "concurrent-replay",
            membershipTypeId,
            SeedUpdatedAt,
            name: "Concurrent replay");

        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(command, CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(command, CancellationToken.None));

        Assert.All(results, AssertSuccessfulResult);
        Assert.Equal(results[0].PrimaryEntityId, results[1].PrimaryEntityId);
        Assert.Equal(results[0].AuditEntryId, results[1].AuditEntryId);
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackCatalogEditAndIdempotencyRow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database, membershipTypeId);
        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_membership_type_edit_audit
            check (action_type <> 'membership_type.edited')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext).ExecuteAsync(
                EditCommand(
                    actor,
                    "edit-audit-failure",
                    membershipTypeId,
                    SeedUpdatedAt,
                    name: "Must roll back"),
                CancellationToken.None));

        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.Equal("Eight visits", membershipType.Name);
        Assert.Equal(SeedUpdatedAt.UtcDateTime, membershipType.UpdatedAt);
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static EditMembershipTypeCommandHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new EditMembershipTypeCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new FixedTimeProvider(TestNow));
    }

    private static EditMembershipTypeCommand EditCommand(
        ActorContext actor,
        string idempotencyKey,
        Guid membershipTypeId,
        DateTimeOffset expectedUpdatedAt,
        string name = "Updated eight visits",
        int durationDays = 30,
        int visitsLimit = 8,
        Money? price = null,
        string? catalogComment = "Original catalog values",
        string? reason = "Catalog values updated",
        string? envelopeComment = null)
    {
        return new EditMembershipTypeCommand(
            new CommandEnvelope(
                actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                TestNow.AddMinutes(-5),
                idempotencyKey,
                reason,
                envelopeComment),
            membershipTypeId,
            expectedUpdatedAt,
            name,
            durationDays,
            visitsLimit,
            price ?? new Money(1200m, "UAH"),
            catalogComment);
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
            accountCommand.Parameters.AddWithValue("display_name", $"{accountKind} test actor");
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

    private static async Task InsertMembershipTypeAsync(
        PostgreSqlTestDatabase database,
        Guid membershipTypeId,
        bool isActive = true,
        DateTimeOffset? deactivatedAt = null)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                @id,
                'Eight visits',
                30,
                8,
                1200,
                'UAH',
                @is_active,
                'Original catalog values',
                @created_at,
                @updated_at,
                @deactivated_at)
            """;
        command.Parameters.AddWithValue("id", membershipTypeId);
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.AddWithValue("created_at", SeedCreatedAt);
        command.Parameters.AddWithValue("updated_at", SeedUpdatedAt);
        command.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value = isActive
            ? DBNull.Value
            : deactivatedAt ?? SeedUpdatedAt;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<MembershipTypeRow> ReadMembershipTypeAsync(
        PostgreSqlTestDatabase database,
        Guid membershipTypeId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select name,
                   duration_days,
                   visits_limit,
                   price_amount,
                   price_currency,
                   is_active,
                   comment,
                   created_at,
                   updated_at,
                   deactivated_at
            from bodylife.membership_types
            where id = @id
            """;
        command.Parameters.AddWithValue("id", membershipTypeId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new MembershipTypeRow(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetDecimal(3),
            reader.GetString(4),
            reader.GetBoolean(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetDateTime(7),
            reader.GetDateTime(8),
            reader.IsDBNull(9) ? null : reader.GetDateTime(9));
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
            reader.GetString(16));
    }

    private static async Task<IdempotencyRow> ReadIdempotencyAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select command_name,
                   status,
                   primary_entity_id,
                   reread_target_id,
                   audit_entry_id,
                   length(result_fingerprint)
            from bodylife.command_idempotency_keys
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new IdempotencyRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetInt32(5));
    }

    private static async Task ExecuteNonQueryAsync(
        PostgreSqlTestDatabase database,
        string commandText)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>($"select count(*) from bodylife.{tableName}");
    }

    private static void AssertCatalogSummary(
        JsonElement summary,
        string name,
        int durationDays,
        int visitsLimit,
        decimal priceAmount,
        string priceCurrency,
        bool isActive,
        string? comment)
    {
        Assert.Equal(name, summary.GetProperty("name").GetString());
        Assert.Equal(durationDays, summary.GetProperty("durationDays").GetInt32());
        Assert.Equal(visitsLimit, summary.GetProperty("visitsLimit").GetInt32());
        Assert.Equal(priceAmount, summary.GetProperty("price").GetProperty("amount").GetDecimal());
        Assert.Equal(priceCurrency, summary.GetProperty("price").GetProperty("currency").GetString());
        Assert.Equal(isActive, summary.GetProperty("isActive").GetBoolean());
        Assert.Equal(comment, summary.GetProperty("comment").GetString());
    }

    private static void AssertSuccessfulResult(CommandResult result)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.NotNull(result.PrimaryEntityId);
        Assert.Equal(MembershipTypeAuditActions.EntityType, result.PrimaryEntityId.Value.Type);
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

    private sealed record MembershipTypeRow(
        string Name,
        int DurationDays,
        int VisitsLimit,
        decimal PriceAmount,
        string PriceCurrency,
        bool IsActive,
        string? Comment,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeactivatedAt);

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
        string BeforeSummary,
        string AfterSummary);

    private sealed record IdempotencyRow(
        string CommandName,
        string Status,
        Guid PrimaryEntityId,
        Guid RereadTargetId,
        Guid AuditEntryId,
        int FingerprintLength);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
