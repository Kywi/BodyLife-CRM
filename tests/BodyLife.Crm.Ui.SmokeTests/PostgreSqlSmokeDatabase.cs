using System.Text.Json;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Ui.SmokeTests;

internal sealed class PostgreSqlSmokeDatabase : IAsyncDisposable
{
    private const string AdminConnectionStringEnvironmentVariable = "BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING";

    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);

    private PostgreSqlSmokeDatabase(string adminConnectionString, string databaseName)
    {
        AdminConnectionStringValue = adminConnectionString;
        DatabaseName = databaseName;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
            Pooling = false,
        }.ConnectionString;
    }

    public string ConnectionString { get; }

    private string AdminConnectionStringValue { get; }

    private string DatabaseName { get; }

    public static async Task<PostgreSqlSmokeDatabase> CreateAsync()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(AdminConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            throw new InvalidOperationException($"{AdminConnectionStringEnvironmentVariable} is required for authenticated UI smoke tests.");
        }

        var databaseName = $"bodylife_ui_smoke_{Guid.NewGuid():N}";
        await ExecuteAdminCommandAsync(adminConnectionString, $"CREATE DATABASE {QuoteIdentifier(databaseName)}");

        return new PostgreSqlSmokeDatabase(adminConnectionString, databaseName);
    }

    public BodyLifeDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BodyLifeDbContext>();
        BodyLifeDbContextOptions.Configure(optionsBuilder, ConnectionString);

        return new BodyLifeDbContext(optionsBuilder.Options);
    }

    public async Task<Guid> SeedMembershipTypeAsync(
        string name,
        int durationDays,
        int visitsLimit,
        decimal priceAmount,
        bool isActive,
        string? comment,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? deactivatedAt)
    {
        var membershipTypeId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(ConnectionString);
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
                @name,
                @duration_days,
                @visits_limit,
                @price_amount,
                'UAH',
                @is_active,
                @comment,
                @created_at,
                @updated_at,
                @deactivated_at)
            """;
        command.Parameters.AddWithValue("id", membershipTypeId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("duration_days", durationDays);
        command.Parameters.AddWithValue("visits_limit", visitsLimit);
        command.Parameters.AddWithValue("price_amount", priceAmount);
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value =
            comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("created_at", createdAt);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        command.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value =
            deactivatedAt ?? (object)DBNull.Value;
        await command.ExecuteNonQueryAsync();

        return membershipTypeId;
    }

    public async Task<long> CountMembershipTypesByNameAsync(string name)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select count(*)
            from bodylife.membership_types
            where name = @name
            """;
        command.Parameters.AddWithValue("name", name);
        return (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The membership type count query returned no value."));
    }

    public async Task<Guid?> FindMembershipTypeIdByNameAsync(string name)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select id
            from bodylife.membership_types
            where name = @name
            order by id
            limit 1
            """;
        command.Parameters.AddWithValue("name", name);
        var result = await command.ExecuteScalarAsync();
        return result is Guid membershipTypeId ? membershipTypeId : null;
    }

    public Task<long> CountMembershipTypeCreateAuditEntriesAsync()
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries
            where action_type = 'membership_type.created'
            """);
    }

    public Task<long> CountCreateMembershipTypeIdempotencyKeysAsync()
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'CreateMembershipType'
            """);
    }

    public async Task<MembershipTypeSmokeSnapshot> ReadMembershipTypeAsync(
        Guid membershipTypeId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                name,
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
            where id = @membership_type_id
            """;
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("The smoke membership type was not found.");
        }

        return new MembershipTypeSmokeSnapshot(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetDecimal(3),
            reader.GetString(4),
            reader.GetBoolean(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetFieldValue<DateTime>(7),
            reader.GetFieldValue<DateTime>(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTime>(9));
    }

    public async Task AdvanceMembershipTypeForStaleTestAsync(
        Guid membershipTypeId,
        string canonicalName)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_types
            set name = @name,
                updated_at = updated_at + interval '1 hour'
            where id = @membership_type_id
            """;
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("name", canonicalName);
        var updatedRows = await command.ExecuteNonQueryAsync();

        if (updatedRows != 1)
        {
            throw new InvalidOperationException(
                $"Expected to advance one smoke membership type, but updated {updatedRows} rows.");
        }
    }

    public Task<long> CountMembershipTypeEditAuditEntriesAsync(Guid membershipTypeId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries
            where action_type = 'membership_type.edited'
              and entity_id = @membership_type_id
            """,
            "membership_type_id",
            membershipTypeId);
    }

    public Task<long> CountEditMembershipTypeIdempotencyKeysAsync(Guid membershipTypeId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'EditMembershipType'
              and primary_entity_id = @membership_type_id
            """,
            "membership_type_id",
            membershipTypeId);
    }

    public async Task<string?> ReadLatestMembershipTypeEditReasonAsync(Guid membershipTypeId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select reason
            from bodylife.business_audit_entries
            where action_type = 'membership_type.edited'
              and entity_id = @membership_type_id
            order by recorded_at desc, id desc
            limit 1
            """;
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        return await command.ExecuteScalarAsync() as string;
    }

    public async Task<DateTime> DeactivateMembershipTypeForAlreadyInactiveTestAsync(
        Guid membershipTypeId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_types
            set is_active = false,
                updated_at = updated_at + interval '2 hours',
                deactivated_at = updated_at + interval '2 hours'
            where id = @membership_type_id
              and is_active
            returning updated_at
            """;
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        var result = await command.ExecuteScalarAsync();

        return result is DateTime updatedAt
            ? updatedAt
            : throw new InvalidOperationException(
                "Expected to deactivate one active smoke membership type.");
    }

    public Task<long> CountMembershipTypeDeactivateAuditEntriesAsync(Guid membershipTypeId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries
            where action_type = 'membership_type.deactivated'
              and entity_id = @membership_type_id
            """,
            "membership_type_id",
            membershipTypeId);
    }

    public Task<long> CountDeactivateMembershipTypeIdempotencyKeysAsync(Guid membershipTypeId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'DeactivateMembershipType'
              and primary_entity_id = @membership_type_id
            """,
            "membership_type_id",
            membershipTypeId);
    }

    public async Task<string?> ReadLatestMembershipTypeDeactivateReasonAsync(
        Guid membershipTypeId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select reason
            from bodylife.business_audit_entries
            where action_type = 'membership_type.deactivated'
              and entity_id = @membership_type_id
            order by recorded_at desc, id desc
            limit 1
            """;
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        return await command.ExecuteScalarAsync() as string;
    }

    public async Task<int> ExpireSessionAsync(string deviceLabel)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.sessions
            set expires_at = started_at + interval '1 millisecond'
            where device_label = @device_label
              and ended_at is null
            """;
        command.Parameters.AddWithValue("device_label", deviceLabel);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> IsSessionEndedAsync(string deviceLabel)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select ended_at is not null
            from bodylife.sessions
            where device_label = @device_label
            order by started_at desc
            limit 1
            """;
        command.Parameters.AddWithValue("device_label", deviceLabel);
        return (bool)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The smoke session was not found."));
    }

    public async Task<Guid> SeedClientAsync(
        Guid createdByAccountId,
        string surname,
        string name,
        string? phone,
        string? cardNumber,
        string? comment = null,
        string operationalStatus = "active")
    {
        var clientId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
        var normalizedPhone = phone is null
            ? null
            : ClientSearchNormalizer.NormalizePhone(phone);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var clientCommand = connection.CreateCommand())
        {
            clientCommand.Transaction = transaction;
            clientCommand.CommandText =
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
                    @id,
                    @surname,
                    @name,
                    null,
                    @normalized_full_name,
                    @phone_raw,
                    @phone_normalized,
                    @phone_last4,
                    @comment,
                    @operational_status,
                    @created_at,
                    @created_by_account_id,
                    @updated_at)
                """;
            clientCommand.Parameters.AddWithValue("id", clientId);
            clientCommand.Parameters.AddWithValue("surname", surname);
            clientCommand.Parameters.AddWithValue("name", name);
            clientCommand.Parameters.AddWithValue(
                "normalized_full_name",
                ClientSearchNormalizer.NormalizeFullName(surname, name, patronymic: null));
            clientCommand.Parameters.Add("phone_raw", NpgsqlDbType.Text).Value =
                phone ?? (object)DBNull.Value;
            clientCommand.Parameters.Add("phone_normalized", NpgsqlDbType.Text).Value =
                normalizedPhone ?? (object)DBNull.Value;
            clientCommand.Parameters.Add("phone_last4", NpgsqlDbType.Text).Value =
                normalizedPhone is null
                    ? DBNull.Value
                    : ClientSearchNormalizer.ExtractPhoneLastFour(normalizedPhone);
            clientCommand.Parameters.Add("comment", NpgsqlDbType.Text).Value =
                comment ?? (object)DBNull.Value;
            clientCommand.Parameters.AddWithValue("operational_status", operationalStatus);
            clientCommand.Parameters.AddWithValue("created_at", createdAt);
            clientCommand.Parameters.AddWithValue("created_by_account_id", createdByAccountId);
            clientCommand.Parameters.AddWithValue("updated_at", createdAt);
            await clientCommand.ExecuteNonQueryAsync();
        }

        if (cardNumber is not null)
        {
            await using var cardCommand = connection.CreateCommand();
            cardCommand.Transaction = transaction;
            cardCommand.CommandText =
                """
                insert into bodylife.client_card_assignments (
                    id,
                    client_id,
                    card_number_raw,
                    card_number_normalized,
                    assigned_at,
                    assigned_by_account_id,
                    ended_at,
                    ended_by_account_id,
                    end_reason,
                    is_current)
                values (
                    @id,
                    @client_id,
                    @card_number_raw,
                    @card_number_normalized,
                    @assigned_at,
                    @assigned_by_account_id,
                    null,
                    null,
                    null,
                    true)
                """;
            cardCommand.Parameters.AddWithValue("id", Guid.NewGuid());
            cardCommand.Parameters.AddWithValue("client_id", clientId);
            cardCommand.Parameters.AddWithValue("card_number_raw", cardNumber);
            cardCommand.Parameters.AddWithValue(
                "card_number_normalized",
                ClientSearchNormalizer.NormalizeCardNumber(cardNumber));
            cardCommand.Parameters.AddWithValue("assigned_at", createdAt);
            cardCommand.Parameters.AddWithValue("assigned_by_account_id", createdByAccountId);
            await cardCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return clientId;
    }

    public async Task<AuditTimelineSmokeScenario> SeedAuditTimelineAsync(
        Guid ownerAccountId,
        Guid sharedAdminAccountId)
    {
        if (ownerAccountId == Guid.Empty || sharedAdminAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Audit timeline actor ids must be non-empty.",
                nameof(ownerAccountId));
        }

        const int pageSize = 10;
        const int totalEntries = 12;
        const string ownerDeviceLabel = "Owner audit workstation";
        const string sharedDeviceLabel = "Shared front desk tablet";
        const string featuredCorrelationId = "audit-ui-paper-fallback";
        const decimal originalPaymentAmount = 1200m;
        const decimal replacementPaymentAmount = 950m;
        const decimal canceledPaymentAmount = 500m;
        const string originalMembershipTypeName = "Eight visits";
        const string updatedMembershipTypeName = "Evening Twelve";
        const decimal originalMembershipTypePrice = 1200m;
        const decimal updatedMembershipTypePrice = 1600.50m;
        var recordedDate = new DateOnly(2026, 7, 18);
        var recordedBase = new DateTimeOffset(
            recordedDate,
            new TimeOnly(9, 0),
            TimeSpan.Zero);
        var clientId = await SeedClientAsync(
            ownerAccountId,
            "Audit",
            "Timeline",
            "+380 67 880 00 01",
            "BL-AUDIT-TIMELINE");
        var clientChangeClientId = await SeedClientAsync(
            ownerAccountId,
            "Kovalchuk",
            "Iryna",
            "+38 (067) 765-43-21",
            "BL-AUDIT-CLIENT-CURRENT");
        var ownerSessionId = Guid.NewGuid();
        var sharedSessionId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var featuredAuditEntryId = Guid.Empty;
        var featuredEntityId = Guid.Empty;
        var featuredOccurredAt = default(DateTimeOffset);
        var featuredRecordedAt = default(DateTimeOffset);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.Transaction = transaction;
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
                values
                    (
                        @owner_session_id,
                        @owner_account_id,
                        'Owner audit workstation',
                        @started_at,
                        @expires_at,
                        @last_seen_at,
                        @last_seen_at),
                    (
                        @shared_session_id,
                        @shared_account_id,
                        @shared_device_label,
                        @started_at,
                        @expires_at,
                        @last_seen_at,
                        @last_seen_at)
                """;
            sessionCommand.Parameters.AddWithValue(
                "owner_session_id",
                ownerSessionId);
            sessionCommand.Parameters.AddWithValue(
                "owner_account_id",
                ownerAccountId);
            sessionCommand.Parameters.AddWithValue(
                "shared_session_id",
                sharedSessionId);
            sessionCommand.Parameters.AddWithValue(
                "shared_account_id",
                sharedAdminAccountId);
            sessionCommand.Parameters.AddWithValue(
                "shared_device_label",
                sharedDeviceLabel);
            sessionCommand.Parameters.AddWithValue(
                "started_at",
                recordedBase.AddHours(-1));
            sessionCommand.Parameters.AddWithValue(
                "expires_at",
                recordedBase.AddDays(30));
            sessionCommand.Parameters.AddWithValue(
                "last_seen_at",
                recordedBase);
            Assert.Equal(2, await sessionCommand.ExecuteNonQueryAsync());
        }

        for (var index = 0; index < totalEntries; index++)
        {
            var isFeatured = index == totalEntries - 1;
            var auditEntryId = Guid.NewGuid();
            var entityId = Guid.NewGuid();
            var recordedAt = recordedBase.AddMinutes(index);
            var occurredAt = isFeatured
                ? recordedAt.AddDays(-2)
                : recordedAt.AddMinutes(-5);
            var actorAccountId = isFeatured
                ? sharedAdminAccountId
                : ownerAccountId;
            var actorAccountType = isFeatured
                ? "shared_reception_admin"
                : "owner";
            var actorRole = isFeatured ? "admin" : "owner";
            var sessionId = isFeatured ? sharedSessionId : ownerSessionId;
            var deviceLabel = isFeatured
                ? sharedDeviceLabel
                : "Owner audit workstation";
            var entryOrigin = isFeatured ? "paper_fallback" : "normal";
            var reason = isFeatured ? "Recovered from paper register" : null;
            var comment = isFeatured
                ? "Entered after reception connectivity returned"
                : null;
            var correlationId = isFeatured
                ? featuredCorrelationId
                : $"audit-ui-{index:D2}";
            var relatedEntityRefs = JsonSerializer.Serialize(new
            {
                clientId,
                membershipId,
            });
            var beforeSummary = JsonSerializer.Serialize(new
            {
                remainingVisits = 10 - index,
            });
            var afterSummary = JsonSerializer.Serialize(new
            {
                remainingVisits = 9 - index,
            });

            await using var auditCommand = connection.CreateCommand();
            auditCommand.Transaction = transaction;
            auditCommand.CommandText =
                """
                insert into bodylife.business_audit_entries (
                    id,
                    action_type,
                    entity_type,
                    entity_id,
                    related_entity_refs,
                    actor_account_id,
                    actor_account_type,
                    actor_role,
                    session_id,
                    device_label,
                    occurred_at,
                    recorded_at,
                    reason,
                    comment,
                    before_summary,
                    after_summary,
                    request_correlation_id,
                    entry_origin,
                    idempotency_key,
                    changed_after_close)
                values (
                    @id,
                    'visit.marked',
                    'visit',
                    @entity_id,
                    @related_entity_refs,
                    @actor_account_id,
                    @actor_account_type,
                    @actor_role,
                    @session_id,
                    @device_label,
                    @occurred_at,
                    @recorded_at,
                    @reason,
                    @comment,
                    @before_summary,
                    @after_summary,
                    @request_correlation_id,
                    @entry_origin,
                    @idempotency_key,
                    @changed_after_close)
                """;
            auditCommand.Parameters.AddWithValue("id", auditEntryId);
            auditCommand.Parameters.AddWithValue("entity_id", entityId);
            auditCommand.Parameters.Add(
                "related_entity_refs",
                NpgsqlDbType.Jsonb).Value = relatedEntityRefs;
            auditCommand.Parameters.AddWithValue(
                "actor_account_id",
                actorAccountId);
            auditCommand.Parameters.AddWithValue(
                "actor_account_type",
                actorAccountType);
            auditCommand.Parameters.AddWithValue("actor_role", actorRole);
            auditCommand.Parameters.AddWithValue("session_id", sessionId);
            auditCommand.Parameters.AddWithValue("device_label", deviceLabel);
            auditCommand.Parameters.AddWithValue("occurred_at", occurredAt);
            auditCommand.Parameters.AddWithValue("recorded_at", recordedAt);
            auditCommand.Parameters.Add("reason", NpgsqlDbType.Varchar).Value =
                reason ?? (object)DBNull.Value;
            auditCommand.Parameters.Add("comment", NpgsqlDbType.Varchar).Value =
                comment ?? (object)DBNull.Value;
            auditCommand.Parameters.Add(
                "before_summary",
                NpgsqlDbType.Jsonb).Value = beforeSummary;
            auditCommand.Parameters.Add(
                "after_summary",
                NpgsqlDbType.Jsonb).Value = afterSummary;
            auditCommand.Parameters.AddWithValue(
                "request_correlation_id",
                correlationId);
            auditCommand.Parameters.AddWithValue("entry_origin", entryOrigin);
            auditCommand.Parameters.AddWithValue(
                "idempotency_key",
                $"audit-ui-key-{index:D2}");
            auditCommand.Parameters.AddWithValue(
                "changed_after_close",
                isFeatured);
            Assert.Equal(1, await auditCommand.ExecuteNonQueryAsync());

            if (isFeatured)
            {
                featuredAuditEntryId = auditEntryId;
                featuredEntityId = entityId;
                featuredOccurredAt = occurredAt;
                featuredRecordedAt = recordedAt;
            }
        }

        var visitCancellationAuditEntryId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var consumptionId = Guid.NewGuid();
        var cancellationId = Guid.NewGuid();
        var visitOccurredAt = recordedBase.AddHours(-2);
        var visitCanceledAt = recordedBase.AddHours(1);
        var membershipEffectiveEndDate = recordedDate.AddDays(14);
        var beforeMembershipState = new
        {
            MembershipId = membershipId,
            CountedVisits = 9,
            RemainingVisits = -1,
            NegativeBalance = 1,
            FirstNegativeVisitId = (Guid?)visitId,
            FirstNegativeVisitDate = (DateOnly?)recordedDate,
            ExtensionDays = 0,
            EffectiveEndDate = membershipEffectiveEndDate,
            LastCountedVisitAt = (DateTimeOffset?)visitOccurredAt,
            Warnings = new[] { "negative_visits" },
        };
        var afterMembershipState = new
        {
            MembershipId = membershipId,
            CountedVisits = 8,
            RemainingVisits = 0,
            NegativeBalance = 0,
            FirstNegativeVisitId = (Guid?)null,
            FirstNegativeVisitDate = (DateOnly?)null,
            ExtensionDays = 0,
            EffectiveEndDate = membershipEffectiveEndDate,
            LastCountedVisitAt = (DateTimeOffset?)null,
            Warnings = Array.Empty<string>(),
        };

        var paymentCorrectionAuditEntryId = Guid.NewGuid();
        var originalPaymentId = Guid.NewGuid();
        var replacementPaymentId = Guid.NewGuid();
        var paymentCorrectionId = Guid.NewGuid();
        var originalPaymentOccurredAt = recordedBase.AddHours(-3);
        var originalPaymentRecordedAt = originalPaymentOccurredAt.AddMinutes(5);
        var replacementPaymentOccurredAt = recordedBase.AddHours(-1);
        var paymentCorrectionRecordedAt = recordedBase.AddHours(2);
        var originalPayment = new AuditPaymentSummarySeed(
            originalPaymentId,
            clientId,
            membershipId,
            originalPaymentAmount,
            "UAH",
            "cash",
            "membership_sale",
            originalPaymentOccurredAt,
            originalPaymentRecordedAt,
            ownerAccountId,
            ownerSessionId,
            "normal",
            EntryBatchId: null,
            "Original cash amount",
            "active");
        var replacementPayment = new AuditPaymentSummarySeed(
            replacementPaymentId,
            clientId,
            membershipId,
            replacementPaymentAmount,
            "UAH",
            "cash",
            "membership_sale",
            replacementPaymentOccurredAt,
            paymentCorrectionRecordedAt,
            sharedAdminAccountId,
            sharedSessionId,
            "normal",
            EntryBatchId: null,
            "Corrected cash amount",
            "active");

        var paymentCancellationAuditEntryId = Guid.NewGuid();
        var canceledPaymentId = Guid.NewGuid();
        var paymentCancellationId = Guid.NewGuid();
        var canceledPaymentOccurredAt = recordedBase.AddHours(-4);
        var paymentCancellationRecordedAt = recordedBase.AddHours(3);
        var paymentToCancel = new AuditPaymentSummarySeed(
            canceledPaymentId,
            clientId,
            MembershipId: null,
            canceledPaymentAmount,
            "UAH",
            "cash",
            "one_off",
            canceledPaymentOccurredAt,
            canceledPaymentOccurredAt.AddMinutes(5),
            ownerAccountId,
            ownerSessionId,
            "normal",
            EntryBatchId: null,
            "One-off cash payment",
            "active");

        var membershipTypeId = Guid.NewGuid();
        var membershipTypeEditAuditEntryId = Guid.NewGuid();
        var membershipTypeDeactivationAuditEntryId = Guid.NewGuid();
        var membershipTypeCreatedAt = recordedBase.AddDays(-60);
        var originalMembershipTypeUpdatedAt = recordedBase.AddDays(-10);
        var membershipTypeEditedAt = recordedBase.AddHours(4);
        var membershipTypeDeactivatedAt = recordedBase.AddHours(5);
        var originalMembershipType = new AuditMembershipTypeSummarySeed(
            originalMembershipTypeName,
            DurationDays: 30,
            VisitsLimit: 8,
            new AuditMoneySummarySeed(originalMembershipTypePrice, "UAH"),
            IsActive: true,
            "Original catalog values",
            membershipTypeCreatedAt,
            originalMembershipTypeUpdatedAt,
            DeactivatedAt: null);
        var editedMembershipType = new AuditMembershipTypeSummarySeed(
            updatedMembershipTypeName,
            DurationDays: 45,
            VisitsLimit: 12,
            new AuditMoneySummarySeed(updatedMembershipTypePrice, "UAH"),
            IsActive: true,
            "Future evening sales",
            membershipTypeCreatedAt,
            membershipTypeEditedAt,
            DeactivatedAt: null);
        var deactivatedMembershipType = editedMembershipType with
        {
            IsActive = false,
            UpdatedAt = membershipTypeDeactivatedAt,
            DeactivatedAt = membershipTypeDeactivatedAt,
        };

        var nonWorkingDayCorrectedAuditEntryId = Guid.NewGuid();
        var correctedOriginalPeriodId = Guid.NewGuid();
        var correctedReplacementPeriodId = Guid.NewGuid();
        var correctedOriginalPeriod = new DateRange(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 3));
        var correctedReplacementPeriod = new DateRange(
            new DateOnly(2026, 8, 4),
            new DateOnly(2026, 8, 5));
        var correctedAt = recordedBase.AddHours(6);
        Guid[] correctedOldMembershipIds = [Guid.NewGuid(), Guid.NewGuid()];
        Guid[] correctedNewMembershipIds =
        [
            correctedOldMembershipIds[0],
            correctedOldMembershipIds[1],
            Guid.NewGuid(),
        ];
        Guid[] correctedOldClientIds = [Guid.NewGuid(), Guid.NewGuid()];
        Guid[] correctedNewClientIds =
        [
            correctedOldClientIds[0],
            correctedOldClientIds[1],
            Guid.NewGuid(),
        ];
        var correctedSource = new AuditNonWorkingDaySourcePeriodSummarySeed(
            correctedOriginalPeriodId,
            correctedOriginalPeriod.StartDate,
            correctedOriginalPeriod.EndDate,
            InclusiveDays: 3,
            "weather_closure",
            "Storm closure",
            recordedBase.AddDays(-20),
            ownerAccountId,
            ownerSessionId,
            "active");
        var correctedOldApplications = correctedOldMembershipIds
            .Zip(
                correctedOldClientIds,
                (affectedMembershipId, affectedClientId) =>
                    new AuditNonWorkingDayBeforeApplicationSummarySeed(
                        Guid.NewGuid(),
                        affectedMembershipId,
                        affectedClientId,
                        correctedOriginalPeriod.StartDate,
                        correctedOriginalPeriod.EndDate,
                        correctedSource.CreatedAt.AddMinutes(-5),
                        correctedSource.CreatedAt,
                        "active"))
            .ToArray();
        var correctedReplacement = new AuditNonWorkingDayReplacementPeriodSummarySeed(
            correctedReplacementPeriodId,
            correctedReplacementPeriod.StartDate,
            correctedReplacementPeriod.EndDate,
            InclusiveDays: 2,
            "maintenance",
            "Boiler replacement",
            correctedAt,
            "active");
        var correctedNewApplications = correctedNewMembershipIds
            .Zip(
                correctedNewClientIds,
                (affectedMembershipId, affectedClientId) =>
                    new AuditNonWorkingDayReplacementApplicationSummarySeed(
                        Guid.NewGuid(),
                        affectedMembershipId,
                        affectedClientId,
                        correctedReplacementPeriod.StartDate,
                        correctedReplacementPeriod.EndDate))
            .ToArray();
        var correctedBefore = new
        {
            Period = correctedSource,
            Applications = correctedOldApplications,
            Preview = new
            {
                ConfirmationFingerprint = "audit-ui-non-working-correction",
                IssuedAt = correctedAt.AddMinutes(-10),
                ExpiresAt = correctedAt.AddMinutes(5),
                OldAffectedCount = correctedOldApplications.Length,
                NewAffectedCount = correctedNewApplications.Length,
            },
        };
        var correctedAfter = new
        {
            Mode = "replace_range",
            OriginalPeriod = correctedSource with { Status = "corrected" },
            ReplacementPeriod = correctedReplacement,
            ReplacementApplications = correctedNewApplications,
            Cancellation = (object?)null,
            OldAffectedCount = correctedOldApplications.Length,
            NewAffectedCount = correctedNewApplications.Length,
            AffectedUnionCount = correctedNewMembershipIds.Length,
            Recalculation = new
            {
                RequestedCount = correctedNewMembershipIds.Length,
                SucceededCount = correctedNewMembershipIds.Length,
                MembershipIds = correctedNewMembershipIds,
            },
        };

        var nonWorkingDayCanceledAuditEntryId = Guid.NewGuid();
        var canceledPeriodId = Guid.NewGuid();
        var nonWorkingDayCancellationId = Guid.NewGuid();
        var canceledPeriod = new DateRange(
            new DateOnly(2026, 8, 10),
            new DateOnly(2026, 8, 11));
        var nonWorkingDayCanceledAt = recordedBase.AddHours(7);
        Guid[] canceledMembershipIds = [Guid.NewGuid(), Guid.NewGuid()];
        Guid[] canceledClientIds = [Guid.NewGuid(), Guid.NewGuid()];
        var canceledSource = new AuditNonWorkingDaySourcePeriodSummarySeed(
            canceledPeriodId,
            canceledPeriod.StartDate,
            canceledPeriod.EndDate,
            InclusiveDays: 2,
            "renovation",
            "Floor refinishing",
            recordedBase.AddDays(-18),
            ownerAccountId,
            ownerSessionId,
            "active");
        var canceledApplications = canceledMembershipIds
            .Zip(
                canceledClientIds,
                (affectedMembershipId, affectedClientId) =>
                    new AuditNonWorkingDayBeforeApplicationSummarySeed(
                        Guid.NewGuid(),
                        affectedMembershipId,
                        affectedClientId,
                        canceledPeriod.StartDate,
                        canceledPeriod.EndDate,
                        canceledSource.CreatedAt.AddMinutes(-5),
                        canceledSource.CreatedAt,
                        "active"))
            .ToArray();
        var canceledBefore = new
        {
            Period = canceledSource,
            Applications = canceledApplications,
            Preview = new
            {
                ConfirmationFingerprint = "audit-ui-non-working-cancellation",
                IssuedAt = nonWorkingDayCanceledAt.AddMinutes(-10),
                ExpiresAt = nonWorkingDayCanceledAt.AddMinutes(5),
                OldAffectedCount = canceledApplications.Length,
                NewAffectedCount = 0,
            },
        };
        var nonWorkingDayCancellationReason = "Closure no longer required";
        var canceledAfter = new
        {
            Mode = "cancel",
            OriginalPeriod = canceledSource with { Status = "canceled" },
            ReplacementPeriod = (object?)null,
            ReplacementApplications =
                Array.Empty<AuditNonWorkingDayReplacementApplicationSummarySeed>(),
            Cancellation = new
            {
                CancellationId = nonWorkingDayCancellationId,
                NonWorkingPeriodId = canceledPeriodId,
                Reason = nonWorkingDayCancellationReason,
                RecordedAt = nonWorkingDayCanceledAt,
            },
            OldAffectedCount = canceledApplications.Length,
            NewAffectedCount = 0,
            AffectedUnionCount = canceledMembershipIds.Length,
            Recalculation = new
            {
                RequestedCount = canceledMembershipIds.Length,
                SucceededCount = canceledMembershipIds.Length,
                MembershipIds = canceledMembershipIds,
            },
        };

        var freezeCancellationAuditEntryId = Guid.NewGuid();
        var canceledFreezeId = Guid.NewGuid();
        var freezeCancellationId = Guid.NewGuid();
        var canceledFreezeClientId = Guid.NewGuid();
        var canceledFreezeMembershipId = Guid.NewGuid();
        var canceledFreezeRange = new DateRange(
            new DateOnly(2026, 8, 20),
            new DateOnly(2026, 8, 22));
        var freezeCancellationOccurredAt = recordedBase.AddHours(8);
        var freezeCancellationRecordedAt = freezeCancellationOccurredAt.AddMinutes(5);
        var freezeCancellationReason = "Freeze dates were entered incorrectly";
        var freezeSource = new AuditFreezeSourceSummarySeed(
            canceledFreezeId,
            canceledFreezeClientId,
            canceledFreezeMembershipId,
            canceledFreezeRange.StartDate,
            canceledFreezeRange.EndDate,
            InclusiveDays: 3,
            "Medical recovery",
            recordedBase.AddDays(-12),
            recordedBase.AddDays(-12).AddMinutes(5),
            "normal",
            EntryBatchId: null,
            "active");
        var freezeBeforeMembership = new AuditFreezeMembershipStateSummarySeed(
            canceledFreezeMembershipId,
            canceledFreezeClientId,
            RemainingVisits: 6,
            NegativeBalance: 0,
            ExtensionDays: 7,
            new DateOnly(2026, 9, 7),
            ["ending_soon"]);
        var freezeAfterMembership = freezeBeforeMembership with
        {
            ExtensionDays = 4,
            EffectiveEndDate = new DateOnly(2026, 9, 4),
        };
        var freezeBefore = new
        {
            Freeze = freezeSource,
            MembershipState = freezeBeforeMembership,
        };
        var freezeAfter = new
        {
            Cancellation = new
            {
                CancellationId = freezeCancellationId,
                FreezeId = canceledFreezeId,
                Reason = freezeCancellationReason,
                OccurredAt = freezeCancellationOccurredAt,
                RecordedAt = freezeCancellationRecordedAt,
                EntryOrigin = "normal",
                EntryBatchId = (Guid?)null,
                ChangedAfterClose = false,
            },
            Freeze = new
            {
                FreezeId = canceledFreezeId,
                ClientId = canceledFreezeClientId,
                MembershipId = canceledFreezeMembershipId,
                StartDate = canceledFreezeRange.StartDate,
                EndDate = canceledFreezeRange.EndDate,
                InclusiveDays = canceledFreezeRange.InclusiveDays,
                Reason = freezeSource.Reason,
                Status = "canceled",
            },
            MembershipState = freezeAfterMembership,
        };

        const string originalClientDisplayName = "Koval Iryna";
        const string updatedClientDisplayName = "Kovalchuk Iryna Mykolaivna";
        const string originalClientPhone = "067 111 22 33";
        const string updatedClientPhone = "+38 (067) 765-43-21";
        const string assignedCardNumber = "BL AUDIT 100-20";
        const string replacementCardNumber = "BL AUDIT 200-40";
        var clientUpdateAuditEntryId = Guid.NewGuid();
        var duplicateAcknowledgementId = Guid.NewGuid();
        var matchedClientId = Guid.NewGuid();
        var clientUpdatedAt = recordedBase.AddHours(9);
        var clientUpdateBefore = new
        {
            Surname = "Koval",
            Name = "Iryna",
            Patronymic = (string?)null,
            Phone = originalClientPhone,
            OperationalStatus = "active",
            Comment = "Prefers morning visits",
            UpdatedAt = recordedBase.AddDays(-1),
        };
        var clientUpdateAfter = new
        {
            Surname = "Kovalchuk",
            Name = "Iryna",
            Patronymic = "Mykolaivna",
            Phone = updatedClientPhone,
            OperationalStatus = "inactive",
            Comment = "Paused by Owner request",
            UpdatedAt = clientUpdatedAt,
            DuplicateWarningAcknowledgements = new[]
            {
                new
                {
                    WarningType = "duplicate_phone",
                    MatchedClientId = matchedClientId,
                    Reason = "Confirmed family member",
                },
            },
        };

        var cardAssignedAuditEntryId = Guid.NewGuid();
        var cardChangedAuditEntryId = Guid.NewGuid();
        var cardClearedAuditEntryId = Guid.NewGuid();
        var assignedCardAt = recordedBase.AddHours(10);
        var replacementCardAt = recordedBase.AddHours(11);
        var cardClearedAt = recordedBase.AddHours(12);
        var assignedCard = new AuditCardAssignmentSummarySeed(
            Guid.NewGuid(),
            assignedCardNumber,
            "BLAUDIT10020",
            assignedCardAt);
        var replacementCard = new AuditCardAssignmentSummarySeed(
            Guid.NewGuid(),
            replacementCardNumber,
            "BLAUDIT20040",
            replacementCardAt);

        const string originalStaffDisplayName = "Main Admin";
        const string updatedStaffDisplayName = "Evening Admin";
        const string staffDeactivationReason = "Staff member left the gym";
        const string credentialResetReason = "Scheduled credential rotation";
        const int endedStaffSessionCount = 2;
        const int credentialResetEndedSessionCount = 2;
        var staffAccountId = Guid.NewGuid();
        var staffDisplayNameUpdatedAuditEntryId = Guid.NewGuid();
        var staffDeactivatedAuditEntryId = Guid.NewGuid();
        var staffActivatedAuditEntryId = Guid.NewGuid();
        var credentialsConfiguredAuditEntryId = Guid.NewGuid();
        var credentialsResetAuditEntryId = Guid.NewGuid();
        var staffDisplayNameUpdatedAt = recordedBase.AddHours(13);
        var staffDeactivatedAt = recordedBase.AddHours(14);
        var staffActivatedAt = recordedBase.AddHours(15);
        var credentialsConfiguredAt = recordedBase.AddHours(16);
        var credentialsResetAt = recordedBase.AddHours(17);

        AuditSeed[] explanationSeeds =
        [
            new(
                visitCancellationAuditEntryId,
                "visit.canceled",
                "visit",
                visitId,
                new
                {
                    ClientId = clientId,
                    MembershipId = membershipId,
                    ActiveConsumptionId = consumptionId,
                    CancellationId = cancellationId,
                },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                visitCanceledAt,
                visitCanceledAt.AddMinutes(5),
                "normal",
                "Mistaken reception entry",
                "Cancellation requested at reception",
                new
                {
                    Visit = new
                    {
                        VisitId = visitId,
                        ClientId = clientId,
                        VisitKind = "membership",
                        MembershipId = membershipId,
                        ConsumptionId = consumptionId,
                        OccurredAt = visitOccurredAt,
                        RecordedAt = visitOccurredAt.AddMinutes(5),
                        EntryOrigin = "normal",
                        EntryBatchId = (Guid?)null,
                        Comment = "Original membership Visit",
                        Status = "active",
                        ConsumptionStatus = "active",
                    },
                    MembershipState = beforeMembershipState,
                },
                new
                {
                    Cancellation = new
                    {
                        CancellationId = cancellationId,
                        VisitId = visitId,
                        Reason = "Mistaken reception entry",
                        OccurredAt = visitCanceledAt,
                        RecordedAt = visitCanceledAt.AddMinutes(5),
                        EntryOrigin = "normal",
                        EntryBatchId = (Guid?)null,
                        ChangedAfterClose = false,
                    },
                    Visit = new
                    {
                        VisitId = visitId,
                        Status = "canceled",
                        ConsumptionId = consumptionId,
                        ConsumptionStatus = "canceled",
                    },
                    MembershipState = afterMembershipState,
                },
                ChangedAfterClose: false),
            new(
                paymentCorrectionAuditEntryId,
                "payment.corrected",
                "payment",
                originalPaymentId,
                new
                {
                    ClientId = clientId,
                    OriginalPaymentId = originalPaymentId,
                    OriginalMembershipId = membershipId,
                    ReplacementPaymentId = replacementPaymentId,
                    ReplacementMembershipId = membershipId,
                    CorrectionId = paymentCorrectionId,
                },
                sharedAdminAccountId,
                "shared_reception_admin",
                "admin",
                sharedSessionId,
                sharedDeviceLabel,
                replacementPaymentOccurredAt,
                paymentCorrectionRecordedAt,
                "normal",
                "Cash amount was entered incorrectly",
                "Replacement confirmed against receipt",
                new { Payment = originalPayment },
                new
                {
                    Correction = new
                    {
                        CorrectionId = paymentCorrectionId,
                        OriginalPaymentId = originalPaymentId,
                        ReplacementPaymentId = replacementPaymentId,
                        ChangedFields = new[] { "amount", "occurred_at", "comment" },
                        Reason = "Cash amount was entered incorrectly",
                        OccurredAt = replacementPaymentOccurredAt,
                        RecordedAt = paymentCorrectionRecordedAt,
                        EntryOrigin = "normal",
                        EntryBatchId = (Guid?)null,
                        ChangedAfterClose = false,
                    },
                    OriginalPayment = originalPayment with { Status = "replaced" },
                    ReplacementPayment = replacementPayment,
                },
                ChangedAfterClose: false),
            new(
                paymentCancellationAuditEntryId,
                "payment.canceled",
                "payment",
                canceledPaymentId,
                new
                {
                    ClientId = clientId,
                    PaymentId = canceledPaymentId,
                    MembershipId = (Guid?)null,
                    CancellationId = paymentCancellationId,
                },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                paymentCancellationRecordedAt.AddMinutes(-5),
                paymentCancellationRecordedAt,
                "normal",
                "Payment entered by mistake",
                "Owner confirmed cancellation",
                new { Payment = paymentToCancel },
                new
                {
                    Cancellation = new
                    {
                        CancellationId = paymentCancellationId,
                        PaymentId = canceledPaymentId,
                        Reason = "Payment entered by mistake",
                        OccurredAt = paymentCancellationRecordedAt.AddMinutes(-5),
                        RecordedAt = paymentCancellationRecordedAt,
                        EntryOrigin = "normal",
                        EntryBatchId = (Guid?)null,
                        ChangedAfterClose = false,
                    },
                    Payment = paymentToCancel with { Status = "canceled" },
                },
                ChangedAfterClose: false),
            new(
                membershipTypeEditAuditEntryId,
                "membership_type.edited",
                "membership_type",
                membershipTypeId,
                new { },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                membershipTypeEditedAt.AddMinutes(-5),
                membershipTypeEditedAt,
                "normal",
                "Updated future offer",
                "Owner approved catalog changes",
                originalMembershipType,
                editedMembershipType,
                ChangedAfterClose: false),
            new(
                membershipTypeDeactivationAuditEntryId,
                "membership_type.deactivated",
                "membership_type",
                membershipTypeId,
                new { },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                membershipTypeDeactivatedAt.AddMinutes(-5),
                membershipTypeDeactivatedAt,
                "normal",
                "Retired future offer",
                "Existing memberships remain valid",
                editedMembershipType,
                deactivatedMembershipType,
                ChangedAfterClose: false),
            new(
                nonWorkingDayCorrectedAuditEntryId,
                "non_working_day.corrected",
                "non_working_period",
                correctedOriginalPeriodId,
                new
                {
                    OriginalPeriodId = correctedOriginalPeriodId,
                    ReplacementPeriodId = (Guid?)correctedReplacementPeriodId,
                    CancellationId = (Guid?)null,
                    OldMembershipIds = correctedOldMembershipIds,
                    NewMembershipIds = correctedNewMembershipIds,
                    AffectedMembershipIds = correctedNewMembershipIds,
                    AffectedClientIds = correctedNewClientIds,
                },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                correctedAt.AddMinutes(-5),
                correctedAt,
                "normal",
                "Closure schedule changed",
                "Confirmed against the correction preview",
                correctedBefore,
                correctedAfter,
                ChangedAfterClose: false),
            new(
                nonWorkingDayCanceledAuditEntryId,
                "non_working_day.canceled",
                "non_working_period",
                canceledPeriodId,
                new
                {
                    OriginalPeriodId = canceledPeriodId,
                    ReplacementPeriodId = (Guid?)null,
                    CancellationId = (Guid?)nonWorkingDayCancellationId,
                    OldMembershipIds = canceledMembershipIds,
                    NewMembershipIds = Array.Empty<Guid>(),
                    AffectedMembershipIds = canceledMembershipIds,
                    AffectedClientIds = canceledClientIds,
                },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                nonWorkingDayCanceledAt.AddMinutes(-5),
                nonWorkingDayCanceledAt,
                "normal",
                nonWorkingDayCancellationReason,
                "Owner confirmed cancellation",
                canceledBefore,
                canceledAfter,
                ChangedAfterClose: false),
            new(
                freezeCancellationAuditEntryId,
                "freeze.canceled",
                "freeze",
                canceledFreezeId,
                new
                {
                    ClientId = canceledFreezeClientId,
                    MembershipId = canceledFreezeMembershipId,
                    CancellationId = freezeCancellationId,
                },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                freezeCancellationOccurredAt,
                freezeCancellationRecordedAt,
                "normal",
                freezeCancellationReason,
                "Owner confirmed the Freeze cancellation",
                freezeBefore,
                freezeAfter,
                ChangedAfterClose: false),
            new(
                clientUpdateAuditEntryId,
                "client.updated",
                "client",
                clientChangeClientId,
                new
                {
                    DuplicateWarningAcknowledgementIds = new[]
                    {
                        duplicateAcknowledgementId,
                    },
                    MatchedClientIds = new[] { matchedClientId },
                },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                clientUpdatedAt.AddMinutes(-5),
                clientUpdatedAt,
                "normal",
                "Owner confirmed profile correction",
                "Duplicate warning reviewed",
                clientUpdateBefore,
                clientUpdateAfter,
                ChangedAfterClose: false),
            new(
                cardAssignedAuditEntryId,
                "card.assigned",
                "client",
                clientChangeClientId,
                new
                {
                    PreviousCardAssignmentId = (Guid?)null,
                    CurrentCardAssignmentId = (Guid?)assignedCard.Id,
                },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                assignedCardAt,
                assignedCardAt.AddMinutes(5),
                "normal",
                Reason: null,
                Comment: null,
                BeforeSummary: new { },
                AfterSummary: assignedCard,
                ChangedAfterClose: false),
            new(
                cardChangedAuditEntryId,
                "card.changed",
                "client",
                clientChangeClientId,
                new
                {
                    PreviousCardAssignmentId = (Guid?)assignedCard.Id,
                    CurrentCardAssignmentId = (Guid?)replacementCard.Id,
                },
                sharedAdminAccountId,
                "shared_reception_admin",
                "admin",
                sharedSessionId,
                sharedDeviceLabel,
                replacementCardAt,
                replacementCardAt.AddMinutes(5),
                "normal",
                "Physical card replaced",
                "Reception issued a replacement",
                assignedCard,
                replacementCard,
                ChangedAfterClose: false),
            new(
                cardClearedAuditEntryId,
                "card.cleared",
                "client",
                clientChangeClientId,
                new
                {
                    PreviousCardAssignmentId = (Guid?)replacementCard.Id,
                    CurrentCardAssignmentId = (Guid?)null,
                },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                cardClearedAt,
                cardClearedAt.AddMinutes(5),
                "normal",
                "Client returned the card",
                "Current assignment cleared",
                replacementCard,
                new { },
                ChangedAfterClose: false),
            new(
                staffDisplayNameUpdatedAuditEntryId,
                "staff_account.display_name_updated",
                "staff_account",
                staffAccountId,
                new { },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                staffDisplayNameUpdatedAt,
                staffDisplayNameUpdatedAt,
                "normal",
                Reason: null,
                Comment: null,
                BeforeSummary: new { DisplayName = originalStaffDisplayName },
                AfterSummary: new { DisplayName = updatedStaffDisplayName },
                ChangedAfterClose: false),
            new(
                staffDeactivatedAuditEntryId,
                "staff_account.deactivated",
                "staff_account",
                staffAccountId,
                new { },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                staffDeactivatedAt,
                staffDeactivatedAt,
                "normal",
                staffDeactivationReason,
                "Active sessions ended immediately",
                new { IsActive = true },
                new
                {
                    IsActive = false,
                    EndedSessionCount = endedStaffSessionCount,
                },
                ChangedAfterClose: false),
            new(
                staffActivatedAuditEntryId,
                "staff_account.activated",
                "staff_account",
                staffAccountId,
                new { },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                staffActivatedAt,
                staffActivatedAt,
                "normal",
                Reason: null,
                Comment: null,
                BeforeSummary: new { IsActive = false },
                AfterSummary: new
                {
                    IsActive = true,
                    EndedSessionCount = 0,
                },
                ChangedAfterClose: false),
            new(
                credentialsConfiguredAuditEntryId,
                "staff_credentials.configured",
                "staff_account",
                staffAccountId,
                new { },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                credentialsConfiguredAt,
                credentialsConfiguredAt,
                "normal",
                Reason: null,
                Comment: null,
                BeforeSummary: new { CredentialsConfigured = false },
                AfterSummary: new
                {
                    CredentialsConfigured = true,
                    EndedSessionCount = 0,
                },
                ChangedAfterClose: false),
            new(
                credentialsResetAuditEntryId,
                "staff_credentials.reset",
                "staff_account",
                staffAccountId,
                new { },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                credentialsResetAt,
                credentialsResetAt,
                "normal",
                credentialResetReason,
                "All active sessions revoked",
                new { CredentialsConfigured = true },
                new
                {
                    CredentialsConfigured = true,
                    EndedSessionCount = credentialResetEndedSessionCount,
                },
                ChangedAfterClose: false),
        ];

        foreach (var explanationSeed in explanationSeeds)
        {
            await InsertAuditSeedAsync(connection, transaction, explanationSeed);
        }

        await transaction.CommitAsync();
        return new AuditTimelineSmokeScenario(
            clientId,
            recordedDate,
            pageSize,
            totalEntries,
            featuredAuditEntryId,
            featuredEntityId,
            sharedAdminAccountId,
            sharedSessionId,
            sharedDeviceLabel,
            featuredOccurredAt,
            featuredRecordedAt,
            featuredCorrelationId,
            new AuditExplanationSmokeScenario(
                visitCancellationAuditEntryId,
                paymentCorrectionAuditEntryId,
                paymentCancellationAuditEntryId,
                membershipTypeEditAuditEntryId,
                membershipTypeDeactivationAuditEntryId,
                originalMembershipTypeName,
                updatedMembershipTypeName,
                originalMembershipTypePrice,
                updatedMembershipTypePrice,
                originalPaymentAmount,
                replacementPaymentAmount,
                canceledPaymentAmount,
                BeforeVisitRemaining: -1,
                AfterVisitRemaining: 0,
                NonWorkingDays: new NonWorkingDayAuditExplanationSmokeScenario(
                    nonWorkingDayCorrectedAuditEntryId,
                    correctedOriginalPeriodId,
                    correctedOriginalPeriod,
                    correctedReplacementPeriod,
                    correctedOldApplications.Length,
                    correctedNewApplications.Length,
                    correctedNewMembershipIds.Length,
                    nonWorkingDayCanceledAuditEntryId,
                    canceledPeriodId,
                    canceledPeriod,
                    canceledApplications.Length),
                FreezeCancellation: new FreezeCancellationAuditExplanationSmokeScenario(
                    freezeCancellationAuditEntryId,
                    canceledFreezeId,
                    canceledFreezeRange,
                    freezeSource.Reason,
                    freezeBeforeMembership.ExtensionDays,
                    freezeAfterMembership.ExtensionDays,
                    freezeBeforeMembership.EffectiveEndDate,
                    freezeAfterMembership.EffectiveEndDate),
                ClientAndCards: new ClientCardAuditExplanationSmokeScenario(
                    clientChangeClientId,
                    clientUpdateAuditEntryId,
                    originalClientDisplayName,
                    updatedClientDisplayName,
                    originalClientPhone,
                    updatedClientPhone,
                    matchedClientId,
                    cardAssignedAuditEntryId,
                    cardChangedAuditEntryId,
                    cardClearedAuditEntryId,
                    assignedCardNumber,
                    replacementCardNumber),
                StaffAccounts: new StaffAccountAuditExplanationSmokeScenario(
                    staffAccountId,
                    staffDisplayNameUpdatedAuditEntryId,
                    originalStaffDisplayName,
                    updatedStaffDisplayName,
                    staffDeactivatedAuditEntryId,
                    staffDeactivationReason,
                    endedStaffSessionCount,
                    staffActivatedAuditEntryId,
                    credentialsConfiguredAuditEntryId,
                    credentialsResetAuditEntryId,
                    credentialResetReason,
                    credentialResetEndedSessionCount)));
    }

    public async Task<ClientHistorySmokeScenario> SeedClientHistoryAsync(
        Guid ownerAccountId,
        Guid sharedAdminAccountId,
        Guid membershipTypeId)
    {
        if (ownerAccountId == Guid.Empty
            || sharedAdminAccountId == Guid.Empty
            || membershipTypeId == Guid.Empty)
        {
            throw new ArgumentException(
                "Client history fixture ids must be non-empty.",
                nameof(ownerAccountId));
        }

        const int pageSize = 10;
        const int totalEntries = 12;
        const string clientDisplayName = "History Evidence";
        const string cardNumber = "BL-CLIENT-HISTORY";
        const string ownerDeviceLabel = "Owner history workstation";
        const string sharedDeviceLabel = "Shared history tablet";
        const decimal originalPaymentAmount = 1200m;
        const decimal replacementPaymentAmount = 950m;
        var occurredDate = new DateOnly(2026, 7, 18);
        var eventBase = new DateTimeOffset(
            occurredDate,
            new TimeOnly(7, 0),
            TimeSpan.Zero);
        var clientId = await SeedClientAsync(
            ownerAccountId,
            "History",
            "Evidence",
            "+380 67 880 00 02",
            cardNumber);
        var membershipIssuedAt = eventBase.AddHours(3).AddMinutes(5);
        var membershipId = await SeedIssuedMembershipAsync(
            ownerAccountId,
            clientId,
            membershipTypeId,
            "Client history monthly",
            visitsLimitSnapshot: 12,
            startDate: new DateOnly(2026, 7, 1),
            durationDays: 30,
            issuedAt: membershipIssuedAt);

        var ownerSessionId = Guid.NewGuid();
        var sharedSessionId = Guid.NewGuid();
        var openingStateId = Guid.NewGuid();
        var canceledVisitId = Guid.NewGuid();
        var canceledVisitCancellationId = Guid.NewGuid();
        var genericVisitIds = Enumerable.Range(0, 3)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var originalPaymentId = Guid.NewGuid();
        var replacementPaymentId = Guid.NewGuid();
        var paymentCorrectionId = Guid.NewGuid();
        var paymentCorrectionBatchId = Guid.NewGuid();
        var freezeId = Guid.NewGuid();
        var freezeCancellationId = Guid.NewGuid();
        var nonWorkingPeriodId = Guid.NewGuid();
        var nonWorkingApplicationId = Guid.NewGuid();

        var openingOccurredAt = eventBase.AddHours(4);
        var openingRecordedAt = openingOccurredAt.AddMinutes(5);
        var visitOccurredAt = eventBase.AddHours(5);
        var visitRecordedAt = visitOccurredAt.AddMinutes(5);
        var visitCanceledAt = eventBase.AddHours(6);
        var visitCancellationRecordedAt = visitCanceledAt.AddMinutes(5);
        var freezeOccurredAt = eventBase.AddHours(7);
        var freezeRecordedAt = freezeOccurredAt.AddMinutes(5);
        var freezeCanceledAt = eventBase.AddHours(8);
        var freezeCancellationRecordedAt = freezeCanceledAt.AddMinutes(5);
        var originalPaymentOccurredAt = eventBase.AddHours(9);
        var originalPaymentRecordedAt = originalPaymentOccurredAt.AddMinutes(5);
        var featuredOccurredAt = eventBase.AddHours(10);
        var featuredRecordedAt = featuredOccurredAt.AddDays(2).AddMinutes(5);
        var nonWorkingOccurredAt = eventBase.AddHours(11);
        var nonWorkingRecordedAt = nonWorkingOccurredAt.AddMinutes(5);
        var featuredAuditEntryId = Guid.NewGuid();
        var originalPaymentAuditEntryId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var sourceCommand = connection.CreateCommand())
        {
            sourceCommand.Transaction = transaction;
            sourceCommand.CommandText =
                """
                insert into bodylife.sessions (
                    id,
                    account_id,
                    device_label,
                    started_at,
                    expires_at,
                    ended_at,
                    last_seen_at)
                values
                    (
                        @owner_session_id,
                        @owner_account_id,
                        @owner_device_label,
                        @session_started_at,
                        @session_expires_at,
                        @session_ended_at,
                        @session_ended_at),
                    (
                        @shared_session_id,
                        @shared_account_id,
                        @shared_device_label,
                        @session_started_at,
                        @session_expires_at,
                        @session_ended_at,
                        @session_ended_at);

                insert into bodylife.membership_opening_states (
                    id,
                    membership_id,
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
                    status)
                values (
                    @opening_state_id,
                    @membership_id,
                    @opening_as_of_date,
                    -2,
                    2,
                    @known_effective_end_date,
                    3,
                    'paper-ledger-history-42',
                    'Opening balance from legacy ledger',
                    @opening_recorded_at,
                    @owner_account_id,
                    @owner_session_id,
                    'manual_backfill',
                    @opening_batch_id,
                    'active');

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
                values
                    (
                        @canceled_visit_id,
                        @client_id,
                        @visit_occurred_at,
                        @visit_recorded_at,
                        @owner_account_id,
                        @owner_session_id,
                        'one_off',
                        'normal',
                        null,
                        'Original front desk check-in',
                        'canceled'),
                    (
                        @generic_visit_1_id,
                        @client_id,
                        @generic_visit_1_occurred_at,
                        @generic_visit_1_recorded_at,
                        @owner_account_id,
                        @owner_session_id,
                        'one_off',
                        'normal',
                        null,
                        'Earlier source visit 1',
                        'active'),
                    (
                        @generic_visit_2_id,
                        @client_id,
                        @generic_visit_2_occurred_at,
                        @generic_visit_2_recorded_at,
                        @owner_account_id,
                        @owner_session_id,
                        'one_off',
                        'normal',
                        null,
                        'Earlier source visit 2',
                        'active'),
                    (
                        @generic_visit_3_id,
                        @client_id,
                        @generic_visit_3_occurred_at,
                        @generic_visit_3_recorded_at,
                        @owner_account_id,
                        @owner_session_id,
                        'one_off',
                        'normal',
                        null,
                        'Earlier source visit 3',
                        'active');

                insert into bodylife.visit_cancellations (
                    id,
                    visit_id,
                    reason,
                    occurred_at,
                    recorded_at,
                    recorded_by_account_id,
                    session_id,
                    entry_origin,
                    entry_batch_id)
                values (
                    @visit_cancellation_id,
                    @canceled_visit_id,
                    'Duplicate reception check-in',
                    @visit_canceled_at,
                    @visit_cancellation_recorded_at,
                    @owner_account_id,
                    @owner_session_id,
                    'normal',
                    null);

                insert into bodylife.payments (
                    id,
                    client_id,
                    membership_id,
                    amount,
                    currency,
                    method,
                    payment_context,
                    occurred_at,
                    recorded_at,
                    recorded_by_account_id,
                    session_id,
                    entry_origin,
                    entry_batch_id,
                    comment,
                    status)
                values
                    (
                        @original_payment_id,
                        @client_id,
                        @membership_id,
                        @original_payment_amount,
                        'UAH',
                        'cash',
                        'membership_sale',
                        @original_payment_occurred_at,
                        @original_payment_recorded_at,
                        @owner_account_id,
                        @owner_session_id,
                        'normal',
                        null,
                        'Original cash amount',
                        'replaced'),
                    (
                        @replacement_payment_id,
                        @client_id,
                        @membership_id,
                        @replacement_payment_amount,
                        'UAH',
                        'cash',
                        'membership_sale',
                        @featured_occurred_at,
                        @featured_recorded_at,
                        @shared_account_id,
                        @shared_session_id,
                        'paper_fallback',
                        @payment_correction_batch_id,
                        'Corrected cash amount from paper register',
                        'active');

                insert into bodylife.payment_corrections (
                    id,
                    client_id,
                    original_payment_id,
                    replacement_payment_id,
                    changed_fields,
                    reason,
                    occurred_at,
                    recorded_at,
                    recorded_by_account_id,
                    session_id,
                    entry_origin,
                    entry_batch_id)
                values (
                    @payment_correction_id,
                    @client_id,
                    @original_payment_id,
                    @replacement_payment_id,
                    '["amount","occurred_at"]'::jsonb,
                    'Cash amount was entered incorrectly',
                    @featured_occurred_at,
                    @featured_recorded_at,
                    @shared_account_id,
                    @shared_session_id,
                    'paper_fallback',
                    @payment_correction_batch_id);

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
                    @freeze_id,
                    @client_id,
                    @membership_id,
                    @freeze_start_date,
                    @freeze_end_date,
                    'Travel recovery window',
                    @freeze_occurred_at,
                    @freeze_recorded_at,
                    @owner_account_id,
                    @owner_session_id,
                    'normal',
                    null,
                    'canceled');

                insert into bodylife.freeze_cancellations (
                    id,
                    freeze_id,
                    reason,
                    occurred_at,
                    recorded_at,
                    recorded_by_account_id,
                    session_id,
                    entry_origin,
                    entry_batch_id)
                values (
                    @freeze_cancellation_id,
                    @freeze_id,
                    'Travel was canceled',
                    @freeze_canceled_at,
                    @freeze_cancellation_recorded_at,
                    @owner_account_id,
                    @owner_session_id,
                    'normal',
                    null);

                insert into bodylife.non_working_periods (
                    id,
                    start_date,
                    end_date,
                    reason_code,
                    reason_comment,
                    created_at,
                    created_by_account_id,
                    session_id,
                    status)
                values (
                    @non_working_period_id,
                    @non_working_start_date,
                    @non_working_end_date,
                    'maintenance',
                    'Ventilation service',
                    @non_working_recorded_at,
                    @owner_account_id,
                    @owner_session_id,
                    'active');

                insert into bodylife.non_working_period_applications (
                    id,
                    non_working_period_id,
                    membership_id,
                    client_id,
                    applied_start_date,
                    applied_end_date,
                    previewed_at,
                    confirmed_at,
                    status)
                values (
                    @non_working_application_id,
                    @non_working_period_id,
                    @membership_id,
                    @client_id,
                    @non_working_start_date,
                    @non_working_end_date,
                    @non_working_previewed_at,
                    @non_working_recorded_at,
                    'active')
                """;
            sourceCommand.Parameters.AddWithValue("owner_session_id", ownerSessionId);
            sourceCommand.Parameters.AddWithValue("owner_account_id", ownerAccountId);
            sourceCommand.Parameters.AddWithValue("owner_device_label", ownerDeviceLabel);
            sourceCommand.Parameters.AddWithValue("shared_session_id", sharedSessionId);
            sourceCommand.Parameters.AddWithValue("shared_account_id", sharedAdminAccountId);
            sourceCommand.Parameters.AddWithValue("shared_device_label", sharedDeviceLabel);
            sourceCommand.Parameters.AddWithValue("session_started_at", eventBase.AddHours(-1));
            sourceCommand.Parameters.AddWithValue("session_expires_at", eventBase.AddDays(30));
            sourceCommand.Parameters.AddWithValue("session_ended_at", featuredRecordedAt.AddMinutes(30));
            sourceCommand.Parameters.AddWithValue("opening_state_id", openingStateId);
            sourceCommand.Parameters.AddWithValue("membership_id", membershipId);
            sourceCommand.Parameters.AddWithValue(
                "opening_as_of_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 15));
            sourceCommand.Parameters.AddWithValue(
                "known_effective_end_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 8, 2));
            sourceCommand.Parameters.AddWithValue("opening_recorded_at", openingRecordedAt);
            sourceCommand.Parameters.AddWithValue("opening_batch_id", Guid.NewGuid());
            sourceCommand.Parameters.AddWithValue("canceled_visit_id", canceledVisitId);
            sourceCommand.Parameters.AddWithValue("client_id", clientId);
            sourceCommand.Parameters.AddWithValue("visit_occurred_at", visitOccurredAt);
            sourceCommand.Parameters.AddWithValue("visit_recorded_at", visitRecordedAt);
            for (var index = 0; index < genericVisitIds.Length; index++)
            {
                sourceCommand.Parameters.AddWithValue(
                    $"generic_visit_{index + 1}_id",
                    genericVisitIds[index]);
                sourceCommand.Parameters.AddWithValue(
                    $"generic_visit_{index + 1}_occurred_at",
                    eventBase.AddHours(index));
                sourceCommand.Parameters.AddWithValue(
                    $"generic_visit_{index + 1}_recorded_at",
                    eventBase.AddHours(index).AddMinutes(5));
            }

            sourceCommand.Parameters.AddWithValue(
                "visit_cancellation_id",
                canceledVisitCancellationId);
            sourceCommand.Parameters.AddWithValue("visit_canceled_at", visitCanceledAt);
            sourceCommand.Parameters.AddWithValue(
                "visit_cancellation_recorded_at",
                visitCancellationRecordedAt);
            sourceCommand.Parameters.AddWithValue("original_payment_id", originalPaymentId);
            sourceCommand.Parameters.AddWithValue("replacement_payment_id", replacementPaymentId);
            sourceCommand.Parameters.AddWithValue("original_payment_amount", originalPaymentAmount);
            sourceCommand.Parameters.AddWithValue(
                "replacement_payment_amount",
                replacementPaymentAmount);
            sourceCommand.Parameters.AddWithValue(
                "original_payment_occurred_at",
                originalPaymentOccurredAt);
            sourceCommand.Parameters.AddWithValue(
                "original_payment_recorded_at",
                originalPaymentRecordedAt);
            sourceCommand.Parameters.AddWithValue("featured_occurred_at", featuredOccurredAt);
            sourceCommand.Parameters.AddWithValue("featured_recorded_at", featuredRecordedAt);
            sourceCommand.Parameters.AddWithValue(
                "payment_correction_batch_id",
                paymentCorrectionBatchId);
            sourceCommand.Parameters.AddWithValue("payment_correction_id", paymentCorrectionId);
            sourceCommand.Parameters.AddWithValue("freeze_id", freezeId);
            sourceCommand.Parameters.AddWithValue(
                "freeze_start_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 10));
            sourceCommand.Parameters.AddWithValue(
                "freeze_end_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 12));
            sourceCommand.Parameters.AddWithValue("freeze_occurred_at", freezeOccurredAt);
            sourceCommand.Parameters.AddWithValue("freeze_recorded_at", freezeRecordedAt);
            sourceCommand.Parameters.AddWithValue(
                "freeze_cancellation_id",
                freezeCancellationId);
            sourceCommand.Parameters.AddWithValue("freeze_canceled_at", freezeCanceledAt);
            sourceCommand.Parameters.AddWithValue(
                "freeze_cancellation_recorded_at",
                freezeCancellationRecordedAt);
            sourceCommand.Parameters.AddWithValue(
                "non_working_period_id",
                nonWorkingPeriodId);
            sourceCommand.Parameters.AddWithValue(
                "non_working_start_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 20));
            sourceCommand.Parameters.AddWithValue(
                "non_working_end_date",
                NpgsqlDbType.Date,
                new DateOnly(2026, 7, 21));
            sourceCommand.Parameters.AddWithValue(
                "non_working_recorded_at",
                nonWorkingRecordedAt);
            sourceCommand.Parameters.AddWithValue(
                "non_working_application_id",
                nonWorkingApplicationId);
            sourceCommand.Parameters.AddWithValue(
                "non_working_previewed_at",
                nonWorkingRecordedAt.AddMinutes(-30));
            Assert.Equal(15, await sourceCommand.ExecuteNonQueryAsync());
        }

        var clientReference = new { clientId, membershipId };
        AuditSeed[] auditSeeds =
        [
            new(
                Guid.NewGuid(),
                "membership.issued",
                "membership",
                membershipId,
                clientReference,
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                eventBase.AddHours(3),
                membershipIssuedAt,
                "normal",
                Reason: null,
                "UI smoke issued snapshot",
                new { },
                new { typeName = "Client history monthly" },
                ChangedAfterClose: false),
            new(
                Guid.NewGuid(),
                "membership_opening_state.created",
                "membership_opening_state",
                openingStateId,
                clientReference,
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                openingOccurredAt,
                openingRecordedAt,
                "manual_backfill",
                "Opening balance from legacy ledger",
                "Recorded from paper ledger",
                new { },
                new { declaredRemainingVisits = -2 },
                ChangedAfterClose: false),
            new(
                Guid.NewGuid(),
                "visit.marked",
                "visit",
                canceledVisitId,
                clientReference,
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                visitOccurredAt,
                visitRecordedAt,
                "normal",
                Reason: null,
                "Original front desk check-in",
                new { },
                new { visitKind = "one_off" },
                ChangedAfterClose: false),
            new(
                Guid.NewGuid(),
                "visit.canceled",
                "visit",
                canceledVisitId,
                clientReference,
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                visitCanceledAt,
                visitCancellationRecordedAt,
                "normal",
                "Duplicate reception check-in",
                "Original visit remains preserved",
                new { status = "active" },
                new { status = "canceled" },
                ChangedAfterClose: false),
            new(
                Guid.NewGuid(),
                "freeze.added",
                "freeze",
                freezeId,
                clientReference,
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                freezeOccurredAt,
                freezeRecordedAt,
                "normal",
                "Travel recovery window",
                Comment: null,
                new { },
                new { status = "active" },
                ChangedAfterClose: false),
            new(
                Guid.NewGuid(),
                "freeze.canceled",
                "freeze",
                freezeId,
                clientReference,
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                freezeCanceledAt,
                freezeCancellationRecordedAt,
                "normal",
                "Travel was canceled",
                "Original freeze remains preserved",
                new { status = "active" },
                new { status = "canceled" },
                ChangedAfterClose: false),
            new(
                originalPaymentAuditEntryId,
                "payment.created",
                "payment",
                originalPaymentId,
                clientReference,
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                originalPaymentOccurredAt,
                originalPaymentRecordedAt,
                "normal",
                Reason: null,
                "Original cash amount",
                new { },
                new { amount = originalPaymentAmount },
                ChangedAfterClose: false),
            new(
                featuredAuditEntryId,
                "payment.corrected",
                "payment",
                originalPaymentId,
                clientReference,
                sharedAdminAccountId,
                "shared_reception_admin",
                "admin",
                sharedSessionId,
                sharedDeviceLabel,
                featuredOccurredAt,
                featuredRecordedAt,
                "paper_fallback",
                "Cash amount was entered incorrectly",
                "Entered after reception connectivity returned",
                new { amount = originalPaymentAmount },
                new { amount = replacementPaymentAmount },
                ChangedAfterClose: true),
            new(
                Guid.NewGuid(),
                "non_working_day.added",
                "non_working_period",
                nonWorkingPeriodId,
                new
                {
                    affectedMembershipIds = new[] { membershipId },
                    affectedClientIds = new[] { clientId },
                },
                ownerAccountId,
                "owner",
                "owner",
                ownerSessionId,
                ownerDeviceLabel,
                nonWorkingOccurredAt,
                nonWorkingRecordedAt,
                "normal",
                Reason: null,
                "Ventilation service",
                new { },
                new { affectedMembershipCount = 1 },
                ChangedAfterClose: false),
        ];

        for (var index = 0; index < genericVisitIds.Length; index++)
        {
            var occurredAt = eventBase.AddHours(index);
            auditSeeds =
            [
                .. auditSeeds,
                new AuditSeed(
                    Guid.NewGuid(),
                    "visit.marked",
                    "visit",
                    genericVisitIds[index],
                    clientReference,
                    ownerAccountId,
                    "owner",
                    "owner",
                    ownerSessionId,
                    ownerDeviceLabel,
                    occurredAt,
                    occurredAt.AddMinutes(5),
                    "normal",
                    Reason: null,
                    $"Earlier source visit {index + 1}",
                    new { },
                    new { visitKind = "one_off" },
                    ChangedAfterClose: false),
            ];
        }

        Assert.Equal(totalEntries, auditSeeds.Length);
        foreach (var auditSeed in auditSeeds)
        {
            await InsertAuditSeedAsync(
                connection,
                transaction,
                auditSeed);
        }

        await transaction.CommitAsync();
        await RebuildMembershipAsync(membershipId);

        return new ClientHistorySmokeScenario(
            clientId,
            clientDisplayName,
            cardNumber,
            occurredDate,
            pageSize,
            totalEntries,
            featuredAuditEntryId,
            originalPaymentAuditEntryId,
            sharedSessionId,
            sharedDeviceLabel,
            featuredOccurredAt,
            featuredRecordedAt,
            originalPaymentAmount,
            replacementPaymentAmount);
    }

    private static async Task InsertAuditSeedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuditSeed seed)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into bodylife.business_audit_entries (
                id,
                action_type,
                entity_type,
                entity_id,
                related_entity_refs,
                actor_account_id,
                actor_account_type,
                actor_role,
                session_id,
                device_label,
                occurred_at,
                recorded_at,
                reason,
                comment,
                before_summary,
                after_summary,
                request_correlation_id,
                entry_origin,
                idempotency_key,
                changed_after_close)
            values (
                @id,
                @action_type,
                @entity_type,
                @entity_id,
                @related_entity_refs,
                @actor_account_id,
                @actor_account_type,
                @actor_role,
                @session_id,
                @device_label,
                @occurred_at,
                @recorded_at,
                @reason,
                @comment,
                @before_summary,
                @after_summary,
                @request_correlation_id,
                @entry_origin,
                @idempotency_key,
                @changed_after_close)
            """;
        command.Parameters.AddWithValue("id", seed.AuditEntryId);
        command.Parameters.AddWithValue("action_type", seed.ActionType);
        command.Parameters.AddWithValue("entity_type", seed.EntityType);
        command.Parameters.AddWithValue("entity_id", seed.EntityId);
        command.Parameters.Add("related_entity_refs", NpgsqlDbType.Jsonb).Value =
            JsonSerializer.Serialize(seed.RelatedEntityRefs, AuditJsonOptions);
        command.Parameters.AddWithValue("actor_account_id", seed.ActorAccountId);
        command.Parameters.AddWithValue("actor_account_type", seed.ActorAccountType);
        command.Parameters.AddWithValue("actor_role", seed.ActorRole);
        command.Parameters.AddWithValue("session_id", seed.SessionId);
        command.Parameters.AddWithValue("device_label", seed.DeviceLabel);
        command.Parameters.AddWithValue("occurred_at", seed.OccurredAt);
        command.Parameters.AddWithValue("recorded_at", seed.RecordedAt);
        command.Parameters.Add("reason", NpgsqlDbType.Varchar).Value =
            seed.Reason ?? (object)DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Varchar).Value =
            seed.Comment ?? (object)DBNull.Value;
        command.Parameters.Add("before_summary", NpgsqlDbType.Jsonb).Value =
            JsonSerializer.Serialize(seed.BeforeSummary, AuditJsonOptions);
        command.Parameters.Add("after_summary", NpgsqlDbType.Jsonb).Value =
            JsonSerializer.Serialize(seed.AfterSummary, AuditJsonOptions);
        command.Parameters.AddWithValue(
            "request_correlation_id",
            $"client-history-{seed.AuditEntryId:N}");
        command.Parameters.AddWithValue("entry_origin", seed.EntryOrigin);
        command.Parameters.AddWithValue(
            "idempotency_key",
            $"client-history-key-{seed.AuditEntryId:N}");
        command.Parameters.AddWithValue(
            "changed_after_close",
            seed.ChangedAfterClose);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    public async Task<Guid> SeedIssuedMembershipAsync(
        Guid issuedByAccountId,
        Guid clientId,
        Guid membershipTypeId,
        string typeNameSnapshot,
        int visitsLimitSnapshot,
        DateOnly? startDate = null,
        int durationDays = 30,
        DateTimeOffset? issuedAt = null)
    {
        if (durationDays <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationDays),
                durationDays,
                "Membership duration must be positive.");
        }

        var membershipId = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        var canonicalStartDate = startDate
            ?? DateOnly.FromDateTime(now.UtcDateTime).AddDays(-7);
        var canonicalIssuedAt = issuedAt ?? now.AddDays(-7);

        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
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
                    @id,
                    @client_id,
                    @membership_type_id,
                    @type_name_snapshot,
                    @duration_days_snapshot,
                    @visits_limit_snapshot,
                    950,
                    'UAH',
                    @start_date,
                    @base_end_date,
                    @issued_at,
                    @issued_by_account_id,
                    'active',
                    'normal',
                    null,
                    'UI smoke issued snapshot')
                """;
            command.Parameters.AddWithValue("id", membershipId);
            command.Parameters.AddWithValue("client_id", clientId);
            command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
            command.Parameters.AddWithValue("type_name_snapshot", typeNameSnapshot);
            command.Parameters.AddWithValue("duration_days_snapshot", durationDays);
            command.Parameters.AddWithValue("visits_limit_snapshot", visitsLimitSnapshot);
            command.Parameters.AddWithValue(
                "start_date",
                NpgsqlDbType.Date,
                canonicalStartDate);
            command.Parameters.AddWithValue(
                "base_end_date",
                NpgsqlDbType.Date,
                canonicalStartDate.AddDays(durationDays - 1));
            command.Parameters.AddWithValue("issued_at", canonicalIssuedAt);
            command.Parameters.AddWithValue("issued_by_account_id", issuedByAccountId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await RebuildMembershipAsync(membershipId);
        return membershipId;
    }

    public async Task<Guid> SeedClientActivityVisitAsync(
        Guid recordedByAccountId,
        Guid clientId,
        DateTimeOffset occurredAt,
        string visitKind,
        string status = "active")
    {
        if (recordedByAccountId == Guid.Empty || clientId == Guid.Empty)
        {
            throw new ArgumentException(
                "Client activity Visit seed ids must be non-empty.",
                nameof(clientId));
        }

        if (visitKind is not ("one_off" or "trial"))
        {
            throw new ArgumentException(
                "Client activity Visit kind must be one_off or trial.",
                nameof(visitKind));
        }

        if (status is not ("active" or "canceled"))
        {
            throw new ArgumentException(
                "Client activity Visit status must be active or canceled.",
                nameof(status));
        }

        var visitId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var recordedAt = occurredAt.AddMinutes(5);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
                'UI inactive report Visit seed',
                @session_started_at,
                @session_expires_at,
                @session_ended_at,
                @session_last_seen_at);

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
                @visit_kind,
                'normal',
                null,
                'Inactive report smoke source',
                @status);

            insert into bodylife.visit_cancellations (
                id,
                visit_id,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id)
            select
                @cancellation_id,
                @visit_id,
                'Canceled inactive report fixture Visit',
                @cancellation_occurred_at,
                @cancellation_recorded_at,
                @account_id,
                @session_id,
                'normal',
                null
            where @status = 'canceled'
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", recordedByAccountId);
        command.Parameters.AddWithValue("session_started_at", occurredAt.AddHours(-1));
        command.Parameters.AddWithValue("session_expires_at", occurredAt.AddDays(1));
        command.Parameters.AddWithValue("session_ended_at", occurredAt.AddMinutes(10));
        command.Parameters.AddWithValue("session_last_seen_at", occurredAt.AddMinutes(5));
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("visit_kind", visitKind);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("cancellation_id", Guid.NewGuid());
        command.Parameters.AddWithValue(
            "cancellation_occurred_at",
            occurredAt.AddMinutes(6));
        command.Parameters.AddWithValue(
            "cancellation_recorded_at",
            occurredAt.AddMinutes(7));
        Assert.Equal(
            status == "canceled" ? 3 : 2,
            await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
        return visitId;
    }

    public async Task SeedMembershipExtensionHistoryAsync(
        Guid recordedByAccountId,
        Guid clientId,
        Guid membershipId)
    {
        var sessionId = Guid.NewGuid();
        var activeFreezeId = Guid.NewGuid();
        var canceledFreezeId = Guid.NewGuid();
        var activePeriodId = Guid.NewGuid();
        var correctedPeriodId = Guid.NewGuid();
        var recordedAt = TimeProvider.System.GetUtcNow();
        var today = DateOnly.FromDateTime(recordedAt.UtcDateTime);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
                'UI extension history seed',
                @session_started_at,
                @session_expires_at,
                null,
                @session_started_at);

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
            values
                (
                    @active_freeze_id,
                    @client_id,
                    @membership_id,
                    @active_freeze_start,
                    @active_freeze_end,
                    'Medical recovery',
                    @recorded_at,
                    @recorded_at,
                    @account_id,
                    @session_id,
                    'normal',
                    null,
                    'active'),
                (
                    @canceled_freeze_id,
                    @client_id,
                    @membership_id,
                    @canceled_freeze_start,
                    @canceled_freeze_end,
                    'Travel plan',
                    @recorded_at,
                    @recorded_at,
                    @account_id,
                    @session_id,
                    'normal',
                    null,
                    'canceled');

            insert into bodylife.non_working_periods (
                id,
                start_date,
                end_date,
                reason_code,
                reason_comment,
                created_at,
                created_by_account_id,
                session_id,
                status)
            values
                (
                    @active_period_id,
                    @active_period_start,
                    @active_period_end,
                    'maintenance',
                    'Ventilation service',
                    @recorded_at,
                    @account_id,
                    @session_id,
                    'active'),
                (
                    @corrected_period_id,
                    @corrected_period_start,
                    @corrected_period_end,
                    'repair',
                    'Floor inspection',
                    @recorded_at,
                    @account_id,
                    @session_id,
                    'corrected');

            insert into bodylife.non_working_period_applications (
                id,
                non_working_period_id,
                membership_id,
                client_id,
                applied_start_date,
                applied_end_date,
                previewed_at,
                confirmed_at,
                status)
            values
                (
                    @active_application_id,
                    @active_period_id,
                    @membership_id,
                    @client_id,
                    @active_period_start,
                    @active_period_end,
                    @previewed_at,
                    @confirmed_at,
                    'active'),
                (
                    @corrected_application_id,
                    @corrected_period_id,
                    @membership_id,
                    @client_id,
                    @corrected_period_start,
                    @corrected_period_end,
                    @previewed_at,
                    @confirmed_at,
                    'corrected')
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", recordedByAccountId);
        command.Parameters.AddWithValue("session_started_at", recordedAt.AddMinutes(-1));
        command.Parameters.AddWithValue("session_expires_at", recordedAt.AddDays(1));
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("previewed_at", recordedAt.AddMinutes(-2));
        command.Parameters.AddWithValue("confirmed_at", recordedAt.AddMinutes(-1));
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("active_freeze_id", activeFreezeId);
        command.Parameters.AddWithValue("canceled_freeze_id", canceledFreezeId);
        command.Parameters.AddWithValue("active_period_id", activePeriodId);
        command.Parameters.AddWithValue("corrected_period_id", correctedPeriodId);
        command.Parameters.AddWithValue("active_application_id", Guid.NewGuid());
        command.Parameters.AddWithValue("corrected_application_id", Guid.NewGuid());
        command.Parameters.AddWithValue(
            "active_freeze_start",
            NpgsqlDbType.Date,
            today.AddDays(-6));
        command.Parameters.AddWithValue(
            "active_freeze_end",
            NpgsqlDbType.Date,
            today.AddDays(-4));
        command.Parameters.AddWithValue(
            "canceled_freeze_start",
            NpgsqlDbType.Date,
            today.AddDays(-2));
        command.Parameters.AddWithValue(
            "canceled_freeze_end",
            NpgsqlDbType.Date,
            today.AddDays(-1));
        command.Parameters.AddWithValue(
            "active_period_start",
            NpgsqlDbType.Date,
            today.AddDays(-5));
        command.Parameters.AddWithValue(
            "active_period_end",
            NpgsqlDbType.Date,
            today.AddDays(-3));
        command.Parameters.AddWithValue(
            "corrected_period_start",
            NpgsqlDbType.Date,
            today.AddDays(-7));
        command.Parameters.AddWithValue(
            "corrected_period_end",
            NpgsqlDbType.Date,
            today.AddDays(-7));

        Assert.Equal(7, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
        await RebuildMembershipAsync(membershipId);
    }

    public async Task<Guid> SeedCancelableFreezeAsync(
        Guid recordedByAccountId,
        Guid clientId,
        Guid membershipId,
        string reason,
        DateOnly? startDate = null,
        DateOnly? endDate = null)
    {
        var freezeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var recordedAt = TimeProvider.System.GetUtcNow();
        var canonicalStartDate = startDate
            ?? DateOnly.FromDateTime(recordedAt.UtcDateTime).AddDays(1);
        var canonicalEndDate = endDate ?? canonicalStartDate.AddDays(1);
        if (canonicalEndDate < canonicalStartDate)
        {
            throw new ArgumentException(
                "Freeze end date must be on or after its start date.",
                nameof(endDate));
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
                'UI cancel Freeze seed',
                @session_started_at,
                @session_expires_at,
                null,
                @session_started_at);

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
                @freeze_id,
                @client_id,
                @membership_id,
                @start_date,
                @end_date,
                @reason,
                @recorded_at,
                @recorded_at,
                @account_id,
                @session_id,
                'normal',
                null,
                'active')
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", recordedByAccountId);
        command.Parameters.AddWithValue("session_started_at", recordedAt.AddMinutes(-1));
        command.Parameters.AddWithValue("session_expires_at", recordedAt.AddDays(1));
        command.Parameters.AddWithValue("freeze_id", freezeId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            canonicalStartDate);
        command.Parameters.AddWithValue(
            "end_date",
            NpgsqlDbType.Date,
            canonicalEndDate);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("recorded_at", recordedAt);

        Assert.Equal(2, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
        await RebuildMembershipAsync(membershipId);
        return freezeId;
    }

    public async Task<Guid> SeedNonWorkingDayCorrectionPeriodAsync(
        Guid recordedByAccountId,
        DateRange period,
        string reasonCode,
        string? reasonComment,
        IReadOnlyList<NonWorkingDayApplicationSmokeSeed> applications)
    {
        if (recordedByAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Owner account id is required.",
                nameof(recordedByAccountId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentNullException.ThrowIfNull(applications);
        if (applications.Any(application => application is null)
            || applications.Select(application => application.MembershipId)
                .Distinct()
                .Count() != applications.Count
            || applications.Select(application => application.ClientId)
                .Distinct()
                .Count() != applications.Count)
        {
            throw new ArgumentException(
                "NonWorkingDay correction applications must be unique.",
                nameof(applications));
        }

        var periodId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var auditEntryId = Guid.NewGuid();
        var recordedAt = TimeProvider.System.GetUtcNow();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
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
                    'UI NonWorkingDay correction preview seed',
                    @session_started_at,
                    @session_expires_at,
                    null,
                    @session_started_at);

                insert into bodylife.non_working_periods (
                    id,
                    start_date,
                    end_date,
                    reason_code,
                    reason_comment,
                    created_at,
                    created_by_account_id,
                    session_id,
                    status)
                values (
                    @period_id,
                    @start_date,
                    @end_date,
                    @reason_code,
                    @reason_comment,
                    @recorded_at,
                    @account_id,
                    @session_id,
                    'active');

                insert into bodylife.business_audit_entries (
                    id,
                    action_type,
                    entity_type,
                    entity_id,
                    related_entity_refs,
                    actor_account_id,
                    actor_account_type,
                    actor_role,
                    session_id,
                    device_label,
                    occurred_at,
                    recorded_at,
                    reason,
                    comment,
                    before_summary,
                    after_summary,
                    request_correlation_id,
                    entry_origin,
                    idempotency_key,
                    changed_after_close)
                values (
                    @audit_entry_id,
                    'non_working_day.added',
                    'non_working_period',
                    @period_id,
                    '[]'::jsonb,
                    @account_id,
                    'owner',
                    'owner',
                    @session_id,
                    'UI NonWorkingDay correction preview seed',
                    @recorded_at,
                    @recorded_at,
                    @reason_code,
                    @reason_comment,
                    '{}'::jsonb,
                    '{}'::jsonb,
                    'ui-non-working-day-correction-preview-seed',
                    'normal',
                    null,
                    false)
                """;
            command.Parameters.AddWithValue("session_id", sessionId);
            command.Parameters.AddWithValue("account_id", recordedByAccountId);
            command.Parameters.AddWithValue(
                "session_started_at",
                recordedAt.AddMinutes(-3));
            command.Parameters.AddWithValue(
                "session_expires_at",
                recordedAt.AddDays(1));
            command.Parameters.AddWithValue("period_id", periodId);
            command.Parameters.AddWithValue("audit_entry_id", auditEntryId);
            command.Parameters.AddWithValue(
                "start_date",
                NpgsqlDbType.Date,
                period.StartDate);
            command.Parameters.AddWithValue(
                "end_date",
                NpgsqlDbType.Date,
                period.EndDate);
            command.Parameters.AddWithValue("reason_code", reasonCode);
            command.Parameters.AddWithValue(
                "reason_comment",
                NpgsqlDbType.Varchar,
                (object?)reasonComment ?? DBNull.Value);
            command.Parameters.AddWithValue("recorded_at", recordedAt);
            Assert.Equal(3, await command.ExecuteNonQueryAsync());
        }

        foreach (var application in applications)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                insert into bodylife.non_working_period_applications (
                    id,
                    non_working_period_id,
                    membership_id,
                    client_id,
                    applied_start_date,
                    applied_end_date,
                    previewed_at,
                    confirmed_at,
                    status)
                values (
                    @id,
                    @period_id,
                    @membership_id,
                    @client_id,
                    @start_date,
                    @end_date,
                    @previewed_at,
                    @confirmed_at,
                    'active')
                """;
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("period_id", periodId);
            command.Parameters.AddWithValue(
                "membership_id",
                application.MembershipId);
            command.Parameters.AddWithValue("client_id", application.ClientId);
            command.Parameters.AddWithValue(
                "start_date",
                NpgsqlDbType.Date,
                period.StartDate);
            command.Parameters.AddWithValue(
                "end_date",
                NpgsqlDbType.Date,
                period.EndDate);
            command.Parameters.AddWithValue(
                "previewed_at",
                recordedAt.AddMinutes(-2));
            command.Parameters.AddWithValue(
                "confirmed_at",
                recordedAt.AddMinutes(-1));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
        foreach (var membershipId in applications
            .Select(application => application.MembershipId)
            .Order())
        {
            await RebuildMembershipAsync(membershipId);
        }

        return periodId;
    }

    public async Task<NonWorkingDayMutationCountSmokeSnapshot>
        ReadNonWorkingDayMutationCountsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                (select count(*) from bodylife.non_working_periods),
                (select count(*) from bodylife.non_working_period_applications),
                (select count(*) from bodylife.non_working_period_cancellations),
                (
                    select count(*)
                    from bodylife.business_audit_entries
                    where action_type like 'non_working_day.%'
                ),
                (
                    select count(*)
                    from bodylife.command_idempotency_keys
                    where command_name in ('AddNonWorkingDay', 'CorrectNonWorkingDay')
                )
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new NonWorkingDayMutationCountSmokeSnapshot(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
    }

    public async Task<NonWorkingDayCorrectionMutationSmokeSnapshot>
        ReadNonWorkingDayCorrectionMutationAsync(Guid originalPeriodId)
    {
        if (originalPeriodId == Guid.Empty)
        {
            throw new ArgumentException(
                "Original period id is required.",
                nameof(originalPeriodId));
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            with correction as (
                select
                    audit.id,
                    audit.action_type,
                    audit.reason,
                    audit.comment,
                    nullif(
                        audit.related_entity_refs ->> 'replacementPeriodId',
                        '')::uuid as replacement_period_id,
                    nullif(
                        audit.related_entity_refs ->> 'cancellationId',
                        '')::uuid as cancellation_id,
                    (audit.after_summary ->> 'oldAffectedCount')::integer
                        as old_affected_count,
                    (audit.after_summary ->> 'newAffectedCount')::integer
                        as new_affected_count,
                    (audit.after_summary ->> 'affectedUnionCount')::integer
                        as affected_union_count
                from bodylife.business_audit_entries audit
                where audit.entity_type = 'non_working_period'
                  and audit.entity_id = @original_period_id
                  and audit.action_type in (
                      'non_working_day.corrected',
                      'non_working_day.canceled')
                order by audit.recorded_at desc, audit.id desc
                limit 1
            )
            select
                correction.id,
                correction.action_type,
                original.status,
                correction.replacement_period_id,
                replacement.status,
                correction.cancellation_id,
                (
                    select count(*)
                    from bodylife.non_working_period_applications application
                    where application.non_working_period_id = original.id
                ),
                (
                    select count(*)
                    from bodylife.non_working_period_applications application
                    where application.non_working_period_id
                        = correction.replacement_period_id
                ),
                correction.reason,
                correction.comment,
                correction.old_affected_count,
                correction.new_affected_count,
                correction.affected_union_count,
                (
                    select count(*)
                    from bodylife.command_idempotency_keys idempotency
                    where idempotency.command_name = 'CorrectNonWorkingDay'
                      and idempotency.audit_entry_id = correction.id
                ),
                coalesce((
                    select bool_and(application.status = original.status)
                    from bodylife.non_working_period_applications application
                    where application.non_working_period_id = original.id
                ), true),
                coalesce((
                    select bool_and(application.status = 'active')
                    from bodylife.non_working_period_applications application
                    where application.non_working_period_id
                        = correction.replacement_period_id
                ), true)
            from correction
            join bodylife.non_working_periods original
              on original.id = @original_period_id
            left join bodylife.non_working_periods replacement
              on replacement.id = correction.replacement_period_id
            """;
        command.Parameters.AddWithValue("original_period_id", originalPeriodId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                "The canonical NonWorkingDay correction mutation was not found.");
        }

        return new NonWorkingDayCorrectionMutationSmokeSnapshot(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.GetInt64(13),
            reader.GetBoolean(14),
            reader.GetBoolean(15));
    }

    public async Task MoveIssuedMembershipStartDateAsync(
        Guid membershipId,
        DateOnly startDate)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.issued_memberships
            set start_date = @start_date,
                base_end_date = @start_date + (duration_days_snapshot - 1)
            where id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, startDate);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        await RebuildMembershipAsync(membershipId);
    }

    public async Task SeedPaymentHistoryAsync(
        Guid recordedByAccountId,
        Guid clientId,
        Guid membershipId)
    {
        var sessionId = Guid.NewGuid();
        var originalPaymentId = Guid.NewGuid();
        var replacementPaymentId = Guid.NewGuid();
        var canceledPaymentId = Guid.NewGuid();
        var trialPaymentId = Guid.NewGuid();
        var recordedAt = TimeProvider.System.GetUtcNow();
        var sourceBatchId = Guid.NewGuid();
        var correctionBatchId = Guid.NewGuid();
        var cancellationBatchId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(ConnectionString);
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
                'UI payment history seed',
                @session_started_at,
                @session_expires_at,
                null,
                @session_last_seen_at);

            insert into bodylife.payments (
                id,
                client_id,
                membership_id,
                amount,
                currency,
                method,
                payment_context,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id,
                comment,
                status)
            values
                (
                    @original_payment_id,
                    @client_id,
                    @membership_id,
                    1000,
                    'UAH',
                    'cash',
                    'membership_sale',
                    @original_occurred_at,
                    @original_recorded_at,
                    @account_id,
                    @session_id,
                    'paper_fallback',
                    @source_batch_id,
                    'Recovered original cash sale',
                    'replaced'),
                (
                    @replacement_payment_id,
                    @client_id,
                    @membership_id,
                    900,
                    'UAH',
                    'cash',
                    'membership_sale',
                    @replacement_occurred_at,
                    @replacement_recorded_at,
                    @account_id,
                    @session_id,
                    'normal',
                    null,
                    'Corrected cash amount',
                    'active'),
                (
                    @canceled_payment_id,
                    @client_id,
                    null,
                    250,
                    'UAH',
                    'cash',
                    'one_off',
                    @canceled_occurred_at,
                    @canceled_recorded_at,
                    @account_id,
                    @session_id,
                    'normal',
                    null,
                    'Duplicate drop-in cash',
                    'canceled'),
                (
                    @trial_payment_id,
                    @client_id,
                    null,
                    100,
                    'UAH',
                    'cash',
                    'trial',
                    @trial_occurred_at,
                    @trial_recorded_at,
                    @account_id,
                    @session_id,
                    'normal',
                    null,
                    'Trial cash entry',
                    'active');

            insert into bodylife.payment_corrections (
                id,
                client_id,
                original_payment_id,
                replacement_payment_id,
                changed_fields,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id)
            values (
                @correction_id,
                @client_id,
                @original_payment_id,
                @replacement_payment_id,
                @changed_fields,
                'Cash amount was entered incorrectly',
                @correction_occurred_at,
                @correction_recorded_at,
                @account_id,
                @session_id,
                'manual_backfill',
                @correction_batch_id);

            insert into bodylife.payment_cancellations (
                id,
                payment_id,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id)
            values (
                @cancellation_id,
                @canceled_payment_id,
                'Duplicate cash entry',
                @cancellation_occurred_at,
                @cancellation_recorded_at,
                @account_id,
                @session_id,
                'paper_fallback',
                @cancellation_batch_id)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", recordedByAccountId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("original_payment_id", originalPaymentId);
        command.Parameters.AddWithValue("replacement_payment_id", replacementPaymentId);
        command.Parameters.AddWithValue("canceled_payment_id", canceledPaymentId);
        command.Parameters.AddWithValue("trial_payment_id", trialPaymentId);
        command.Parameters.AddWithValue("correction_id", Guid.NewGuid());
        command.Parameters.AddWithValue("cancellation_id", Guid.NewGuid());
        command.Parameters.AddWithValue("source_batch_id", sourceBatchId);
        command.Parameters.AddWithValue("correction_batch_id", correctionBatchId);
        command.Parameters.AddWithValue("cancellation_batch_id", cancellationBatchId);
        command.Parameters.AddWithValue("session_started_at", recordedAt.AddDays(-1));
        command.Parameters.AddWithValue("session_expires_at", recordedAt.AddDays(1));
        command.Parameters.AddWithValue("session_last_seen_at", recordedAt.AddMinutes(-5));
        command.Parameters.AddWithValue("original_occurred_at", recordedAt.AddHours(-4));
        command.Parameters.AddWithValue("original_recorded_at", recordedAt.AddHours(-3));
        command.Parameters.AddWithValue("replacement_occurred_at", recordedAt.AddHours(-3));
        command.Parameters.AddWithValue("replacement_recorded_at", recordedAt.AddHours(-2));
        command.Parameters.AddWithValue("canceled_occurred_at", recordedAt.AddHours(-2));
        command.Parameters.AddWithValue("canceled_recorded_at", recordedAt.AddHours(-1));
        command.Parameters.AddWithValue("trial_occurred_at", recordedAt.AddHours(-1));
        command.Parameters.AddWithValue("trial_recorded_at", recordedAt.AddMinutes(-30));
        command.Parameters.AddWithValue("correction_occurred_at", recordedAt.AddHours(-2));
        command.Parameters.AddWithValue("correction_recorded_at", recordedAt.AddHours(-1));
        command.Parameters.AddWithValue("cancellation_occurred_at", recordedAt.AddHours(-1));
        command.Parameters.AddWithValue("cancellation_recorded_at", recordedAt.AddMinutes(-30));
        command.Parameters.AddWithValue(
            "changed_fields",
            NpgsqlDbType.Jsonb,
            "[\"amount\",\"occurred_at\"]");
        Assert.Equal(7, await command.ExecuteNonQueryAsync());
    }

    public async Task SeedDailyReportAsync(
        Guid recordedByAccountId,
        Guid clientId,
        DateOnly businessDate)
    {
        var sessionId = Guid.NewGuid();
        var activeVisitId = Guid.NewGuid();
        var canceledVisitId = Guid.NewGuid();
        var visitCancellationId = Guid.NewGuid();
        var originalPaymentId = Guid.NewGuid();
        var replacementPaymentId = Guid.NewGuid();
        var canceledPaymentId = Guid.NewGuid();
        var paymentCorrectionId = Guid.NewGuid();
        var paymentCancellationId = Guid.NewGuid();
        var dayStart = new DateTimeOffset(
            businessDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var activeVisitOccurredAt = dayStart.AddHours(9);
        var activeVisitRecordedAt = activeVisitOccurredAt.AddMinutes(5);
        var canceledVisitOccurredAt = dayStart.AddHours(10);
        var canceledVisitRecordedAt = canceledVisitOccurredAt.AddMinutes(5);
        var visitCancellationOccurredAt = dayStart.AddHours(11);
        var visitCancellationRecordedAt = visitCancellationOccurredAt.AddMinutes(5);
        var originalPaymentOccurredAt = dayStart.AddHours(12);
        var originalPaymentRecordedAt = originalPaymentOccurredAt.AddMinutes(5);
        var replacementPaymentOccurredAt = dayStart.AddHours(13);
        var replacementPaymentRecordedAt = replacementPaymentOccurredAt.AddMinutes(5);
        var canceledPaymentOccurredAt = dayStart.AddHours(14);
        var canceledPaymentRecordedAt = canceledPaymentOccurredAt.AddMinutes(5);
        var paymentCorrectionOccurredAt = dayStart.AddHours(15);
        var paymentCorrectionRecordedAt = paymentCorrectionOccurredAt.AddMinutes(5);
        var paymentCancellationOccurredAt = dayStart.AddHours(16);
        var paymentCancellationRecordedAt = paymentCancellationOccurredAt.AddMinutes(5);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
                'UI daily report seed',
                @session_started_at,
                @session_expires_at,
                @session_ended_at,
                @session_last_seen_at);

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
            values
                (
                    @active_visit_id,
                    @client_id,
                    @active_visit_occurred_at,
                    @active_visit_recorded_at,
                    @account_id,
                    @session_id,
                    'one_off',
                    'normal',
                    null,
                    'Active daily report visit',
                    'active'),
                (
                    @canceled_visit_id,
                    @client_id,
                    @canceled_visit_occurred_at,
                    @canceled_visit_recorded_at,
                    @account_id,
                    @session_id,
                    'trial',
                    'paper_fallback',
                    @visit_batch_id,
                    'Canceled daily report visit',
                    'canceled');

            insert into bodylife.visit_cancellations (
                id,
                visit_id,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id)
            values (
                @visit_cancellation_id,
                @canceled_visit_id,
                'Duplicate report visit',
                @visit_cancellation_occurred_at,
                @visit_cancellation_recorded_at,
                @account_id,
                @session_id,
                'manual_backfill',
                @visit_cancellation_batch_id);

            insert into bodylife.payments (
                id,
                client_id,
                membership_id,
                amount,
                currency,
                method,
                payment_context,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id,
                comment,
                status)
            values
                (
                    @original_payment_id,
                    @client_id,
                    null,
                    1000,
                    'UAH',
                    'cash',
                    'one_off',
                    @original_payment_occurred_at,
                    @original_payment_recorded_at,
                    @account_id,
                    @session_id,
                    'paper_fallback',
                    @payment_batch_id,
                    'Original daily report payment',
                    'replaced'),
                (
                    @replacement_payment_id,
                    @client_id,
                    null,
                    900,
                    'UAH',
                    'cash',
                    'one_off',
                    @replacement_payment_occurred_at,
                    @replacement_payment_recorded_at,
                    @account_id,
                    @session_id,
                    'normal',
                    null,
                    'Replacement daily report payment',
                    'active'),
                (
                    @canceled_payment_id,
                    @client_id,
                    null,
                    250,
                    'UAH',
                    'cash',
                    'trial',
                    @canceled_payment_occurred_at,
                    @canceled_payment_recorded_at,
                    @account_id,
                    @session_id,
                    'normal',
                    null,
                    'Canceled daily report payment',
                    'canceled');

            insert into bodylife.payment_corrections (
                id,
                client_id,
                original_payment_id,
                replacement_payment_id,
                changed_fields,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id)
            values (
                @payment_correction_id,
                @client_id,
                @original_payment_id,
                @replacement_payment_id,
                @changed_fields,
                'Corrected report amount',
                @payment_correction_occurred_at,
                @payment_correction_recorded_at,
                @account_id,
                @session_id,
                'manual_backfill',
                @payment_correction_batch_id);

            insert into bodylife.payment_cancellations (
                id,
                payment_id,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id)
            values (
                @payment_cancellation_id,
                @canceled_payment_id,
                'Duplicate report payment',
                @payment_cancellation_occurred_at,
                @payment_cancellation_recorded_at,
                @account_id,
                @session_id,
                'paper_fallback',
                @payment_cancellation_batch_id)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", recordedByAccountId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("session_started_at", dayStart.AddHours(8));
        command.Parameters.AddWithValue("session_expires_at", dayStart.AddHours(18));
        command.Parameters.AddWithValue("session_ended_at", dayStart.AddHours(17));
        command.Parameters.AddWithValue("session_last_seen_at", dayStart.AddHours(16));
        command.Parameters.AddWithValue("active_visit_id", activeVisitId);
        command.Parameters.AddWithValue("canceled_visit_id", canceledVisitId);
        command.Parameters.AddWithValue("visit_cancellation_id", visitCancellationId);
        command.Parameters.AddWithValue("visit_batch_id", Guid.NewGuid());
        command.Parameters.AddWithValue("visit_cancellation_batch_id", Guid.NewGuid());
        command.Parameters.AddWithValue("active_visit_occurred_at", activeVisitOccurredAt);
        command.Parameters.AddWithValue("active_visit_recorded_at", activeVisitRecordedAt);
        command.Parameters.AddWithValue("canceled_visit_occurred_at", canceledVisitOccurredAt);
        command.Parameters.AddWithValue("canceled_visit_recorded_at", canceledVisitRecordedAt);
        command.Parameters.AddWithValue(
            "visit_cancellation_occurred_at",
            visitCancellationOccurredAt);
        command.Parameters.AddWithValue(
            "visit_cancellation_recorded_at",
            visitCancellationRecordedAt);
        command.Parameters.AddWithValue("original_payment_id", originalPaymentId);
        command.Parameters.AddWithValue("replacement_payment_id", replacementPaymentId);
        command.Parameters.AddWithValue("canceled_payment_id", canceledPaymentId);
        command.Parameters.AddWithValue("payment_correction_id", paymentCorrectionId);
        command.Parameters.AddWithValue("payment_cancellation_id", paymentCancellationId);
        command.Parameters.AddWithValue("payment_batch_id", Guid.NewGuid());
        command.Parameters.AddWithValue("payment_correction_batch_id", Guid.NewGuid());
        command.Parameters.AddWithValue("payment_cancellation_batch_id", Guid.NewGuid());
        command.Parameters.AddWithValue(
            "original_payment_occurred_at",
            originalPaymentOccurredAt);
        command.Parameters.AddWithValue(
            "original_payment_recorded_at",
            originalPaymentRecordedAt);
        command.Parameters.AddWithValue(
            "replacement_payment_occurred_at",
            replacementPaymentOccurredAt);
        command.Parameters.AddWithValue(
            "replacement_payment_recorded_at",
            replacementPaymentRecordedAt);
        command.Parameters.AddWithValue(
            "canceled_payment_occurred_at",
            canceledPaymentOccurredAt);
        command.Parameters.AddWithValue(
            "canceled_payment_recorded_at",
            canceledPaymentRecordedAt);
        command.Parameters.AddWithValue(
            "payment_correction_occurred_at",
            paymentCorrectionOccurredAt);
        command.Parameters.AddWithValue(
            "payment_correction_recorded_at",
            paymentCorrectionRecordedAt);
        command.Parameters.AddWithValue(
            "payment_cancellation_occurred_at",
            paymentCancellationOccurredAt);
        command.Parameters.AddWithValue(
            "payment_cancellation_recorded_at",
            paymentCancellationRecordedAt);
        command.Parameters.AddWithValue(
            "changed_fields",
            NpgsqlDbType.Jsonb,
            "[\"amount\",\"occurred_at\"]");
        Assert.Equal(9, await command.ExecuteNonQueryAsync());

        var clientReference = new { clientId };
        AuditSeed[] auditSeeds =
        [
            new(
                Guid.NewGuid(),
                "visit.marked",
                "visit",
                activeVisitId,
                clientReference,
                recordedByAccountId,
                "owner",
                "owner",
                sessionId,
                "UI daily report seed",
                activeVisitOccurredAt,
                activeVisitRecordedAt,
                "normal",
                Reason: null,
                "Active daily report visit",
                new { },
                new { visitKind = "one_off" },
                ChangedAfterClose: false),
            new(
                Guid.NewGuid(),
                "visit.marked",
                "visit",
                canceledVisitId,
                clientReference,
                recordedByAccountId,
                "owner",
                "owner",
                sessionId,
                "UI daily report seed",
                canceledVisitOccurredAt,
                canceledVisitRecordedAt,
                "paper_fallback",
                Reason: null,
                "Canceled daily report visit",
                new { },
                new { visitKind = "trial" },
                ChangedAfterClose: false),
            new(
                Guid.NewGuid(),
                "visit.canceled",
                "visit",
                canceledVisitId,
                clientReference,
                recordedByAccountId,
                "owner",
                "owner",
                sessionId,
                "UI daily report seed",
                visitCancellationOccurredAt,
                visitCancellationRecordedAt,
                "manual_backfill",
                "Duplicate report visit",
                Comment: null,
                new { status = "active" },
                new { status = "canceled" },
                ChangedAfterClose: false),
            new(
                Guid.NewGuid(),
                "payment.created",
                "payment",
                originalPaymentId,
                clientReference,
                recordedByAccountId,
                "owner",
                "owner",
                sessionId,
                "UI daily report seed",
                originalPaymentOccurredAt,
                originalPaymentRecordedAt,
                "paper_fallback",
                Reason: null,
                "Original daily report payment",
                new { },
                new { amount = 1000m },
                ChangedAfterClose: false),
            new(
                Guid.NewGuid(),
                "payment.corrected",
                "payment",
                originalPaymentId,
                clientReference,
                recordedByAccountId,
                "owner",
                "owner",
                sessionId,
                "UI daily report seed",
                paymentCorrectionOccurredAt,
                paymentCorrectionRecordedAt,
                "manual_backfill",
                "Corrected report amount",
                Comment: null,
                new { amount = 1000m },
                new { amount = 900m },
                ChangedAfterClose: false),
            new(
                Guid.NewGuid(),
                "payment.created",
                "payment",
                canceledPaymentId,
                clientReference,
                recordedByAccountId,
                "owner",
                "owner",
                sessionId,
                "UI daily report seed",
                canceledPaymentOccurredAt,
                canceledPaymentRecordedAt,
                "normal",
                Reason: null,
                "Canceled daily report payment",
                new { },
                new { amount = 250m },
                ChangedAfterClose: false),
            new(
                Guid.NewGuid(),
                "payment.canceled",
                "payment",
                canceledPaymentId,
                clientReference,
                recordedByAccountId,
                "owner",
                "owner",
                sessionId,
                "UI daily report seed",
                paymentCancellationOccurredAt,
                paymentCancellationRecordedAt,
                "paper_fallback",
                "Duplicate report payment",
                Comment: null,
                new { status = "active" },
                new { status = "canceled" },
                ChangedAfterClose: false),
        ];

        foreach (var auditSeed in auditSeeds)
        {
            await InsertAuditSeedAsync(connection, transaction, auditSeed);
        }

        await transaction.CommitAsync();
    }

    public async Task<Guid[]> SeedCountedMembershipVisitsAsync(
        Guid recordedByAccountId,
        Guid clientId,
        Guid membershipId,
        DateTimeOffset firstOccurredAt,
        int count)
    {
        if (recordedByAccountId == Guid.Empty
            || clientId == Guid.Empty
            || membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership report Visit seed ids must be non-empty.",
                nameof(membershipId));
        }

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "At least one counted Visit is required.");
        }

        var sessionId = Guid.NewGuid();
        var lastOccurredAt = firstOccurredAt.AddHours(count - 1);
        var visitIds = new Guid[count];

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.Transaction = transaction;
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
                    @session_id,
                    @account_id,
                    'UI Membership report Visit seed',
                    @started_at,
                    @expires_at,
                    @ended_at,
                    @last_seen_at)
                """;
            sessionCommand.Parameters.AddWithValue("session_id", sessionId);
            sessionCommand.Parameters.AddWithValue("account_id", recordedByAccountId);
            sessionCommand.Parameters.AddWithValue("started_at", firstOccurredAt.AddHours(-1));
            sessionCommand.Parameters.AddWithValue("expires_at", lastOccurredAt.AddDays(1));
            sessionCommand.Parameters.AddWithValue("ended_at", lastOccurredAt.AddMinutes(10));
            sessionCommand.Parameters.AddWithValue("last_seen_at", lastOccurredAt.AddMinutes(5));
            Assert.Equal(1, await sessionCommand.ExecuteNonQueryAsync());
        }

        for (var index = 0; index < count; index++)
        {
            var visitId = visitIds[index] = Guid.NewGuid();
            var occurredAt = firstOccurredAt.AddHours(index);
            var recordedAt = occurredAt.AddMinutes(5);
            await using var visitCommand = connection.CreateCommand();
            visitCommand.Transaction = transaction;
            visitCommand.CommandText =
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
                    'Membership report smoke source',
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
            visitCommand.Parameters.AddWithValue("visit_id", visitId);
            visitCommand.Parameters.AddWithValue("consumption_id", Guid.NewGuid());
            visitCommand.Parameters.AddWithValue("client_id", clientId);
            visitCommand.Parameters.AddWithValue("membership_id", membershipId);
            visitCommand.Parameters.AddWithValue("occurred_at", occurredAt);
            visitCommand.Parameters.AddWithValue("recorded_at", recordedAt);
            visitCommand.Parameters.AddWithValue("account_id", recordedByAccountId);
            visitCommand.Parameters.AddWithValue("session_id", sessionId);
            Assert.Equal(2, await visitCommand.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
        await RebuildMembershipAsync(membershipId);
        return visitIds;
    }

    public async Task<int> CountLowRemainingMembershipsAsync(int threshold)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select count(*)
            from bodylife.issued_memberships membership
            join bodylife.membership_state_cache cache
                on cache.membership_id = membership.id
            where membership.status = 'active'
                and cache.recalculation_version = @recalculation_version
                and cache.remaining_visits <= @threshold
            """;
        command.Parameters.AddWithValue(
            "recalculation_version",
            MembershipStateCacheRebuilder.CurrentRecalculationVersion);
        command.Parameters.AddWithValue("threshold", threshold);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task SeedNegativeMembershipOpeningStateAsync(
        Guid recordedByAccountId,
        Guid membershipId,
        DateOnly openingAsOfDate,
        int declaredRemainingVisits,
        int declaredNegativeBalance)
    {
        if (recordedByAccountId == Guid.Empty || membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Negative opening-state seed ids must be non-empty.",
                nameof(membershipId));
        }

        if (openingAsOfDate == default
            || declaredRemainingVisits >= 0
            || declaredNegativeBalance <= 0)
        {
            throw new ArgumentException(
                "A negative opening state requires a date, signed remaining visits and positive negative balance.",
                nameof(declaredRemainingVisits));
        }

        var sessionId = Guid.NewGuid();
        var recordedAt = new DateTimeOffset(
            openingAsOfDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
                'UI negative report opening seed',
                @session_started_at,
                @session_expires_at,
                @session_ended_at,
                @session_last_seen_at);

            insert into bodylife.membership_opening_states (
                id,
                membership_id,
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
                status)
            values (
                @opening_state_id,
                @membership_id,
                @opening_as_of_date,
                @declared_remaining_visits,
                @declared_negative_balance,
                null,
                null,
                'UI negative report opening source',
                'Legacy negative balance retained for report proof',
                @recorded_at,
                @account_id,
                @session_id,
                'manual_backfill',
                @entry_batch_id,
                'active')
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", recordedByAccountId);
        command.Parameters.AddWithValue("session_started_at", recordedAt.AddHours(-1));
        command.Parameters.AddWithValue("session_expires_at", recordedAt.AddDays(1));
        command.Parameters.AddWithValue("session_ended_at", recordedAt.AddMinutes(10));
        command.Parameters.AddWithValue("session_last_seen_at", recordedAt.AddMinutes(5));
        command.Parameters.AddWithValue("opening_state_id", Guid.NewGuid());
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue(
            "opening_as_of_date",
            NpgsqlDbType.Date,
            openingAsOfDate);
        command.Parameters.AddWithValue(
            "declared_remaining_visits",
            declaredRemainingVisits);
        command.Parameters.AddWithValue(
            "declared_negative_balance",
            declaredNegativeBalance);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("entry_batch_id", Guid.NewGuid());
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();

        await RebuildMembershipAsync(membershipId);
    }

    public async Task<int> CountNegativeMembershipsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select count(*)
            from bodylife.issued_memberships membership
            join bodylife.membership_state_cache cache
                on cache.membership_id = membership.id
            where membership.status = 'active'
                and cache.recalculation_version = @recalculation_version
                and cache.negative_balance > 0
            """;
        command.Parameters.AddWithValue(
            "recalculation_version",
            MembershipStateCacheRebuilder.CurrentRecalculationVersion);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task SeedEndingSoonFreezeAsync(
        Guid recordedByAccountId,
        Guid clientId,
        Guid membershipId,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (startDate == default || endDate < startDate)
        {
            throw new ArgumentException(
                "A valid inclusive ending-soon Freeze range is required.",
                nameof(startDate));
        }

        var sessionId = Guid.NewGuid();
        var recordedAt = TimeProvider.System.GetUtcNow();

        await using var connection = new NpgsqlConnection(ConnectionString);
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
                'UI ending-soon report seed',
                @session_started_at,
                @session_expires_at,
                null,
                @session_last_seen_at);

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
                @freeze_id,
                @client_id,
                @membership_id,
                @start_date,
                @end_date,
                'Ending-soon extension explanation',
                @recorded_at,
                @recorded_at,
                @account_id,
                @session_id,
                'normal',
                null,
                'active')
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", recordedByAccountId);
        command.Parameters.AddWithValue("session_started_at", recordedAt.AddMinutes(-1));
        command.Parameters.AddWithValue("session_expires_at", recordedAt.AddDays(1));
        command.Parameters.AddWithValue("session_last_seen_at", recordedAt);
        command.Parameters.AddWithValue("freeze_id", Guid.NewGuid());
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, startDate);
        command.Parameters.AddWithValue("end_date", NpgsqlDbType.Date, endDate);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        await RebuildMembershipAsync(membershipId);
    }

    public async Task<Guid> InsertExternalCountedVisitAsync(
        Guid clientId,
        Guid membershipId)
    {
        var visitId = Guid.NewGuid();
        var recordedAt = TimeProvider.System.GetUtcNow();

        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            var actor = await ReadLatestActiveSessionAsync(connection);
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
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
                    'Concurrent UI smoke source',
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
            command.Parameters.AddWithValue("client_id", clientId);
            command.Parameters.AddWithValue("membership_id", membershipId);
            command.Parameters.AddWithValue("occurred_at", recordedAt.AddSeconds(-1));
            command.Parameters.AddWithValue("recorded_at", recordedAt);
            command.Parameters.AddWithValue("account_id", actor.AccountId);
            command.Parameters.AddWithValue("session_id", actor.SessionId);
            Assert.Equal(2, await command.ExecuteNonQueryAsync());
            await transaction.CommitAsync();
        }

        await RebuildMembershipAsync(membershipId);
        return visitId;
    }

    public async Task<Guid> InsertActiveFreezeForTodayAsync(
        Guid clientId,
        Guid membershipId)
    {
        var freezeId = Guid.NewGuid();
        var recordedAt = TimeProvider.System.GetUtcNow();
        var visitDate = DateOnly.FromDateTime(recordedAt.UtcDateTime);

        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            var actor = await ReadLatestActiveSessionAsync(connection);
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
                    @start_date,
                    @end_date,
                    'Concurrent UI smoke freeze',
                    @occurred_at,
                    @recorded_at,
                    @account_id,
                    @session_id,
                    'normal',
                    null,
                    'active')
                """;
            command.Parameters.AddWithValue("id", freezeId);
            command.Parameters.AddWithValue("client_id", clientId);
            command.Parameters.AddWithValue("membership_id", membershipId);
            command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, visitDate);
            command.Parameters.AddWithValue("end_date", NpgsqlDbType.Date, visitDate);
            command.Parameters.AddWithValue("occurred_at", recordedAt);
            command.Parameters.AddWithValue("recorded_at", recordedAt);
            command.Parameters.AddWithValue("account_id", actor.AccountId);
            command.Parameters.AddWithValue("session_id", actor.SessionId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await RebuildMembershipAsync(membershipId);
        return freezeId;
    }

    public async Task<long> CountActiveVisitsAsync(Guid clientId, string? visitKind = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = visitKind is null
            ?
            """
            select count(*)
            from bodylife.visits
            where client_id = @client_id
              and status = 'active'
            """
            :
            """
            select count(*)
            from bodylife.visits
            where client_id = @client_id
              and visit_kind = @visit_kind
              and status = 'active'
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        if (visitKind is not null)
        {
            command.Parameters.AddWithValue("visit_kind", visitKind);
        }

        return (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The Visit count query returned no value."));
    }

    public Task<long> CountActiveVisitConsumptionsAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.visit_consumptions
            where client_id = @client_id
              and status = 'active'
            """,
            clientId);
    }

    public Task<long> CountMarkVisitAuditEntriesAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries audit
            inner join bodylife.visits visit on visit.id = audit.entity_id
            where audit.action_type = 'visit.marked'
              and visit.client_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountMarkVisitIdempotencyKeysAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'MarkVisit'
              and reread_target_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountCancelVisitAuditEntriesAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries audit
            inner join bodylife.visits visit on visit.id = audit.entity_id
            where audit.action_type = 'visit.canceled'
              and visit.client_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountCancelVisitIdempotencyKeysAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'CancelVisit'
              and reread_target_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountActivePaymentsAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.payments
            where client_id = @client_id
              and status = 'active'
            """,
            clientId);
    }

    public Task<long> CountActiveFreezesAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.freezes
            where client_id = @client_id
              and status = 'active'
            """,
            clientId);
    }

    public Task<long> CountAddFreezeAuditEntriesAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries audit
            inner join bodylife.freezes freeze_row on freeze_row.id = audit.entity_id
            where audit.action_type = 'freeze.added'
              and freeze_row.client_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountAddFreezeIdempotencyKeysAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'AddFreeze'
              and reread_target_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountFreezeCancellationsAsync(Guid freezeId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.freeze_cancellations
            where freeze_id = @freeze_id
            """,
            "freeze_id",
            freezeId);
    }

    public Task<long> CountCancelFreezeAuditEntriesAsync(Guid freezeId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries
            where action_type = 'freeze.canceled'
              and entity_id = @freeze_id
            """,
            "freeze_id",
            freezeId);
    }

    public Task<long> CountCancelFreezeIdempotencyKeysAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'CancelFreeze'
              and reread_target_id = @client_id
            """,
            clientId);
    }

    public async Task<string> ReadFreezeStatusAsync(Guid freezeId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select status
            from bodylife.freezes
            where id = @freeze_id
            """;
        command.Parameters.AddWithValue("freeze_id", freezeId);

        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The smoke Freeze was not found."));
    }

    public async Task<string> ReadFreezeCancellationReasonAsync(Guid freezeId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select reason
            from bodylife.freeze_cancellations
            where freeze_id = @freeze_id
            order by recorded_at desc, id desc
            limit 1
            """;
        command.Parameters.AddWithValue("freeze_id", freezeId);

        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException(
                "The smoke Freeze cancellation was not found."));
    }

    public async Task<FreezeCancellationAuditSmokeSnapshot>
        ReadCancelFreezeAuditAsync(Guid freezeId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select reason, comment, changed_after_close
            from bodylife.business_audit_entries
            where action_type = 'freeze.canceled'
              and entity_id = @freeze_id
            order by recorded_at desc, id desc
            limit 1
            """;
        command.Parameters.AddWithValue("freeze_id", freezeId);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                "The smoke Freeze cancellation audit entry was not found.");
        }

        return new FreezeCancellationAuditSmokeSnapshot(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetBoolean(2));
    }

    public async Task<FreezeSmokeSnapshot> ReadLatestActiveFreezeAsync(Guid clientId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                membership_id,
                start_date,
                end_date,
                reason,
                status
            from bodylife.freezes
            where client_id = @client_id
              and status = 'active'
            order by recorded_at desc, id desc
            limit 1
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("The active smoke Freeze was not found.");
        }

        return new FreezeSmokeSnapshot(
            reader.GetGuid(0),
            reader.GetFieldValue<DateOnly>(1),
            reader.GetFieldValue<DateOnly>(2),
            reader.GetString(3),
            reader.GetString(4));
    }

    public Task<long> CountIssuedMembershipsAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.issued_memberships
            where client_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountIssueMembershipAuditEntriesAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries audit
            inner join bodylife.issued_memberships membership
                on membership.id = audit.entity_id
            where audit.action_type = 'membership.issued'
              and membership.client_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountIssueMembershipIdempotencyKeysAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'IssueMembership'
              and reread_target_id = @client_id
            """,
            clientId);
    }

    public async Task<IssuedMembershipSmokeSnapshot> ReadLatestIssuedMembershipAsync(
        Guid clientId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                id,
                membership_type_id,
                type_name_snapshot,
                duration_days_snapshot,
                visits_limit_snapshot,
                price_amount_snapshot,
                price_currency_snapshot,
                start_date,
                base_end_date,
                comment,
                status
            from bodylife.issued_memberships
            where client_id = @client_id
            order by issued_at desc, id desc
            limit 1
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("The issued smoke Membership was not found.");
        }

        return new IssuedMembershipSmokeSnapshot(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetDecimal(5),
            reader.GetString(6),
            reader.GetFieldValue<DateOnly>(7),
            reader.GetFieldValue<DateOnly>(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10));
    }

    public Task<long> CountCreatePaymentAuditEntriesAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries audit
            inner join bodylife.payments payment on payment.id = audit.entity_id
            where audit.action_type = 'payment.created'
              and payment.client_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountCreatePaymentIdempotencyKeysAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'CreatePayment'
              and reread_target_id = @client_id
            """,
            clientId);
    }

    public async Task<PaymentSmokeSnapshot> ReadLatestActivePaymentAsync(Guid clientId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                amount,
                currency,
                payment_context,
                membership_id,
                comment,
                status
            from bodylife.payments
            where client_id = @client_id
              and status = 'active'
            order by recorded_at desc, id desc
            limit 1
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("The active smoke Payment was not found.");
        }

        return new PaymentSmokeSnapshot(
            reader.GetDecimal(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5));
    }

    public Task<long> CountPaymentCorrectionsAsync(Guid originalPaymentId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.payment_corrections
            where original_payment_id = @entity_id
            """,
            "entity_id",
            originalPaymentId);
    }

    public Task<long> CountPaymentCancellationsAsync(Guid paymentId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.payment_cancellations
            where payment_id = @entity_id
            """,
            "entity_id",
            paymentId);
    }

    public Task<long> CountCorrectPaymentAuditEntriesAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries audit
            inner join bodylife.payments payment on payment.id = audit.entity_id
            where audit.action_type in ('payment.corrected', 'payment.canceled')
              and payment.client_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountCorrectPaymentIdempotencyKeysAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'CorrectPayment'
              and reread_target_id = @client_id
            """,
            clientId);
    }

    public async Task<MembershipStateSmokeSnapshot> ReadMembershipStateAsync(
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                counted_visits,
                remaining_visits,
                negative_balance,
                first_negative_visit_date,
                effective_end_date
            from bodylife.membership_state_cache
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("The smoke Membership state was not found.");
        }

        return new MembershipStateSmokeSnapshot(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
            reader.GetFieldValue<DateOnly>(4));
    }

    public async Task AdvanceClientUpdatedAtAsync(Guid clientId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.clients
            set updated_at = updated_at + interval '1 hour'
            where id = @client_id
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        var updatedRows = await command.ExecuteNonQueryAsync();

        if (updatedRows != 1)
        {
            throw new InvalidOperationException(
                $"Expected to advance one smoke client, but updated {updatedRows} rows.");
        }
    }

    public Task<long> CountClientUpdateAuditEntriesAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries
            where action_type = 'client.updated'
              and entity_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountUpdateClientIdempotencyKeysAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'UpdateClient'
              and primary_entity_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountDuplicateAcknowledgementsAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.duplicate_warning_acknowledgements
            where client_id = @client_id
            """,
            clientId);
    }

    public async Task<long> CountClientsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from bodylife.clients";
        return (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The client count query returned no value."));
    }

    public Task<Guid?> FindClientIdByCurrentCardAsync(string cardNumber)
    {
        return FindClientIdAsync(
            """
            select client_id
            from bodylife.client_card_assignments
            where card_number_normalized = @value
              and is_current
            """,
            ClientSearchNormalizer.NormalizeCardNumber(cardNumber));
    }

    public Task<Guid?> FindClientIdByPhoneAsync(string phone)
    {
        return FindClientIdAsync(
            """
            select id
            from bodylife.clients
            where phone_normalized = @value
            """,
            ClientSearchNormalizer.NormalizePhone(phone));
    }

    public Task<long> CountClientCreateAuditEntriesAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries
            where action_type = 'client.created'
              and entity_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountCreateClientIdempotencyKeysAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'CreateClient'
              and primary_entity_id = @client_id
            """,
            clientId);
    }

    public async Task ReplaceCurrentCardForStaleTestAsync(
        Guid clientId,
        string newCardNumber)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        Guid currentAssignmentId;
        Guid assignedByAccountId;
        DateTimeOffset assignedAt;

        await using (var readCommand = connection.CreateCommand())
        {
            readCommand.Transaction = transaction;
            readCommand.CommandText =
                """
                select id, assigned_by_account_id, assigned_at
                from bodylife.client_card_assignments
                where client_id = @client_id
                  and is_current
                for update
                """;
            readCommand.Parameters.AddWithValue("client_id", clientId);
            await using var reader = await readCommand.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                throw new InvalidOperationException("The stale-card smoke client has no current assignment.");
            }

            currentAssignmentId = reader.GetGuid(0);
            assignedByAccountId = reader.GetGuid(1);
            assignedAt = new DateTimeOffset(reader.GetDateTime(2));

            if (await reader.ReadAsync())
            {
                throw new InvalidOperationException("The stale-card smoke client has multiple current assignments.");
            }
        }

        var changedAt = assignedAt.AddMinutes(1);
        await using (var endCommand = connection.CreateCommand())
        {
            endCommand.Transaction = transaction;
            endCommand.CommandText =
                """
                update bodylife.client_card_assignments
                set ended_at = @changed_at,
                    ended_by_account_id = @assigned_by_account_id,
                    end_reason = 'UI smoke stale replacement',
                    is_current = false
                where id = @assignment_id
                """;
            endCommand.Parameters.AddWithValue("changed_at", changedAt);
            endCommand.Parameters.AddWithValue("assigned_by_account_id", assignedByAccountId);
            endCommand.Parameters.AddWithValue("assignment_id", currentAssignmentId);

            if (await endCommand.ExecuteNonQueryAsync() != 1)
            {
                throw new InvalidOperationException("The stale-card smoke assignment was not ended.");
            }
        }

        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                insert into bodylife.client_card_assignments (
                    id,
                    client_id,
                    card_number_raw,
                    card_number_normalized,
                    assigned_at,
                    assigned_by_account_id,
                    ended_at,
                    ended_by_account_id,
                    end_reason,
                    is_current)
                values (
                    @id,
                    @client_id,
                    @card_number_raw,
                    @card_number_normalized,
                    @assigned_at,
                    @assigned_by_account_id,
                    null,
                    null,
                    null,
                    true)
                """;
            insertCommand.Parameters.AddWithValue("id", Guid.NewGuid());
            insertCommand.Parameters.AddWithValue("client_id", clientId);
            insertCommand.Parameters.AddWithValue("card_number_raw", newCardNumber);
            insertCommand.Parameters.AddWithValue(
                "card_number_normalized",
                ClientSearchNormalizer.NormalizeCardNumber(newCardNumber));
            insertCommand.Parameters.AddWithValue("assigned_at", changedAt);
            insertCommand.Parameters.AddWithValue("assigned_by_account_id", assignedByAccountId);
            await insertCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public Task<long> CountCardAuditEntriesAsync(Guid clientId, string actionType)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.business_audit_entries
            where action_type = @action_type
              and entity_id = @client_id
            """,
            clientId,
            actionType);
    }

    public Task<long> CountCardCommandIdempotencyKeysAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.command_idempotency_keys
            where command_name = 'AssignOrChangeCard'
              and primary_entity_id = @client_id
            """,
            clientId);
    }

    public Task<long> CountCardAssignmentsAsync(Guid clientId)
    {
        return CountRowsAsync(
            """
            select count(*)
            from bodylife.client_card_assignments
            where client_id = @client_id
            """,
            clientId);
    }

    public async Task<string?> ReadCurrentCardNumberAsync(Guid clientId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select card_number_raw
            from bodylife.client_card_assignments
            where client_id = @client_id
              and is_current
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        return await command.ExecuteScalarAsync() as string;
    }

    private async Task RebuildMembershipAsync(Guid membershipId)
    {
        await using var dbContext = CreateDbContext();
        var rebuild = await new MembershipStateCacheRebuilder(
                dbContext,
                TimeProvider.System,
                extensionSourceProviders:
                [
                    new MembershipFreezeExtensionSourceReader(dbContext),
                    new MembershipNonWorkingDayExtensionSourceReader(dbContext),
                ])
            .RebuildAsync(membershipId);
        Assert.True(
            rebuild.Succeeded,
            $"Membership state rebuild returned {rebuild.Status} for UI smoke fixture {membershipId}.");
    }

    private static async Task<ActiveSessionActor> ReadLatestActiveSessionAsync(
        NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select id, account_id
            from bodylife.sessions
            where ended_at is null
              and expires_at > now()
            order by started_at desc, id desc
            limit 1
            """;
        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new ActiveSessionActor(reader.GetGuid(0), reader.GetGuid(1))
            : throw new InvalidOperationException(
                "An active UI smoke session is required for the external source fixture.");
    }

    public async ValueTask DisposeAsync()
    {
        await ExecuteAdminCommandAsync(
            AdminConnectionStringValue,
            $"DROP DATABASE IF EXISTS {QuoteIdentifier(DatabaseName)} WITH (FORCE)");
    }

    private static async Task ExecuteAdminCommandAsync(string connectionString, string commandText)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountRowsAsync(
        string commandText,
        Guid clientId,
        string? actionType = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("client_id", clientId);

        if (actionType is not null)
        {
            command.Parameters.AddWithValue("action_type", actionType);
        }

        return (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The smoke evidence query returned no value."));
    }

    private async Task<long> CountRowsAsync(string commandText)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The smoke evidence query returned no value."));
    }

    private async Task<long> CountRowsAsync(
        string commandText,
        string parameterName,
        Guid entityId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue(parameterName, entityId);
        return (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The smoke evidence query returned no value."));
    }

    private async Task<Guid?> FindClientIdAsync(string commandText, string value)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("value", value);
        var result = await command.ExecuteScalarAsync();
        return result is Guid clientId ? clientId : null;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record ActiveSessionActor(Guid SessionId, Guid AccountId);

    private sealed record AuditSeed(
        Guid AuditEntryId,
        string ActionType,
        string EntityType,
        Guid EntityId,
        object RelatedEntityRefs,
        Guid ActorAccountId,
        string ActorAccountType,
        string ActorRole,
        Guid SessionId,
        string DeviceLabel,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string EntryOrigin,
        string? Reason,
        string? Comment,
        object BeforeSummary,
        object AfterSummary,
        bool ChangedAfterClose);

    private sealed record AuditPaymentSummarySeed(
        Guid PaymentId,
        Guid ClientId,
        Guid? MembershipId,
        decimal Amount,
        string Currency,
        string Method,
        string PaymentContext,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status);

    private sealed record AuditMembershipTypeSummarySeed(
        string Name,
        int DurationDays,
        int VisitsLimit,
        AuditMoneySummarySeed Price,
        bool IsActive,
        string? Comment,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? DeactivatedAt);

    private sealed record AuditCardAssignmentSummarySeed(
        Guid Id,
        string CardNumber,
        string CardNumberNormalized,
        DateTimeOffset AssignedAt);

    private sealed record AuditNonWorkingDaySourcePeriodSummarySeed(
        Guid PeriodId,
        DateOnly StartDate,
        DateOnly EndDate,
        int InclusiveDays,
        string ReasonCode,
        string? ReasonComment,
        DateTimeOffset CreatedAt,
        Guid CreatedByAccountId,
        Guid SessionId,
        string Status);

    private sealed record AuditNonWorkingDayReplacementPeriodSummarySeed(
        Guid PeriodId,
        DateOnly StartDate,
        DateOnly EndDate,
        int InclusiveDays,
        string ReasonCode,
        string? ReasonComment,
        DateTimeOffset CreatedAt,
        string Status);

    private sealed record AuditNonWorkingDayBeforeApplicationSummarySeed(
        Guid ApplicationId,
        Guid MembershipId,
        Guid ClientId,
        DateOnly StartDate,
        DateOnly EndDate,
        DateTimeOffset PreviewedAt,
        DateTimeOffset ConfirmedAt,
        string Status);

    private sealed record AuditNonWorkingDayReplacementApplicationSummarySeed(
        Guid ApplicationId,
        Guid MembershipId,
        Guid ClientId,
        DateOnly AppliedStartDate,
        DateOnly AppliedEndDate);

    private sealed record AuditFreezeSourceSummarySeed(
        Guid FreezeId,
        Guid ClientId,
        Guid MembershipId,
        DateOnly StartDate,
        DateOnly EndDate,
        int InclusiveDays,
        string Reason,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string EntryOrigin,
        Guid? EntryBatchId,
        string Status);

    private sealed record AuditFreezeMembershipStateSummarySeed(
        Guid MembershipId,
        Guid ClientId,
        int RemainingVisits,
        int NegativeBalance,
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        string[] Warnings);

    private sealed record AuditMoneySummarySeed(
        decimal Amount,
        string Currency);
}

