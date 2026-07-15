using System.Text.Json;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlPaymentsStorageTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        15,
        18,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset OriginalOccurredAt = new(
        2026,
        7,
        14,
        10,
        30,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset ReplacementOccurredAt = new(
        2026,
        7,
        15,
        11,
        15,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly MembershipStartDate = new(2026, 7, 1);
    private static readonly DateOnly MembershipBaseEndDate = new(2026, 7, 30);

    [PostgreSqlFact]
    public async Task MigrationCreatesCanonicalPaymentTablesConstraintsAndReportIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        Assert.Equal(
            [
                "id",
                "client_id",
                "membership_id",
                "amount",
                "currency",
                "method",
                "payment_context",
                "occurred_at",
                "recorded_at",
                "recorded_by_account_id",
                "session_id",
                "entry_origin",
                "entry_batch_id",
                "comment",
                "status",
            ],
            await ReadColumnNamesAsync(database, "payments"));
        Assert.Equal(
            [
                "id",
                "payment_id",
                "reason",
                "occurred_at",
                "recorded_at",
                "recorded_by_account_id",
                "session_id",
                "entry_origin",
                "entry_batch_id",
            ],
            await ReadColumnNamesAsync(database, "payment_cancellations"));
        Assert.Equal(
            [
                "id",
                "client_id",
                "original_payment_id",
                "replacement_payment_id",
                "changed_fields",
                "reason",
                "occurred_at",
                "recorded_at",
                "recorded_by_account_id",
                "session_id",
                "entry_origin",
                "entry_batch_id",
            ],
            await ReadColumnNamesAsync(database, "payment_corrections"));

        string[] expectedConstraints =
        [
            "AK_payments_id_client_id",
            "FK_payment_corrections_payments_original_client",
            "FK_payment_corrections_payments_replacement_client",
            "FK_payments_issued_memberships_membership_client",
            "ck_payment_cancellations_entry_origin",
            "ck_payment_cancellations_reason_not_empty",
            "ck_payment_corrections_changed_fields",
            "ck_payment_corrections_distinct_payments",
            "ck_payment_corrections_entry_origin",
            "ck_payment_corrections_reason_not_empty",
            "ck_payments_amount_positive",
            "ck_payments_comment_not_empty",
            "ck_payments_currency_canonical",
            "ck_payments_entry_origin",
            "ck_payments_method",
            "ck_payments_payment_context",
            "ck_payments_status",
        ];
        foreach (var constraint in expectedConstraints)
        {
            Assert.True(
                await ConstraintExistsAsync(database, constraint),
                $"Expected constraint '{constraint}' was not found.");
        }

        var activeDailyIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_payments_active_daily_report");
        Assert.Contains(
            "(occurred_at, method, client_id)",
            activeDailyIndex,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INCLUDE (amount)", activeDailyIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", activeDailyIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active", activeDailyIndex, StringComparison.OrdinalIgnoreCase);

        var dailySourceIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_payments_daily_source");
        Assert.Contains(
            "(occurred_at, status, method, client_id)",
            dailySourceIndex,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INCLUDE (amount)", dailySourceIndex, StringComparison.OrdinalIgnoreCase);

        var membershipTimelineIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_payments_membership_timeline");
        Assert.Contains(
            "(membership_id, client_id, occurred_at DESC)",
            membershipTimelineIndex,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", membershipTimelineIndex, StringComparison.OrdinalIgnoreCase);

        await AssertUniqueIndexAsync(
            database,
            "ux_payment_cancellations_payment_id",
            "(payment_id)");
        await AssertUniqueIndexAsync(
            database,
            "ux_payment_corrections_original_payment_id",
            "(original_payment_id, client_id)");
        await AssertUniqueIndexAsync(
            database,
            "ux_payment_corrections_replacement_payment_id",
            "(replacement_payment_id, client_id)");
    }

    [PostgreSqlFact]
    public async Task ReplacementFactsPreserveAccountabilityAndDriveCanonicalDailyCash()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var entryBatchId = Guid.NewGuid();

        var originalPaymentId = await InsertPaymentAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            amount: 1000m,
            occurredAt: OriginalOccurredAt,
            entryOrigin: "paper_fallback",
            entryBatchId: entryBatchId,
            comment: "Recovered from the reception paper sheet",
            status: "replaced");
        var replacementPaymentId = await InsertPaymentAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            amount: 1200m,
            occurredAt: ReplacementOccurredAt,
            recordedAt: TestNow.AddMinutes(1));
        await InsertPaymentAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            membershipId: null,
            amount: 300m,
            paymentContext: "one_off",
            occurredAt: ReplacementOccurredAt.AddMinutes(15),
            recordedAt: TestNow.AddMinutes(2));
        var correctionId = await InsertCorrectionAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            originalPaymentId,
            replacementPaymentId,
            changedFieldsJson: "[\"amount\",\"occurred_at\"]",
            reason: "Corrected amount and business date",
            entryOrigin: "manual_backfill",
            entryBatchId: entryBatchId,
            recordedAt: TestNow.AddMinutes(3));

        var original = await ReadPaymentAsync(
            database.ConnectionString,
            originalPaymentId);
        Assert.Equal(fixture.ClientId, original.ClientId);
        Assert.Equal(fixture.MembershipId, original.MembershipId);
        Assert.Equal(1000m, original.Amount);
        Assert.Equal("UAH", original.Currency);
        Assert.Equal("cash", original.Method);
        Assert.Equal("membership_sale", original.PaymentContext);
        Assert.Equal(OriginalOccurredAt, original.OccurredAt);
        Assert.Equal("paper_fallback", original.EntryOrigin);
        Assert.Equal(entryBatchId, original.EntryBatchId);
        Assert.Equal("replaced", original.Status);

        var replacement = await ReadPaymentAsync(
            database.ConnectionString,
            replacementPaymentId);
        Assert.Equal(1200m, replacement.Amount);
        Assert.Equal(ReplacementOccurredAt, replacement.OccurredAt);
        Assert.Equal("active", replacement.Status);
        Assert.Equal(fixture.ActorAccountId, replacement.RecordedByAccountId);
        Assert.Equal(fixture.SessionId, replacement.SessionId);

        var correction = await ReadCorrectionAsync(
            database.ConnectionString,
            correctionId);
        Assert.Equal(fixture.ClientId, correction.ClientId);
        Assert.Equal(originalPaymentId, correction.OriginalPaymentId);
        Assert.Equal(replacementPaymentId, correction.ReplacementPaymentId);
        Assert.Equal(["amount", "occurred_at"], correction.ChangedFields);
        Assert.Equal("Corrected amount and business date", correction.Reason);
        Assert.Equal("manual_backfill", correction.EntryOrigin);
        Assert.Equal(entryBatchId, correction.EntryBatchId);

        var originalDay = await ReadDailyCashAsync(
            database.ConnectionString,
            new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero));
        var replacementDay = await ReadDailyCashAsync(
            database.ConnectionString,
            new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DailyCashSnapshot(0, 0m), originalDay);
        Assert.Equal(new DailyCashSnapshot(2, 1500m), replacementDay);
        Assert.Equal(3L, await CountRowsAsync(database, "payments"));
        Assert.Equal(1L, await CountRowsAsync(database, "payment_corrections"));
    }

    [PostgreSqlFact]
    public async Task PaymentConstraintsRejectInvalidCashFactsAndCrossClientMembership()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);

        await AssertPostgresViolationAsync(
            () => InsertPaymentAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                fixture.MembershipId,
                amount: 0m),
            PostgresErrorCodes.CheckViolation,
            "ck_payments_amount_positive");
        await AssertPostgresViolationAsync(
            () => InsertPaymentAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                fixture.MembershipId,
                method: "card"),
            PostgresErrorCodes.CheckViolation,
            "ck_payments_method");
        await AssertPostgresViolationAsync(
            () => InsertPaymentAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                fixture.MembershipId,
                paymentContext: "subscription"),
            PostgresErrorCodes.CheckViolation,
            "ck_payments_payment_context");
        await AssertPostgresViolationAsync(
            () => InsertPaymentAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                fixture.MembershipId,
                currency: "uah"),
            PostgresErrorCodes.CheckViolation,
            "ck_payments_currency_canonical");
        await AssertPostgresViolationAsync(
            () => InsertPaymentAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                fixture.MembershipId,
                entryOrigin: "spreadsheet"),
            PostgresErrorCodes.CheckViolation,
            "ck_payments_entry_origin");
        await AssertPostgresViolationAsync(
            () => InsertPaymentAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                fixture.MembershipId,
                comment: "   "),
            PostgresErrorCodes.CheckViolation,
            "ck_payments_comment_not_empty");
        await AssertPostgresViolationAsync(
            () => InsertPaymentAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                fixture.MembershipId,
                status: "deleted"),
            PostgresErrorCodes.CheckViolation,
            "ck_payments_status");
        await AssertPostgresViolationAsync(
            () => InsertPaymentAsync(
                database.ConnectionString,
                fixture,
                fixture.OtherClientId,
                fixture.MembershipId),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_payments_issued_memberships_membership_client");

        await InsertPaymentAsync(
            database.ConnectionString,
            fixture,
            fixture.OtherClientId,
            membershipId: null,
            paymentContext: "trial");

        Assert.Equal(1L, await CountRowsAsync(database, "payments"));
    }

    [PostgreSqlFact]
    public async Task CorrectionAndCancellationFactsRequireExplainableRelationships()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);

        var originalPaymentId = await InsertPaymentAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            status: "replaced");
        var replacementPaymentId = await InsertPaymentAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            amount: 1100m,
            recordedAt: TestNow.AddMinutes(1));
        var otherClientPaymentId = await InsertPaymentAsync(
            database.ConnectionString,
            fixture,
            fixture.OtherClientId,
            fixture.OtherMembershipId,
            recordedAt: TestNow.AddMinutes(2));

        await AssertPostgresViolationAsync(
            () => InsertCorrectionAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                originalPaymentId,
                originalPaymentId),
            PostgresErrorCodes.CheckViolation,
            "ck_payment_corrections_distinct_payments");
        await AssertPostgresViolationAsync(
            () => InsertCorrectionAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                originalPaymentId,
                replacementPaymentId,
                changedFieldsJson: "[]"),
            PostgresErrorCodes.CheckViolation,
            "ck_payment_corrections_changed_fields");
        await AssertPostgresViolationAsync(
            () => InsertCorrectionAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                originalPaymentId,
                replacementPaymentId,
                reason: "  "),
            PostgresErrorCodes.CheckViolation,
            "ck_payment_corrections_reason_not_empty");
        await AssertPostgresViolationAsync(
            () => InsertCorrectionAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                originalPaymentId,
                replacementPaymentId,
                entryOrigin: "spreadsheet"),
            PostgresErrorCodes.CheckViolation,
            "ck_payment_corrections_entry_origin");
        await AssertPostgresViolationAsync(
            () => InsertCorrectionAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                originalPaymentId,
                otherClientPaymentId),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_payment_corrections_payments_replacement_client");

        await InsertCorrectionAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            originalPaymentId,
            replacementPaymentId);

        var secondReplacementId = await InsertPaymentAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            amount: 1200m,
            recordedAt: TestNow.AddMinutes(3));
        await AssertPostgresViolationAsync(
            () => InsertCorrectionAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                originalPaymentId,
                secondReplacementId),
            PostgresErrorCodes.UniqueViolation,
            "ux_payment_corrections_original_payment_id");

        var secondOriginalId = await InsertPaymentAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            amount: 900m,
            recordedAt: TestNow.AddMinutes(4),
            status: "replaced");
        await AssertPostgresViolationAsync(
            () => InsertCorrectionAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                secondOriginalId,
                replacementPaymentId),
            PostgresErrorCodes.UniqueViolation,
            "ux_payment_corrections_replacement_payment_id");

        var canceledPaymentId = await InsertPaymentAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            membershipId: null,
            paymentContext: "other",
            recordedAt: TestNow.AddMinutes(5),
            status: "canceled");
        await InsertCancellationAsync(
            database.ConnectionString,
            fixture,
            canceledPaymentId);
        await AssertPostgresViolationAsync(
            () => InsertCancellationAsync(
                database.ConnectionString,
                fixture,
                canceledPaymentId,
                recordedAt: TestNow.AddMinutes(7)),
            PostgresErrorCodes.UniqueViolation,
            "ux_payment_cancellations_payment_id");

        var uncanceledPaymentId = await InsertPaymentAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            membershipId: null,
            paymentContext: "other",
            recordedAt: TestNow.AddMinutes(8));
        await AssertPostgresViolationAsync(
            () => InsertCancellationAsync(
                database.ConnectionString,
                fixture,
                uncanceledPaymentId,
                reason: "  "),
            PostgresErrorCodes.CheckViolation,
            "ck_payment_cancellations_reason_not_empty");
        await AssertPostgresViolationAsync(
            () => InsertCancellationAsync(
                database.ConnectionString,
                fixture,
                uncanceledPaymentId,
                entryOrigin: "spreadsheet"),
            PostgresErrorCodes.CheckViolation,
            "ck_payment_cancellations_entry_origin");

        await AssertPostgresViolationAsync(
            () => DeletePaymentAsync(
                database.ConnectionString,
                originalPaymentId),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_payment_corrections_payments_original_client");
        await AssertPostgresViolationAsync(
            () => DeletePaymentAsync(
                database.ConnectionString,
                canceledPaymentId),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_payment_cancellations_payments_payment_id");
    }

    private static async Task<PaymentFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var bootstrap = await new OwnerBootstrapper(
            dbContext,
            new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var actorAccountId = bootstrap.AccountId!.Value;
        var sessionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var otherMembershipId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
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
                @actor_account_id,
                'Reception tablet',
                @session_started_at,
                @session_expires_at,
                null,
                @recorded_at);

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
            values
                (
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
                    @actor_account_id,
                    @recorded_at),
                (
                    @other_client_id,
                    'Petrenko',
                    'Olena',
                    null,
                    'PETRENKO OLENA',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @recorded_at,
                    @actor_account_id,
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
                'Payment storage fixture',
                30,
                8,
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
            values
                (
                    @membership_id,
                    @client_id,
                    @membership_type_id,
                    'Payment storage fixture',
                    30,
                    8,
                    1000,
                    'UAH',
                    @start_date,
                    @base_end_date,
                    @recorded_at,
                    @actor_account_id,
                    'active',
                    'normal',
                    null,
                    null),
                (
                    @other_membership_id,
                    @other_client_id,
                    @membership_type_id,
                    'Payment storage fixture',
                    30,
                    8,
                    1000,
                    'UAH',
                    @start_date,
                    @base_end_date,
                    @recorded_at,
                    @actor_account_id,
                    'active',
                    'normal',
                    null,
                    null)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("actor_account_id", actorAccountId);
        command.Parameters.AddWithValue("session_started_at", TestNow.AddMinutes(-1));
        command.Parameters.AddWithValue("session_expires_at", TestNow.AddHours(12));
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("other_client_id", otherClientId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("other_membership_id", otherMembershipId);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            MembershipStartDate);
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            MembershipBaseEndDate);
        Assert.Equal(6, await command.ExecuteNonQueryAsync());

        return new PaymentFixture(
            actorAccountId,
            sessionId,
            clientId,
            otherClientId,
            membershipId,
            otherMembershipId);
    }

    private static async Task<Guid> InsertPaymentAsync(
        string connectionString,
        PaymentFixture fixture,
        Guid clientId,
        Guid? membershipId,
        decimal amount = 1000m,
        string currency = "UAH",
        string method = "cash",
        string paymentContext = "membership_sale",
        DateTimeOffset? occurredAt = null,
        DateTimeOffset? recordedAt = null,
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        string? comment = null,
        string status = "active")
    {
        var paymentId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
            values (
                @id,
                @client_id,
                @membership_id,
                @amount,
                @currency,
                @method,
                @payment_context,
                @occurred_at,
                @recorded_at,
                @recorded_by_account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id,
                @comment,
                @status)
            """;
        command.Parameters.AddWithValue("id", paymentId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.Add("membership_id", NpgsqlDbType.Uuid).Value =
            membershipId ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("currency", currency);
        command.Parameters.AddWithValue("method", method);
        command.Parameters.AddWithValue("payment_context", paymentContext);
        command.Parameters.AddWithValue("occurred_at", occurredAt ?? OriginalOccurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt ?? TestNow);
        command.Parameters.AddWithValue(
            "recorded_by_account_id",
            fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value =
            comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return paymentId;
    }

    private static async Task<Guid> InsertCorrectionAsync(
        string connectionString,
        PaymentFixture fixture,
        Guid clientId,
        Guid originalPaymentId,
        Guid replacementPaymentId,
        string changedFieldsJson = "[\"amount\"]",
        string reason = "Corrected payment",
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        DateTimeOffset? recordedAt = null)
    {
        var correctionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                @id,
                @client_id,
                @original_payment_id,
                @replacement_payment_id,
                @changed_fields,
                @reason,
                @occurred_at,
                @recorded_at,
                @recorded_by_account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id)
            """;
        command.Parameters.AddWithValue("id", correctionId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("original_payment_id", originalPaymentId);
        command.Parameters.AddWithValue("replacement_payment_id", replacementPaymentId);
        command.Parameters.AddWithValue(
            "changed_fields",
            NpgsqlDbType.Jsonb,
            changedFieldsJson);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("occurred_at", TestNow.AddMinutes(5));
        command.Parameters.AddWithValue("recorded_at", recordedAt ?? TestNow.AddMinutes(6));
        command.Parameters.AddWithValue(
            "recorded_by_account_id",
            fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return correctionId;
    }

    private static async Task<Guid> InsertCancellationAsync(
        string connectionString,
        PaymentFixture fixture,
        Guid paymentId,
        string reason = "Mistaken payment",
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        DateTimeOffset? recordedAt = null)
    {
        var cancellationId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                @id,
                @payment_id,
                @reason,
                @occurred_at,
                @recorded_at,
                @recorded_by_account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id)
            """;
        command.Parameters.AddWithValue("id", cancellationId);
        command.Parameters.AddWithValue("payment_id", paymentId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("occurred_at", TestNow.AddMinutes(5));
        command.Parameters.AddWithValue("recorded_at", recordedAt ?? TestNow.AddMinutes(6));
        command.Parameters.AddWithValue(
            "recorded_by_account_id",
            fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return cancellationId;
    }

    private static async Task<PaymentSnapshot> ReadPaymentAsync(
        string connectionString,
        Guid paymentId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                client_id,
                membership_id,
                amount,
                currency,
                method,
                payment_context,
                occurred_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id,
                status
            from bodylife.payments
            where id = @id
            """;
        command.Parameters.AddWithValue("id", paymentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new PaymentSnapshot(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetDecimal(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetGuid(7),
            reader.GetGuid(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            reader.GetString(11));
    }

    private static async Task<PaymentCorrectionSnapshot> ReadCorrectionAsync(
        string connectionString,
        Guid correctionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                client_id,
                original_payment_id,
                replacement_payment_id,
                changed_fields::text,
                reason,
                entry_origin,
                entry_batch_id
            from bodylife.payment_corrections
            where id = @id
            """;
        command.Parameters.AddWithValue("id", correctionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new PaymentCorrectionSnapshot(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            JsonSerializer.Deserialize<string[]>(reader.GetString(3))!,
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6));
    }

    private static async Task<DailyCashSnapshot> ReadDailyCashAsync(
        string connectionString,
        DateTimeOffset dayStart)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select count(*), coalesce(sum(amount), 0)
            from bodylife.payments
            where occurred_at >= @day_start
                and occurred_at < @day_end
                and status = 'active'
                and method = 'cash'
            """;
        command.Parameters.AddWithValue("day_start", dayStart);
        command.Parameters.AddWithValue("day_end", dayStart.AddDays(1));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new DailyCashSnapshot(reader.GetInt64(0), reader.GetDecimal(1));
    }

    private static async Task<IReadOnlyList<string>> ReadColumnNamesAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select column_name
            from information_schema.columns
            where table_schema = 'bodylife'
                and table_name = @table_name
            order by ordinal_position
            """;
        command.Parameters.AddWithValue("table_name", tableName);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<bool> ConstraintExistsAsync(
        PostgreSqlTestDatabase database,
        string constraintName)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select exists (
                select 1
                from pg_constraint
                where conname = @constraint_name)
            """;
        command.Parameters.AddWithValue("constraint_name", constraintName);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadIndexDefinitionAsync(
        PostgreSqlTestDatabase database,
        string indexName)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select indexdef
            from pg_indexes
            where schemaname = 'bodylife'
                and indexname = @index_name
            """;
        command.Parameters.AddWithValue("index_name", indexName);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertUniqueIndexAsync(
        PostgreSqlTestDatabase database,
        string indexName,
        string expectedColumns)
    {
        var definition = await ReadIndexDefinitionAsync(database, indexName);
        Assert.Contains("UNIQUE INDEX", definition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedColumns, definition, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return (await database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{tableName}"))!;
    }

    private static async Task DeletePaymentAsync(
        string connectionString,
        Guid paymentId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "delete from bodylife.payments where id = @id";
        command.Parameters.AddWithValue("id", paymentId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertPostgresViolationAsync(
        Func<Task> action,
        string sqlState,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(sqlState, exception.SqlState);
        Assert.Equal(constraintName, exception.ConstraintName);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record PaymentFixture(
        Guid ActorAccountId,
        Guid SessionId,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId,
        Guid OtherMembershipId);

    private sealed record PaymentSnapshot(
        Guid ClientId,
        Guid? MembershipId,
        decimal Amount,
        string Currency,
        string Method,
        string PaymentContext,
        DateTimeOffset OccurredAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId,
        string Status);

    private sealed record PaymentCorrectionSnapshot(
        Guid ClientId,
        Guid OriginalPaymentId,
        Guid ReplacementPaymentId,
        string[] ChangedFields,
        string Reason,
        string EntryOrigin,
        Guid? EntryBatchId);

    private sealed record DailyCashSnapshot(long PaymentCount, decimal CashSum);
}
