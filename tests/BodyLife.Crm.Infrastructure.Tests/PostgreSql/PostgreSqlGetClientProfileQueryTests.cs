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

public sealed class PostgreSqlGetClientProfileQueryTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SeedCreatedAt = TestNow.AddDays(-10);
    private static readonly DateTimeOffset SeedUpdatedAt = TestNow.AddDays(-2);
    private static readonly DateTimeOffset SeedAssignedAt = TestNow.AddDays(-5);

    [PostgreSqlFact]
    public async Task AcceptedActorsReadCanonicalIdentityCardVersionAndImplementedActions()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var namedAdmin = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var sharedAdmin = await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            owner.AccountId.Value,
            "Profile",
            "Client",
            "Middle",
            "+38 (067) 123-45-67",
            "Reception comment");
        var assignmentId = await InsertCardAsync(
            database,
            clientId,
            owner.AccountId.Value,
            "bl - profile");
        var membershipAsOfDate = new DateOnly(2026, 7, 12);
        var handler = CreateHandler(dbContext);
        var results = new List<GetClientProfileResult>();

        foreach (var actor in new[] { owner, namedAdmin, sharedAdmin })
        {
            results.Add(await handler.ExecuteAsync(
                new GetClientProfileQuery(actor, clientId, membershipAsOfDate),
                CancellationToken.None));
        }

        Assert.All(results, AssertSuccessful);
        var profile = results[0].Profile!;
        Assert.Equal(clientId, profile.ClientId);
        Assert.Equal("Profile", profile.Surname);
        Assert.Equal("Client", profile.Name);
        Assert.Equal("Middle", profile.Patronymic);
        Assert.Equal("Profile Client Middle", profile.DisplayName);
        Assert.Equal("+38 (067) 123-45-67", profile.Phone);
        Assert.Equal("Reception comment", profile.Comment);
        Assert.Equal(ClientOperationalStatus.Active, profile.OperationalStatus);
        Assert.Equal(SeedCreatedAt, profile.CreatedAt);
        Assert.Equal(SeedUpdatedAt, profile.UpdatedAt);
        Assert.Equal(membershipAsOfDate, profile.MembershipAsOfDate);
        Assert.NotNull(profile.CurrentCard);
        Assert.Equal(assignmentId, profile.CurrentCard.AssignmentId);
        Assert.Equal("bl - profile", profile.CurrentCard.CardNumber);
        Assert.Equal(SeedAssignedAt, profile.CurrentCard.AssignedAt);
        Assert.Null(profile.Membership.CurrentMembership);
        Assert.Empty(profile.Membership.Timeline);
        Assert.Empty(profile.Membership.Warnings);
        Assert.Empty(profile.Warnings);
        Assert.Equal(2, profile.AllowedActions.Items.Count);
        Assert.True(profile.AllowedActions.IsAllowed(ClientProfileActionKeys.UpdateClient));
        Assert.True(profile.AllowedActions.IsAllowed(ClientProfileActionKeys.AssignOrChangeCard));
        Assert.All(
            profile.AllowedActions.Items,
            permission => Assert.Equal(
                ClientProfileActionKeys.AdminOrOwnerPolicy,
                permission.RequiredPolicy));
        Assert.Equal(
            TestNow.AddMinutes(-5).UtcDateTime,
            await ReadSessionLastSeenAsync(database, owner.SessionId.Value));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InactiveClientWithoutCardReturnsBothClientsOwnedWarnings()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Inactive",
            "Client",
            operationalStatus: "inactive");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientProfileQuery(actor, clientId),
            CancellationToken.None);

        AssertSuccessful(result);
        var profile = result.Profile!;
        Assert.Equal(ClientOperationalStatus.Inactive, profile.OperationalStatus);
        Assert.Null(profile.CurrentCard);
        Assert.Equal(
            new[] { "client_inactive", "no_current_card" },
            profile.Warnings.Select(warning => warning.Code));
        Assert.Null(profile.MembershipAsOfDate);
    }

    [PostgreSqlFact]
    public async Task HistoricalCardIsNotExposedAsCurrentProfileState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database, clientId, actor.AccountId.Value, "Historical", "Card");
        await InsertCardAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "BL-HISTORICAL",
            isCurrent: false);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientProfileQuery(actor, clientId),
            CancellationToken.None);

        AssertSuccessful(result);
        Assert.Null(result.Profile!.CurrentCard);
        Assert.Contains(result.Profile.Warnings, warning => warning.Code == "no_current_card");
    }

    [PostgreSqlFact]
    public async Task InactiveExpiredAndMismatchedActorsAreDeniedWithoutProfileData()
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
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database, clientId, validActor.AccountId.Value, "Protected", "Profile");
        var handler = CreateHandler(dbContext);
        var results = new List<GetClientProfileResult>();

        foreach (var actor in new[] { inactiveActor, expiredActor, mismatchedActor })
        {
            results.Add(await handler.ExecuteAsync(
                new GetClientProfileQuery(actor, clientId),
                CancellationToken.None));
        }

        Assert.All(results, result =>
        {
            Assert.Equal(GetClientProfileStatus.PermissionDenied, result.Status);
            Assert.Equal("permission_denied", result.ErrorCode);
            Assert.Null(result.Profile);
        });
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
    }

    [PostgreSqlFact]
    public async Task EmptyMissingAndUnsupportedCompositionRequestsReturnStableErrors()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var handler = CreateHandler(dbContext);

        var emptyId = await handler.ExecuteAsync(
            new GetClientProfileQuery(actor, Guid.Empty),
            CancellationToken.None);
        var missing = await handler.ExecuteAsync(
            new GetClientProfileQuery(actor, Guid.NewGuid()),
            CancellationToken.None);
        var history = await handler.ExecuteAsync(
            new GetClientProfileQuery(actor, Guid.NewGuid(), IncludeHistory: true),
            CancellationToken.None);
        var drillDowns = await handler.ExecuteAsync(
            new GetClientProfileQuery(actor, Guid.NewGuid(), IncludeDrillDowns: true),
            CancellationToken.None);

        Assert.Equal(GetClientProfileStatus.ValidationFailed, emptyId.Status);
        Assert.Equal("clientId", emptyId.ErrorField);
        Assert.Equal(GetClientProfileStatus.NotFound, missing.Status);
        Assert.Equal("not_found", missing.ErrorCode);
        Assert.Equal(GetClientProfileStatus.ValidationFailed, history.Status);
        Assert.Equal("includeHistory", history.ErrorField);
        Assert.Equal(GetClientProfileStatus.ValidationFailed, drillDowns.Status);
        Assert.Equal("includeDrillDowns", drillDowns.ErrorField);
        Assert.All(new[] { emptyId, missing, history, drillDowns }, result => Assert.Null(result.Profile));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ProfileRereadsCanonicalStateAfterIdentityAndCardCommands()
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
            "Before",
            "Client",
            phone: "067 111 22 33");
        var oldAssignmentId = await InsertCardAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "BL-BEFORE");
        var auditAppender = new BusinessAuditAppender(dbContext);
        var timeProvider = new FixedTimeProvider(TestNow);
        var updateResult = await new UpdateClientCommandHandler(
            dbContext,
            new FindClientDuplicateCandidatesQueryHandler(dbContext),
            auditAppender,
            timeProvider).ExecuteAsync(
                new UpdateClientCommand(
                    CommandEnvelope(actor, "profile-update"),
                    clientId,
                    SeedUpdatedAt,
                    "After",
                    "Client",
                    "Middle",
                    "050 999 88 77",
                    "Updated comment",
                    ClientOperationalStatus.Active,
                    []),
                CancellationToken.None);
        var cardResult = await new AssignOrChangeCardCommandHandler(
            dbContext,
            auditAppender,
            timeProvider).ExecuteAsync(
                new AssignOrChangeCardCommand(
                    CommandEnvelope(actor, "profile-card", reason: "Damaged card"),
                    clientId,
                    oldAssignmentId,
                    "BL-AFTER",
                    ClearCurrentCard: false),
                CancellationToken.None);
        var auditCountBeforeQuery = await CountRowsAsync(database, "business_audit_entries");
        var idempotencyCountBeforeQuery = await CountRowsAsync(database, "command_idempotency_keys");

        var profileResult = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientProfileQuery(actor, clientId),
            CancellationToken.None);

        Assert.Equal(CommandStatus.Success, updateResult.Status);
        Assert.Equal(CommandStatus.Success, cardResult.Status);
        AssertSuccessful(profileResult);
        var profile = profileResult.Profile!;
        Assert.Equal("After Client Middle", profile.DisplayName);
        Assert.Equal("050 999 88 77", profile.Phone);
        Assert.Equal("Updated comment", profile.Comment);
        Assert.Equal(TestNow, profile.UpdatedAt);
        Assert.NotNull(profile.CurrentCard);
        Assert.NotEqual(oldAssignmentId, profile.CurrentCard.AssignmentId);
        Assert.Equal("BL-AFTER", profile.CurrentCard.CardNumber);
        Assert.Equal(TestNow, profile.CurrentCard.AssignedAt);
        Assert.Equal(updateResult.PrimaryEntityId, updateResult.RereadTargetId);
        Assert.Equal(cardResult.PrimaryEntityId, cardResult.RereadTargetId);
        Assert.Equal(2L, auditCountBeforeQuery);
        Assert.Equal(2L, idempotencyCountBeforeQuery);
        Assert.Equal(auditCountBeforeQuery, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(idempotencyCountBeforeQuery, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static GetClientProfileQueryHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new GetClientProfileQueryHandler(dbContext, new FixedTimeProvider(TestNow));
    }

    private static CommandEnvelope CommandEnvelope(
        ActorContext actor,
        string idempotencyKey,
        string? reason = null)
    {
        return new CommandEnvelope(
            actor,
            new RequestCorrelationId($"correlation-{idempotencyKey}"),
            EntryOrigin.Normal,
            TestNow,
            idempotencyKey,
            reason,
            Comment: null);
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
            accountCommand.Parameters.AddWithValue("display_name", $"{accountKind} profile actor");
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
        string? phone = null,
        string? comment = null,
        string operationalStatus = "active")
    {
        var normalizedPhone = phone is null ? null : ClientSearchNormalizer.NormalizePhone(phone);
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
        command.Parameters.AddWithValue(
            "normalized_full_name",
            ClientSearchNormalizer.NormalizeFullName(surname, name, patronymic));
        command.Parameters.Add("phone_raw", NpgsqlDbType.Text).Value = phone ?? (object)DBNull.Value;
        command.Parameters.Add("phone_normalized", NpgsqlDbType.Text).Value =
            normalizedPhone ?? (object)DBNull.Value;
        command.Parameters.Add("phone_last4", NpgsqlDbType.Text).Value = phoneLastFour ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("operational_status", operationalStatus);
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value = comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("created_at", SeedCreatedAt);
        command.Parameters.AddWithValue("created_by_account_id", actorAccountId);
        command.Parameters.AddWithValue("updated_at", SeedUpdatedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> InsertCardAsync(
        PostgreSqlTestDatabase database,
        Guid clientId,
        Guid actorAccountId,
        string cardNumber,
        bool isCurrent = true)
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
                @ended_at,
                @ended_by_account_id,
                @end_reason,
                @is_current)
            """;
        command.Parameters.AddWithValue("id", assignmentId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("card_number_raw", cardNumber);
        command.Parameters.AddWithValue(
            "card_number_normalized",
            ClientSearchNormalizer.NormalizeCardNumber(cardNumber));
        command.Parameters.AddWithValue("assigned_at", SeedAssignedAt);
        command.Parameters.AddWithValue("assigned_by_account_id", actorAccountId);
        command.Parameters.Add("ended_at", NpgsqlDbType.TimestampTz).Value = isCurrent
            ? DBNull.Value
            : SeedAssignedAt.AddDays(1);
        command.Parameters.Add("ended_by_account_id", NpgsqlDbType.Uuid).Value = isCurrent
            ? DBNull.Value
            : actorAccountId;
        command.Parameters.Add("end_reason", NpgsqlDbType.Text).Value = isCurrent
            ? DBNull.Value
            : "Historical profile card";
        command.Parameters.AddWithValue("is_current", isCurrent);
        await command.ExecuteNonQueryAsync();
        return assignmentId;
    }

    private static async Task<DateTime> ReadSessionLastSeenAsync(
        PostgreSqlTestDatabase database,
        Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select last_seen_at from bodylife.sessions where id = @id";
        command.Parameters.AddWithValue("id", sessionId);
        return (DateTime)(await command.ExecuteScalarAsync())!;
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>($"select count(*) from bodylife.{tableName}");
    }

    private static void AssertSuccessful(GetClientProfileResult result)
    {
        Assert.Equal(GetClientProfileStatus.Success, result.Status);
        Assert.NotNull(result.Profile);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
