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

public sealed class PostgreSqlCreateMembershipTypeCommandTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 12, 20, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task OwnerCreatesCanonicalActiveTypeWithAuditAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner,
            deviceLabel: "owner tablet");
        var command = CreateCommand(
            actor,
            "create-morning-eight",
            name: "  Morning   Eight  ",
            durationDays: 30,
            visitsLimit: 8,
            price: new Money(1200.50m, "uah"),
            comment: "  Before noon only.  ");

        var result = await CreateHandler(dbContext).ExecuteAsync(command, CancellationToken.None);

        AssertSuccessfulResult(result);
        var membershipTypeId = result.PrimaryEntityId!.Value.Value;
        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.Equal("Morning Eight", membershipType.Name);
        Assert.Equal(30, membershipType.DurationDays);
        Assert.Equal(8, membershipType.VisitsLimit);
        Assert.Equal(1200.50m, membershipType.PriceAmount);
        Assert.Equal("UAH", membershipType.PriceCurrency);
        Assert.True(membershipType.IsActive);
        Assert.Equal("Before noon only.", membershipType.Comment);
        Assert.Equal(TestNow.UtcDateTime, membershipType.CreatedAt);
        Assert.Equal(TestNow.UtcDateTime, membershipType.UpdatedAt);
        Assert.Null(membershipType.DeactivatedAt);

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(MembershipTypeAuditActions.Created, audit.ActionType);
        Assert.Equal(MembershipTypeAuditActions.EntityType, audit.EntityType);
        Assert.Equal(membershipTypeId, audit.EntityId);
        Assert.Equal(actor.AccountId.Value, audit.ActorAccountId);
        Assert.Equal("owner", audit.ActorAccountType);
        Assert.Equal("owner", audit.ActorRole);
        Assert.Equal(actor.SessionId.Value, audit.SessionId);
        Assert.Equal("owner tablet", audit.DeviceLabel);
        Assert.Equal(command.Envelope.OccurredAt!.Value.UtcDateTime, audit.OccurredAt);
        Assert.Equal(TestNow.UtcDateTime, audit.RecordedAt);
        Assert.Equal(command.Envelope.RequestCorrelationId.Value, audit.RequestCorrelationId);
        Assert.Equal("normal", audit.EntryOrigin);
        Assert.Equal(command.Envelope.IdempotencyKey, audit.IdempotencyKey);
        Assert.Equal("{}", audit.BeforeSummary);

        using var afterSummary = JsonDocument.Parse(audit.AfterSummary);
        var summary = afterSummary.RootElement;
        Assert.Equal("Morning Eight", summary.GetProperty("name").GetString());
        Assert.Equal(30, summary.GetProperty("durationDays").GetInt32());
        Assert.Equal(8, summary.GetProperty("visitsLimit").GetInt32());
        Assert.Equal(1200.50m, summary.GetProperty("price").GetProperty("amount").GetDecimal());
        Assert.Equal("UAH", summary.GetProperty("price").GetProperty("currency").GetString());
        Assert.True(summary.GetProperty("isActive").GetBoolean());
        Assert.Equal("Before noon only.", summary.GetProperty("comment").GetString());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("deactivatedAt").ValueKind);

        var idempotency = await ReadIdempotencyAsync(database);
        Assert.Equal("CreateMembershipType", idempotency.CommandName);
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
    public async Task NamedAdminSharedReceptionAndMismatchedOwnerAreDeniedWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var namedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var sharedReception = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin);
        var mismatchedOwner = namedAdmin with
        {
            Role = ActorRole.Owner,
            AccountKind = AccountKind.Owner,
        };
        var handler = CreateHandler(dbContext);

        var results = new[]
        {
            await handler.ExecuteAsync(
                CreateCommand(namedAdmin, "named-admin"),
                CancellationToken.None),
            await handler.ExecuteAsync(
                CreateCommand(sharedReception, "shared-reception"),
                CancellationToken.None),
            await handler.ExecuteAsync(
                CreateCommand(mismatchedOwner, "mismatched-owner"),
                CancellationToken.None),
        };

        Assert.All(results, result => AssertError(result, CommandErrorCode.PermissionDenied));
        Assert.Equal(0L, await CountRowsAsync(database, "membership_types"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ExpiredInactiveAndUnknownOwnersAreDeniedWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner,
            sessionExpiresAt: TestNow.AddMinutes(-1));
        var handler = CreateHandler(dbContext);

        var expiredResult = await handler.ExecuteAsync(
            CreateCommand(actor, "expired-owner"),
            CancellationToken.None);
        await MakeActorInactiveAsync(database, actor);
        var inactiveResult = await handler.ExecuteAsync(
            CreateCommand(actor, "inactive-owner"),
            CancellationToken.None);
        var unknownActor = new ActorContext(
            AccountId.New(),
            ActorRole.Owner,
            AccountKind.Owner,
            SessionId.New(),
            "unknown owner device");
        var unknownResult = await handler.ExecuteAsync(
            CreateCommand(unknownActor, "unknown-owner"),
            CancellationToken.None);

        AssertError(expiredResult, CommandErrorCode.PermissionDenied);
        AssertError(inactiveResult, CommandErrorCode.PermissionDenied);
        AssertError(unknownResult, CommandErrorCode.PermissionDenied);
        Assert.Equal(0L, await CountRowsAsync(database, "membership_types"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InvalidCatalogAndEnvelopeAreRejectedWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var handler = CreateHandler(dbContext);
        var validCommand = CreateCommand(actor, "valid-command");

        var results = new[]
        {
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
                        IdempotencyKey = "invalid-fallback",
                        EntryOrigin = EntryOrigin.PaperFallback,
                        OccurredAt = null,
                        Reason = null,
                        Comment = null,
                    },
                },
                CancellationToken.None),
        };

        Assert.All(results, result => AssertError(result, CommandErrorCode.ValidationFailed));
        Assert.Equal("name", Assert.Single(results[0].Errors).Field);
        Assert.Equal("durationDays", Assert.Single(results[1].Errors).Field);
        Assert.Equal("visitsLimit", Assert.Single(results[2].Errors).Field);
        Assert.Equal("currency", Assert.Single(results[3].Errors).Field);
        Assert.Equal("idempotencyKey", Assert.Single(results[4].Errors).Field);
        Assert.Equal("entryOrigin", Assert.Single(results[5].Errors).Field);
        Assert.Equal(0L, await CountRowsAsync(database, "membership_types"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ExplicitInactiveCreatePersistsCompleteLifecycleAndAuditSummary()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var command = CreateCommand(
            actor,
            "create-inactive",
            name: "Legacy trial",
            isActive: false);

        var result = await CreateHandler(dbContext).ExecuteAsync(command, CancellationToken.None);

        AssertSuccessfulResult(result);
        var membershipType = await ReadMembershipTypeAsync(
            database,
            result.PrimaryEntityId!.Value.Value);
        Assert.False(membershipType.IsActive);
        Assert.Equal(TestNow.UtcDateTime, membershipType.CreatedAt);
        Assert.Equal(TestNow.UtcDateTime, membershipType.UpdatedAt);
        Assert.Equal(TestNow.UtcDateTime, membershipType.DeactivatedAt);

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        using var afterSummary = JsonDocument.Parse(audit.AfterSummary);
        Assert.False(afterSummary.RootElement.GetProperty("isActive").GetBoolean());
        Assert.NotEqual(
            JsonValueKind.Null,
            afterSummary.RootElement.GetProperty("deactivatedAt").ValueKind);
    }

    [PostgreSqlFact]
    public async Task IdempotentReplayReturnsOriginalResultAndRejectsChangedPayload()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var handler = CreateHandler(dbContext);
        var command = CreateCommand(actor, "replay-membership-type");

        var firstResult = await handler.ExecuteAsync(command, CancellationToken.None);
        var replayResult = await handler.ExecuteAsync(command, CancellationToken.None);
        var changedResult = await handler.ExecuteAsync(
            command with { Name = "Changed catalog type" },
            CancellationToken.None);

        AssertSuccessfulResult(firstResult);
        AssertSuccessfulResult(replayResult);
        Assert.Equal(firstResult.PrimaryEntityId, replayResult.PrimaryEntityId);
        Assert.Equal(firstResult.RereadTargetId, replayResult.RereadTargetId);
        Assert.Equal(firstResult.AuditEntryId, replayResult.AuditEntryId);
        AssertError(changedResult, CommandErrorCode.DuplicateSubmission);
        Assert.Equal(1L, await CountRowsAsync(database, "membership_types"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentChangedPayloadsWithOneKeyCommitOneCompleteWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var migrationContext = database.CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var firstCommand = CreateCommand(
            actor,
            "concurrent-membership-type",
            name: "Concurrent A");
        var secondCommand = firstCommand with { Name = "Concurrent B" };

        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(firstCommand, CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(secondCommand, CancellationToken.None));

        var success = Assert.Single(results, result => result.Status == CommandStatus.Success);
        var rejected = Assert.Single(results, result => result.Status == CommandStatus.Error);
        AssertSuccessfulResult(success);
        AssertError(rejected, CommandErrorCode.DuplicateSubmission);
        Assert.Equal(1L, await CountRowsAsync(database, "membership_types"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackCatalogAndIdempotencyRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_membership_type_audit
            check (action_type <> 'membership_type.created')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext).ExecuteAsync(
                CreateCommand(actor, "audit-failure"),
                CancellationToken.None));

        Assert.Equal(0L, await CountRowsAsync(database, "membership_types"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static CreateMembershipTypeCommandHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new CreateMembershipTypeCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new FixedTimeProvider(TestNow));
    }

    private static CreateMembershipTypeCommand CreateCommand(
        ActorContext actor,
        string idempotencyKey,
        string name = "Eight visits",
        int durationDays = 30,
        int visitsLimit = 8,
        Money? price = null,
        string? comment = null,
        bool isActive = true)
    {
        return new CreateMembershipTypeCommand(
            new CommandEnvelope(
                actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                TestNow.AddMinutes(-5),
                idempotencyKey,
                Reason: null,
                Comment: null),
            name,
            durationDays,
            visitsLimit,
            price ?? new Money(1200m, "UAH"),
            comment,
            isActive);
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

    private static async Task MakeActorInactiveAsync(
        PostgreSqlTestDatabase database,
        ActorContext actor)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.Transaction = transaction;
            sessionCommand.CommandText =
                """
                update bodylife.sessions
                set expires_at = @expires_at
                where id = @session_id
                """;
            sessionCommand.Parameters.AddWithValue("expires_at", TestNow.AddHours(1));
            sessionCommand.Parameters.AddWithValue("session_id", actor.SessionId.Value);
            Assert.Equal(1, await sessionCommand.ExecuteNonQueryAsync());
        }

        await using (var accountCommand = connection.CreateCommand())
        {
            accountCommand.Transaction = transaction;
            accountCommand.CommandText =
                """
                update bodylife.accounts
                set is_active = false,
                    deactivated_at = @deactivated_at
                where id = @account_id
                """;
            accountCommand.Parameters.AddWithValue("deactivated_at", TestNow);
            accountCommand.Parameters.AddWithValue("account_id", actor.AccountId.Value);
            Assert.Equal(1, await accountCommand.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
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
            reader.GetString(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14));
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
