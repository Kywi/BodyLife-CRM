using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlPreviewIssueMembershipQueryTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        13,
        21,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly ProposedStartDate = new(2026, 8, 1);
    private static readonly DateOnly ProposedBaseEndDate = new(2026, 8, 30);
    private static readonly DateOnly ExistingStartDate = new(2026, 7, 1);
    private static readonly DateOnly ExistingBaseEndDate = new(2026, 7, 30);

    [PostgreSqlFact]
    public async Task AcceptedActorsReadCanonicalCatalogPreviewWithoutSideEffects()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var namedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var sharedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin);
        var fixture = await SeedPreviewFixtureAsync(database, owner.AccountId.Value);
        var handler = CreateHandler(dbContext);
        var results = new List<PreviewIssueMembershipResult>();

        foreach (var actor in new[] { owner, namedAdmin, sharedAdmin })
        {
            results.Add(await handler.ExecuteAsync(
                new PreviewIssueMembershipQuery(
                    actor,
                    fixture.ClientId,
                    fixture.MembershipTypeId,
                    ProposedStartDate),
                CancellationToken.None));
        }

        Assert.All(results, AssertSuccessful);
        var preview = results[0].Preview!;
        Assert.Equal(fixture.ClientId, preview.ClientId);
        Assert.Equal(fixture.MembershipTypeId, preview.MembershipTypeId);
        Assert.Equal("Eight visits / 30 days", preview.Snapshot.TypeName);
        Assert.Equal(30, preview.Snapshot.DurationDays);
        Assert.Equal(8, preview.Snapshot.VisitsLimit);
        Assert.Equal(new Money(1200m, "UAH"), preview.Snapshot.Price);
        Assert.Equal(ProposedStartDate, preview.ProposedStartDate);
        Assert.Equal(ProposedBaseEndDate, preview.BaseEndDate);
        Assert.Equal(8, preview.ExpectedInitialState.RemainingVisits);
        Assert.Equal(0, preview.ExpectedInitialState.NegativeBalance);
        Assert.Null(preview.ExistingNegativeState);
        Assert.Empty(preview.Warnings);
        Assert.True(preview.CanProceedToIssue);
        Assert.All(
            results,
            result =>
            {
                var permission = Assert.Single(result.AllowedActions.Items);
                Assert.Equal(MembershipActionKeys.Issue, permission.ActionKey);
                Assert.Equal(MembershipActionKeys.AdminOrOwnerPolicy, permission.RequiredPolicy);
                Assert.True(permission.IsAllowed);
            });
        Assert.Empty(dbContext.ChangeTracker.Entries());
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task SingleNegativeStateRequiresDecisionAndPreservesCanonicalCache()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedPreviewFixtureAsync(database, owner.AccountId.Value);
        var existingMembership = await InsertIssuedMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value);
        await InsertCacheAsync(
            database,
            existingMembership,
            remainingVisits: -2,
            firstNegativeVisitDate: new DateOnly(2026, 7, 20));
        var cacheBefore = await ReadCacheFingerprintAsync(
            database,
            existingMembership.MembershipId);
        var handler = CreateHandler(dbContext);

        var undecided = await handler.ExecuteAsync(
            Query(owner, fixture),
            CancellationToken.None);
        var leaveVisible = await handler.ExecuteAsync(
            Query(
                owner,
                fixture,
                MembershipNegativeHandlingDecision.LeaveVisible),
            CancellationToken.None);
        var deferredCoverage = await handler.ExecuteAsync(
            Query(
                owner,
                fixture,
                MembershipNegativeHandlingDecision.CoverWithNewMembership),
            CancellationToken.None);

        AssertSuccessful(undecided);
        AssertSuccessful(leaveVisible);
        AssertSuccessful(deferredCoverage);
        var undecidedPreview = undecided.Preview!;
        Assert.Equal(2, undecidedPreview.ExistingNegativeState!.NegativeBalance);
        Assert.Equal(
            new DateOnly(2026, 7, 20),
            undecidedPreview.ExistingNegativeState.FirstNegativeVisitDate);
        Assert.True(undecidedPreview.RequiresNegativeHandlingDecision);
        Assert.False(undecidedPreview.CanProceedToIssue);
        Assert.Single(undecidedPreview.Warnings);
        Assert.False(leaveVisible.Preview!.RequiresNegativeHandlingDecision);
        Assert.True(leaveVisible.Preview.CanProceedToIssue);
        Assert.Equal(
            MembershipNegativeHandlingDecision.LeaveVisible,
            leaveVisible.Preview.SelectedNegativeHandlingDecision);
        Assert.False(deferredCoverage.Preview!.RequiresNegativeHandlingDecision);
        Assert.False(deferredCoverage.Preview.CanProceedToIssue);
        Assert.Equal(
            MembershipNegativeHandlingDecision.CoverWithNewMembership,
            deferredCoverage.Preview.SelectedNegativeHandlingDecision);
        Assert.Equal(
            cacheBefore,
            await ReadCacheFingerprintAsync(database, existingMembership.MembershipId));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task OpeningStateNegativeCanKeepUnknownFirstNegativeDate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedPreviewFixtureAsync(database, owner.AccountId.Value);
        var existingMembership = await InsertIssuedMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value);
        await InsertCacheAsync(
            database,
            existingMembership,
            remainingVisits: -3,
            firstNegativeVisitDate: null);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            Query(owner, fixture),
            CancellationToken.None);

        AssertSuccessful(result);
        Assert.Equal(3, result.Preview!.ExistingNegativeState!.NegativeBalance);
        Assert.Null(result.Preview.ExistingNegativeState.FirstNegativeVisitDate);
        Assert.True(result.Preview.RequiresNegativeHandlingDecision);
        Assert.False(result.Preview.CanProceedToIssue);
    }

    [PostgreSqlFact]
    public async Task MultipleNegativeCandidatesFailUntilOnlyOneActiveCandidateRemains()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedPreviewFixtureAsync(database, owner.AccountId.Value);
        var firstMembership = await InsertIssuedMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value,
            issuedAt: TestNow.AddDays(-2));
        var secondMembership = await InsertIssuedMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value,
            issuedAt: TestNow.AddDays(-1));
        await InsertCacheAsync(
            database,
            firstMembership,
            remainingVisits: -1,
            firstNegativeVisitDate: new DateOnly(2026, 7, 18));
        await InsertCacheAsync(
            database,
            secondMembership,
            remainingVisits: -2,
            firstNegativeVisitDate: new DateOnly(2026, 7, 22));
        var handler = CreateHandler(dbContext);

        var ambiguous = await handler.ExecuteAsync(
            Query(owner, fixture),
            CancellationToken.None);

        Assert.Equal(PreviewIssueMembershipStatus.ValidationFailed, ambiguous.Status);
        Assert.Equal("validation_failed", ambiguous.ErrorCode);
        Assert.Equal("clientId", ambiguous.ErrorField);
        Assert.Contains("Multiple active memberships", ambiguous.ErrorMessage);
        Assert.Null(ambiguous.Preview);
        Assert.Empty(ambiguous.AllowedActions.Items);

        await UpdateMembershipStatusAsync(
            database,
            secondMembership.MembershipId,
            "canceled");
        var unambiguous = await handler.ExecuteAsync(
            Query(owner, fixture),
            CancellationToken.None);

        AssertSuccessful(unambiguous);
        Assert.Equal(1, unambiguous.Preview!.ExistingNegativeState!.NegativeBalance);
        Assert.Equal(
            new DateOnly(2026, 7, 18),
            unambiguous.Preview.ExistingNegativeState.FirstNegativeVisitDate);
    }

    [PostgreSqlFact]
    public async Task MissingStaleAndInconsistentCacheFailWithoutRepairingOnRead()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedPreviewFixtureAsync(database, owner.AccountId.Value);
        var existingMembership = await InsertIssuedMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value);
        var handler = CreateHandler(dbContext);

        var missing = await handler.ExecuteAsync(
            Query(owner, fixture),
            CancellationToken.None);
        AssertRecalculationFailed(missing);
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));

        await InsertCacheAsync(
            database,
            existingMembership,
            remainingVisits: -1,
            firstNegativeVisitDate: new DateOnly(2026, 7, 20),
            recalculationVersion: 1);
        var staleBefore = await ReadCacheFingerprintAsync(
            database,
            existingMembership.MembershipId);
        var stale = await handler.ExecuteAsync(
            Query(owner, fixture),
            CancellationToken.None);
        AssertRecalculationFailed(stale);
        Assert.Equal(
            staleBefore,
            await ReadCacheFingerprintAsync(database, existingMembership.MembershipId));

        await UpdateCacheVersionAndEffectiveEndAsync(
            database,
            existingMembership.MembershipId,
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            ExistingBaseEndDate.AddDays(1));
        var inconsistentBefore = await ReadCacheFingerprintAsync(
            database,
            existingMembership.MembershipId);
        var inconsistent = await handler.ExecuteAsync(
            Query(owner, fixture),
            CancellationToken.None);

        AssertRecalculationFailed(inconsistent);
        Assert.Equal(
            inconsistentBefore,
            await ReadCacheFingerprintAsync(database, existingMembership.MembershipId));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InvalidMissingInactiveAndDecisionInputsReturnStableFailures()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedPreviewFixtureAsync(database, owner.AccountId.Value);
        var inactiveTypeId = await InsertMembershipTypeAsync(database, isActive: false);
        var handler = CreateHandler(dbContext);

        var emptyClient = await handler.ExecuteAsync(
            new PreviewIssueMembershipQuery(
                owner,
                Guid.Empty,
                fixture.MembershipTypeId,
                ProposedStartDate),
            CancellationToken.None);
        var emptyType = await handler.ExecuteAsync(
            new PreviewIssueMembershipQuery(
                owner,
                fixture.ClientId,
                Guid.Empty,
                ProposedStartDate),
            CancellationToken.None);
        var emptyDate = await handler.ExecuteAsync(
            new PreviewIssueMembershipQuery(
                owner,
                fixture.ClientId,
                fixture.MembershipTypeId,
                default),
            CancellationToken.None);
        var invalidDecision = await handler.ExecuteAsync(
            new PreviewIssueMembershipQuery(
                owner,
                fixture.ClientId,
                fixture.MembershipTypeId,
                ProposedStartDate,
                (MembershipNegativeHandlingDecision)999),
            CancellationToken.None);
        var missingClient = await handler.ExecuteAsync(
            new PreviewIssueMembershipQuery(
                owner,
                Guid.NewGuid(),
                fixture.MembershipTypeId,
                ProposedStartDate),
            CancellationToken.None);
        var missingType = await handler.ExecuteAsync(
            new PreviewIssueMembershipQuery(
                owner,
                fixture.ClientId,
                Guid.NewGuid(),
                ProposedStartDate),
            CancellationToken.None);
        var inactiveType = await handler.ExecuteAsync(
            new PreviewIssueMembershipQuery(
                owner,
                fixture.ClientId,
                inactiveTypeId,
                ProposedStartDate),
            CancellationToken.None);
        var unnecessaryDecision = await handler.ExecuteAsync(
            Query(
                owner,
                fixture,
                MembershipNegativeHandlingDecision.LeaveVisible),
            CancellationToken.None);

        AssertValidationFailure(emptyClient, "clientId");
        AssertValidationFailure(emptyType, "membershipTypeId");
        AssertValidationFailure(emptyDate, "proposedStartDate");
        AssertValidationFailure(invalidDecision, "negativeHandlingDecision");
        Assert.Equal(PreviewIssueMembershipStatus.NotFound, missingClient.Status);
        Assert.Equal("not_found", missingClient.ErrorCode);
        Assert.Equal("clientId", missingClient.ErrorField);
        Assert.Equal(PreviewIssueMembershipStatus.NotFound, missingType.Status);
        Assert.Equal("not_found", missingType.ErrorCode);
        Assert.Equal("membershipTypeId", missingType.ErrorField);
        Assert.Equal(
            PreviewIssueMembershipStatus.MembershipTypeInactive,
            inactiveType.Status);
        Assert.Equal("membership_type_inactive", inactiveType.ErrorCode);
        Assert.Equal("membershipTypeId", inactiveType.ErrorField);
        AssertValidationFailure(unnecessaryDecision, "negativeHandlingDecision");
        Assert.All(
            new[]
            {
                emptyClient,
                emptyType,
                emptyDate,
                invalidDecision,
                missingClient,
                missingType,
                inactiveType,
                unnecessaryDecision,
            },
            result =>
            {
                Assert.Null(result.Preview);
                Assert.Empty(result.AllowedActions.Items);
            });
    }

    [PostgreSqlFact]
    public async Task CalendarOverflowReturnsProposedStartDateValidationFailure()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var clientId = await InsertClientAsync(database, owner.AccountId.Value);
        var membershipTypeId = await InsertMembershipTypeAsync(
            database,
            durationDays: 2);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new PreviewIssueMembershipQuery(
                owner,
                clientId,
                membershipTypeId,
                DateOnly.MaxValue),
            CancellationToken.None);

        AssertValidationFailure(result, "proposedStartDate");
        Assert.Contains("supported calendar range", result.ErrorMessage);
    }

    [PostgreSqlFact]
    public async Task InactiveExpiredEndedUnknownAndForgedActorsAreDenied()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedPreviewFixtureAsync(database, owner.AccountId.Value);
        var inactiveAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            isActive: false);
        var expiredAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            sessionExpiresAt: TestNow.AddMinutes(-1));
        var endedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            sessionEndedAt: TestNow.AddMinutes(-1));
        var namedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var forgedOwner = namedAdmin with
        {
            Role = ActorRole.Owner,
            AccountKind = AccountKind.Owner,
        };
        var unknownOwner = new ActorContext(
            AccountId.New(),
            ActorRole.Owner,
            AccountKind.Owner,
            SessionId.New(),
            "unknown device");
        var invalidActor = new ActorContext(
            new AccountId(Guid.Empty),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(Guid.Empty),
            null);
        var handler = CreateHandler(dbContext);

        foreach (var actor in new[]
                 {
                     inactiveAdmin,
                     expiredAdmin,
                     endedAdmin,
                     forgedOwner,
                     unknownOwner,
                     invalidActor,
                 })
        {
            var result = await handler.ExecuteAsync(
                Query(actor, fixture),
                CancellationToken.None);

            Assert.Equal(PreviewIssueMembershipStatus.PermissionDenied, result.Status);
            Assert.Equal("permission_denied", result.ErrorCode);
            Assert.Null(result.Preview);
            Assert.Empty(result.AllowedActions.Items);
        }
    }

    [Fact]
    public void PersistenceRegistrationExposesScopedIssuePreviewHandler()
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

        var serviceType = typeof(IBodyLifeQueryHandler<
            PreviewIssueMembershipQuery,
            PreviewIssueMembershipResult>);
        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == serviceType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(PreviewIssueMembershipQueryHandler), descriptor.ImplementationType);
    }

    private static PreviewIssueMembershipQueryHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new PreviewIssueMembershipQueryHandler(
            dbContext,
            new FixedTimeProvider(TestNow));
    }

    private static PreviewIssueMembershipQuery Query(
        ActorContext actor,
        PreviewFixture fixture,
        MembershipNegativeHandlingDecision? decision = null)
    {
        return new PreviewIssueMembershipQuery(
            actor,
            fixture.ClientId,
            fixture.MembershipTypeId,
            ProposedStartDate,
            decision);
    }

    private static void AssertSuccessful(PreviewIssueMembershipResult result)
    {
        Assert.Equal(PreviewIssueMembershipStatus.Success, result.Status);
        Assert.NotNull(result.Preview);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
    }

    private static void AssertValidationFailure(
        PreviewIssueMembershipResult result,
        string errorField)
    {
        Assert.Equal(PreviewIssueMembershipStatus.ValidationFailed, result.Status);
        Assert.Equal("validation_failed", result.ErrorCode);
        Assert.Equal(errorField, result.ErrorField);
        Assert.Null(result.Preview);
        Assert.Empty(result.AllowedActions.Items);
    }

    private static void AssertRecalculationFailed(PreviewIssueMembershipResult result)
    {
        Assert.Equal(PreviewIssueMembershipStatus.RecalculationFailed, result.Status);
        Assert.Equal("recalculation_failed", result.ErrorCode);
        Assert.Null(result.Preview);
        Assert.Empty(result.AllowedActions.Items);
    }

    private static async Task<ActorContext> SeedActorAsync(
        PostgreSqlTestDatabase database,
        ActorRole role,
        AccountKind accountKind,
        bool isActive = true,
        DateTimeOffset? sessionExpiresAt = null,
        DateTimeOffset? sessionEndedAt = null,
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
                @ended_at,
                @last_seen_at)
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("display_name", $"{accountKind} preview actor");
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
            sessionExpiresAt ?? TestNow.AddHours(10));
        command.Parameters.Add("ended_at", NpgsqlDbType.TimestampTz).Value =
            sessionEndedAt ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        return new ActorContext(
            new AccountId(accountId),
            role,
            accountKind,
            new SessionId(sessionId),
            deviceLabel);
    }

    private static async Task<PreviewFixture> SeedPreviewFixtureAsync(
        PostgreSqlTestDatabase database,
        Guid createdByAccountId)
    {
        var clientId = await InsertClientAsync(database, createdByAccountId);
        var membershipTypeId = await InsertMembershipTypeAsync(database);
        return new PreviewFixture(clientId, membershipTypeId);
    }

    private static async Task<Guid> InsertClientAsync(
        PostgreSqlTestDatabase database,
        Guid createdByAccountId)
    {
        var clientId = Guid.NewGuid();
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
                'Preview',
                'Client',
                null,
                'PREVIEW CLIENT',
                null,
                null,
                null,
                null,
                'active',
                @recorded_at,
                @created_by_account_id,
                @recorded_at)
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddDays(-5));
        command.Parameters.AddWithValue("created_by_account_id", createdByAccountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return clientId;
    }

    private static async Task<Guid> InsertMembershipTypeAsync(
        PostgreSqlTestDatabase database,
        bool isActive = true,
        int durationDays = 30)
    {
        var membershipTypeId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
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
                @membership_type_id,
                'Eight visits / 30 days',
                @duration_days,
                8,
                1200,
                'UAH',
                @is_active,
                'Reception issue option',
                @created_at,
                @updated_at,
                @deactivated_at)
            """;
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("duration_days", durationDays);
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-10));
        command.Parameters.AddWithValue("updated_at", TestNow.AddDays(-1));
        command.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value = isActive
            ? DBNull.Value
            : TestNow.AddDays(-1);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return membershipTypeId;
    }

    private static async Task<IssuedMembershipFixture> InsertIssuedMembershipAsync(
        PostgreSqlTestDatabase database,
        PreviewFixture fixture,
        Guid issuedByAccountId,
        string status = "active",
        DateTimeOffset? issuedAt = null)
    {
        var membershipId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
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
                @membership_id,
                @client_id,
                @membership_type_id,
                'Historical two visits / 30 days',
                30,
                2,
                900,
                'UAH',
                @start_date,
                @base_end_date,
                @issued_at,
                @issued_by_account_id,
                @status,
                'normal',
                null,
                null)
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, ExistingStartDate);
        command.Parameters.AddWithValue("base_end_date", NpgsqlDbType.Date, ExistingBaseEndDate);
        command.Parameters.AddWithValue("issued_at", issuedAt ?? TestNow.AddDays(-3));
        command.Parameters.AddWithValue("issued_by_account_id", issuedByAccountId);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return new IssuedMembershipFixture(membershipId, ExistingBaseEndDate);
    }

    private static async Task InsertCacheAsync(
        PostgreSqlTestDatabase database,
        IssuedMembershipFixture membership,
        int remainingVisits,
        DateOnly? firstNegativeVisitDate,
        int recalculationVersion = MembershipStateCacheRebuilder.CurrentRecalculationVersion)
    {
        var negativeBalance = Math.Max(0, -remainingVisits);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                @counted_visits,
                @remaining_visits,
                @negative_balance,
                null,
                @first_negative_visit_date,
                0,
                @effective_end_date,
                @last_counted_visit_at,
                @recalculated_at,
                @recalculation_version)
            """;
        command.Parameters.AddWithValue("membership_id", membership.MembershipId);
        command.Parameters.AddWithValue("counted_visits", 2 - remainingVisits);
        command.Parameters.AddWithValue("remaining_visits", remainingVisits);
        command.Parameters.AddWithValue("negative_balance", negativeBalance);
        command.Parameters.Add("first_negative_visit_date", NpgsqlDbType.Date).Value =
            firstNegativeVisitDate ?? (object)DBNull.Value;
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            membership.BaseEndDate);
        command.Parameters.AddWithValue("last_counted_visit_at", TestNow.AddDays(-1));
        command.Parameters.AddWithValue("recalculated_at", TestNow.AddMinutes(-10));
        command.Parameters.AddWithValue("recalculation_version", recalculationVersion);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task UpdateMembershipStatusAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        string status)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.issued_memberships
            set status = @status
            where id = @membership_id
            """;
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("membership_id", membershipId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task UpdateCacheVersionAndEffectiveEndAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        int recalculationVersion,
        DateOnly effectiveEndDate)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_state_cache
            set recalculation_version = @recalculation_version,
                effective_end_date = @effective_end_date
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("recalculation_version", recalculationVersion);
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            effectiveEndDate);
        command.Parameters.AddWithValue("membership_id", membershipId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<CacheFingerprint> ReadCacheFingerprintAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select remaining_visits,
                   negative_balance,
                   effective_end_date,
                   recalculated_at,
                   recalculation_version
            from bodylife.membership_state_cache
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new CacheFingerprint(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetFieldValue<DateOnly>(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetInt32(4));
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{tableName}");
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

    private sealed record PreviewFixture(Guid ClientId, Guid MembershipTypeId);

    private sealed record IssuedMembershipFixture(Guid MembershipId, DateOnly BaseEndDate);

    private sealed record CacheFingerprint(
        int RemainingVisits,
        int NegativeBalance,
        DateOnly EffectiveEndDate,
        DateTimeOffset RecalculatedAt,
        int RecalculationVersion);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
