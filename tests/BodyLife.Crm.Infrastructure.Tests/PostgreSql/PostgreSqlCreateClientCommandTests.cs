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

public sealed class PostgreSqlCreateClientCommandTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 10, 13, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task OwnerNamedAdminAndSharedReceptionCanCreateClientsWithoutCardOrPhone()
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
            results.Add(await handler.ExecuteAsync(
                CreateCommand(
                    actors[index],
                    $"allowed-actor-{index}",
                    surname: $"Client{index}",
                    name: "Allowed"),
                CancellationToken.None));
        }

        Assert.All(results, AssertSuccessfulClientResult);
        Assert.Equal(3L, await CountRowsAsync(database, "clients"));
        Assert.Equal(0L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(3L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(3L, await CountRowsAsync(database, "command_idempotency_keys"));
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
        var validActor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var mismatchedActor = validActor with
        {
            Role = ActorRole.Owner,
        };
        var handler = CreateHandler(dbContext);

        var results = new[]
        {
            await handler.ExecuteAsync(
                CreateCommand(inactiveActor, "inactive-actor", "Inactive", "Actor"),
                CancellationToken.None),
            await handler.ExecuteAsync(
                CreateCommand(expiredActor, "expired-actor", "Expired", "Actor"),
                CancellationToken.None),
            await handler.ExecuteAsync(
                CreateCommand(mismatchedActor, "mismatched-actor", "Mismatch", "Actor"),
                CancellationToken.None),
        };

        Assert.All(results, result => AssertError(result, CommandErrorCode.PermissionDenied));
        Assert.Equal(0L, await CountRowsAsync(database, "clients"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task CommandCommitsCardAcknowledgementsAuditAndIdempotencyAtomically()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin,
            deviceLabel: "front desk tablet");
        var matchedClientId = Guid.Parse("00000000-0000-0000-0000-000000000401");
        await InsertClientAsync(
            database,
            matchedClientId,
            actor.AccountId.Value,
            "Іваненко",
            "Олексій",
            "Петрович",
            "+38 (067) 123-45-67");
        var command = CreateCommand(
            actor,
            "create-with-warnings",
            surname: "  іваненко ",
            name: "олексій",
            patronymic: "петрович",
            phone: "+38 067 123 45 67",
            cardNumber: " bl - 1001 ",
            comment: "Reception note",
            acknowledgements:
            [
                new ClientDuplicateWarningAcknowledgement(
                    matchedClientId,
                    ClientDuplicateWarningType.DuplicatePhone,
                    "Phone owner confirmed"),
                new ClientDuplicateWarningAcknowledgement(
                    matchedClientId,
                    ClientDuplicateWarningType.SimilarName,
                    "Identity details checked"),
            ]);

        var result = await CreateHandler(dbContext).ExecuteAsync(command, CancellationToken.None);

        AssertSuccessfulClientResult(result);
        var clientId = result.PrimaryEntityId!.Value.Value;
        var client = await ReadClientAsync(database, clientId);
        Assert.Equal("іваненко", client.Surname);
        Assert.Equal("олексій", client.Name);
        Assert.Equal("петрович", client.Patronymic);
        Assert.Equal("ІВАНЕНКО ОЛЕКСІЙ ПЕТРОВИЧ", client.NormalizedFullName);
        Assert.Equal("+38 067 123 45 67", client.PhoneRaw);
        Assert.Equal("380671234567", client.PhoneNormalized);
        Assert.Equal("4567", client.PhoneLastFour);
        Assert.Equal("Reception note", client.Comment);
        Assert.Equal(actor.AccountId.Value, client.CreatedByAccountId);
        Assert.Equal(TestNow.UtcDateTime, client.CreatedAt);
        Assert.Equal(2L, await CountRowsAsync(database, "clients"));
        Assert.Equal(1L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(2L, await CountRowsAsync(database, "duplicate_warning_acknowledgements"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
        Assert.Equal(
            "BL-1001",
            await database.ExecuteScalarAsync<string>(
                "select card_number_normalized from bodylife.client_card_assignments"));
        Assert.Equal(
            "duplicate_phone,similar_name",
            await database.ExecuteScalarAsync<string>(
                """
                select string_agg(warning_type, ',' order by warning_type)
                from bodylife.duplicate_warning_acknowledgements
                """));

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(ClientAuditActions.Created, audit.ActionType);
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
        Assert.Contains("bl - 1001", audit.AfterSummary, StringComparison.Ordinal);
        Assert.Contains("duplicate_phone", audit.AfterSummary, StringComparison.Ordinal);
        Assert.Contains("similar_name", audit.AfterSummary, StringComparison.Ordinal);
        Assert.Contains(matchedClientId.ToString(), audit.RelatedEntityRefs, StringComparison.OrdinalIgnoreCase);

        var idempotentClientId = await database.ExecuteScalarAsync<Guid>(
            "select primary_entity_id from bodylife.command_idempotency_keys");
        var idempotentAuditId = await database.ExecuteScalarAsync<Guid>(
            "select audit_entry_id from bodylife.command_idempotency_keys");
        var fingerprintLength = await database.ExecuteScalarAsync<int>(
            "select length(result_fingerprint) from bodylife.command_idempotency_keys");
        Assert.Equal(clientId, idempotentClientId);
        Assert.Equal(result.AuditEntryId.Value.Value, idempotentAuditId);
        Assert.Equal(64, fingerprintLength);
    }

    [PostgreSqlFact]
    public async Task DuplicateCandidatesRequireTheExactAcknowledgementSet()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var matchedClientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            matchedClientId,
            actor.AccountId.Value,
            "Іваненко",
            "Олексій",
            patronymic: null,
            "067 123 45 67");
        var handler = CreateHandler(dbContext);
        var baseCommand = CreateCommand(
            actor,
            "missing-warnings",
            "Іваненко",
            "Олексій",
            phone: "0671234567");

        var missingResult = await handler.ExecuteAsync(baseCommand, CancellationToken.None);
        var partialResult = await handler.ExecuteAsync(
            baseCommand with
            {
                Envelope = baseCommand.Envelope with { IdempotencyKey = "partial-warnings" },
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
                Envelope = baseCommand.Envelope with { IdempotencyKey = "unexpected-warning" },
                DuplicateWarningAcknowledgements =
                [
                    new ClientDuplicateWarningAcknowledgement(
                        Guid.NewGuid(),
                        ClientDuplicateWarningType.DuplicatePhone,
                        "Stale warning"),
                ],
            },
            CancellationToken.None);

        Assert.Equal(2, missingResult.Errors.Count);
        Assert.All(
            missingResult.Errors,
            error => Assert.Equal(CommandErrorCode.DuplicateWarningNotAcknowledged, error.Code));
        Assert.Single(partialResult.Errors);
        AssertError(partialResult, CommandErrorCode.DuplicateWarningNotAcknowledged);
        AssertError(unexpectedResult, CommandErrorCode.ValidationFailed);
        Assert.Equal(1L, await CountRowsAsync(database, "clients"));
        Assert.Equal(0L, await CountRowsAsync(database, "duplicate_warning_acknowledgements"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InvalidInputAndMissingIdempotencyAreRejectedBeforeMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var handler = CreateHandler(dbContext);
        var validCommand = CreateCommand(actor, "valid-key", "Valid", "Client");

        var missingKeyResult = await handler.ExecuteAsync(
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
        var invalidBackfillResult = await handler.ExecuteAsync(
            validCommand with
            {
                Envelope = validCommand.Envelope with
                {
                    IdempotencyKey = "invalid-backfill",
                    EntryOrigin = EntryOrigin.PaperFallback,
                    OccurredAt = null,
                    Reason = null,
                    Comment = null,
                },
            },
            CancellationToken.None);

        AssertError(missingKeyResult, CommandErrorCode.ValidationFailed);
        Assert.Equal("idempotencyKey", Assert.Single(missingKeyResult.Errors).Field);
        AssertError(invalidPhoneResult, CommandErrorCode.ValidationFailed);
        Assert.Equal("phone", Assert.Single(invalidPhoneResult.Errors).Field);
        AssertError(invalidBackfillResult, CommandErrorCode.ValidationFailed);
        Assert.Equal(0L, await CountRowsAsync(database, "clients"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ExistingCurrentCardReturnsStableErrorWithoutPartialRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var existingClientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            existingClientId,
            actor.AccountId.Value,
            "Existing",
            "Client",
            patronymic: null,
            phoneRaw: null);
        await InsertCurrentCardAsync(database, existingClientId, actor.AccountId.Value, "BL-EXISTING");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                actor,
                "card-conflict",
                "New",
                "Client",
                cardNumber: "bl-existing"),
            CancellationToken.None);

        AssertError(result, CommandErrorCode.CardNumberAlreadyCurrent);
        Assert.Equal(1L, await CountRowsAsync(database, "clients"));
        Assert.Equal(1L, await CountRowsAsync(database, "client_card_assignments"));
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
        var handler = CreateHandler(dbContext);
        var command = CreateCommand(actor, "replay-key", "Replay", "Client");

        var firstResult = await handler.ExecuteAsync(command, CancellationToken.None);
        var replayResult = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with
                {
                    RequestCorrelationId = new RequestCorrelationId("replay-correlation-2"),
                },
                Surname = " replay ",
                Name = "client",
            },
            CancellationToken.None);
        var changedPayloadResult = await handler.ExecuteAsync(
            command with
            {
                Surname = "Different",
            },
            CancellationToken.None);

        AssertSuccessfulClientResult(firstResult);
        AssertSuccessfulClientResult(replayResult);
        Assert.Equal(firstResult.PrimaryEntityId, replayResult.PrimaryEntityId);
        Assert.Equal(firstResult.AuditEntryId, replayResult.AuditEntryId);
        AssertError(changedPayloadResult, CommandErrorCode.DuplicateSubmission);
        Assert.Equal(1L, await CountRowsAsync(database, "clients"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentCurrentCardCommandsCommitOnlyOneCompleteWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var migrationContext = database.CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var firstTask = CreateHandler(firstContext).ExecuteAsync(
            CreateCommand(
                actor,
                "concurrent-card-1",
                "First",
                "Client",
                cardNumber: "BL-CONCURRENT-COMMAND"),
            CancellationToken.None);
        var secondTask = CreateHandler(secondContext).ExecuteAsync(
            CreateCommand(
                actor,
                "concurrent-card-2",
                "Second",
                "Client",
                cardNumber: "BL-CONCURRENT-COMMAND"),
            CancellationToken.None);

        var results = await Task.WhenAll(firstTask, secondTask);

        var success = Assert.Single(results, result => result.Status == CommandStatus.Success);
        AssertSuccessfulClientResult(success);
        var conflict = Assert.Single(results, result => result.Status == CommandStatus.Error);
        Assert.Contains(
            Assert.Single(conflict.Errors).Code,
            new[]
            {
                CommandErrorCode.CardNumberAlreadyCurrent,
                CommandErrorCode.ConcurrencyConflict,
            });
        Assert.Equal(1L, await CountRowsAsync(database, "clients"));
        Assert.Equal(1L, await CountRowsAsync(database, "client_card_assignments"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task PaperFallbackPersistsOccurredAndRecordedTimesWithAuditOrigin()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var occurredAt = TestNow.AddDays(-2);
        var command = CreateCommand(actor, "paper-fallback", "Paper", "Client") with
        {
            Envelope = new CommandEnvelope(
                actor,
                new RequestCorrelationId("paper-fallback-correlation"),
                EntryOrigin.PaperFallback,
                occurredAt,
                "paper-fallback",
                "Paper batch 2026-07-08 line 4",
                Comment: null),
        };

        var result = await CreateHandler(dbContext).ExecuteAsync(command, CancellationToken.None);

        AssertSuccessfulClientResult(result);
        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal("paper_fallback", audit.EntryOrigin);
        Assert.Equal(occurredAt.UtcDateTime, audit.OccurredAt);
        Assert.Equal(TestNow.UtcDateTime, audit.RecordedAt);
        Assert.Equal("Paper batch 2026-07-08 line 4", audit.Reason);
        var clientId = result.PrimaryEntityId!.Value.Value;
        var client = await ReadClientAsync(database, clientId);
        Assert.Equal(TestNow.UtcDateTime, client.CreatedAt);
    }

    private static CreateClientCommandHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new CreateClientCommandHandler(
            dbContext,
            new FindClientDuplicateCandidatesQueryHandler(dbContext),
            new BusinessAuditAppender(dbContext),
            new FixedTimeProvider(TestNow));
    }

    private static CreateClientCommand CreateCommand(
        ActorContext actor,
        string idempotencyKey,
        string surname,
        string name,
        string? patronymic = null,
        string? phone = null,
        string? cardNumber = null,
        string? comment = null,
        IReadOnlyList<ClientDuplicateWarningAcknowledgement>? acknowledgements = null)
    {
        return new CreateClientCommand(
            new CommandEnvelope(
                actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                TestNow.AddMinutes(-5),
                idempotencyKey,
                Reason: null,
                Comment: null),
            surname,
            name,
            patronymic,
            phone,
            cardNumber,
            comment,
            ClientOperationalStatus.Active,
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
            sessionCommand.Parameters.Add("device_label", NpgsqlDbType.Varchar).Value = deviceLabel ?? (object)DBNull.Value;
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
        string? patronymic,
        string? phoneRaw)
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
                'active',
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
        command.Parameters.Add("phone_normalized", NpgsqlDbType.Text).Value = normalizedPhone ?? (object)DBNull.Value;
        command.Parameters.Add("phone_last4", NpgsqlDbType.Text).Value = phoneLastFour ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-1));
        command.Parameters.AddWithValue("created_by_account_id", actorAccountId);
        command.Parameters.AddWithValue("updated_at", TestNow.AddDays(-1));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertCurrentCardAsync(
        PostgreSqlTestDatabase database,
        Guid clientId,
        Guid actorAccountId,
        string cardNumber)
    {
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
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("card_number_raw", cardNumber);
        command.Parameters.AddWithValue(
            "card_number_normalized",
            ClientSearchNormalizer.NormalizeCardNumber(cardNumber));
        command.Parameters.AddWithValue("assigned_at", TestNow.AddDays(-1));
        command.Parameters.AddWithValue("assigned_by_account_id", actorAccountId);
        await command.ExecuteNonQueryAsync();
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
                   comment,
                   created_by_account_id,
                   created_at
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
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetGuid(8),
            reader.GetDateTime(9));
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
                   request_correlation_id,
                   entry_origin,
                   idempotency_key,
                   related_entity_refs::text,
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
            reader.GetString(11),
            reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15));
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
        string? Comment,
        Guid CreatedByAccountId,
        DateTime CreatedAt);

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
        string RequestCorrelationId,
        string EntryOrigin,
        string? IdempotencyKey,
        string RelatedEntityRefs,
        string AfterSummary);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
