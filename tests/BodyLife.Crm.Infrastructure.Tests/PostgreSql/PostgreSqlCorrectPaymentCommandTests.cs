using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
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

public sealed class PostgreSqlCorrectPaymentCommandTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        18,
        12,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset OriginalOccurredAt = new(
        2026,
        7,
        16,
        9,
        30,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset ReplacementOccurredAt = new(
        2026,
        7,
        17,
        10,
        15,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset CorrectionOccurredAt = new(
        2026,
        7,
        18,
        11,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task ReplaceCommitsSourceFactsAuditAndIdempotencyWithOldAndNewDates()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var dayStatusProvider = new StubPaymentDayReconciliationStatusProvider();

        var result = await CreateHandler(dbContext, dayStatusProvider).ExecuteAsync(
            CreateReplaceCommand(fixture, "replace-payment"),
            CancellationToken.None);

        AssertSuccessfulResult(
            result,
            CorrectPaymentCommand.CorrectionEntityType,
            fixture.ClientId);
        Assert.False(result.ChangedAfterClose);
        Assert.Equal(
            [
                new EntityId(CorrectPaymentCommand.PaymentEntityType, fixture.OriginalPaymentId),
                new EntityId(
                    CorrectPaymentCommand.PaymentEntityType,
                    result.RelatedEntityIds[1].Value),
            ],
            result.RelatedEntityIds);
        Assert.Equal(
            [
                DateOnly.FromDateTime(OriginalOccurredAt.UtcDateTime),
                DateOnly.FromDateTime(ReplacementOccurredAt.UtcDateTime),
            ],
            dayStatusProvider.RequestedDates.Order().ToArray());

        var payments = await ReadPaymentsAsync(database);
        Assert.Equal(2, payments.Count);
        var original = Assert.Single(
            payments,
            payment => payment.Id == fixture.OriginalPaymentId);
        var replacement = Assert.Single(
            payments,
            payment => payment.Id != fixture.OriginalPaymentId);
        Assert.Equal("replaced", original.Status);
        Assert.Equal(500m, original.Amount);
        Assert.Equal(OriginalOccurredAt, original.OccurredAt);
        Assert.Equal("Original receipt", original.Comment);
        Assert.Equal(fixture.ClientId, replacement.ClientId);
        Assert.Equal(fixture.MembershipId, replacement.MembershipId);
        Assert.Equal(650m, replacement.Amount);
        Assert.Equal("UAH", replacement.Currency);
        Assert.Equal("cash", replacement.Method);
        Assert.Equal("membership_sale", replacement.PaymentContext);
        Assert.Equal(ReplacementOccurredAt, replacement.OccurredAt);
        Assert.Equal(TestNow, replacement.RecordedAt);
        Assert.Equal(fixture.Owner.AccountId.Value, replacement.RecordedByAccountId);
        Assert.Equal(fixture.Owner.SessionId.Value, replacement.SessionId);
        Assert.Equal("normal", replacement.EntryOrigin);
        Assert.Null(replacement.EntryBatchId);
        Assert.Equal("Corrected receipt", replacement.Comment);
        Assert.Equal("active", replacement.Status);

        var correction = await ReadCorrectionAsync(database);
        Assert.Equal(result.PrimaryEntityId!.Value.Value, correction.Id);
        Assert.Equal(fixture.ClientId, correction.ClientId);
        Assert.Equal(fixture.OriginalPaymentId, correction.OriginalPaymentId);
        Assert.Equal(replacement.Id, correction.ReplacementPaymentId);
        Assert.Equal(
            ["amount", "occurred_at", "comment"],
            JsonSerializer.Deserialize<string[]>(correction.ChangedFieldsJson) ?? []);
        Assert.Equal("Corrected cash receipt", correction.Reason);
        Assert.Equal(CorrectionOccurredAt, correction.OccurredAt);
        Assert.Equal(TestNow, correction.RecordedAt);
        Assert.Equal("normal", correction.EntryOrigin);

        var audit = await ReadAuditAsync(database);
        Assert.Equal(result.AuditEntryId!.Value.Value, audit.Id);
        Assert.Equal(PaymentAuditActions.Corrected, audit.ActionType);
        Assert.Equal(PaymentAuditActions.EntityType, audit.EntityType);
        Assert.Equal(fixture.OriginalPaymentId, audit.EntityId);
        Assert.Equal("Corrected cash receipt", audit.Reason);
        Assert.Equal("Operator correction note", audit.Comment);
        Assert.Equal(CorrectionOccurredAt, audit.OccurredAt);
        Assert.Equal(TestNow, audit.RecordedAt);
        Assert.False(audit.ChangedAfterClose);
        AssertCorrectionAuditDates(audit, OriginalOccurredAt, ReplacementOccurredAt);

        var idempotency = await ReadIdempotencyAsync(database);
        Assert.Equal("CorrectPayment", idempotency.CommandName);
        Assert.Equal("replace-payment", idempotency.IdempotencyKey);
        Assert.Equal(correction.Id, idempotency.PrimaryEntityId);
        Assert.Equal(fixture.ClientId, idempotency.RereadTargetId);
        Assert.Equal(audit.Id, idempotency.AuditEntryId);
        Assert.Equal("succeeded", idempotency.Status);
        Assert.False(string.IsNullOrWhiteSpace(idempotency.ResultFingerprint));
    }

    [PostgreSqlFact]
    public async Task SharedAdminCancelPreservesFallbackMetadataAndRejectsLaterCorrection()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var sharedAdmin = await InsertAdminActorAsync(
            database,
            AccountKind.SharedReceptionAdmin,
            "Shared Reception");
        var entryBatchId = Guid.NewGuid();
        var command = CreateCancelCommand(
            fixture,
            "cancel-payment",
            sharedAdmin,
            EntryOrigin.PaperFallback,
            entryBatchId);
        var handler = CreateHandler(dbContext);

        var result = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var laterCorrection = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with
                {
                    IdempotencyKey = "cancel-payment-again",
                    RequestCorrelationId = new RequestCorrelationId(
                        "correlation-cancel-payment-again"),
                },
            },
            CancellationToken.None);

        AssertSuccessfulResult(
            result,
            CorrectPaymentCommand.CancellationEntityType,
            fixture.ClientId);
        AssertEquivalentSuccess(result, replay);
        AssertError(laterCorrection, CommandErrorCode.AlreadyCanceled, "originalPaymentId");

        var payment = Assert.Single(await ReadPaymentsAsync(database));
        Assert.Equal("canceled", payment.Status);
        var cancellation = await ReadCancellationAsync(database);
        Assert.Equal(result.PrimaryEntityId!.Value.Value, cancellation.Id);
        Assert.Equal(fixture.OriginalPaymentId, cancellation.PaymentId);
        Assert.Equal("Corrected paper receipt", cancellation.Reason);
        Assert.Equal(CorrectionOccurredAt, cancellation.OccurredAt);
        Assert.Equal(TestNow, cancellation.RecordedAt);
        Assert.Equal(sharedAdmin.AccountId.Value, cancellation.RecordedByAccountId);
        Assert.Equal(sharedAdmin.SessionId.Value, cancellation.SessionId);
        Assert.Equal("paper_fallback", cancellation.EntryOrigin);
        Assert.Equal(entryBatchId, cancellation.EntryBatchId);

        var audit = await ReadAuditAsync(database);
        Assert.Equal(PaymentAuditActions.Canceled, audit.ActionType);
        Assert.Equal("shared_reception_admin", audit.ActorAccountType);
        Assert.Equal("admin", audit.ActorRole);
        Assert.Equal("paper_fallback", audit.EntryOrigin);
        Assert.Equal("Recovered paper line 7", audit.Comment);
        Assert.False(audit.ChangedAfterClose);
        using var after = JsonDocument.Parse(audit.AfterSummaryJson);
        Assert.Equal(
            "canceled",
            after.RootElement.GetProperty("payment").GetProperty("status").GetString());
        Assert.Equal(
            entryBatchId,
            after.RootElement
                .GetProperty("cancellation")
                .GetProperty("entryBatchId")
                .GetGuid());
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ReconciledDayDeniesAdminAndMarksOwnerCorrectionAsChangedAfterClose()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var namedAdmin = await InsertAdminActorAsync(
            database,
            AccountKind.NamedAdmin,
            "Named Admin");
        var originalBusinessDate = DateOnly.FromDateTime(OriginalOccurredAt.UtcDateTime);
        var dayStatusProvider = new StubPaymentDayReconciliationStatusProvider(
            new HashSet<DateOnly> { originalBusinessDate });
        var handler = CreateHandler(dbContext, dayStatusProvider);

        var denied = await handler.ExecuteAsync(
            CreateCancelCommand(fixture, "admin-closed-day", namedAdmin),
            CancellationToken.None);
        var allowed = await handler.ExecuteAsync(
            CreateCancelCommand(fixture, "owner-closed-day", fixture.Owner),
            CancellationToken.None);

        AssertError(
            denied,
            CommandErrorCode.DayClosedRequiresOwner,
            "originalPaymentId");
        AssertSuccessfulResult(
            allowed,
            CorrectPaymentCommand.CancellationEntityType,
            fixture.ClientId);
        Assert.True(allowed.ChangedAfterClose);

        var audit = await ReadAuditAsync(database);
        Assert.True(audit.ChangedAfterClose);
        using var after = JsonDocument.Parse(audit.AfterSummaryJson);
        Assert.True(after.RootElement
            .GetProperty("cancellation")
            .GetProperty("changedAfterClose")
            .GetBoolean());
        Assert.Equal(2, dayStatusProvider.RequestedDates.Count);
        Assert.All(
            dayStatusProvider.RequestedDates,
            requestedDate => Assert.Equal(originalBusinessDate, requestedDate));
    }

    [PostgreSqlFact]
    public async Task ReplaceReplaysSamePayloadAndRejectsChangedPayloadForSameKey()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var command = CreateReplaceCommand(fixture, "replace-replay");
        var handler = CreateHandler(dbContext);

        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var changedPayload = await handler.ExecuteAsync(
            command with
            {
                Replacement = command.Replacement! with
                {
                    Amount = new Money(675m, "UAH"),
                },
            },
            CancellationToken.None);

        AssertSuccessfulResult(
            first,
            CorrectPaymentCommand.CorrectionEntityType,
            fixture.ClientId);
        AssertEquivalentSuccess(first, replay);
        AssertError(
            changedPayload,
            CommandErrorCode.DuplicateSubmission,
            "idempotencyKey");
        await AssertCorrectionCountsAsync(database, 2, 1, 0, 1, 1);
    }

    [PostgreSqlFact]
    public async Task ConcurrentSameKeyProducesOneCompleteReplacementWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        CorrectPaymentFixture fixture;
        await using (var setupContext = database.CreateDbContext())
        {
            await setupContext.Database.MigrateAsync();
            fixture = await SeedFixtureAsync(database, setupContext);
        }

        var command = CreateReplaceCommand(fixture, "replace-concurrent");
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(command, CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(command, CancellationToken.None));

        Assert.All(
            results,
            result => AssertSuccessfulResult(
                result,
                CorrectPaymentCommand.CorrectionEntityType,
                fixture.ClientId));
        AssertEquivalentSuccess(results[0], results[1]);
        await AssertCorrectionCountsAsync(database, 2, 1, 0, 1, 1);
    }

    [PostgreSqlFact]
    public async Task InvalidShapesRelationshipsAndNegativeClosureFailWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var negativeClosurePaymentId = await InsertPaymentAsync(
            database,
            fixture,
            PaymentContext.NegativeClosure,
            amount: 200m);
        var handler = CreateHandler(dbContext);
        var replace = CreateReplaceCommand(fixture, "invalid-base");

        var missingReason = await handler.ExecuteAsync(
            replace with
            {
                Envelope = replace.Envelope with { Reason = "  " },
            },
            CancellationToken.None);
        var replaceWithoutReplacement = await handler.ExecuteAsync(
            replace with
            {
                Envelope = WithKey(replace.Envelope, "missing-replacement"),
                Replacement = null,
            },
            CancellationToken.None);
        var cancelWithReplacement = await handler.ExecuteAsync(
            replace with
            {
                Envelope = WithKey(replace.Envelope, "cancel-with-replacement"),
                Mode = PaymentCorrectionMode.Cancel,
            },
            CancellationToken.None);
        var noChange = await handler.ExecuteAsync(
            replace with
            {
                Envelope = WithKey(replace.Envelope, "no-change"),
                Replacement = new PaymentReplacement(
                    fixture.MembershipId,
                    new Money(500m, "UAH"),
                    PaymentContext.MembershipSale,
                    OriginalOccurredAt,
                    "Original receipt"),
            },
            CancellationToken.None);
        var crossClientMembership = await handler.ExecuteAsync(
            replace with
            {
                Envelope = WithKey(replace.Envelope, "cross-client-membership"),
                Replacement = replace.Replacement! with
                {
                    MembershipId = fixture.OtherMembershipId,
                },
            },
            CancellationToken.None);
        var negativeClosure = await handler.ExecuteAsync(
            CreateCancelCommand(fixture, "negative-closure") with
            {
                OriginalPaymentId = negativeClosurePaymentId,
            },
            CancellationToken.None);

        AssertError(missingReason, CommandErrorCode.ReasonRequired, "reason");
        AssertError(
            replaceWithoutReplacement,
            CommandErrorCode.ValidationFailed,
            "replacement");
        AssertError(
            cancelWithReplacement,
            CommandErrorCode.ValidationFailed,
            "replacement");
        AssertError(noChange, CommandErrorCode.ValidationFailed, "replacement");
        AssertError(
            crossClientMembership,
            CommandErrorCode.NotFound,
            "replacement.membershipId");
        AssertError(
            negativeClosure,
            CommandErrorCode.MembershipNotEligible,
            "originalPaymentId");
        await AssertCorrectionCountsAsync(database, 2, 0, 0, 0, 0);

        Assert.All(
            await ReadPaymentsAsync(database),
            payment => Assert.Equal("active", payment.Status));
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackReplacementCancellationAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_payment_correction_audit
            check (action_type not in ('payment.corrected', 'payment.canceled'))
            """);
        var handler = CreateHandler(dbContext);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.ExecuteAsync(
                CreateReplaceCommand(fixture, "replace-audit-failure"),
                CancellationToken.None));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.ExecuteAsync(
                CreateCancelCommand(fixture, "cancel-audit-failure"),
                CancellationToken.None));

        await AssertCorrectionCountsAsync(database, 1, 0, 0, 0, 0);
        Assert.Equal(
            "active",
            Assert.Single(await ReadPaymentsAsync(database)).Status);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public void PersistenceRegistrationResolvesCorrectPaymentWorkflow()
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

        Assert.IsType<CorrectPaymentCommandHandler>(
            scope.ServiceProvider.GetRequiredService<
                IBodyLifeCommandHandler<CorrectPaymentCommand>>());
        var dayStatusProvider = scope.ServiceProvider.GetRequiredService<
            IPaymentDayReconciliationStatusProvider>();
        Assert.Equal(
            "OpenPaymentDayReconciliationStatusProvider",
            dayStatusProvider.GetType().Name);
    }

    private static CorrectPaymentCommandHandler CreateHandler(
        BodyLifeDbContext dbContext,
        IPaymentDayReconciliationStatusProvider? dayStatusProvider = null)
    {
        return new CorrectPaymentCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            dayStatusProvider ?? new StubPaymentDayReconciliationStatusProvider(),
            new FixedTimeProvider(TestNow));
    }

    private static CorrectPaymentCommand CreateReplaceCommand(
        CorrectPaymentFixture fixture,
        string idempotencyKey,
        ActorContext? actor = null)
    {
        return new CorrectPaymentCommand(
            CreateEnvelope(
                actor ?? fixture.Owner,
                idempotencyKey,
                EntryOrigin.Normal,
                "  Corrected cash receipt  ",
                "  Operator correction note  "),
            fixture.OriginalPaymentId,
            PaymentCorrectionMode.Replace,
            new PaymentReplacement(
                fixture.MembershipId,
                new Money(650m, "uah"),
                PaymentContext.MembershipSale,
                ReplacementOccurredAt,
                "  Corrected receipt  "));
    }

    private static CorrectPaymentCommand CreateCancelCommand(
        CorrectPaymentFixture fixture,
        string idempotencyKey,
        ActorContext? actor = null,
        EntryOrigin entryOrigin = EntryOrigin.Normal,
        Guid? entryBatchId = null)
    {
        var comment = entryOrigin == EntryOrigin.PaperFallback
            ? "  Recovered paper line 7  "
            : "  Operator cancellation note  ";
        var reason = entryOrigin == EntryOrigin.PaperFallback
            ? "  Corrected paper receipt  "
            : "  Payment entered by mistake  ";

        return new CorrectPaymentCommand(
            CreateEnvelope(
                actor ?? fixture.Owner,
                idempotencyKey,
                entryOrigin,
                reason,
                comment),
            fixture.OriginalPaymentId,
            PaymentCorrectionMode.Cancel,
            Replacement: null,
            EntryBatchId: entryBatchId);
    }

    private static CommandEnvelope CreateEnvelope(
        ActorContext actor,
        string idempotencyKey,
        EntryOrigin entryOrigin,
        string? reason,
        string? comment)
    {
        return new CommandEnvelope(
            actor,
            new RequestCorrelationId($"correlation-{idempotencyKey}"),
            entryOrigin,
            CorrectionOccurredAt,
            idempotencyKey,
            reason,
            comment);
    }

    private static CommandEnvelope WithKey(
        CommandEnvelope envelope,
        string idempotencyKey)
    {
        return envelope with
        {
            RequestCorrelationId = new RequestCorrelationId(
                $"correlation-{idempotencyKey}"),
            IdempotencyKey = idempotencyKey,
        };
    }

    private static async Task<CorrectPaymentFixture> SeedFixtureAsync(
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
        var originalPaymentId = Guid.NewGuid();

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
                    'Correct',
                    'Payment',
                    null,
                    'CORRECT PAYMENT',
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
                'Correct Payment fixture',
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
                    'Correct Payment fixture',
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
                    'Correct Payment fixture',
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
                @original_payment_id,
                @client_id,
                @membership_id,
                500,
                'UAH',
                'cash',
                'membership_sale',
                @original_occurred_at,
                @original_recorded_at,
                @account_id,
                @session_id,
                'normal',
                null,
                'Original receipt',
                'active')
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
            new DateOnly(2026, 7, 1));
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 30));
        command.Parameters.AddWithValue("issued_at", TestNow.AddDays(-15));
        command.Parameters.AddWithValue("original_payment_id", originalPaymentId);
        command.Parameters.AddWithValue("original_occurred_at", OriginalOccurredAt);
        command.Parameters.AddWithValue("original_recorded_at", TestNow.AddHours(-2));
        Assert.Equal(7, await command.ExecuteNonQueryAsync());

        return new CorrectPaymentFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "  Reception tablet  "),
            clientId,
            membershipId,
            otherMembershipId,
            originalPaymentId);
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

    private static async Task<Guid> InsertPaymentAsync(
        PostgreSqlTestDatabase database,
        CorrectPaymentFixture fixture,
        PaymentContext paymentContext,
        decimal amount)
    {
        var paymentId = Guid.NewGuid();
        var storedContext = paymentContext == PaymentContext.NegativeClosure
            ? "negative_closure"
            : throw new ArgumentOutOfRangeException(nameof(paymentContext));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
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
                'UAH',
                'cash',
                @payment_context,
                @occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                'normal',
                null,
                'Negative closure source',
                'active')
            """;
        command.Parameters.AddWithValue("id", paymentId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("payment_context", storedContext);
        command.Parameters.AddWithValue("occurred_at", OriginalOccurredAt.AddMinutes(30));
        command.Parameters.AddWithValue("recorded_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("account_id", fixture.Owner.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Owner.SessionId.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return paymentId;
    }

    private static async Task<IReadOnlyList<PaymentRow>> ReadPaymentsAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select id,
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
                   status
            from bodylife.payments
            order by recorded_at, id
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var payments = new List<PaymentRow>();
        while (await reader.ReadAsync())
        {
            payments.Add(new PaymentRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetDecimal(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetGuid(9),
                reader.GetGuid(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetGuid(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetString(14)));
        }

        return payments;
    }

    private static async Task<PaymentCorrectionRow> ReadCorrectionAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select id,
                   client_id,
                   original_payment_id,
                   replacement_payment_id,
                   changed_fields::text,
                   reason,
                   occurred_at,
                   recorded_at,
                   entry_origin
            from bodylife.payment_corrections
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var correction = new PaymentCorrectionRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetString(8));
        Assert.False(await reader.ReadAsync());
        return correction;
    }

    private static async Task<PaymentCancellationRow> ReadCancellationAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select id,
                   payment_id,
                   reason,
                   occurred_at,
                   recorded_at,
                   recorded_by_account_id,
                   session_id,
                   entry_origin,
                   entry_batch_id
            from bodylife.payment_cancellations
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var cancellation = new PaymentCancellationRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetGuid(5),
            reader.GetGuid(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8));
        Assert.False(await reader.ReadAsync());
        return cancellation;
    }

    private static async Task<AuditRow> ReadAuditAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select id,
                   action_type,
                   entity_type,
                   entity_id,
                   actor_account_type,
                   actor_role,
                   occurred_at,
                   recorded_at,
                   reason,
                   comment,
                   before_summary::text,
                   after_summary::text,
                   entry_origin,
                   changed_after_close
            from bodylife.business_audit_entries
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var audit = new AuditRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetBoolean(13));
        Assert.False(await reader.ReadAsync());
        return audit;
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
                   idempotency_key,
                   primary_entity_id,
                   reread_target_id,
                   audit_entry_id,
                   status,
                   result_fingerprint
            from bodylife.command_idempotency_keys
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var idempotency = new IdempotencyRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6));
        Assert.False(await reader.ReadAsync());
        return idempotency;
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

    private static async Task AssertCorrectionCountsAsync(
        PostgreSqlTestDatabase database,
        long payments,
        long corrections,
        long cancellations,
        long auditEntries,
        long idempotencyRecords)
    {
        Assert.Equal(payments, await CountRowsAsync(database, "payments"));
        Assert.Equal(
            corrections,
            await CountRowsAsync(database, "payment_corrections"));
        Assert.Equal(
            cancellations,
            await CountRowsAsync(database, "payment_cancellations"));
        Assert.Equal(
            auditEntries,
            await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(
            idempotencyRecords,
            await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{tableName}");
    }

    private static void AssertCorrectionAuditDates(
        AuditRow audit,
        DateTimeOffset expectedOriginalDate,
        DateTimeOffset expectedReplacementDate)
    {
        using var before = JsonDocument.Parse(audit.BeforeSummaryJson);
        using var after = JsonDocument.Parse(audit.AfterSummaryJson);
        var beforePayment = before.RootElement.GetProperty("payment");
        var afterOriginal = after.RootElement.GetProperty("originalPayment");
        var afterReplacement = after.RootElement.GetProperty("replacementPayment");
        Assert.Equal(
            expectedOriginalDate,
            beforePayment.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(500m, beforePayment.GetProperty("amount").GetDecimal());
        Assert.Equal("active", beforePayment.GetProperty("status").GetString());
        Assert.Equal(
            expectedOriginalDate,
            afterOriginal.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal("replaced", afterOriginal.GetProperty("status").GetString());
        Assert.Equal(
            expectedReplacementDate,
            afterReplacement.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(650m, afterReplacement.GetProperty("amount").GetDecimal());
        Assert.Equal("active", afterReplacement.GetProperty("status").GetString());
    }

    private static void AssertSuccessfulResult(
        CommandResult result,
        string primaryEntityType,
        Guid clientId)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.True(result.PrimaryEntityId.HasValue);
        Assert.Equal(primaryEntityType, result.PrimaryEntityId.Value.Type);
        Assert.NotEqual(Guid.Empty, result.PrimaryEntityId.Value.Value);
        Assert.Equal(
            new EntityId(CorrectPaymentCommand.CanonicalRereadEntityType, clientId),
            result.RereadTargetId);
        Assert.True(result.AuditEntryId.HasValue);
        Assert.Empty(result.Errors);
    }

    private static void AssertEquivalentSuccess(
        CommandResult expected,
        CommandResult actual)
    {
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.PrimaryEntityId, actual.PrimaryEntityId);
        Assert.Equal(expected.RelatedEntityIds, actual.RelatedEntityIds);
        Assert.Equal(expected.RereadTargetId, actual.RereadTargetId);
        Assert.Equal(expected.Warnings, actual.Warnings);
        Assert.Equal(expected.AuditEntryId, actual.AuditEntryId);
        Assert.Equal(expected.ChangedAfterClose, actual.ChangedAfterClose);
        Assert.Equal(expected.Errors, actual.Errors);
    }

    private static void AssertError(
        CommandResult result,
        CommandErrorCode errorCode,
        string? field = null)
    {
        Assert.Equal(CommandStatus.Error, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal(errorCode, error.Code);
        if (field is not null)
        {
            Assert.Equal(field, error.Field);
        }

        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
        Assert.Null(result.AuditEntryId);
    }

    private sealed record CorrectPaymentFixture(
        ActorContext Owner,
        Guid ClientId,
        Guid MembershipId,
        Guid OtherMembershipId,
        Guid OriginalPaymentId);

    private sealed record PaymentRow(
        Guid Id,
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

    private sealed record PaymentCorrectionRow(
        Guid Id,
        Guid ClientId,
        Guid OriginalPaymentId,
        Guid ReplacementPaymentId,
        string ChangedFieldsJson,
        string Reason,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string EntryOrigin);

    private sealed record PaymentCancellationRow(
        Guid Id,
        Guid PaymentId,
        string Reason,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId);

    private sealed record AuditRow(
        Guid Id,
        string ActionType,
        string EntityType,
        Guid EntityId,
        string ActorAccountType,
        string ActorRole,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string? Reason,
        string? Comment,
        string BeforeSummaryJson,
        string AfterSummaryJson,
        string EntryOrigin,
        bool ChangedAfterClose);

    private sealed record IdempotencyRow(
        string CommandName,
        string IdempotencyKey,
        Guid PrimaryEntityId,
        Guid RereadTargetId,
        Guid AuditEntryId,
        string Status,
        string ResultFingerprint);

    private sealed class StubPaymentDayReconciliationStatusProvider(
        IReadOnlySet<DateOnly>? reconciledDates = null)
        : IPaymentDayReconciliationStatusProvider
    {
        private readonly object sync = new();

        public List<DateOnly> RequestedDates { get; } = [];

        public Task<PaymentDayReconciliationStatus> GetStatusAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                RequestedDates.Add(businessDate);
            }

            return Task.FromResult(
                reconciledDates?.Contains(businessDate) == true
                    ? PaymentDayReconciliationStatus.Reconciled
                    : PaymentDayReconciliationStatus.Open);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
