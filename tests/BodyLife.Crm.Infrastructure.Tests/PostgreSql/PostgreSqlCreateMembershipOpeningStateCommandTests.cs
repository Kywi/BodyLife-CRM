using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlCreateMembershipOpeningStateCommandTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        13,
        14,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly TestStartDate = new(2026, 7, 1);
    private static readonly DateOnly TestBaseEndDate = new(2026, 7, 30);
    private static readonly DateOnly TestOpeningAsOfDate = new(2026, 7, 13);

    [PostgreSqlFact]
    public async Task NamedAdminCreatesSourceRebuildsCacheAndWritesAuditAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            deviceLabel: "reception tablet");
        var membership = await SeedMembershipAsync(database, actor.AccountId.Value);
        var entryBatchId = Guid.NewGuid();
        var command = CreateCommand(
            actor,
            membership.MembershipId,
            "opening-success",
            declaredRemainingVisits: -2,
            knownEffectiveEndDate: new DateOnly(2026, 8, 3),
            knownExtensionDays: 4,
            sourceReference: "  Paper register 2026, page 12  ",
            entryBatchId: entryBatchId);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);

        AssertSuccessfulResult(result, membership.MembershipId);
        var openingStateId = result.PrimaryEntityId!.Value.Value;
        var openingState = await ReadOpeningStateAsync(database, openingStateId);
        Assert.Equal(membership.MembershipId, openingState.MembershipId);
        Assert.Equal(TestOpeningAsOfDate, openingState.OpeningAsOfDate);
        Assert.Equal(-2, openingState.DeclaredRemainingVisits);
        Assert.Equal(2, openingState.DeclaredNegativeBalance);
        Assert.Equal(new DateOnly(2026, 8, 3), openingState.KnownEffectiveEndDate);
        Assert.Equal(4, openingState.KnownExtensionDays);
        Assert.Equal("Paper register 2026, page 12", openingState.SourceReference);
        Assert.Equal(
            "Active membership history before launch is incomplete",
            openingState.Reason);
        Assert.Equal(TestNow, openingState.RecordedAt);
        Assert.Equal(actor.AccountId.Value, openingState.RecordedByAccountId);
        Assert.Equal(actor.SessionId.Value, openingState.RecordedSessionId);
        Assert.Equal("manual_backfill", openingState.EntryOrigin);
        Assert.Equal(entryBatchId, openingState.EntryBatchId);
        Assert.Equal("active", openingState.Status);

        var cache = await ReadCacheAsync(database, membership.MembershipId);
        Assert.Equal(0, cache.CountedVisits);
        Assert.Equal(-2, cache.RemainingVisits);
        Assert.Equal(2, cache.NegativeBalance);
        Assert.Null(cache.FirstNegativeVisitId);
        Assert.Null(cache.FirstNegativeVisitDate);
        Assert.Equal(4, cache.ExtensionDays);
        Assert.Equal(new DateOnly(2026, 8, 3), cache.EffectiveEndDate);
        Assert.Null(cache.LastCountedVisitAt);
        Assert.Equal(TestNow, cache.RecalculatedAt);
        Assert.Equal(MembershipStateCacheRebuilder.CurrentRecalculationVersion, cache.Version);

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(MembershipAuditActions.OpeningStateCreated, audit.ActionType);
        Assert.Equal(MembershipAuditActions.OpeningStateEntityType, audit.EntityType);
        Assert.Equal(openingStateId, audit.EntityId);
        Assert.Equal(actor.AccountId.Value, audit.ActorAccountId);
        Assert.Equal("named_admin", audit.ActorAccountType);
        Assert.Equal("admin", audit.ActorRole);
        Assert.Equal(actor.SessionId.Value, audit.SessionId);
        Assert.Equal("reception tablet", audit.DeviceLabel);
        Assert.Equal(command.Envelope.OccurredAt, audit.OccurredAt);
        Assert.Equal(TestNow, audit.RecordedAt);
        Assert.Equal("manual_backfill", audit.EntryOrigin);
        Assert.Equal(command.Envelope.IdempotencyKey, audit.IdempotencyKey);
        Assert.Equal(command.Envelope.RequestCorrelationId.Value, audit.RequestCorrelationId);
        Assert.Equal(
            "Active membership history before launch is incomplete",
            audit.Reason);
        Assert.Equal("Launch backfill", audit.Comment);
        Assert.Equal("{}", audit.BeforeSummary);

        using var related = JsonDocument.Parse(audit.RelatedEntityRefs);
        Assert.Equal(
            membership.ClientId,
            related.RootElement.GetProperty("clientId").GetGuid());
        Assert.Equal(
            membership.MembershipId,
            related.RootElement.GetProperty("membershipId").GetGuid());

        using var after = JsonDocument.Parse(audit.AfterSummary);
        var summary = after.RootElement;
        Assert.Equal(
            openingStateId,
            summary.GetProperty("openingStateId").GetGuid());
        Assert.Equal(-2, summary.GetProperty("declaredRemainingVisits").GetInt32());
        Assert.Equal(2, summary.GetProperty("declaredNegativeBalance").GetInt32());
        Assert.Equal(
            "Paper register 2026, page 12",
            summary.GetProperty("sourceReference").GetString());
        var recalculated = summary.GetProperty("recalculatedState");
        Assert.Equal(-2, recalculated.GetProperty("remainingVisits").GetInt32());
        Assert.Equal(2, recalculated.GetProperty("negativeBalance").GetInt32());
        Assert.Equal(4, recalculated.GetProperty("extensionDays").GetInt32());
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            recalculated.GetProperty("recalculationVersion").GetInt32());

        var idempotency = await ReadIdempotencyAsync(
            database,
            "CreateMembershipOpeningState",
            "opening-success");
        Assert.Equal("succeeded", idempotency.Status);
        Assert.Equal(openingStateId, idempotency.PrimaryEntityId);
        Assert.Equal(membership.MembershipId, idempotency.RereadTargetId);
        Assert.Equal(result.AuditEntryId.Value.Value, idempotency.AuditEntryId);
        Assert.Equal("manual_backfill", idempotency.EntryOrigin);
        Assert.Equal(64, idempotency.FingerprintLength);
        Assert.Equal(1L, await CountRowsAsync(database, "membership_opening_states"));
        Assert.Equal(1L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task OwnerAndSharedReceptionActorsCanCreateOpeningStates()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var sharedReception = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin);
        var ownerMembership = await SeedMembershipAsync(database, owner.AccountId.Value);
        var receptionMembership = await SeedMembershipAsync(
            database,
            sharedReception.AccountId.Value);
        var handler = CreateHandler(dbContext);

        var ownerResult = await handler.ExecuteAsync(
            CreateCommand(owner, ownerMembership.MembershipId, "owner-opening"),
            CancellationToken.None);
        var receptionResult = await handler.ExecuteAsync(
            CreateCommand(
                sharedReception,
                receptionMembership.MembershipId,
                "reception-opening"),
            CancellationToken.None);

        AssertSuccessfulResult(ownerResult, ownerMembership.MembershipId);
        AssertSuccessfulResult(receptionResult, receptionMembership.MembershipId);
        Assert.Equal(2L, await CountRowsAsync(database, "membership_opening_states"));
        Assert.Equal(2L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(2L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(2L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ForgedExpiredInactiveAndUnknownActorsAreDeniedWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var validAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var expiredOwner = await SeedActorAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner,
            sessionExpiresAt: TestNow.AddMinutes(-1));
        var inactiveAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            isActive: false);
        var membership = await SeedMembershipAsync(database, validAdmin.AccountId.Value);
        var forgedOwner = validAdmin with
        {
            Role = ActorRole.Owner,
            AccountKind = AccountKind.Owner,
        };
        var invalidShape = validAdmin with { AccountKind = AccountKind.Owner };
        var unknownAdmin = new ActorContext(
            AccountId.New(),
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            SessionId.New(),
            "unknown admin device");
        var handler = CreateHandler(dbContext);

        var results = new[]
        {
            await handler.ExecuteAsync(
                CreateCommand(forgedOwner, membership.MembershipId, "forged-owner"),
                CancellationToken.None),
            await handler.ExecuteAsync(
                CreateCommand(invalidShape, membership.MembershipId, "invalid-shape"),
                CancellationToken.None),
            await handler.ExecuteAsync(
                CreateCommand(expiredOwner, membership.MembershipId, "expired-owner"),
                CancellationToken.None),
            await handler.ExecuteAsync(
                CreateCommand(inactiveAdmin, membership.MembershipId, "inactive-admin"),
                CancellationToken.None),
            await handler.ExecuteAsync(
                CreateCommand(unknownAdmin, membership.MembershipId, "unknown-admin"),
                CancellationToken.None),
        };

        Assert.All(results, result => AssertError(result, CommandErrorCode.PermissionDenied));
        await AssertNoCommandMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task InvalidBackfillEnvelopeSourceAndDeclarationAreRejectedWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var membership = await SeedMembershipAsync(database, actor.AccountId.Value);
        var handler = CreateHandler(dbContext);
        var valid = CreateCommand(actor, membership.MembershipId, "valid-opening");

        var results = new[]
        {
            await handler.ExecuteAsync(
                valid with
                {
                    Envelope = valid.Envelope with
                    {
                        IdempotencyKey = "normal-origin",
                        EntryOrigin = EntryOrigin.Normal,
                    },
                },
                CancellationToken.None),
            await handler.ExecuteAsync(
                valid with
                {
                    Envelope = valid.Envelope with
                    {
                        IdempotencyKey = "missing-occurred",
                        OccurredAt = null,
                    },
                },
                CancellationToken.None),
            await handler.ExecuteAsync(
                valid with
                {
                    Envelope = valid.Envelope with
                    {
                        IdempotencyKey = "missing-reason",
                        Reason = "   ",
                    },
                },
                CancellationToken.None),
            await handler.ExecuteAsync(
                valid with { Envelope = valid.Envelope with { IdempotencyKey = null } },
                CancellationToken.None),
            await handler.ExecuteAsync(
                valid with
                {
                    Envelope = valid.Envelope with { IdempotencyKey = "blank-source" },
                    SourceReference = "   ",
                },
                CancellationToken.None),
            await handler.ExecuteAsync(
                valid with
                {
                    Envelope = valid.Envelope with { IdempotencyKey = "empty-batch" },
                    EntryBatchId = Guid.Empty,
                },
                CancellationToken.None),
            await handler.ExecuteAsync(
                valid with
                {
                    Envelope = valid.Envelope with { IdempotencyKey = "min-balance" },
                    DeclaredRemainingVisits = int.MinValue,
                },
                CancellationToken.None),
            await handler.ExecuteAsync(
                valid with
                {
                    Envelope = valid.Envelope with { IdempotencyKey = "negative-extension" },
                    KnownExtensionDays = -1,
                },
                CancellationToken.None),
            await handler.ExecuteAsync(
                valid with
                {
                    Envelope = valid.Envelope with { IdempotencyKey = "end-before-opening" },
                    KnownEffectiveEndDate = TestOpeningAsOfDate.AddDays(-1),
                },
                CancellationToken.None),
        };

        Assert.All(results, result => AssertError(result, CommandErrorCode.ValidationFailed));
        Assert.Equal("entryOrigin", Assert.Single(results[0].Errors).Field);
        Assert.Equal("occurredAt", Assert.Single(results[1].Errors).Field);
        Assert.Equal("reason", Assert.Single(results[2].Errors).Field);
        Assert.Equal("idempotencyKey", Assert.Single(results[3].Errors).Field);
        Assert.Equal("sourceReference", Assert.Single(results[4].Errors).Field);
        Assert.Equal("entryBatchId", Assert.Single(results[5].Errors).Field);
        Assert.Equal("declaredRemainingVisits", Assert.Single(results[6].Errors).Field);
        Assert.Equal("knownExtensionDays", Assert.Single(results[7].Errors).Field);
        Assert.Equal("knownEffectiveEndDate", Assert.Single(results[8].Errors).Field);
        await AssertNoCommandMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task MissingNonActiveAndCrossSourceInvalidMembershipsAreRejected()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var active = await SeedMembershipAsync(database, actor.AccountId.Value);
        var canceled = await SeedMembershipAsync(
            database,
            actor.AccountId.Value,
            status: "canceled");
        var handler = CreateHandler(dbContext);

        var missing = await handler.ExecuteAsync(
            CreateCommand(actor, Guid.NewGuid(), "missing-membership"),
            CancellationToken.None);
        var nonActive = await handler.ExecuteAsync(
            CreateCommand(actor, canceled.MembershipId, "canceled-membership"),
            CancellationToken.None);
        var beforeStart = await handler.ExecuteAsync(
            CreateCommand(actor, active.MembershipId, "before-start") with
            {
                OpeningAsOfDate = TestStartDate.AddDays(-1),
            },
            CancellationToken.None);
        var afterEnd = await handler.ExecuteAsync(
            CreateCommand(actor, active.MembershipId, "after-end") with
            {
                OpeningAsOfDate = TestBaseEndDate.AddDays(1),
            },
            CancellationToken.None);
        var shortenedTerm = await handler.ExecuteAsync(
            CreateCommand(actor, active.MembershipId, "shortened-term") with
            {
                KnownEffectiveEndDate = TestBaseEndDate.AddDays(-1),
            },
            CancellationToken.None);

        AssertError(missing, CommandErrorCode.NotFound);
        AssertError(nonActive, CommandErrorCode.MembershipNotEligible);
        AssertError(beforeStart, CommandErrorCode.ValidationFailed);
        AssertError(afterEnd, CommandErrorCode.ValidationFailed);
        AssertError(shortenedTerm, CommandErrorCode.ValidationFailed);
        await AssertNoCommandMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task ExistingActiveOpeningStateReturnsStaleAndPreservesCanonicalState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin);
        var membership = await SeedMembershipAsync(database, actor.AccountId.Value);
        var handler = CreateHandler(dbContext);

        var first = await handler.ExecuteAsync(
            CreateCommand(
                actor,
                membership.MembershipId,
                "first-opening",
                declaredRemainingVisits: -1),
            CancellationToken.None);
        var stale = await handler.ExecuteAsync(
            CreateCommand(
                actor,
                membership.MembershipId,
                "second-opening",
                declaredRemainingVisits: 2),
            CancellationToken.None);

        AssertSuccessfulResult(first, membership.MembershipId);
        AssertError(stale, CommandErrorCode.StaleState);
        var cache = await ReadCacheAsync(database, membership.MembershipId);
        Assert.Equal(-1, cache.RemainingVisits);
        Assert.Equal(1, cache.NegativeBalance);
        Assert.Equal(1L, await CountRowsAsync(database, "membership_opening_states"));
        Assert.Equal(1L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task IdempotentReplayReturnsOriginalAndChangedPayloadIsDuplicate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membership = await SeedMembershipAsync(database, actor.AccountId.Value);
        var handler = CreateHandler(dbContext);
        var command = CreateCommand(actor, membership.MembershipId, "opening-replay");

        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var changed = await handler.ExecuteAsync(
            command with { DeclaredRemainingVisits = -1 },
            CancellationToken.None);

        AssertSuccessfulResult(first, membership.MembershipId);
        AssertSuccessfulResult(replay, membership.MembershipId);
        Assert.Equal(first.PrimaryEntityId, replay.PrimaryEntityId);
        Assert.Equal(first.RereadTargetId, replay.RereadTargetId);
        Assert.Equal(first.AuditEntryId, replay.AuditEntryId);
        AssertError(changed, CommandErrorCode.DuplicateSubmission);
        Assert.Equal(1L, await CountRowsAsync(database, "membership_opening_states"));
        Assert.Equal(1L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentDifferentKeysSerializeAndCommitOneCompleteWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var migrationContext = database.CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        var actor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var membership = await SeedMembershipAsync(database, actor.AccountId.Value);
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var firstCommand = CreateCommand(
            actor,
            membership.MembershipId,
            "concurrent-opening-a",
            declaredRemainingVisits: -1);
        var secondCommand = CreateCommand(
            actor,
            membership.MembershipId,
            "concurrent-opening-b",
            declaredRemainingVisits: 1);

        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(firstCommand, CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(secondCommand, CancellationToken.None));

        var success = Assert.Single(results, result => result.Status == CommandStatus.Success);
        var stale = Assert.Single(results, result => result.Status == CommandStatus.Error);
        AssertSuccessfulResult(success, membership.MembershipId);
        AssertError(stale, CommandErrorCode.StaleState);
        var opening = await ReadOpeningStateAsync(
            database,
            success.PrimaryEntityId!.Value.Value);
        var cache = await ReadCacheAsync(database, membership.MembershipId);
        Assert.Equal(opening.DeclaredRemainingVisits, cache.RemainingVisits);
        Assert.Equal(opening.DeclaredNegativeBalance, cache.NegativeBalance);
        Assert.Equal(1L, await CountRowsAsync(database, "membership_opening_states"));
        Assert.Equal(1L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackSourceCacheAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membership = await SeedMembershipAsync(database, actor.AccountId.Value);
        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_opening_state_audit
            check (action_type <> 'membership_opening_state.created')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext).ExecuteAsync(
                CreateCommand(actor, membership.MembershipId, "audit-failure"),
                CancellationToken.None));

        await AssertNoCommandMutationAsync(database);
    }

    private static CreateMembershipOpeningStateCommandHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        return new CreateMembershipOpeningStateCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new MembershipStateCacheRebuilder(dbContext, timeProvider),
            timeProvider);
    }

    private static CreateMembershipOpeningStateCommand CreateCommand(
        ActorContext actor,
        Guid membershipId,
        string idempotencyKey,
        int declaredRemainingVisits = 2,
        DateOnly? knownEffectiveEndDate = null,
        int? knownExtensionDays = null,
        string sourceReference = "Paper register 2026, page 12",
        Guid? entryBatchId = null)
    {
        return new CreateMembershipOpeningStateCommand(
            new CommandEnvelope(
                actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.ManualBackfill,
                new DateTimeOffset(
                    TestOpeningAsOfDate,
                    new TimeOnly(10, 30),
                    TimeSpan.Zero),
                idempotencyKey,
                Reason: "  Active membership history before launch is incomplete  ",
                Comment: "  Launch backfill  "),
            membershipId,
            TestOpeningAsOfDate,
            declaredRemainingVisits,
            knownEffectiveEndDate,
            knownExtensionDays,
            sourceReference,
            entryBatchId);
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
                @is_active,
                @created_at,
                @deactivated_at);

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
                @last_seen_at)
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("display_name", $"{accountKind} test actor");
        command.Parameters.AddWithValue("account_type", MapAccountKind(accountKind));
        command.Parameters.AddWithValue("role", MapRole(role));
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.AddWithValue("created_at", TestNow.AddHours(-2));
        command.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value = isActive
            ? DBNull.Value
            : TestNow.AddHours(-1);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.Add("device_label", NpgsqlDbType.Varchar).Value =
            deviceLabel ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue(
            "expires_at",
            sessionExpiresAt ?? TestNow.AddHours(11));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        return new ActorContext(
            new AccountId(accountId),
            role,
            accountKind,
            new SessionId(sessionId),
            deviceLabel);
    }

    private static async Task<MembershipFixture> SeedMembershipAsync(
        PostgreSqlTestDatabase database,
        Guid issuedByAccountId,
        string status = "active")
    {
        var fixture = new MembershipFixture(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                'Ivanenko',
                'Ivan',
                null,
                'IVANENKO IVAN',
                null,
                null,
                null,
                null,
                'active',
                @recorded_at,
                @issued_by_account_id,
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
                'Two visits / 30 days',
                30,
                2,
                1000,
                'UAH',
                true,
                null,
                @recorded_at,
                @recorded_at,
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
                'Two visits / 30 days',
                30,
                2,
                1000,
                'UAH',
                @start_date,
                @base_end_date,
                @recorded_at,
                @issued_by_account_id,
                @status,
                'manual_backfill',
                null,
                'Opening-state command fixture')
            """;
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("issued_by_account_id", issuedByAccountId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, TestStartDate);
        command.Parameters.AddWithValue("base_end_date", NpgsqlDbType.Date, TestBaseEndDate);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(3, await command.ExecuteNonQueryAsync());

        return fixture;
    }

    private static async Task<OpeningStateRow> ReadOpeningStateAsync(
        PostgreSqlTestDatabase database,
        Guid openingStateId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select membership_id,
                   opening_as_of_date,
                   declared_remaining_visits,
                   declared_negative_balance,
                   known_effective_end_date,
                   known_extension_days,
                   source_reference,
                   reason,
                   recorded_at,
                   recorded_by_account_id,
                   recorded_session_id,
                   entry_origin,
                   entry_batch_id,
                   status
            from bodylife.membership_opening_states
            where id = @id
            """;
        command.Parameters.AddWithValue("id", openingStateId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new OpeningStateRow(
            reader.GetGuid(0),
            reader.GetFieldValue<DateOnly>(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetGuid(9),
            reader.GetGuid(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetGuid(12),
            reader.GetString(13));
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
                   extension_days,
                   effective_end_date,
                   last_counted_visit_at,
                   recalculated_at,
                   recalculation_version
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
            reader.GetInt32(5),
            reader.GetFieldValue<DateOnly>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetInt32(9));
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
                   related_entity_refs::text,
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
            reader.GetString(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17));
    }

    private static async Task<IdempotencyRow> ReadIdempotencyAsync(
        PostgreSqlTestDatabase database,
        string commandName,
        string idempotencyKey)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select status,
                   primary_entity_id,
                   reread_target_id,
                   audit_entry_id,
                   entry_origin,
                   length(result_fingerprint)
            from bodylife.command_idempotency_keys
            where command_name = @command_name
              and idempotency_key = @idempotency_key
            """;
        command.Parameters.AddWithValue("command_name", commandName);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new IdempotencyRow(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
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

    private static async Task AssertNoCommandMutationAsync(PostgreSqlTestDatabase database)
    {
        Assert.Equal(0L, await CountRowsAsync(database, "membership_opening_states"));
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static void AssertSuccessfulResult(CommandResult result, Guid membershipId)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.NotNull(result.PrimaryEntityId);
        Assert.Equal(
            MembershipAuditActions.OpeningStateEntityType,
            result.PrimaryEntityId.Value.Type);
        Assert.NotEqual(Guid.Empty, result.PrimaryEntityId.Value.Value);
        Assert.Equal(
            new EntityId(
                CreateMembershipOpeningStateCommand.CanonicalRereadEntityType,
                membershipId),
            result.RereadTargetId);
        Assert.NotNull(result.AuditEntryId);
        Assert.Empty(result.RelatedEntityIds);
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

    private sealed record MembershipFixture(
        Guid ClientId,
        Guid MembershipTypeId,
        Guid MembershipId);

    private sealed record OpeningStateRow(
        Guid MembershipId,
        DateOnly OpeningAsOfDate,
        int DeclaredRemainingVisits,
        int DeclaredNegativeBalance,
        DateOnly? KnownEffectiveEndDate,
        int? KnownExtensionDays,
        string SourceReference,
        string Reason,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid RecordedSessionId,
        string EntryOrigin,
        Guid? EntryBatchId,
        string Status);

    private sealed record CacheRow(
        int CountedVisits,
        int RemainingVisits,
        int NegativeBalance,
        Guid? FirstNegativeVisitId,
        DateOnly? FirstNegativeVisitDate,
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        DateTimeOffset? LastCountedVisitAt,
        DateTimeOffset RecalculatedAt,
        int Version);

    private sealed record AuditRow(
        string ActionType,
        string EntityType,
        Guid EntityId,
        string RelatedEntityRefs,
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
        string? IdempotencyKey);

    private sealed record IdempotencyRow(
        string Status,
        Guid PrimaryEntityId,
        Guid RereadTargetId,
        Guid AuditEntryId,
        string EntryOrigin,
        int FingerprintLength);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
