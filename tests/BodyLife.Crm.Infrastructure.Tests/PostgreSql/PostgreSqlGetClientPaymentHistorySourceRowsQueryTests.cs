using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetClientPaymentHistorySourceRowsQueryTests
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(
        JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        22,
        12,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryKeepsCreatedCorrectedAndCanceledSourcesInAuditChronology()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var source = await SeedHistoryAsync(database, fixture);
        var handler = CreateHandler(dbContext);

        var firstResult = await handler.ExecuteAsync(
            new GetClientPaymentHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 2),
            CancellationToken.None);
        var secondResult = await handler.ExecuteAsync(
            new GetClientPaymentHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 2,
                Offset: 2),
            CancellationToken.None);
        var rangedResult = await handler.ExecuteAsync(
            new GetClientPaymentHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                OccurredFromInclusive: TestNow.AddDays(-2).AddHours(-12),
                OccurredBeforeExclusive: TestNow.AddHours(-12)),
            CancellationToken.None);

        var firstPage = AssertSuccess(firstResult, fixture.ClientId);
        Assert.True(firstPage.HasMore);
        Assert.Equal(2, firstPage.NextOffset);
        Assert.Equal(
            [
                ClientPaymentHistorySourceKind.CanceledPayment,
                ClientPaymentHistorySourceKind.CorrectedPayment,
            ],
            firstPage.Items.Select(row => row.Kind));

        var canceledRow = firstPage.Items[0];
        Assert.Equal(source.CanceledPaymentId, canceledRow.PaymentId);
        Assert.Equal(TestNow.AddDays(-1), canceledRow.OccurredAt);
        Assert.Equal(
            TestNow.AddDays(-1).AddMinutes(10),
            canceledRow.RecordedAt);
        Assert.Equal(EntryOrigin.ManualBackfill, canceledRow.EntryOrigin);
        Assert.Null(canceledRow.CreatedPayment);
        Assert.Null(canceledRow.Correction);
        var cancellation = Assert.IsType<PaymentCancellationHistorySource>(
            canceledRow.Cancellation);
        Assert.Equal(source.CancellationId, cancellation.CancellationId);
        Assert.Equal("Duplicate cash entry", cancellation.Reason);
        Assert.Equal(source.CancellationBatchId, cancellation.EntryBatchId);
        Assert.Equal(fixture.Actor.AccountId, cancellation.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId, cancellation.RecordedSessionId);
        Assert.Equal(new Money(300m, "UAH"), cancellation.Payment.Amount);
        Assert.Equal(
            ClientPaymentRowStatus.Canceled,
            cancellation.Payment.CurrentStatus);
        Assert.Equal(
            source.CancellationId,
            cancellation.Payment.CurrentCancellationId);
        Assert.Equal(
            PaymentAuditActions.Canceled,
            canceledRow.AuditEntry.ActionType);
        Assert.True(canceledRow.AuditEntry.ChangedAfterClose);

        var correctedRow = firstPage.Items[1];
        Assert.Equal(source.OriginalPaymentId, correctedRow.PaymentId);
        Assert.Equal(TestNow.AddDays(-2), correctedRow.OccurredAt);
        Assert.Equal(
            TestNow.AddDays(-1).AddHours(-12),
            correctedRow.RecordedAt);
        Assert.Equal(EntryOrigin.PaperFallback, correctedRow.EntryOrigin);
        Assert.Null(correctedRow.CreatedPayment);
        Assert.Null(correctedRow.Cancellation);
        var correction = Assert.IsType<PaymentCorrectionHistorySource>(
            correctedRow.Correction);
        Assert.Equal(source.CorrectionId, correction.CorrectionId);
        Assert.Equal(source.OriginalPaymentId, correction.OriginalPaymentId);
        Assert.Equal(source.ReplacementPaymentId, correction.ReplacementPaymentId);
        Assert.Equal(["amount"], correction.ChangedFields);
        Assert.Equal("Correct cash amount", correction.Reason);
        Assert.Equal(source.CorrectionBatchId, correction.EntryBatchId);
        Assert.Equal(fixture.Actor.AccountId, correction.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId, correction.RecordedSessionId);
        Assert.Equal(new Money(1000m, "UAH"), correction.OriginalPayment.Amount);
        Assert.Equal(
            ClientPaymentRowStatus.Replaced,
            correction.OriginalPayment.CurrentStatus);
        Assert.Equal(
            source.CorrectionId,
            correction.OriginalPayment.OutgoingCorrectionId);
        Assert.Equal(new Money(900m, "UAH"), correction.ReplacementPayment.Amount);
        Assert.Equal(
            ClientPaymentRowStatus.Active,
            correction.ReplacementPayment.CurrentStatus);
        Assert.Equal(
            source.CorrectionId,
            correction.ReplacementPayment.IncomingCorrectionId);
        Assert.Equal(
            TestNow.AddDays(-1).AddHours(-12),
            correction.ReplacementPayment.RecordedAt);
        Assert.Equal(
            EntryOrigin.PaperFallback,
            correction.ReplacementPayment.EntryOrigin);
        Assert.Equal(
            PaymentAuditActions.Corrected,
            correctedRow.AuditEntry.ActionType);
        Assert.Equal("Correct cash amount", correctedRow.AuditEntry.Reason);
        Assert.True(correctedRow.AuditEntry.ChangedAfterClose);

        var secondPage = AssertSuccess(secondResult, fixture.ClientId);
        Assert.False(secondPage.HasMore);
        Assert.Null(secondPage.NextOffset);
        Assert.Equal(
            [source.CanceledPaymentId, source.OriginalPaymentId],
            secondPage.Items.Select(row => row.PaymentId));
        Assert.All(secondPage.Items, row => Assert.Equal(
            ClientPaymentHistorySourceKind.CreatedPayment,
            row.Kind));
        Assert.Equal(
            ClientPaymentRowStatus.Canceled,
            secondPage.Items[0].CreatedPayment!.CurrentStatus);
        Assert.Equal(
            ClientPaymentRowStatus.Replaced,
            secondPage.Items[1].CreatedPayment!.CurrentStatus);
        Assert.Equal("Original cash", secondPage.Items[1].CreatedPayment!.Comment);

        var rangedPage = AssertSuccess(rangedResult, fixture.ClientId);
        Assert.Equal(
            [
                ClientPaymentHistorySourceKind.CanceledPayment,
                ClientPaymentHistorySourceKind.CorrectedPayment,
            ],
            rangedPage.Items.Select(row => row.Kind));
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenAuditHasNoCanonicalPayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            PaymentAuditActions.Created,
            Guid.NewGuid(),
            fixture.ClientId,
            TestNow.AddDays(-1),
            TestNow.AddDays(-1),
            "normal");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientPaymentHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientPaymentHistorySourceRowsStatus.SourceInconsistent);
    }

    [PostgreSqlFact]
    public async Task QueryFailsClosedWhenReplacementEnvelopeDisagreesWithCorrection()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var source = await SeedHistoryAsync(database, fixture);
        await ChangePaymentRecordedAtAsync(
            database,
            source.ReplacementPaymentId,
            TestNow.AddDays(-1).AddHours(-12).AddMinutes(1));

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientPaymentHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            result,
            GetClientPaymentHistorySourceRowsStatus.SourceInconsistent);
    }

    [PostgreSqlFact]
    public async Task ValidationMissingClientAndInactiveActorReturnNoRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);

        var missingId = await handler.ExecuteAsync(
            new GetClientPaymentHistorySourceRowsQuery(
                fixture.Actor,
                Guid.Empty),
            CancellationToken.None);
        var reversedRange = await handler.ExecuteAsync(
            new GetClientPaymentHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                TestNow,
                TestNow),
            CancellationToken.None);
        var invalidLimit = await handler.ExecuteAsync(
            new GetClientPaymentHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: GetClientPaymentHistorySourceRowsQuery.MaxLimit + 1),
            CancellationToken.None);
        var missingClient = await handler.ExecuteAsync(
            new GetClientPaymentHistorySourceRowsQuery(
                fixture.Actor,
                Guid.NewGuid()),
            CancellationToken.None);

        await DeactivateActorAsync(database, fixture.Actor.AccountId.Value);
        var denied = await handler.ExecuteAsync(
            new GetClientPaymentHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);

        AssertFailure(
            missingId,
            GetClientPaymentHistorySourceRowsStatus.ValidationFailed,
            "clientId");
        AssertFailure(
            reversedRange,
            GetClientPaymentHistorySourceRowsStatus.ValidationFailed,
            "occurredBeforeExclusive");
        AssertFailure(
            invalidLimit,
            GetClientPaymentHistorySourceRowsStatus.ValidationFailed,
            "limit");
        AssertFailure(
            missingClient,
            GetClientPaymentHistorySourceRowsStatus.NotFound,
            "clientId");
        AssertFailure(
            denied,
            GetClientPaymentHistorySourceRowsStatus.PermissionDenied);
    }

    [Fact]
    public void PersistenceRegistrationResolvesPaymentHistorySourceQuery()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] =
                    "Host=localhost;Database=bodylife;Username=bodylife;Password=not-used",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBodyLifePersistence(configuration);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(
                    IBodyLifeQueryHandler<
                        GetClientPaymentHistorySourceRowsQuery,
                        GetClientPaymentHistorySourceRowsResult>)
                && descriptor.ImplementationType
                    == typeof(GetClientPaymentHistorySourceRowsQueryHandler)
                && descriptor.Lifetime == ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetClientPaymentHistorySourceRowsQuery,
                GetClientPaymentHistorySourceRowsResult>>());
    }

    private static GetClientPaymentHistorySourceRowsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        return new GetClientPaymentHistorySourceRowsQueryHandler(
            dbContext,
            new GetClientAuditEntriesQueryHandler(dbContext, timeProvider));
    }

    private static async Task<PaymentHistoryFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var bootstrap = await new OwnerBootstrapper(
                dbContext,
                new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var accountId = bootstrap.AccountId!.Value;
        var sessionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();
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
                @account_id,
                'Reception tablet',
                @started_at,
                @expires_at,
                null,
                @last_seen_at);

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
                    'Payment',
                    'History',
                    null,
                    'PAYMENT HISTORY',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @created_at,
                    @account_id,
                    @created_at),
                (
                    @other_client_id,
                    'Other',
                    'Client',
                    null,
                    'OTHER CLIENT',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @created_at,
                    @account_id,
                    @created_at)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-2));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("other_client_id", otherClientId);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-30));
        Assert.Equal(3, await command.ExecuteNonQueryAsync());

        return new PaymentHistoryFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Reception tablet"),
            clientId,
            otherClientId);
    }

    private static async Task<PaymentHistorySourceIds> SeedHistoryAsync(
        PostgreSqlTestDatabase database,
        PaymentHistoryFixture fixture)
    {
        var originalPaymentId = Guid.NewGuid();
        var replacementPaymentId = Guid.NewGuid();
        var canceledPaymentId = Guid.NewGuid();
        var correctionId = Guid.NewGuid();
        var correctionBatchId = Guid.NewGuid();
        var cancellationId = Guid.NewGuid();
        var cancellationBatchId = Guid.NewGuid();
        var correctionRecordedAt = TestNow.AddDays(-1).AddHours(-12);

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
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
                values
                    (
                        @original_payment_id,
                        @client_id,
                        null,
                        1000,
                        'UAH',
                        'cash',
                        'other',
                        @original_occurred_at,
                        @original_recorded_at,
                        @account_id,
                        @session_id,
                        'normal',
                        null,
                        'Original cash',
                        'replaced'),
                    (
                        @replacement_payment_id,
                        @client_id,
                        null,
                        900,
                        'UAH',
                        'cash',
                        'other',
                        @original_occurred_at,
                        @correction_recorded_at,
                        @account_id,
                        @session_id,
                        'paper_fallback',
                        @correction_batch_id,
                        'Corrected amount',
                        'active'),
                    (
                        @canceled_payment_id,
                        @client_id,
                        null,
                        300,
                        'UAH',
                        'cash',
                        'one_off',
                        @canceled_occurred_at,
                        @canceled_recorded_at,
                        @account_id,
                        @session_id,
                        'normal',
                        null,
                        'Duplicate cash',
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
                    @correction_id,
                    @client_id,
                    @original_payment_id,
                    @replacement_payment_id,
                    '["amount"]'::jsonb,
                    'Correct cash amount',
                    @correction_occurred_at,
                    @correction_recorded_at,
                    @account_id,
                    @session_id,
                    'paper_fallback',
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
                    'manual_backfill',
                    @cancellation_batch_id)
                """;
            command.Parameters.AddWithValue("original_payment_id", originalPaymentId);
            command.Parameters.AddWithValue("replacement_payment_id", replacementPaymentId);
            command.Parameters.AddWithValue("canceled_payment_id", canceledPaymentId);
            command.Parameters.AddWithValue("client_id", fixture.ClientId);
            command.Parameters.AddWithValue(
                "original_occurred_at",
                TestNow.AddDays(-4));
            command.Parameters.AddWithValue(
                "original_recorded_at",
                TestNow.AddDays(-4).AddMinutes(5));
            command.Parameters.AddWithValue(
                "canceled_occurred_at",
                TestNow.AddDays(-3));
            command.Parameters.AddWithValue(
                "canceled_recorded_at",
                TestNow.AddDays(-3).AddMinutes(3));
            command.Parameters.AddWithValue(
                "account_id",
                fixture.Actor.AccountId.Value);
            command.Parameters.AddWithValue(
                "session_id",
                fixture.Actor.SessionId.Value);
            command.Parameters.AddWithValue("correction_id", correctionId);
            command.Parameters.AddWithValue(
                "correction_occurred_at",
                TestNow.AddDays(-2));
            command.Parameters.AddWithValue(
                "correction_recorded_at",
                correctionRecordedAt);
            command.Parameters.AddWithValue(
                "correction_batch_id",
                correctionBatchId);
            command.Parameters.AddWithValue("cancellation_id", cancellationId);
            command.Parameters.AddWithValue(
                "cancellation_occurred_at",
                TestNow.AddDays(-1));
            command.Parameters.AddWithValue(
                "cancellation_recorded_at",
                TestNow.AddDays(-1).AddMinutes(10));
            command.Parameters.AddWithValue(
                "cancellation_batch_id",
                cancellationBatchId);
            Assert.Equal(5, await command.ExecuteNonQueryAsync());
        }

        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            PaymentAuditActions.Created,
            originalPaymentId,
            fixture.ClientId,
            TestNow.AddDays(-4),
            TestNow.AddDays(-4).AddMinutes(5),
            "normal",
            comment: "Original cash");
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            PaymentAuditActions.Created,
            canceledPaymentId,
            fixture.ClientId,
            TestNow.AddDays(-3),
            TestNow.AddDays(-3).AddMinutes(3),
            "normal",
            comment: "Duplicate cash");
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            PaymentAuditActions.Corrected,
            originalPaymentId,
            fixture.ClientId,
            TestNow.AddDays(-2),
            correctionRecordedAt,
            "paper_fallback",
            reason: "Correct cash amount",
            changedAfterClose: true);
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            PaymentAuditActions.Canceled,
            canceledPaymentId,
            fixture.ClientId,
            TestNow.AddDays(-1),
            TestNow.AddDays(-1).AddMinutes(10),
            "manual_backfill",
            reason: "Duplicate cash entry",
            changedAfterClose: true);
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            "payment.refunded",
            originalPaymentId,
            fixture.ClientId,
            TestNow.AddMinutes(-20),
            TestNow.AddMinutes(-19),
            "normal");
        await InsertAuditAsync(
            database,
            fixture,
            Guid.NewGuid(),
            PaymentAuditActions.Created,
            Guid.NewGuid(),
            fixture.OtherClientId,
            TestNow.AddMinutes(-10),
            TestNow.AddMinutes(-9),
            "normal");

        return new PaymentHistorySourceIds(
            originalPaymentId,
            replacementPaymentId,
            canceledPaymentId,
            correctionId,
            correctionBatchId,
            cancellationId,
            cancellationBatchId);
    }

    private static async Task InsertAuditAsync(
        PostgreSqlTestDatabase database,
        PaymentHistoryFixture fixture,
        Guid auditId,
        string actionType,
        Guid paymentId,
        Guid clientId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin,
        string? reason = null,
        string? comment = null,
        bool changedAfterClose = false)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
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
                'payment',
                @payment_id,
                @related_entity_refs,
                @actor_account_id,
                'owner',
                'owner',
                @session_id,
                'Reception tablet',
                @occurred_at,
                @recorded_at,
                @reason,
                @comment,
                '{"state":"before"}'::jsonb,
                '{"state":"after"}'::jsonb,
                @request_correlation_id,
                @entry_origin,
                @idempotency_key,
                @changed_after_close)
            """;
        command.Parameters.AddWithValue("id", auditId);
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.AddWithValue("payment_id", paymentId);
        command.Parameters.Add(
            "related_entity_refs",
            NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(
                new { ClientId = clientId },
                AuditJsonOptions);
        command.Parameters.AddWithValue(
            "actor_account_id",
            fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.Add("reason", NpgsqlDbType.Varchar).Value =
            reason ?? (object)DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Varchar).Value =
            comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue(
            "request_correlation_id",
            $"payment-history-{auditId:N}");
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.AddWithValue(
            "idempotency_key",
            $"payment-history-idempotency-{auditId:N}");
        command.Parameters.AddWithValue("changed_after_close", changedAfterClose);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task ChangePaymentRecordedAtAsync(
        PostgreSqlTestDatabase database,
        Guid paymentId,
        DateTimeOffset recordedAt)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.payments
            set recorded_at = @recorded_at
            where id = @payment_id
            """;
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("payment_id", paymentId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeactivateActorAsync(
        PostgreSqlTestDatabase database,
        Guid accountId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.accounts
            set is_active = false,
                deactivated_at = @deactivated_at
            where id = @id
            """;
        command.Parameters.AddWithValue("deactivated_at", TestNow);
        command.Parameters.AddWithValue("id", accountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static ClientPaymentHistorySourceRowsPage AssertSuccess(
        GetClientPaymentHistorySourceRowsResult result,
        Guid clientId)
    {
        Assert.Equal(GetClientPaymentHistorySourceRowsStatus.Success, result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        var page = Assert.IsType<ClientPaymentHistorySourceRowsPage>(result.Page);
        Assert.Equal(clientId, page.ClientId);
        return page;
    }

    private static void AssertFailure(
        GetClientPaymentHistorySourceRowsResult result,
        GetClientPaymentHistorySourceRowsStatus status,
        string? field = null)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Page);
        Assert.NotNull(result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(field, result.ErrorField);
    }

    private sealed record PaymentHistoryFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId);

    private sealed record PaymentHistorySourceIds(
        Guid OriginalPaymentId,
        Guid ReplacementPaymentId,
        Guid CanceledPaymentId,
        Guid CorrectionId,
        Guid CorrectionBatchId,
        Guid CancellationId,
        Guid CancellationBatchId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