public sealed record MembershipTypeSmokeSnapshot(
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

public sealed record MembershipStateSmokeSnapshot(
    int CountedVisits,
    int RemainingVisits,
    int NegativeBalance,
    DateOnly? FirstNegativeVisitDate,
    DateOnly EffectiveEndDate);

public sealed record IssuedMembershipSmokeSnapshot(
    Guid MembershipId,
    Guid MembershipTypeId,
    string TypeNameSnapshot,
    int DurationDaysSnapshot,
    int VisitsLimitSnapshot,
    decimal PriceAmountSnapshot,
    string PriceCurrencySnapshot,
    DateOnly StartDate,
    DateOnly BaseEndDate,
    string? Comment,
    string Status);

public sealed record PaymentSmokeSnapshot(
    decimal Amount,
    string Currency,
    string PaymentContext,
    Guid? MembershipId,
    string? Comment,
    string Status);

public sealed record FreezeSmokeSnapshot(
    Guid MembershipId,
    DateOnly StartDate,
    DateOnly EndDate,
    string Reason,
    string Status);

public sealed record FreezeCancellationAuditSmokeSnapshot(
    string Reason,
    string? Comment,
    bool ChangedAfterClose);

public sealed record NonWorkingDayMutationCountSmokeSnapshot(
    long PeriodCount,
    long ApplicationCount,
    long CancellationCount,
    long AuditCount,
    long IdempotencyCount);

public sealed record NonWorkingDayCorrectionMutationSmokeSnapshot(
    Guid AuditEntryId,
    string ActionType,
    string OriginalStatus,
    Guid? ReplacementPeriodId,
    string? ReplacementStatus,
    Guid? CancellationId,
    long OriginalApplicationCount,
    long ReplacementApplicationCount,
    string CorrectionReason,
    string CorrectionComment,
    int OldAffectedCount,
    int NewAffectedCount,
    int AffectedUnionCount,
    long IdempotencyCount,
    bool OriginalApplicationsMatchStatus,
    bool ReplacementApplicationsAreActive);

public sealed record NonWorkingDayApplicationSmokeSeed(
    Guid ClientId,
    Guid MembershipId);
