using System.Text.Json;
using BodyLife.Crm.Application.Commands;
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

public sealed class PostgreSqlCreatePaymentCommandTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        16,
        12,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset PaymentOccurredAt = new(
        2026,
        7,
        16,
        9,
        30,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly MembershipStartDate = new(2026, 7, 1);
    private static readonly DateOnly MembershipBaseEndDate = new(2026, 7, 30);

    [PostgreSqlFact]
    public async Task MembershipPaymentCommitsAuditAndIdempotencyWithoutChangingMembershipState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var beforeMembershipState = await ReadMembershipStateAsync(
            database,
            fixture.MembershipId);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                fixture,
                "membership-payment",
                PaymentContext.MembershipSale,
                fixture.MembershipId,
                amount: 1250m),
            CancellationToken.None);

        AssertSuccessfulResult(result, fixture.ClientId);
        Assert.Equal(
            [new EntityId("membership", fixture.MembershipId)],
            result.RelatedEntityIds);
        Assert.Empty(result.Warnings);
        Assert.False(result.ChangedAfterClose);

        var paymentId = result.PrimaryEntityId!.Value.Value;
        var payment = await ReadPaymentAsync(database, paymentId);
        Assert.Equal(fixture.ClientId, payment.ClientId);
        Assert.Equal(fixture.MembershipId, payment.MembershipId);
        Assert.Equal(1250m, payment.Amount);
        Assert.Equal("UAH", payment.Currency);
        Assert.Equal("cash", payment.Method);
        Assert.Equal("membership_sale", payment.PaymentContext);
        Assert.Equal(PaymentOccurredAt, payment.OccurredAt);
        Assert.Equal(TestNow, payment.RecordedAt);
        Assert.Equal(fixture.Actor.AccountId.Value, payment.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId.Value, payment.SessionId);
        Assert.Equal("normal", payment.EntryOrigin);
        Assert.Null(payment.EntryBatchId);
        Assert.Equal("Front desk Payment", payment.Comment);
        Assert.Equal("active", payment.Status);

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(PaymentAuditActions.Created, audit.ActionType);
        Assert.Equal(PaymentAuditActions.EntityType, audit.EntityType);
        Assert.Equal(paymentId, audit.EntityId);
        Assert.Equal(fixture.Actor.AccountId.Value, audit.ActorAccountId);
        Assert.Equal("owner", audit.ActorAccountType);
        Assert.Equal("owner", audit.ActorRole);
        Assert.Equal(fixture.Actor.SessionId.Value, audit.SessionId);
        Assert.Equal("Reception tablet", audit.DeviceLabel);
        Assert.Equal(PaymentOccurredAt, audit.OccurredAt);
        Assert.Equal(TestNow, audit.RecordedAt);
        Assert.Equal("Front desk Payment", audit.Comment);
        Assert.Equal("correlation-membership-payment", audit.RequestCorrelationId);
        Assert.Equal("normal", audit.EntryOrigin);
        Assert.Equal("membership-payment", audit.IdempotencyKey);
        Assert.Equal("{}", audit.BeforeSummary);
        using (var related = JsonDocument.Parse(audit.RelatedEntityRefs))
        {
            Assert.Equal(2, related.RootElement.EnumerateObject().Count());
            Assert.Equal(
                fixture.ClientId,
                related.RootElement.GetProperty("clientId").GetGuid());
            Assert.Equal(
                fixture.MembershipId,
                related.RootElement.GetProperty("membershipId").GetGuid());
        }

        using (var after = JsonDocument.Parse(audit.AfterSummary))
        {
            Assert.Single(after.RootElement.EnumerateObject());
            var summary = after.RootElement.GetProperty("payment");
            Assert.Equal(13, summary.EnumerateObject().Count());
            Assert.Equal(paymentId, summary.GetProperty("paymentId").GetGuid());
            Assert.Equal(
                fixture.ClientId,
                summary.GetProperty("clientId").GetGuid());
            Assert.Equal(
                fixture.MembershipId,
                summary.GetProperty("membershipId").GetGuid());
            Assert.Equal(1250m, summary.GetProperty("amount").GetDecimal());
            Assert.Equal("UAH", summary.GetProperty("currency").GetString());
            Assert.Equal("cash", summary.GetProperty("method").GetString());
            Assert.Equal(
                "membership_sale",
                summary.GetProperty("paymentContext").GetString());
            Assert.Equal(
                PaymentOccurredAt,
                summary.GetProperty("occurredAt").GetDateTimeOffset());
            Assert.Equal(
                TestNow,
                summary.GetProperty("recordedAt").GetDateTimeOffset());
            Assert.Equal("normal", summary.GetProperty("entryOrigin").GetString());
            Assert.Equal(JsonValueKind.Null, summary.GetProperty("entryBatchId").ValueKind);
            Assert.Equal(
                "Front desk Payment",
                summary.GetProperty("comment").GetString());
            Assert.Equal("active", summary.GetProperty("status").GetString());
        }

        var idempotency = await ReadIdempotencyAsync(database, "membership-payment");
        Assert.Equal("CreatePayment", idempotency.CommandName);
        Assert.Equal(fixture.Actor.AccountId.Value, idempotency.AccountId);
        Assert.Equal(fixture.Actor.SessionId.Value, idempotency.SessionId);
        Assert.Equal(paymentId, idempotency.PrimaryEntityId);
        Assert.Equal(fixture.ClientId, idempotency.RereadTargetId);
        Assert.Equal(result.AuditEntryId.Value.Value, idempotency.AuditEntryId);
        Assert.Equal("succeeded", idempotency.Status);
        Assert.False(string.IsNullOrWhiteSpace(idempotency.ResultFingerprint));

        Assert.Equal(
            beforeMembershipState,
            await ReadMembershipStateAsync(database, fixture.MembershipId));
        Assert.Equal(1L, await CountRowsAsync(database, "payments"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
        Assert.Equal(0L, await CountRowsAsync(database, "payment_corrections"));
        Assert.Equal(0L, await CountRowsAsync(database, "payment_cancellations"));
    }

    [PostgreSqlFact]
    public async Task OwnerNamedAndSharedAdminsCreateStandaloneCashContexts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var namedAdmin = await InsertAdminActorAsync(
            database,
            AccountKind.NamedAdmin,
            "Named Admin");
        var sharedAdmin = await InsertAdminActorAsync(
            database,
            AccountKind.SharedReceptionAdmin,
            "Shared Reception");
        var handler = CreateHandler(dbContext);

        var oneOff = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "owner-one-off",
                PaymentContext.OneOff,
                actor: fixture.Actor,
                amount: 200m),
            CancellationToken.None);
        var trial = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "named-trial",
                PaymentContext.Trial,
                actor: namedAdmin,
                amount: 150m),
            CancellationToken.None);
        var other = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "shared-other",
                PaymentContext.Other,
                actor: sharedAdmin,
                amount: 300m),
            CancellationToken.None);

        AssertSuccessfulResult(oneOff, fixture.ClientId);
        AssertSuccessfulResult(trial, fixture.ClientId);
        AssertSuccessfulResult(other, fixture.ClientId);
        Assert.Empty(oneOff.RelatedEntityIds);
        Assert.Empty(trial.RelatedEntityIds);
        Assert.Empty(other.RelatedEntityIds);

        var attribution = await ReadPaymentAttributionAsync(database);
        Assert.Equal(3, attribution.Count);
        Assert.All(attribution, row => Assert.Equal("cash", row.Method));
        Assert.Equal(
            fixture.Actor.AccountId.Value,
            Assert.Single(attribution, row => row.PaymentContext == "one_off")
                .RecordedByAccountId);
        Assert.Equal(
            namedAdmin.AccountId.Value,
            Assert.Single(attribution, row => row.PaymentContext == "trial")
                .RecordedByAccountId);
        Assert.Equal(
            sharedAdmin.AccountId.Value,
            Assert.Single(attribution, row => row.PaymentContext == "other")
                .RecordedByAccountId);
        Assert.Equal(3L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(3L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task PaperFallbackPreservesOccurredRecordedBatchAndAuditMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var entryBatchId = Guid.NewGuid();
        var occurredAt = PaymentOccurredAt.AddDays(-2);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                fixture,
                "paper-fallback",
                PaymentContext.OneOff,
                amount: 250m,
                origin: EntryOrigin.PaperFallback,
                occurredAt: occurredAt,
                reason: "Recovered from signed reception sheet",
                entryBatchId: entryBatchId),
            CancellationToken.None);

        AssertSuccessfulResult(result, fixture.ClientId);
        var payment = await ReadPaymentAsync(
            database,
            result.PrimaryEntityId!.Value.Value);
        Assert.Equal(occurredAt, payment.OccurredAt);
        Assert.Equal(TestNow, payment.RecordedAt);
        Assert.Equal("paper_fallback", payment.EntryOrigin);
        Assert.Equal(entryBatchId, payment.EntryBatchId);

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(occurredAt, audit.OccurredAt);
        Assert.Equal(TestNow, audit.RecordedAt);
        Assert.Equal("paper_fallback", audit.EntryOrigin);
        Assert.Equal("Recovered from signed reception sheet", audit.Reason);
        Assert.Equal("paper-fallback", audit.IdempotencyKey);
    }

    [PostgreSqlFact]
    public async Task IdempotentReplayReturnsOriginalAndChangedPayloadIsDuplicate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);
        var command = CreateCommand(
            fixture,
            "payment-replay",
            PaymentContext.MembershipSale,
            fixture.MembershipId);

        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var changed = await handler.ExecuteAsync(
            command with { Amount = new Money(1100m, "UAH") },
            CancellationToken.None);

        AssertSuccessfulResult(first, fixture.ClientId);
        AssertSuccessfulResult(replay, fixture.ClientId);
        Assert.Equal(first.PrimaryEntityId, replay.PrimaryEntityId);
        Assert.Equal(first.RereadTargetId, replay.RereadTargetId);
        Assert.Equal(first.RelatedEntityIds, replay.RelatedEntityIds);
        Assert.Equal(first.AuditEntryId, replay.AuditEntryId);
        AssertError(changed, CommandErrorCode.DuplicateSubmission, "idempotencyKey");
        Assert.Equal(1L, await CountRowsAsync(database, "payments"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentSameKeySerializesToOneCompleteWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        PaymentFixture fixture;
        await using (var setupContext = database.CreateDbContext())
        {
            await setupContext.Database.MigrateAsync();
            fixture = await SeedFixtureAsync(database, setupContext);
        }

        var command = CreateCommand(
            fixture,
            "concurrent-payment",
            PaymentContext.MembershipSale,
            fixture.MembershipId);
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(command, CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(command, CancellationToken.None));

        Assert.All(results, result => AssertSuccessfulResult(result, fixture.ClientId));
        Assert.Equal(results[0].PrimaryEntityId, results[1].PrimaryEntityId);
        Assert.Equal(results[0].AuditEntryId, results[1].AuditEntryId);
        Assert.Equal(1L, await CountRowsAsync(database, "payments"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InvalidInputsAndReservedNegativeClosureFailWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);
        var valid = CreateCommand(
            fixture,
            "valid-payment",
            PaymentContext.OneOff);

        var emptyClient = await handler.ExecuteAsync(
            valid with { ClientId = Guid.Empty },
            CancellationToken.None);
        var emptyMembership = await handler.ExecuteAsync(
            valid with { MembershipId = Guid.Empty },
            CancellationToken.None);
        var zeroAmount = await handler.ExecuteAsync(
            valid with { Amount = new Money(0m, "UAH") },
            CancellationToken.None);
        var unknownContext = await handler.ExecuteAsync(
            valid with { PaymentContext = (PaymentContext)999 },
            CancellationToken.None);
        var negativeClosure = await handler.ExecuteAsync(
            valid with { PaymentContext = PaymentContext.NegativeClosure },
            CancellationToken.None);
        var missingOccurredAt = await handler.ExecuteAsync(
            valid with
            {
                Envelope = valid.Envelope with { OccurredAt = null },
            },
            CancellationToken.None);
        var unsupportedOccurredAt = await handler.ExecuteAsync(
            valid with
            {
                Envelope = valid.Envelope with { OccurredAt = DateTimeOffset.MaxValue },
            },
            CancellationToken.None);
        var missingIdempotency = await handler.ExecuteAsync(
            valid with
            {
                Envelope = valid.Envelope with { IdempotencyKey = "  " },
            },
            CancellationToken.None);
        var fallbackWithoutContext = await handler.ExecuteAsync(
            valid with
            {
                Envelope = valid.Envelope with
                {
                    EntryOrigin = EntryOrigin.PaperFallback,
                    Reason = null,
                    Comment = null,
                },
            },
            CancellationToken.None);
        var normalWithBatch = await handler.ExecuteAsync(
            valid with { EntryBatchId = Guid.NewGuid() },
            CancellationToken.None);
        var invalidActorShape = await handler.ExecuteAsync(
            valid with
            {
                Envelope = valid.Envelope with
                {
                    Actor = fixture.Actor with
                    {
                        AccountKind = AccountKind.NamedAdmin,
                    },
                },
            },
            CancellationToken.None);

        AssertError(emptyClient, CommandErrorCode.ValidationFailed, "clientId");
        AssertError(emptyMembership, CommandErrorCode.ValidationFailed, "membershipId");
        AssertError(zeroAmount, CommandErrorCode.ValidationFailed, "amount");
        AssertError(unknownContext, CommandErrorCode.ValidationFailed, "paymentContext");
        AssertError(negativeClosure, CommandErrorCode.ValidationFailed, "paymentContext");
        AssertError(missingOccurredAt, CommandErrorCode.ValidationFailed, "occurredAt");
        AssertError(unsupportedOccurredAt, CommandErrorCode.ValidationFailed, "occurredAt");
        AssertError(
            missingIdempotency,
            CommandErrorCode.ValidationFailed,
            "idempotencyKey");
        AssertError(
            fallbackWithoutContext,
            CommandErrorCode.ValidationFailed,
            "entryOrigin");
        AssertError(normalWithBatch, CommandErrorCode.ValidationFailed, "entryBatchId");
        AssertError(invalidActorShape, CommandErrorCode.PermissionDenied);
        await AssertNoPaymentMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task MissingClientMembershipAndEndedSessionFailWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);

        var missingClient = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "missing-client",
                PaymentContext.OneOff,
                clientId: Guid.NewGuid()),
            CancellationToken.None);
        var crossClientMembership = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "cross-client-membership",
                PaymentContext.MembershipSale,
                fixture.OtherMembershipId),
            CancellationToken.None);
        var missingMembership = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "missing-membership",
                PaymentContext.MembershipSale,
                Guid.NewGuid()),
            CancellationToken.None);
        await EndSessionAsync(database, fixture.Actor.SessionId.Value);
        var endedSession = await handler.ExecuteAsync(
            CreateCommand(
                fixture,
                "ended-session",
                PaymentContext.OneOff),
            CancellationToken.None);

        AssertError(missingClient, CommandErrorCode.NotFound, "clientId");
        AssertError(crossClientMembership, CommandErrorCode.NotFound, "membershipId");
        AssertError(missingMembership, CommandErrorCode.NotFound, "membershipId");
        AssertError(endedSession, CommandErrorCode.PermissionDenied);
        await AssertNoPaymentMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task CompetingMembershipLockReturnsConcurrencyConflictWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await using var lockConnection = new NpgsqlConnection(database.ConnectionString);
        await lockConnection.OpenAsync();
        await using var lockTransaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.Transaction = lockTransaction;
            lockCommand.CommandText =
                "select id from bodylife.issued_memberships where id = @id for update";
            lockCommand.Parameters.AddWithValue("id", fixture.MembershipId);
            Assert.Equal(fixture.MembershipId, await lockCommand.ExecuteScalarAsync());
        }

        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("set lock_timeout = '250ms'");
        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                fixture,
                "membership-lock-conflict",
                PaymentContext.MembershipSale,
                fixture.MembershipId),
            CancellationToken.None);

        await lockTransaction.RollbackAsync();
        AssertError(result, CommandErrorCode.ConcurrencyConflict);
        await AssertNoPaymentMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackPaymentAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_payment_created_audit
            check (action_type <> 'payment.created')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext).ExecuteAsync(
                CreateCommand(
                    fixture,
                    "audit-failure",
                    PaymentContext.MembershipSale,
                    fixture.MembershipId),
                CancellationToken.None));

        await AssertNoPaymentMutationAsync(database);
        Assert.Equal(1L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public void PersistenceRegistrationResolvesCreatePaymentHandler()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] =
                    "Host=localhost;Database=bodylife;Username=bodylife;Password=test",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddBodyLifePersistence(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<CreatePaymentCommandHandler>(
            scope.ServiceProvider.GetRequiredService<
                IBodyLifeCommandHandler<CreatePaymentCommand>>());
    }

    private static CreatePaymentCommandHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new CreatePaymentCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new FixedTimeProvider(TestNow));
    }

    private static CreatePaymentCommand CreateCommand(
        PaymentFixture fixture,
        string idempotencyKey,
        PaymentContext paymentContext,
        Guid? membershipId = null,
        decimal amount = 1000m,
        ActorContext? actor = null,
        EntryOrigin origin = EntryOrigin.Normal,
        DateTimeOffset? occurredAt = null,
        string? reason = null,
        Guid? entryBatchId = null,
        Guid? clientId = null)
    {
        return new CreatePaymentCommand(
            new CommandEnvelope(
                actor ?? fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                origin,
                occurredAt ?? PaymentOccurredAt,
                idempotencyKey,
                reason,
                "  Front desk Payment  "),
            clientId ?? fixture.ClientId,
            membershipId,
            new Money(amount, "uah"),
            paymentContext,
            entryBatchId);
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

        var accountId = bootstrap.AccountId!.Value;
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
                    'Client',
                    null,
                    'PAYMENT CLIENT',
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
                    @created_at);

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
                'Create Payment fixture',
                30,
                8,
                1000,
                'UAH',
                true,
                null,
                @created_at,
                @created_at,
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
                    'Create Payment fixture',
                    30,
                    8,
                    1000,
                    'UAH',
                    @start_date,
                    @base_end_date,
                    @issued_at,
                    @account_id,
                    'active',
                    'normal',
                    null,
                    null),
                (
                    @other_membership_id,
                    @other_client_id,
                    @membership_type_id,
                    'Create Payment fixture',
                    30,
                    8,
                    1000,
                    'UAH',
                    @start_date,
                    @base_end_date,
                    @issued_at,
                    @account_id,
                    'active',
                    'normal',
                    null,
                    null);

            insert into bodylife.membership_state_cache (
                membership_id,
                counted_visits,
                remaining_visits,
                negative_balance,
                first_negative_visit_id,
                first_negative_visit_date,
                extension_days,
                effective_end_date,
                last_counted_visit_at,
                recalculated_at,
                recalculation_version)
            values (
                @membership_id,
                10,
                -2,
                2,
                @first_negative_visit_id,
                @first_negative_visit_date,
                0,
                @base_end_date,
                @last_counted_visit_at,
                @recalculated_at,
                4)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("other_client_id", otherClientId);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-30));
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
        command.Parameters.AddWithValue("issued_at", TestNow.AddDays(-15));
        command.Parameters.AddWithValue("first_negative_visit_id", Guid.NewGuid());
        command.Parameters.AddWithValue(
            "first_negative_visit_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 14));
        command.Parameters.AddWithValue(
            "last_counted_visit_at",
            TestNow.AddDays(-2));
        command.Parameters.AddWithValue("recalculated_at", TestNow.AddDays(-1));
        Assert.Equal(7, await command.ExecuteNonQueryAsync());

        var actor = new ActorContext(
            new AccountId(accountId),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(sessionId),
            "  Reception tablet  ");
        return new PaymentFixture(
            actor,
            clientId,
            otherClientId,
            membershipId,
            otherMembershipId);
    }

    private static async Task<ActorContext> InsertAdminActorAsync(
        PostgreSqlTestDatabase database,
        AccountKind accountKind,
        string displayName)
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var accountType = accountKind switch
        {
            AccountKind.NamedAdmin => "named_admin",
            AccountKind.SharedReceptionAdmin => "shared_reception_admin",
            _ => throw new ArgumentOutOfRangeException(nameof(accountKind)),
        };

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
                'admin',
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
                'Reception tablet',
                @started_at,
                @expires_at,
                null,
                @last_seen_at)
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("display_name", displayName);
        command.Parameters.AddWithValue("account_type", accountType);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-1));
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        return new ActorContext(
            new AccountId(accountId),
            ActorRole.Admin,
            accountKind,
            new SessionId(sessionId),
            "Reception tablet");
    }

    private static async Task EndSessionAsync(
        PostgreSqlTestDatabase database,
        Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "update bodylife.sessions set ended_at = @ended_at where id = @id";
        command.Parameters.AddWithValue("ended_at", TestNow);
        command.Parameters.AddWithValue("id", sessionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<PaymentRow> ReadPaymentAsync(
        PostgreSqlTestDatabase database,
        Guid paymentId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select client_id,
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
                   status
            from bodylife.payments
            where id = @id
            """;
        command.Parameters.AddWithValue("id", paymentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new PaymentRow(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetDecimal(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetGuid(8),
            reader.GetGuid(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetGuid(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetString(13));
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
        string idempotencyKey)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select command_name,
                   account_id,
                   session_id,
                   primary_entity_id,
                   reread_target_id,
                   audit_entry_id,
                   status,
                   result_fingerprint
            from bodylife.command_idempotency_keys
            where idempotency_key = @idempotency_key
            """;
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new IdempotencyRow(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetGuid(5),
            reader.GetString(6),
            reader.GetString(7));
    }

    private static async Task<MembershipStateRow> ReadMembershipStateAsync(
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
        return new MembershipStateRow(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetGuid(3),
            reader.GetFieldValue<DateOnly>(4),
            reader.GetInt32(5),
            reader.GetFieldValue<DateOnly>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetInt32(9));
    }

    private static async Task<IReadOnlyList<PaymentAttributionRow>>
        ReadPaymentAttributionAsync(PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select payment_context, method, recorded_by_account_id
            from bodylife.payments
            order by payment_context
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<PaymentAttributionRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new PaymentAttributionRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetGuid(2)));
        }

        return rows;
    }

    private static async Task ExecuteNonQueryAsync(
        PostgreSqlTestDatabase database,
        string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{tableName}");
    }

    private static async Task AssertNoPaymentMutationAsync(
        PostgreSqlTestDatabase database)
    {
        Assert.Equal(0L, await CountRowsAsync(database, "payments"));
        Assert.Equal(0L, await CountRowsAsync(database, "payment_corrections"));
        Assert.Equal(0L, await CountRowsAsync(database, "payment_cancellations"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static void AssertSuccessfulResult(CommandResult result, Guid clientId)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.True(result.PrimaryEntityId.HasValue);
        Assert.Equal("payment", result.PrimaryEntityId.Value.Type);
        Assert.NotEqual(Guid.Empty, result.PrimaryEntityId.Value.Value);
        Assert.Equal(new EntityId("client", clientId), result.RereadTargetId);
        Assert.True(result.AuditEntryId.HasValue);
        Assert.Empty(result.Errors);
    }

    private static void AssertError(
        CommandResult result,
        CommandErrorCode code,
        string? field = null)
    {
        Assert.Equal(CommandStatus.Error, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        if (field is not null)
        {
            Assert.Equal(field, error.Field);
        }

        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
        Assert.Null(result.AuditEntryId);
    }

    private sealed record PaymentFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId,
        Guid OtherMembershipId);

    private sealed record PaymentRow(
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
        string CommandName,
        Guid AccountId,
        Guid SessionId,
        Guid PrimaryEntityId,
        Guid RereadTargetId,
        Guid AuditEntryId,
        string Status,
        string ResultFingerprint);

    private sealed record MembershipStateRow(
        int CountedVisits,
        int RemainingVisits,
        int NegativeBalance,
        Guid FirstNegativeVisitId,
        DateOnly FirstNegativeVisitDate,
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        DateTimeOffset LastCountedVisitAt,
        DateTimeOffset RecalculatedAt,
        int RecalculationVersion);

    private sealed record PaymentAttributionRow(
        string PaymentContext,
        string Method,
        Guid RecordedByAccountId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
