using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
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

public sealed class PostgreSqlGetClientPaymentRowsQueryTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        16,
        16,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset FirstPaymentOccurredAt = new(
        2026,
        7,
        16,
        9,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryReturnsOwnedCanonicalRowsAndRetainedHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var originalBatchId = Guid.NewGuid();
        var originalPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            1000m,
            "membership_sale",
            FirstPaymentOccurredAt,
            TestNow.AddMinutes(-30),
            entryOrigin: "paper_fallback",
            entryBatchId: originalBatchId,
            comment: "Recovered sale",
            status: "replaced");
        var replacementPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            900m,
            "membership_sale",
            FirstPaymentOccurredAt.AddHours(1),
            TestNow.AddMinutes(-20),
            comment: "Corrected amount");
        var correctionBatchId = Guid.NewGuid();
        var correctionId = await InsertCorrectionAsync(
            database,
            fixture,
            originalPaymentId,
            replacementPaymentId,
            "[\"amount\",\"occurred_at\"]",
            "Cash amount was entered incorrectly",
            FirstPaymentOccurredAt.AddHours(2),
            TestNow.AddMinutes(-10),
            "manual_backfill",
            correctionBatchId);
        var canceledPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            250m,
            "one_off",
            FirstPaymentOccurredAt.AddHours(3),
            TestNow.AddMinutes(-8),
            comment: "Drop-in cash",
            status: "canceled");
        var cancellationBatchId = Guid.NewGuid();
        var cancellationId = await InsertCancellationAsync(
            database,
            fixture,
            canceledPaymentId,
            "Duplicate cash entry",
            FirstPaymentOccurredAt.AddHours(4),
            TestNow.AddMinutes(-5),
            "paper_fallback",
            cancellationBatchId);
        var latestPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            100m,
            "trial",
            FirstPaymentOccurredAt.AddHours(5),
            TestNow.AddMinutes(-1));
        await InsertPaymentAsync(
            database,
            fixture,
            fixture.OtherClientId,
            membershipId: null,
            400m,
            "other",
            FirstPaymentOccurredAt.AddHours(6),
            TestNow);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientPaymentRowsQuery(fixture.Actor, fixture.ClientId),
            CancellationToken.None);

        var page = AssertSuccess(result, fixture.ClientId);
        Assert.False(page.HasMore);
        Assert.Equal(
            [
                latestPaymentId,
                canceledPaymentId,
                replacementPaymentId,
                originalPaymentId,
            ],
            page.Items.Select(row => row.PaymentId).ToArray());

        var latest = page.Items[0];
        Assert.Equal(PaymentMethod.Cash, latest.Method);
        Assert.Equal(PaymentContext.Trial, latest.PaymentContext);
        Assert.Equal(new Money(100m, "UAH"), latest.Amount);
        Assert.Equal(ClientPaymentRowStatus.Active, latest.Status);
        Assert.Null(latest.MembershipId);
        Assert.Null(latest.MembershipTypeNameSnapshot);
        Assert.Null(latest.Cancellation);
        Assert.Null(latest.CorrectionFromOriginal);
        Assert.Null(latest.CorrectionToReplacement);
        Assert.True(latest.AllowedActions.IsAllowed(PaymentActionKeys.Correct));
        Assert.Equal(
            PaymentActionKeys.AdminOrOwnerPolicy,
            Assert.Single(latest.AllowedActions.Items).RequiredPolicy);

        var canceled = page.Items[1];
        Assert.Equal(fixture.ClientId, canceled.ClientId);
        Assert.Equal(PaymentContext.OneOff, canceled.PaymentContext);
        Assert.Equal("Drop-in cash", canceled.Comment);
        Assert.Equal(ClientPaymentRowStatus.Canceled, canceled.Status);
        var cancellation = Assert.IsType<ClientPaymentCancellation>(
            canceled.Cancellation);
        Assert.Equal(cancellationId, cancellation.CancellationId);
        Assert.Equal("Duplicate cash entry", cancellation.Reason);
        Assert.Equal(FirstPaymentOccurredAt.AddHours(4), cancellation.OccurredAt);
        Assert.Equal(TestNow.AddMinutes(-5), cancellation.RecordedAt);
        Assert.Equal(fixture.Actor.AccountId.Value, cancellation.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId.Value, cancellation.SessionId);
        Assert.Equal(EntryOrigin.PaperFallback, cancellation.EntryOrigin);
        Assert.Equal(cancellationBatchId, cancellation.EntryBatchId);
        Assert.Null(canceled.CorrectionFromOriginal);
        Assert.Null(canceled.CorrectionToReplacement);
        Assert.Empty(canceled.AllowedActions.Items);

        var replacement = page.Items[2];
        Assert.Equal(fixture.MembershipId, replacement.MembershipId);
        Assert.Equal(
            "Payment history fixture",
            replacement.MembershipTypeNameSnapshot);
        Assert.Equal(new Money(900m, "UAH"), replacement.Amount);
        Assert.Equal(ClientPaymentRowStatus.Active, replacement.Status);
        var correctionFromOriginal = Assert.IsType<ClientPaymentCorrection>(
            replacement.CorrectionFromOriginal);
        AssertCorrection(
            correctionFromOriginal,
            correctionId,
            originalPaymentId,
            replacementPaymentId,
            correctionBatchId);
        Assert.Null(replacement.CorrectionToReplacement);
        Assert.True(replacement.AllowedActions.IsAllowed(PaymentActionKeys.Correct));

        var original = page.Items[3];
        Assert.Equal(EntryOrigin.PaperFallback, original.EntryOrigin);
        Assert.Equal(originalBatchId, original.EntryBatchId);
        Assert.Equal("Recovered sale", original.Comment);
        Assert.Equal(ClientPaymentRowStatus.Replaced, original.Status);
        Assert.Null(original.CorrectionFromOriginal);
        var correctionToReplacement = Assert.IsType<ClientPaymentCorrection>(
            original.CorrectionToReplacement);
        AssertCorrection(
            correctionToReplacement,
            correctionId,
            originalPaymentId,
            replacementPaymentId,
            correctionBatchId);
        Assert.Empty(original.AllowedActions.Items);

        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
    }

    [PostgreSqlFact]
    public async Task QueryUsesDeterministicLimitAndReportsMoreRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var thirdId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            100m,
            "other",
            FirstPaymentOccurredAt,
            TestNow,
            paymentId: firstId);
        await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            200m,
            "other",
            FirstPaymentOccurredAt,
            TestNow,
            paymentId: secondId);
        await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            300m,
            "other",
            FirstPaymentOccurredAt,
            TestNow,
            paymentId: thirdId);

        var handler = CreateHandler(dbContext);
        var result = await handler.ExecuteAsync(
            new GetClientPaymentRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 2),
            CancellationToken.None);
        var selectedResult = await handler.ExecuteAsync(
            new GetClientPaymentRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 2,
                RequiredPaymentId: firstId),
            CancellationToken.None);

        var page = AssertSuccess(result, fixture.ClientId);
        Assert.True(page.HasMore);
        Assert.Equal(
            [thirdId, secondId],
            page.Items.Select(row => row.PaymentId).ToArray());
        var selectedPage = AssertSuccess(selectedResult, fixture.ClientId);
        Assert.True(selectedPage.HasMore);
        Assert.Equal(
            [thirdId, secondId, firstId],
            selectedPage.Items.Select(row => row.PaymentId).ToArray());
    }

    [PostgreSqlFact]
    public async Task ReconciledDayAllowsOwnerDeniesAdminAndReservesNegativeClosure()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var ordinaryPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            300m,
            "one_off",
            FirstPaymentOccurredAt,
            TestNow.AddMinutes(-2));
        var negativeClosurePaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            200m,
            "negative_closure",
            FirstPaymentOccurredAt.AddMinutes(1),
            TestNow.AddMinutes(-1));
        var reconciledProvider = new RecordingPaymentDayStatusProvider(
            PaymentDayReconciliationStatus.Reconciled);

        var ownerResult = await CreateHandler(
            dbContext,
            reconciledProvider).ExecuteAsync(
                new GetClientPaymentRowsQuery(fixture.Actor, fixture.ClientId),
                CancellationToken.None);

        var ownerRows = AssertSuccess(ownerResult, fixture.ClientId).Items;
        var ownerOrdinary = Assert.Single(
            ownerRows,
            row => row.PaymentId == ordinaryPaymentId);
        var ownerPermission = Assert.Single(ownerOrdinary.AllowedActions.Items);
        Assert.True(ownerPermission.IsAllowed);
        Assert.Equal(PaymentActionKeys.OwnerPolicy, ownerPermission.RequiredPolicy);
        Assert.Empty(Assert.Single(
            ownerRows,
            row => row.PaymentId == negativeClosurePaymentId).AllowedActions.Items);

        await UpdateActorIdentityAsync(
            database,
            fixture.Actor.AccountId.Value,
            "named_admin",
            "admin");
        var adminActor = fixture.Actor with
        {
            Role = ActorRole.Admin,
            AccountKind = AccountKind.NamedAdmin,
        };
        var adminResult = await CreateHandler(
            dbContext,
            reconciledProvider).ExecuteAsync(
                new GetClientPaymentRowsQuery(adminActor, fixture.ClientId),
                CancellationToken.None);

        var adminRows = AssertSuccess(adminResult, fixture.ClientId).Items;
        var deniedPermission = Assert.Single(Assert.Single(
            adminRows,
            row => row.PaymentId == ordinaryPaymentId).AllowedActions.Items);
        Assert.False(deniedPermission.IsAllowed);
        Assert.Equal(PaymentActionKeys.OwnerPolicy, deniedPermission.RequiredPolicy);
        Assert.Equal("day_closed_requires_owner", deniedPermission.DeniedReasonCode);
        Assert.Empty(Assert.Single(
            adminRows,
            row => row.PaymentId == negativeClosurePaymentId).AllowedActions.Items);
        Assert.Equal(2, reconciledProvider.RequestedDates.Count);
    }

    [PostgreSqlFact]
    public async Task ValidationMissingClientAndInactiveActorReturnNoRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var handler = CreateHandler(dbContext);

        var empty = await handler.ExecuteAsync(
            new GetClientPaymentRowsQuery(fixture.Actor, fixture.ClientId),
            CancellationToken.None);
        var missingId = await handler.ExecuteAsync(
            new GetClientPaymentRowsQuery(fixture.Actor, Guid.Empty),
            CancellationToken.None);
        var invalidLowLimit = await handler.ExecuteAsync(
            new GetClientPaymentRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 0),
            CancellationToken.None);
        var invalidHighLimit = await handler.ExecuteAsync(
            new GetClientPaymentRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: GetClientPaymentRowsQuery.MaxLimit + 1),
            CancellationToken.None);
        var invalidRequiredPaymentId = await handler.ExecuteAsync(
            new GetClientPaymentRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                RequiredPaymentId: Guid.Empty),
            CancellationToken.None);
        var missingClient = await handler.ExecuteAsync(
            new GetClientPaymentRowsQuery(fixture.Actor, Guid.NewGuid()),
            CancellationToken.None);
        await DeactivateActorAsync(database, fixture.Actor.AccountId.Value);
        var denied = await handler.ExecuteAsync(
            new GetClientPaymentRowsQuery(fixture.Actor, fixture.ClientId),
            CancellationToken.None);

        Assert.Empty(AssertSuccess(empty, fixture.ClientId).Items);
        AssertFailure(
            missingId,
            GetClientPaymentRowsStatus.ValidationFailed,
            "clientId");
        AssertFailure(
            invalidLowLimit,
            GetClientPaymentRowsStatus.ValidationFailed,
            "limit");
        AssertFailure(
            invalidHighLimit,
            GetClientPaymentRowsStatus.ValidationFailed,
            "limit");
        AssertFailure(
            invalidRequiredPaymentId,
            GetClientPaymentRowsStatus.ValidationFailed,
            "requiredPaymentId");
        AssertFailure(
            missingClient,
            GetClientPaymentRowsStatus.NotFound,
            "clientId");
        AssertFailure(denied, GetClientPaymentRowsStatus.PermissionDenied);
    }

    [PostgreSqlFact]
    public async Task CanceledPaymentWithoutRetainedCancellationFailsClosed()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            100m,
            "other",
            FirstPaymentOccurredAt,
            TestNow,
            status: "canceled");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientPaymentRowsQuery(fixture.Actor, fixture.ClientId),
            CancellationToken.None);

        AssertFailure(result, GetClientPaymentRowsStatus.SourceInconsistent);
        Assert.Equal("source_inconsistent", result.ErrorCode);
    }

    [PostgreSqlFact]
    public async Task NonStringCorrectionChangedFieldsFailClosed()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var originalPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            100m,
            "other",
            FirstPaymentOccurredAt,
            TestNow.AddMinutes(-2),
            status: "replaced");
        var replacementPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            200m,
            "other",
            FirstPaymentOccurredAt.AddMinutes(1),
            TestNow.AddMinutes(-1));
        await InsertCorrectionAsync(
            database,
            fixture,
            originalPaymentId,
            replacementPaymentId,
            "[1]",
            "Malformed changed field source",
            FirstPaymentOccurredAt.AddMinutes(2),
            TestNow,
            "normal",
            entryBatchId: null);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientPaymentRowsQuery(fixture.Actor, fixture.ClientId),
            CancellationToken.None);

        AssertFailure(result, GetClientPaymentRowsStatus.SourceInconsistent);
    }

    [Fact]
    public void PersistenceRegistrationResolvesClientPaymentRowsQuery()
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
                        GetClientPaymentRowsQuery,
                        GetClientPaymentRowsResult>)
                && descriptor.ImplementationType
                    == typeof(GetClientPaymentRowsQueryHandler)
                && descriptor.Lifetime == ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetClientPaymentRowsQuery,
                GetClientPaymentRowsResult>>());
    }

    private static GetClientPaymentRowsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext,
        IPaymentDayReconciliationStatusProvider? dayStatusProvider = null)
    {
        return new GetClientPaymentRowsQueryHandler(
            dbContext,
            dayStatusProvider ?? new RecordingPaymentDayStatusProvider(
                PaymentDayReconciliationStatus.Open),
            new FixedTimeProvider(TestNow));
    }

    private static async Task<ClientPaymentRowsFixture> SeedFixtureAsync(
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
                    'Reader',
                    null,
                    'PAYMENT READER',
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
                'Payment history fixture',
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
            values (
                @membership_id,
                @client_id,
                @membership_type_id,
                'Payment history fixture',
                30,
                8,
                1000,
                'UAH',
                @start_date,
                @base_end_date,
                @created_at,
                @account_id,
                'active',
                'normal',
                null,
                null)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-2));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("other_client_id", otherClientId);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-30));
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 1));
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 30));
        Assert.Equal(5, await command.ExecuteNonQueryAsync());

        return new ClientPaymentRowsFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Reception tablet"),
            clientId,
            otherClientId,
            membershipId);
    }

    private static async Task<Guid> InsertPaymentAsync(
        PostgreSqlTestDatabase database,
        ClientPaymentRowsFixture fixture,
        Guid clientId,
        Guid? membershipId,
        decimal amount,
        string paymentContext,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        string? comment = null,
        string status = "active",
        Guid? paymentId = null)
    {
        var id = paymentId ?? Guid.NewGuid();
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
                @entry_origin,
                @entry_batch_id,
                @comment,
                @status)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.Add("membership_id", NpgsqlDbType.Uuid).Value =
            membershipId ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("payment_context", paymentContext);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Varchar).Value =
            comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return id;
    }

    private static async Task<Guid> InsertCancellationAsync(
        PostgreSqlTestDatabase database,
        ClientPaymentRowsFixture fixture,
        Guid paymentId,
        string reason,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin,
        Guid? entryBatchId)
    {
        var cancellationId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
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
                @account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id)
            """;
        command.Parameters.AddWithValue("id", cancellationId);
        command.Parameters.AddWithValue("payment_id", paymentId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return cancellationId;
    }

    private static async Task<Guid> InsertCorrectionAsync(
        PostgreSqlTestDatabase database,
        ClientPaymentRowsFixture fixture,
        Guid originalPaymentId,
        Guid replacementPaymentId,
        string changedFieldsJson,
        string reason,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin,
        Guid? entryBatchId)
    {
        var correctionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
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
                @account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id)
            """;
        command.Parameters.AddWithValue("id", correctionId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("original_payment_id", originalPaymentId);
        command.Parameters.AddWithValue("replacement_payment_id", replacementPaymentId);
        command.Parameters.AddWithValue(
            "changed_fields",
            NpgsqlDbType.Jsonb,
            changedFieldsJson);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return correctionId;
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

    private static async Task UpdateActorIdentityAsync(
        PostgreSqlTestDatabase database,
        Guid accountId,
        string accountType,
        string role)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.accounts
            set account_type = @account_type,
                role = @role
            where id = @id
            """;
        command.Parameters.AddWithValue("account_type", accountType);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("id", accountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static ClientPaymentRowsPage AssertSuccess(
        GetClientPaymentRowsResult result,
        Guid clientId)
    {
        Assert.Equal(GetClientPaymentRowsStatus.Success, result.Status);
        var page = Assert.IsType<ClientPaymentRowsPage>(result.Page);
        Assert.Equal(clientId, page.ClientId);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
        return page;
    }

    private static void AssertFailure(
        GetClientPaymentRowsResult result,
        GetClientPaymentRowsStatus status,
        string? field = null)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Page);
        Assert.NotNull(result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        if (field is not null)
        {
            Assert.Equal(field, result.ErrorField);
        }
    }

    private static void AssertCorrection(
        ClientPaymentCorrection correction,
        Guid correctionId,
        Guid originalPaymentId,
        Guid replacementPaymentId,
        Guid correctionBatchId)
    {
        Assert.Equal(correctionId, correction.CorrectionId);
        Assert.Equal(originalPaymentId, correction.OriginalPaymentId);
        Assert.Equal(replacementPaymentId, correction.ReplacementPaymentId);
        Assert.Equal(["amount", "occurred_at"], correction.ChangedFields);
        Assert.Equal("Cash amount was entered incorrectly", correction.Reason);
        Assert.Equal(FirstPaymentOccurredAt.AddHours(2), correction.OccurredAt);
        Assert.Equal(TestNow.AddMinutes(-10), correction.RecordedAt);
        Assert.Equal(EntryOrigin.ManualBackfill, correction.EntryOrigin);
        Assert.Equal(correctionBatchId, correction.EntryBatchId);
    }

    private sealed record ClientPaymentRowsFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId);

    private sealed class RecordingPaymentDayStatusProvider(
        PaymentDayReconciliationStatus status)
        : IPaymentDayReconciliationStatusProvider
    {
        public List<DateOnly> RequestedDates { get; } = [];

        public Task<PaymentDayReconciliationStatus> GetStatusAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
        {
            RequestedDates.Add(businessDate);
            return Task.FromResult(status);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
