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

public sealed class PostgreSqlDeactivateMembershipTypeCommandTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 12, 23, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SeedCreatedAt = TestNow.AddDays(-10);
    private static readonly DateTimeOffset SeedUpdatedAt = TestNow.AddDays(-1);

    [PostgreSqlFact]
    public async Task OwnerDeactivatesTypeWithLifecycleAuditIdempotencyAndCanonicalReread()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner,
            deviceLabel: "owner catalog tablet");
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database, membershipTypeId);
        var command = DeactivateCommand(
            actor,
            "deactivate-eight-visits",
            membershipTypeId,
            SeedUpdatedAt,
            reason: "  Retired future offer.  ",
            comment: "  Existing memberships remain valid.  ");

        var result = await CreateHandler(dbContext).ExecuteAsync(command, CancellationToken.None);

        AssertSuccessfulResult(result);
        Assert.Equal(membershipTypeId, result.PrimaryEntityId!.Value.Value);
        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.Equal("Eight visits", membershipType.Name);
        Assert.Equal(30, membershipType.DurationDays);
        Assert.Equal(8, membershipType.VisitsLimit);
        Assert.Equal(1200m, membershipType.PriceAmount);
        Assert.Equal("UAH", membershipType.PriceCurrency);
        Assert.False(membershipType.IsActive);
        Assert.Equal("Original catalog values", membershipType.Comment);
        Assert.Equal(SeedCreatedAt.UtcDateTime, membershipType.CreatedAt);
        Assert.Equal(TestNow.UtcDateTime, membershipType.UpdatedAt);
        Assert.Equal(TestNow.UtcDateTime, membershipType.DeactivatedAt);

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(MembershipTypeAuditActions.Deactivated, audit.ActionType);
        Assert.Equal(MembershipTypeAuditActions.EntityType, audit.EntityType);
        Assert.Equal(membershipTypeId, audit.EntityId);
        Assert.Equal(actor.AccountId.Value, audit.ActorAccountId);
        Assert.Equal("owner", audit.ActorAccountType);
        Assert.Equal("owner", audit.ActorRole);
        Assert.Equal(actor.SessionId.Value, audit.SessionId);
        Assert.Equal("owner catalog tablet", audit.DeviceLabel);
        Assert.Equal(command.Envelope.OccurredAt!.Value.UtcDateTime, audit.OccurredAt);
        Assert.Equal(TestNow.UtcDateTime, audit.RecordedAt);
        Assert.Equal("Retired future offer.", audit.Reason);
        Assert.Equal("Existing memberships remain valid.", audit.Comment);
        Assert.Equal(command.Envelope.RequestCorrelationId.Value, audit.RequestCorrelationId);
        Assert.Equal("normal", audit.EntryOrigin);
        Assert.Equal(command.Envelope.IdempotencyKey, audit.IdempotencyKey);

        using var beforeSummary = JsonDocument.Parse(audit.BeforeSummary);
        using var afterSummary = JsonDocument.Parse(audit.AfterSummary);
        AssertCatalogSummary(beforeSummary.RootElement, isActive: true, deactivated: false);
        AssertCatalogSummary(afterSummary.RootElement, isActive: false, deactivated: true);

        var idempotency = await ReadIdempotencyAsync(database);
        Assert.Equal("DeactivateMembershipType", idempotency.CommandName);
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
        var namedAdmin = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
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
                DeactivateCommand(namedAdmin, "named-admin", membershipTypeId, SeedUpdatedAt),
                CancellationToken.None),
            await handler.ExecuteAsync(
                DeactivateCommand(sharedReception, "shared-reception", membershipTypeId, SeedUpdatedAt),
                CancellationToken.None),
            await handler.ExecuteAsync(
                DeactivateCommand(forgedOwner, "forged-owner", membershipTypeId, SeedUpdatedAt),
                CancellationToken.None),
            await handler.ExecuteAsync(
                DeactivateCommand(expiredOwner, "expired-owner", membershipTypeId, SeedUpdatedAt),
                CancellationToken.None),
        };

        Assert.All(results, result => AssertError(result, CommandErrorCode.PermissionDenied));
        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.True(membershipType.IsActive);
        Assert.Null(membershipType.DeactivatedAt);
        Assert.Equal(SeedUpdatedAt.UtcDateTime, membershipType.UpdatedAt);
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InvalidIdentityVersionEnvelopeAndReasonAreRejectedWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database, membershipTypeId);
        var handler = CreateHandler(dbContext);
        var validCommand = DeactivateCommand(
            actor,
            "valid-deactivation",
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
            await handler.ExecuteAsync(
                validCommand with
                {
                    Envelope = validCommand.Envelope with
                    {
                        IdempotencyKey = "invalid-fallback",
                        EntryOrigin = EntryOrigin.PaperFallback,
                        OccurredAt = null,
                    },
                },
                CancellationToken.None),
        };

        Assert.All(results, result => AssertError(result, CommandErrorCode.ValidationFailed));
        Assert.Equal("membershipTypeId", Assert.Single(results[0].Errors).Field);
        Assert.Equal("expectedUpdatedAt", Assert.Single(results[1].Errors).Field);
        Assert.Equal("idempotencyKey", Assert.Single(results[2].Errors).Field);
        Assert.Equal("reason", Assert.Single(results[3].Errors).Field);
        Assert.Equal("entryOrigin", Assert.Single(results[4].Errors).Field);
        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.True(membershipType.IsActive);
        Assert.Null(membershipType.DeactivatedAt);
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task MissingStaleAndAlreadyInactiveRequestsReturnStableErrorsWithoutSideEffects()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var activeMembershipTypeId = Guid.NewGuid();
        var inactiveMembershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database, activeMembershipTypeId);
        await InsertMembershipTypeAsync(
            database,
            inactiveMembershipTypeId,
            isActive: false,
            deactivatedAt: SeedUpdatedAt);
        var handler = CreateHandler(dbContext);

        var missingResult = await handler.ExecuteAsync(
            DeactivateCommand(actor, "missing-type", Guid.NewGuid(), SeedUpdatedAt),
            CancellationToken.None);
        var staleResult = await handler.ExecuteAsync(
            DeactivateCommand(
                actor,
                "stale-type",
                activeMembershipTypeId,
                SeedUpdatedAt.AddMinutes(-1)),
            CancellationToken.None);
        var alreadyInactiveResult = await handler.ExecuteAsync(
            DeactivateCommand(
                actor,
                "already-inactive",
                inactiveMembershipTypeId,
                SeedUpdatedAt),
            CancellationToken.None);

        AssertError(missingResult, CommandErrorCode.NotFound);
        AssertError(staleResult, CommandErrorCode.StaleState);
        AssertError(alreadyInactiveResult, CommandErrorCode.AlreadyInactive);
        Assert.True((await ReadMembershipTypeAsync(database, activeMembershipTypeId)).IsActive);
        var inactiveMembershipType = await ReadMembershipTypeAsync(database, inactiveMembershipTypeId);
        Assert.False(inactiveMembershipType.IsActive);
        Assert.Equal(SeedUpdatedAt.UtcDateTime, inactiveMembershipType.DeactivatedAt);
        Assert.Equal(2L, await CountRowsAsync(database, "membership_types"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task IdempotentReplayReturnsOriginalResultAndRejectsChangedReason()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(database, membershipTypeId);
        var handler = CreateHandler(dbContext);
        var command = DeactivateCommand(
            actor,
            "deactivate-replay",
            membershipTypeId,
            SeedUpdatedAt,
            reason: "Retired future offer");

        var firstResult = await handler.ExecuteAsync(command, CancellationToken.None);
        var replayResult = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with
                {
                    RequestCorrelationId = new RequestCorrelationId(
                        "deactivate-replay-correlation-2"),
                    Reason = "  Retired future offer  ",
                },
            },
            CancellationToken.None);
        var changedResult = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with { Reason = "Different retirement reason" },
            },
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
    public async Task ConcurrentDeactivationsFromOneExpectedVersionCommitOnlyOneWorkflow()
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
                DeactivateCommand(
                    actor,
                    "concurrent-deactivation-1",
                    membershipTypeId,
                    SeedUpdatedAt,
                    reason: "First retirement request"),
                CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(
                DeactivateCommand(
                    actor,
                    "concurrent-deactivation-2",
                    membershipTypeId,
                    SeedUpdatedAt,
                    reason: "Second retirement request"),
                CancellationToken.None));

        AssertSuccessfulResult(Assert.Single(results, result => result.Status == CommandStatus.Success));
        AssertError(
            Assert.Single(results, result => result.Status == CommandStatus.Error),
            CommandErrorCode.StaleState);
        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.False(membershipType.IsActive);
        Assert.Equal(TestNow.UtcDateTime, membershipType.DeactivatedAt);
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
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
        var command = DeactivateCommand(
            actor,
            "concurrent-deactivation-replay",
            membershipTypeId,
            SeedUpdatedAt);

        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(command, CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(command, CancellationToken.None));

        Assert.All(results, AssertSuccessfulResult);
        Assert.Equal(results[0].PrimaryEntityId, results[1].PrimaryEntityId);
        Assert.Equal(results[0].AuditEntryId, results[1].AuditEntryId);
        Assert.Equal(1L, await CountRowsAsync(database, "membership_types"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task DeactivationAdvancesLifecycleWhenServerClockEqualsExistingVersion()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membershipTypeId = Guid.NewGuid();
        await InsertMembershipTypeAsync(
            database,
            membershipTypeId,
            updatedAt: TestNow);
        var command = DeactivateCommand(
            actor,
            "monotonic-deactivation",
            membershipTypeId,
            TestNow);

        var result = await CreateHandler(dbContext).ExecuteAsync(command, CancellationToken.None);

        AssertSuccessfulResult(result);
        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        var expectedLifecycleTimestamp = TestNow.AddTicks(10).UtcDateTime;
        Assert.Equal(expectedLifecycleTimestamp, membershipType.UpdatedAt);
        Assert.Equal(expectedLifecycleTimestamp, membershipType.DeactivatedAt);
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackLifecycleAndIdempotencyRow()
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
            add constraint ck_test_reject_membership_type_deactivation_audit
            check (action_type <> 'membership_type.deactivated')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext).ExecuteAsync(
                DeactivateCommand(
                    actor,
                    "deactivation-audit-failure",
                    membershipTypeId,
                    SeedUpdatedAt),
                CancellationToken.None));

        var membershipType = await ReadMembershipTypeAsync(database, membershipTypeId);
        Assert.True(membershipType.IsActive);
        Assert.Null(membershipType.DeactivatedAt);
        Assert.Equal(SeedUpdatedAt.UtcDateTime, membershipType.UpdatedAt);
        Assert.Equal(1L, await CountRowsAsync(database, "membership_types"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static DeactivateMembershipTypeCommandHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new DeactivateMembershipTypeCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new FixedTimeProvider(TestNow));
    }

    private static DeactivateMembershipTypeCommand DeactivateCommand(
        ActorContext actor,
        string idempotencyKey,
        Guid membershipTypeId,
        DateTimeOffset expectedUpdatedAt,
        string? reason = "Membership type retired",
        string? comment = null)
    {
        return new DeactivateMembershipTypeCommand(
            new CommandEnvelope(
                actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                TestNow.AddMinutes(-5),
                idempotencyKey,
                reason,
                comment),
            membershipTypeId,
            expectedUpdatedAt);
    }

    private static async Task<ActorContext> SeedActorAsync(
        PostgreSqlTestDatabase database,
        ActorRole role,
        AccountKind accountKind,
        DateTimeOffset? sessionExpiresAt = null,
        string? deviceLabel = "test device")
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
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
                @role,
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
                @device_label,
                @started_at,
                @expires_at,
                null,
                @last_seen_at);
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("display_name", $"{accountKind} test actor");
        command.Parameters.AddWithValue("account_type", MapAccountKind(accountKind));
        command.Parameters.AddWithValue("role", MapRole(role));
        command.Parameters.AddWithValue("created_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.Add("device_label", NpgsqlDbType.Varchar).Value =
            deviceLabel ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue(
            "expires_at",
            sessionExpiresAt ?? TestNow.AddHours(11));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        await command.ExecuteNonQueryAsync();

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
        DateTimeOffset? deactivatedAt = null,
        DateTimeOffset? updatedAt = null)
    {
        var canonicalUpdatedAt = updatedAt ?? SeedUpdatedAt;
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
        command.Parameters.AddWithValue("updated_at", canonicalUpdatedAt);
        command.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value = isActive
            ? DBNull.Value
            : deactivatedAt ?? canonicalUpdatedAt;
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
        bool isActive,
        bool deactivated)
    {
        Assert.Equal("Eight visits", summary.GetProperty("name").GetString());
        Assert.Equal(30, summary.GetProperty("durationDays").GetInt32());
        Assert.Equal(8, summary.GetProperty("visitsLimit").GetInt32());
        Assert.Equal(1200m, summary.GetProperty("price").GetProperty("amount").GetDecimal());
        Assert.Equal("UAH", summary.GetProperty("price").GetProperty("currency").GetString());
        Assert.Equal(isActive, summary.GetProperty("isActive").GetBoolean());
        Assert.Equal(
            deactivated ? JsonValueKind.String : JsonValueKind.Null,
            summary.GetProperty("deactivatedAt").ValueKind);
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
