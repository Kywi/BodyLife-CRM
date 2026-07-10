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

public sealed class PostgreSqlUpdateClientCommandTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SeedUpdatedAt = TestNow.AddDays(-1);

    [PostgreSqlFact]
    public async Task OwnerNamedAdminAndSharedReceptionCanUpdateClients()
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
            await InsertClientAsync(
                database,
                clientId,
                actors[index].AccountId.Value,
                $"Allowed{index}",
                "Before");
            results.Add(await handler.ExecuteAsync(
                UpdateCommand(
                    actors[index],
                    $"allowed-actor-{index}",
                    clientId,
                    SeedUpdatedAt,
                    $"Allowed{index}",
                    "After"),
                CancellationToken.None));
        }

        Assert.All(results, AssertSuccessfulClientResult);
        Assert.Equal(3L, await CountRowsAsync(database, "clients"));
        Assert.Equal(3L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(3L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task UpdateCommitsNormalizedProfileStatusAuditAndIdempotencyWithoutChangingCard()
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
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Old",
            "Member",
            "Initial",
            "067 111 22 33",
            "Old note");
        var cardAssignmentId = await InsertCurrentCardAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "BL-UPDATE-1001");
        var command = UpdateCommand(
            actor,
            "update-profile",
            clientId,
            SeedUpdatedAt,
            surname: "  New  ",
            name: "Member",
            patronymic: "Middle",
            phone: "+38 (067) 765-43-21",
            comment: "  Updated at reception  ",
            operationalStatus: ClientOperationalStatus.Inactive);

        var result = await CreateHandler(dbContext).ExecuteAsync(command, CancellationToken.None);

        AssertSuccessfulClientResult(result);
        Assert.Equal(clientId, result.PrimaryEntityId!.Value.Value);
        var client = await ReadClientAsync(database, clientId);
        Assert.Equal("New", client.Surname);
        Assert.Equal("Member", client.Name);
        Assert.Equal("Middle", client.Patronymic);
        Assert.Equal("NEW MEMBER MIDDLE", client.NormalizedFullName);
        Assert.Equal("+38 (067) 765-43-21", client.PhoneRaw);
        Assert.Equal("380677654321", client.PhoneNormalized);
        Assert.Equal("4321", client.PhoneLastFour);
        Assert.Equal("inactive", client.OperationalStatus);
        Assert.Equal("Updated at reception", client.Comment);
        Assert.Equal(TestNow.UtcDateTime, client.UpdatedAt);
        Assert.Equal(actor.AccountId.Value, client.CreatedByAccountId);

        var card = await ReadCurrentCardAsync(database, clientId);
        Assert.Equal(cardAssignmentId, card.Id);
        Assert.Equal("BL-UPDATE-1001", card.CardNumberRaw);
        Assert.Equal("BL-UPDATE-1001", card.CardNumberNormalized);
        Assert.True(card.IsCurrent);
        Assert.Equal(1L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(0L, await CountRowsAsync(database, "duplicate_warning_acknowledgements"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(ClientAuditActions.Updated, audit.ActionType);
        Assert.Equal(ClientAuditActions.EntityType, audit.EntityType);
        Assert.Equal(clientId, audit.EntityId);
        Assert.Equal(actor.AccountId.Value, audit.ActorAccountId);
        Assert.Equal("shared_reception_admin", audit.ActorAccountType);
        Assert.Equal("admin", audit.ActorRole);
        Assert.Equal(actor.SessionId.Value, audit.SessionId);
        Assert.Equal("front desk tablet", audit.DeviceLabel);
        Assert.Equal(command.Envelope.RequestCorrelationId.Value, audit.RequestCorrelationId);
        Assert.Equal("normal", audit.EntryOrigin);
        Assert.Equal(command.Envelope.IdempotencyKey, audit.IdempotencyKey);
        Assert.Contains("Old", audit.BeforeSummary, StringComparison.Ordinal);
        Assert.Contains("Old note", audit.BeforeSummary, StringComparison.Ordinal);
        Assert.Contains("New", audit.AfterSummary, StringComparison.Ordinal);
        Assert.Contains("Updated at reception", audit.AfterSummary, StringComparison.Ordinal);
        Assert.Contains("inactive", audit.AfterSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("BL-UPDATE-1001", audit.AfterSummary, StringComparison.Ordinal);

        Assert.Equal(
            "UpdateClient",
            await database.ExecuteScalarAsync<string>(
                "select command_name from bodylife.command_idempotency_keys"));
        Assert.Equal(
            clientId,
            await database.ExecuteScalarAsync<Guid>(
                "select reread_target_id from bodylife.command_idempotency_keys"));
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
            await InsertClientAsync(
                database,
                clientId,
                actors[index].AccountId.Value,
                $"Denied{index}",
                "Before");
            results.Add(await handler.ExecuteAsync(
                UpdateCommand(
                    actors[index],
                    $"denied-actor-{index}",
                    clientId,
                    SeedUpdatedAt,
                    $"Denied{index}",
                    "After"),
                CancellationToken.None));
        }

        Assert.All(results, result => AssertError(result, CommandErrorCode.PermissionDenied));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.clients where name = 'After'"));
    }

    [PostgreSqlFact]
    public async Task MissingStaleAndNoOpUpdatesAreRejectedWithoutSideEffects()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Stable",
            "Client",
            comment: "No changes");
        var handler = CreateHandler(dbContext);

        var missingResult = await handler.ExecuteAsync(
            UpdateCommand(
                actor,
                "missing-client",
                Guid.NewGuid(),
                SeedUpdatedAt,
                "Missing",
                "Client"),
            CancellationToken.None);
        var staleResult = await handler.ExecuteAsync(
            UpdateCommand(
                actor,
                "stale-client",
                clientId,
                SeedUpdatedAt.AddMinutes(-1),
                "Changed",
                "Client"),
            CancellationToken.None);
        var noOpResult = await handler.ExecuteAsync(
            UpdateCommand(
                actor,
                "no-op-client",
                clientId,
                SeedUpdatedAt,
                "Stable",
                "Client",
                comment: "No changes"),
            CancellationToken.None);

        AssertError(missingResult, CommandErrorCode.NotFound);
        AssertError(staleResult, CommandErrorCode.StaleState);
        AssertError(noOpResult, CommandErrorCode.ValidationFailed);
        var client = await ReadClientAsync(database, clientId);
        Assert.Equal("Stable", client.Surname);
        Assert.Equal("Client", client.Name);
        Assert.Equal(SeedUpdatedAt.UtcDateTime, client.UpdatedAt);
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task DuplicateChecksExcludeTheEditedClientAndRequireTheExactAcknowledgementSet()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Same",
            "Identity",
            phoneRaw: "067 123 45 67");
        var handler = CreateHandler(dbContext);

        var selfOnlyResult = await handler.ExecuteAsync(
            UpdateCommand(
                actor,
                "self-exclusion",
                clientId,
                SeedUpdatedAt,
                "Same",
                "Identity",
                phone: "067 123 45 67",
                comment: "Self match excluded"),
            CancellationToken.None);

        AssertSuccessfulClientResult(selfOnlyResult);
        var matchedClientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            matchedClientId,
            actor.AccountId.Value,
            "Same",
            "Identity",
            phoneRaw: "067 123 45 67");
        var baseCommand = UpdateCommand(
            actor,
            "missing-acknowledgements",
            clientId,
            TestNow,
            "Same",
            "Identity",
            phone: "067 123 45 67",
            comment: "Duplicate checked");

        var missingResult = await handler.ExecuteAsync(baseCommand, CancellationToken.None);
        var partialResult = await handler.ExecuteAsync(
            baseCommand with
            {
                Envelope = baseCommand.Envelope with { IdempotencyKey = "partial-acknowledgements" },
                DuplicateWarningAcknowledgements =
                [
                    new ClientDuplicateWarningAcknowledgement(
                        matchedClientId,
                        ClientDuplicateWarningType.DuplicatePhone,
                        "Phone checked"),
                ],
            },
            CancellationToken.None);
        var unexpectedResult = await handler.ExecuteAsync(
            baseCommand with
            {
                Envelope = baseCommand.Envelope with { IdempotencyKey = "unexpected-acknowledgement" },
                DuplicateWarningAcknowledgements =
                [
                    new ClientDuplicateWarningAcknowledgement(
                        Guid.NewGuid(),
                        ClientDuplicateWarningType.DuplicatePhone,
                        "Stale warning"),
                ],
            },
            CancellationToken.None);
        var acceptedResult = await handler.ExecuteAsync(
            baseCommand with
            {
                Envelope = baseCommand.Envelope with { IdempotencyKey = "accepted-acknowledgements" },
                DuplicateWarningAcknowledgements =
                [
                    new ClientDuplicateWarningAcknowledgement(
                        matchedClientId,
                        ClientDuplicateWarningType.DuplicatePhone,
                        "Phone checked"),
                    new ClientDuplicateWarningAcknowledgement(
                        matchedClientId,
                        ClientDuplicateWarningType.SimilarName,
                        "Identity checked"),
                ],
            },
            CancellationToken.None);

        Assert.Equal(2, missingResult.Errors.Count);
        Assert.All(
            missingResult.Errors,
            error => Assert.Equal(CommandErrorCode.DuplicateWarningNotAcknowledged, error.Code));
        AssertError(partialResult, CommandErrorCode.DuplicateWarningNotAcknowledged);
        AssertError(unexpectedResult, CommandErrorCode.ValidationFailed);
        AssertSuccessfulClientResult(acceptedResult);
        Assert.Equal(2L, await CountRowsAsync(database, "duplicate_warning_acknowledgements"));
        Assert.Equal(
            "duplicate_phone,similar_name",
            await database.ExecuteScalarAsync<string>(
                """
                select string_agg(warning_type, ',' order by warning_type)
                from bodylife.duplicate_warning_acknowledgements
                """));
        Assert.Equal(
            matchedClientId,
            await database.ExecuteScalarAsync<Guid>(
                "select matched_client_id from bodylife.duplicate_warning_acknowledgements limit 1"));
        Assert.Equal(2L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(2L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InvalidCommandIdentityAndEnvelopeAreRejectedBeforeMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database, clientId, actor.AccountId.Value, "Valid", "Client");
        var handler = CreateHandler(dbContext);
        var validCommand = UpdateCommand(
            actor,
            "valid-update",
            clientId,
            SeedUpdatedAt,
            "Changed",
            "Client");

        var emptyClientIdResult = await handler.ExecuteAsync(
            validCommand with { ClientId = Guid.Empty },
            CancellationToken.None);
        var missingExpectedVersionResult = await handler.ExecuteAsync(
            validCommand with { ExpectedUpdatedAt = default },
            CancellationToken.None);
        var missingIdempotencyResult = await handler.ExecuteAsync(
            validCommand with
            {
                Envelope = validCommand.Envelope with { IdempotencyKey = null },
            },
            CancellationToken.None);
        var invalidPhoneResult = await handler.ExecuteAsync(
            validCommand with
            {
                Envelope = validCommand.Envelope with { IdempotencyKey = "invalid-phone" },
                Phone = "12",
            },
            CancellationToken.None);

        AssertError(emptyClientIdResult, CommandErrorCode.ValidationFailed);
        Assert.Equal("clientId", Assert.Single(emptyClientIdResult.Errors).Field);
        AssertError(missingExpectedVersionResult, CommandErrorCode.ValidationFailed);
        Assert.Equal("expectedUpdatedAt", Assert.Single(missingExpectedVersionResult.Errors).Field);
        AssertError(missingIdempotencyResult, CommandErrorCode.ValidationFailed);
        Assert.Equal("idempotencyKey", Assert.Single(missingIdempotencyResult.Errors).Field);
        AssertError(invalidPhoneResult, CommandErrorCode.ValidationFailed);
        Assert.Equal("phone", Assert.Single(invalidPhoneResult.Errors).Field);
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
        await InsertClientAsync(database, clientId, actor.AccountId.Value, "Replay", "Before");
        var handler = CreateHandler(dbContext);
        var command = UpdateCommand(
            actor,
            "update-replay",
            clientId,
            SeedUpdatedAt,
            "Replay",
            "After");

        var firstResult = await handler.ExecuteAsync(command, CancellationToken.None);
        var replayResult = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with
                {
                    RequestCorrelationId = new RequestCorrelationId("update-replay-correlation-2"),
                },
                Surname = " replay ",
                Name = "after",
            },
            CancellationToken.None);
        var changedPayloadResult = await handler.ExecuteAsync(
            command with { Name = "Different" },
            CancellationToken.None);

        AssertSuccessfulClientResult(firstResult);
        AssertSuccessfulClientResult(replayResult);
        Assert.Equal(firstResult.PrimaryEntityId, replayResult.PrimaryEntityId);
        Assert.Equal(firstResult.AuditEntryId, replayResult.AuditEntryId);
        AssertError(changedPayloadResult, CommandErrorCode.DuplicateSubmission);
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentUpdatesFromOneExpectedVersionCommitOnlyOneWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var migrationContext = database.CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database, clientId, actor.AccountId.Value, "Concurrent", "Before");
        await InsertCurrentCardAsync(database, clientId, actor.AccountId.Value, "BL-CONCURRENT-UPDATE");
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var firstTask = CreateHandler(firstContext).ExecuteAsync(
            UpdateCommand(
                actor,
                "concurrent-update-1",
                clientId,
                SeedUpdatedAt,
                "Concurrent",
                "First"),
            CancellationToken.None);
        var secondTask = CreateHandler(secondContext).ExecuteAsync(
            UpdateCommand(
                actor,
                "concurrent-update-2",
                clientId,
                SeedUpdatedAt,
                "Concurrent",
                "Second"),
            CancellationToken.None);

        var results = await Task.WhenAll(firstTask, secondTask);

        AssertSuccessfulClientResult(Assert.Single(results, result => result.Status == CommandStatus.Success));
        var rejectedResult = Assert.Single(results, result => result.Status == CommandStatus.Error);
        Assert.Contains(
            Assert.Single(rejectedResult.Errors).Code,
            new[] { CommandErrorCode.StaleState, CommandErrorCode.ConcurrencyConflict });
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
        Assert.Equal(1L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(
            "BL-CONCURRENT-UPDATE",
            await database.ExecuteScalarAsync<string>(
                "select card_number_normalized from bodylife.client_card_assignments"));
    }

    private static UpdateClientCommandHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new UpdateClientCommandHandler(
            dbContext,
            new FindClientDuplicateCandidatesQueryHandler(dbContext),
            new BusinessAuditAppender(dbContext),
            new FixedTimeProvider(TestNow));
    }

    private static UpdateClientCommand UpdateCommand(
        ActorContext actor,
        string idempotencyKey,
        Guid clientId,
        DateTimeOffset expectedUpdatedAt,
        string surname,
        string name,
        string? patronymic = null,
        string? phone = null,
        string? comment = null,
        ClientOperationalStatus operationalStatus = ClientOperationalStatus.Active,
        IReadOnlyList<ClientDuplicateWarningAcknowledgement>? acknowledgements = null)
    {
        return new UpdateClientCommand(
            new CommandEnvelope(
                actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                TestNow.AddMinutes(-5),
                idempotencyKey,
                Reason: null,
                Comment: null),
            clientId,
            expectedUpdatedAt,
            surname,
            name,
            patronymic,
            phone,
            comment,
            operationalStatus,
            acknowledgements ?? []);
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
            accountCommand.Parameters.AddWithValue("display_name", $"{accountKind} update actor");
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
        string surname,
        string name,
        string? patronymic = null,
        string? phoneRaw = null,
        string? comment = null,
        string operationalStatus = "active")
    {
        var normalizedFullName = ClientSearchNormalizer.NormalizeFullName(surname, name, patronymic);
        var normalizedPhone = phoneRaw is null ? null : ClientSearchNormalizer.NormalizePhone(phoneRaw);
        var phoneLastFour = normalizedPhone is null
            ? null
            : ClientSearchNormalizer.ExtractPhoneLastFour(normalizedPhone);
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
                operational_status,
                comment,
                created_at,
                created_by_account_id,
                updated_at)
            values (
                @id,
                @surname,
                @name,
                @patronymic,
                @normalized_full_name,
                @phone_raw,
                @phone_normalized,
                @phone_last4,
                @operational_status,
                @comment,
                @created_at,
                @created_by_account_id,
                @updated_at)
            """;
        command.Parameters.AddWithValue("id", clientId);
        command.Parameters.AddWithValue("surname", surname);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.Add("patronymic", NpgsqlDbType.Text).Value = patronymic ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("normalized_full_name", normalizedFullName);
        command.Parameters.Add("phone_raw", NpgsqlDbType.Text).Value = phoneRaw ?? (object)DBNull.Value;
        command.Parameters.Add("phone_normalized", NpgsqlDbType.Text).Value =
            normalizedPhone ?? (object)DBNull.Value;
        command.Parameters.Add("phone_last4", NpgsqlDbType.Text).Value = phoneLastFour ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("operational_status", operationalStatus);
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value = comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-2));
        command.Parameters.AddWithValue("created_by_account_id", actorAccountId);
        command.Parameters.AddWithValue("updated_at", SeedUpdatedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> InsertCurrentCardAsync(
        PostgreSqlTestDatabase database,
        Guid clientId,
        Guid actorAccountId,
        string cardNumber)
    {
        var assignmentId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
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
        command.Parameters.AddWithValue("id", assignmentId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("card_number_raw", cardNumber);
        command.Parameters.AddWithValue(
            "card_number_normalized",
            ClientSearchNormalizer.NormalizeCardNumber(cardNumber));
        command.Parameters.AddWithValue("assigned_at", SeedUpdatedAt);
        command.Parameters.AddWithValue("assigned_by_account_id", actorAccountId);
        await command.ExecuteNonQueryAsync();
        return assignmentId;
    }

    private static async Task<ClientRow> ReadClientAsync(
        PostgreSqlTestDatabase database,
        Guid clientId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select surname,
                   name,
                   patronymic,
                   normalized_full_name,
                   phone_raw,
                   phone_normalized,
                   phone_last4,
                   operational_status,
                   comment,
                   created_by_account_id,
                   updated_at
            from bodylife.clients
            where id = @id
            """;
        command.Parameters.AddWithValue("id", clientId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new ClientRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetGuid(9),
            reader.GetDateTime(10));
    }

    private static async Task<CardRow> ReadCurrentCardAsync(
        PostgreSqlTestDatabase database,
        Guid clientId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select id,
                   card_number_raw,
                   card_number_normalized,
                   is_current
            from bodylife.client_card_assignments
            where client_id = @client_id
              and is_current
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new CardRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3));
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
            reader.GetString(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12));
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

    private sealed record ClientRow(
        string Surname,
        string Name,
        string? Patronymic,
        string NormalizedFullName,
        string? PhoneRaw,
        string? PhoneNormalized,
        string? PhoneLastFour,
        string OperationalStatus,
        string? Comment,
        Guid CreatedByAccountId,
        DateTime UpdatedAt);

    private sealed record CardRow(
        Guid Id,
        string CardNumberRaw,
        string CardNumberNormalized,
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
        string RequestCorrelationId,
        string EntryOrigin,
        string? IdempotencyKey,
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
