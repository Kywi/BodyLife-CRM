using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Ui.SmokeTests;

internal sealed class PostgreSqlSmokeDatabase : IAsyncDisposable
{
    private const string AdminConnectionStringEnvironmentVariable = "BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING";

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

    public async Task<Guid> SeedIssuedMembershipAsync(
        Guid issuedByAccountId,
        Guid clientId,
        Guid membershipTypeId,
        string typeNameSnapshot,
        int visitsLimitSnapshot)
    {
        var membershipId = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        var startDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-7);
        const int durationDays = 30;

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
            command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, startDate);
            command.Parameters.AddWithValue(
                "base_end_date",
                NpgsqlDbType.Date,
                startDate.AddDays(durationDays - 1));
            command.Parameters.AddWithValue("issued_at", now.AddDays(-7));
            command.Parameters.AddWithValue("issued_by_account_id", issuedByAccountId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await RebuildMembershipAsync(membershipId);
        return membershipId;
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

    public async Task CancelVisitAsync(Guid visitId, string reason)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sessionActor = await ReadLatestActiveSessionAsync(connection);
        var actor = new ActorContext(
            new AccountId(sessionActor.AccountId),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(sessionActor.SessionId),
            "UI smoke Visit history cancellation");
        var now = TimeProvider.System.GetUtcNow();
        var timeProvider = new FixedTimeProvider(now);

        await using var dbContext = CreateDbContext();
        var result = await new CancelVisitCommandHandler(
                dbContext,
                new BusinessAuditAppender(dbContext),
                new CancelVisitSourcePreparer(dbContext),
                new MembershipStateRecalculator(
                    new MembershipStateCacheRebuilder(dbContext, timeProvider)),
                new GetMembershipStateQueryHandler(dbContext, timeProvider),
                new OpenVisitDayReconciliationStatusProvider(),
                timeProvider)
            .ExecuteAsync(
                new CancelVisitCommand(
                    new CommandEnvelope(
                        actor,
                        new RequestCorrelationId($"ui-smoke-cancel-{visitId:N}"),
                        EntryOrigin.Normal,
                        now,
                        $"ui-smoke-cancel-{visitId:N}",
                        reason,
                        "Canceled to verify canonical reception history."),
                    visitId),
                CancellationToken.None);

        Assert.Equal(CommandStatus.Success, result.Status);
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
                TimeProvider.System)
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }

    private sealed class OpenVisitDayReconciliationStatusProvider
        : IVisitDayReconciliationStatusProvider
    {
        public Task<VisitDayReconciliationStatus> GetStatusAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(VisitDayReconciliationStatus.Open);
        }
    }
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
