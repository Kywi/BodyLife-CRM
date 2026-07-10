using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlSearchClientsQueryTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 10, 16, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task AcceptedActorsCanSearchWhileInvalidCanonicalActorsAreDeniedWithoutWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var namedAdmin = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var sharedAdmin = await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin);
        await InsertClientAsync(database, Guid.NewGuid(), owner.AccountId.Value, "Allowed", "Search");
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
        var mismatchedActor = namedAdmin with { Role = ActorRole.Owner };
        var handler = CreateHandler(dbContext);

        var allowedResults = new List<SearchClientsResult>();

        foreach (var actor in new[] { owner, namedAdmin, sharedAdmin })
        {
            allowedResults.Add(await handler.ExecuteAsync(
                new SearchClientsQuery(actor, "Allowed", ClientSearchMode.Name),
                CancellationToken.None));
        }

        var deniedResults = new List<SearchClientsResult>();

        foreach (var actor in new[] { inactiveActor, expiredActor, mismatchedActor })
        {
            deniedResults.Add(await handler.ExecuteAsync(
                new SearchClientsQuery(actor, "Allowed", ClientSearchMode.Name),
                CancellationToken.None));
        }

        Assert.All(allowedResults, result =>
        {
            Assert.Equal(SearchClientsStatus.Success, result.Status);
            Assert.Single(result.Items);
            Assert.Null(result.ErrorCode);
        });
        Assert.All(deniedResults, result =>
        {
            Assert.Equal(SearchClientsStatus.PermissionDenied, result.Status);
            Assert.Equal("permission_denied", result.ErrorCode);
            Assert.Empty(result.Items);
            Assert.Null(result.AutoOpenClientId);
        });
        Assert.Equal(
            TestNow.AddMinutes(-5).UtcDateTime,
            await database.ExecuteScalarAsync<DateTime>(
                $"select last_seen_at from bodylife.sessions where id = '{owner.SessionId.Value}'"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ExactCurrentCardHasPriorityAndIsTheOnlyAutoOpenTarget()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin);
        var exactClientId = Guid.NewGuid();
        var partialClientId = Guid.NewGuid();
        var historicalClientId = Guid.NewGuid();
        await InsertClientAsync(database, exactClientId, actor.AccountId.Value, "Exact", "Card");
        await InsertClientAsync(database, partialClientId, actor.AccountId.Value, "Partial", "Card");
        await InsertClientAsync(database, historicalClientId, actor.AccountId.Value, "Historical", "Card");
        await InsertCardAsync(database, exactClientId, actor.AccountId.Value, "bl - 1001");
        await InsertCardAsync(database, partialClientId, actor.AccountId.Value, "XX-BL-1001-YY");
        await InsertCardAsync(
            database,
            historicalClientId,
            actor.AccountId.Value,
            "BL-1001",
            isCurrent: false);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new SearchClientsQuery(actor, "  bl - 1001  "),
            CancellationToken.None);

        AssertSuccessful(result);
        Assert.Equal(exactClientId, result.AutoOpenClientId);
        Assert.Equal(2, result.Items.Count);
        var exact = result.Items[0];
        Assert.Equal(exactClientId, exact.ClientId);
        Assert.Equal(ClientSearchMatchType.ExactCard, exact.MatchType);
        Assert.Equal(0, exact.MatchPriority);
        Assert.Equal("bl - 1001", exact.CurrentCardNumber);
        Assert.Null(exact.CurrentMembership);
        Assert.Empty(exact.Warnings);
        var partial = result.Items[1];
        Assert.Equal(partialClientId, partial.ClientId);
        Assert.Equal(ClientSearchMatchType.PartialCard, partial.MatchType);
        Assert.Equal(40, partial.MatchPriority);
        Assert.DoesNotContain(result.Items, item => item.ClientId == historicalClientId);
        Assert.Null(result.NextPageCursor);
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
    }

    [PostgreSqlFact]
    public async Task PartialCardMatchesAreDeterministicAndNeverAutoOpen()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var betaId = Guid.NewGuid();
        var alphaId = Guid.NewGuid();
        await InsertClientAsync(database, betaId, actor.AccountId.Value, "Beta", "Client");
        await InsertClientAsync(database, alphaId, actor.AccountId.Value, "Alpha", "Client");
        await InsertCardAsync(database, betaId, actor.AccountId.Value, "BL-1002");
        await InsertCardAsync(database, alphaId, actor.AccountId.Value, "BL-1001");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new SearchClientsQuery(actor, "100", ClientSearchMode.Card),
            CancellationToken.None);

        AssertSuccessful(result);
        Assert.Null(result.AutoOpenClientId);
        Assert.Equal(new[] { alphaId, betaId }, result.Items.Select(item => item.ClientId));
        Assert.All(result.Items, item =>
        {
            Assert.Equal(ClientSearchMatchType.PartialCard, item.MatchType);
            Assert.Equal(40, item.MatchPriority);
        });
    }

    [PostgreSqlFact]
    public async Task NameSearchRanksExactFullNameBeforeContainedName()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var exactId = Guid.NewGuid();
        var partialId = Guid.NewGuid();
        var reversedId = Guid.NewGuid();
        await InsertClientAsync(database, exactId, actor.AccountId.Value, "Adams", "Alice");
        await InsertClientAsync(database, partialId, actor.AccountId.Value, "Adams", "Alice", "Marie");
        await InsertClientAsync(database, reversedId, actor.AccountId.Value, "Alice", "Adams");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new SearchClientsQuery(actor, "  adams   alice ", ClientSearchMode.Name),
            CancellationToken.None);

        AssertSuccessful(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(exactId, result.Items[0].ClientId);
        Assert.Equal("Adams Alice", result.Items[0].DisplayName);
        Assert.Equal(ClientSearchMatchType.ExactName, result.Items[0].MatchType);
        Assert.Equal(30, result.Items[0].MatchPriority);
        Assert.Equal(partialId, result.Items[1].ClientId);
        Assert.Equal("Adams Alice Marie", result.Items[1].DisplayName);
        Assert.Equal(ClientSearchMatchType.PartialName, result.Items[1].MatchType);
        Assert.Equal(60, result.Items[1].MatchPriority);
        Assert.DoesNotContain(result.Items, item => item.ClientId == reversedId);
        Assert.All(result.Items, item =>
            Assert.Contains(item.Warnings, warning => warning.Code == "no_current_card"));
        Assert.Null(result.AutoOpenClientId);
    }

    [PostgreSqlFact]
    public async Task PhoneAndLastFourModesUseCanonicalPhoneColumns()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            firstId,
            actor.AccountId.Value,
            "Alpha",
            "Phone",
            phone: "+38 (067) 123-45-67");
        await InsertClientAsync(
            database,
            secondId,
            actor.AccountId.Value,
            "Beta",
            "Phone",
            phone: "050 000 45 67");
        var handler = CreateHandler(dbContext);

        var exactPhone = await handler.ExecuteAsync(
            new SearchClientsQuery(
                actor,
                "+38 067 123 45 67",
                ClientSearchMode.Phone),
            CancellationToken.None);
        var partialPhone = await handler.ExecuteAsync(
            new SearchClientsQuery(actor, "12345", ClientSearchMode.Phone),
            CancellationToken.None);
        var lastFour = await handler.ExecuteAsync(
            new SearchClientsQuery(actor, "45-67", ClientSearchMode.LastFour),
            CancellationToken.None);
        var autoLastFour = await handler.ExecuteAsync(
            new SearchClientsQuery(actor, "4567", ClientSearchMode.Auto),
            CancellationToken.None);

        Assert.Equal(firstId, Assert.Single(exactPhone.Items).ClientId);
        Assert.Equal(ClientSearchMatchType.ExactPhone, exactPhone.Items[0].MatchType);
        Assert.Equal(firstId, Assert.Single(partialPhone.Items).ClientId);
        Assert.Equal(ClientSearchMatchType.PartialPhone, partialPhone.Items[0].MatchType);
        Assert.Equal(new[] { firstId, secondId }, lastFour.Items.Select(item => item.ClientId));
        Assert.All(lastFour.Items, item => Assert.Equal(ClientSearchMatchType.PhoneLastFour, item.MatchType));
        Assert.Equal(new[] { firstId, secondId }, autoLastFour.Items.Select(item => item.ClientId));
        Assert.All(autoLastFour.Items, item => Assert.Equal(ClientSearchMatchType.PhoneLastFour, item.MatchType));
        Assert.Null(lastFour.AutoOpenClientId);
        Assert.Null(autoLastFour.AutoOpenClientId);
    }

    [PostgreSqlFact]
    public async Task InactiveClientsRequireExplicitInclusionAndCarryServerWarnings()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        await InsertClientAsync(database, activeId, actor.AccountId.Value, "Shared", "Name");
        await InsertClientAsync(
            database,
            inactiveId,
            actor.AccountId.Value,
            "Shared",
            "Name",
            operationalStatus: "inactive");
        var handler = CreateHandler(dbContext);

        var activeOnly = await handler.ExecuteAsync(
            new SearchClientsQuery(actor, "Shared Name", ClientSearchMode.Name),
            CancellationToken.None);
        var includingInactive = await handler.ExecuteAsync(
            new SearchClientsQuery(
                actor,
                "Shared Name",
                ClientSearchMode.Name,
                IncludeInactive: true),
            CancellationToken.None);

        Assert.Equal(activeId, Assert.Single(activeOnly.Items).ClientId);
        Assert.Equal(new[] { activeId, inactiveId }, includingInactive.Items.Select(item => item.ClientId));
        var inactive = includingInactive.Items[1];
        Assert.Equal(ClientOperationalStatus.Inactive, inactive.OperationalStatus);
        Assert.Contains(inactive.Warnings, warning => warning.Code == "client_inactive");
        Assert.Contains(inactive.Warnings, warning => warning.Code == "no_current_card");
    }

    [PostgreSqlFact]
    public async Task CursorPaginationPreservesStableOrderWithoutDuplicateRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin);
        var expectedIds = new List<Guid>();

        foreach (var suffix in new[] { "Alpha", "Beta", "Delta", "Gamma", "Omega" })
        {
            var clientId = Guid.NewGuid();
            expectedIds.Add(clientId);
            await InsertClientAsync(database, clientId, actor.AccountId.Value, "Page", suffix);
        }

        var handler = CreateHandler(dbContext);
        var firstPage = await handler.ExecuteAsync(
            new SearchClientsQuery(actor, "Page", ClientSearchMode.Name, Limit: 2),
            CancellationToken.None);
        var secondPage = await handler.ExecuteAsync(
            new SearchClientsQuery(
                actor,
                "Page",
                ClientSearchMode.Name,
                Limit: 2,
                PageCursor: firstPage.NextPageCursor),
            CancellationToken.None);
        var thirdPage = await handler.ExecuteAsync(
            new SearchClientsQuery(
                actor,
                "Page",
                ClientSearchMode.Name,
                Limit: 2,
                PageCursor: secondPage.NextPageCursor),
            CancellationToken.None);

        Assert.Equal("2", firstPage.NextPageCursor);
        Assert.Equal("4", secondPage.NextPageCursor);
        Assert.Null(thirdPage.NextPageCursor);
        var actualIds = firstPage.Items
            .Concat(secondPage.Items)
            .Concat(thirdPage.Items)
            .Select(item => item.ClientId)
            .ToArray();
        Assert.Equal(expectedIds, actualIds);
        Assert.Equal(5, actualIds.Distinct().Count());
    }

    [PostgreSqlFact]
    public async Task InvalidSearchInputsReturnValidationResultsWithoutDatabaseWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var handler = CreateHandler(dbContext);
        var queries = new[]
        {
            new SearchClientsQuery(actor, " "),
            new SearchClientsQuery(actor, "valid", (ClientSearchMode)999),
            new SearchClientsQuery(actor, "valid", Limit: 0),
            new SearchClientsQuery(actor, "valid", Limit: 51),
            new SearchClientsQuery(actor, "valid", PageCursor: "-1"),
            new SearchClientsQuery(actor, "valid", PageCursor: "not-a-cursor"),
            new SearchClientsQuery(actor, "12345", ClientSearchMode.LastFour),
            new SearchClientsQuery(actor, "phone-text", ClientSearchMode.Phone),
            new SearchClientsQuery(actor, "bad\u0001card", ClientSearchMode.Card),
        };

        var results = new List<SearchClientsResult>();

        foreach (var query in queries)
        {
            results.Add(await handler.ExecuteAsync(query, CancellationToken.None));
        }

        Assert.All(results, result =>
        {
            Assert.Equal(SearchClientsStatus.ValidationFailed, result.Status);
            Assert.Equal("validation_failed", result.ErrorCode);
            Assert.NotNull(result.ErrorField);
            Assert.Empty(result.Items);
            Assert.Null(result.AutoOpenClientId);
            Assert.Null(result.NextPageCursor);
        });
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task NoMatchReturnsSuccessfulEmptyResult()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        await InsertClientAsync(database, Guid.NewGuid(), actor.AccountId.Value, "Known", "Client");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new SearchClientsQuery(actor, "Missing", ClientSearchMode.Name),
            CancellationToken.None);

        AssertSuccessful(result);
        Assert.Empty(result.Items);
        Assert.Null(result.AutoOpenClientId);
        Assert.Null(result.NextPageCursor);
    }

    private static SearchClientsQueryHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new SearchClientsQueryHandler(dbContext, new FixedTimeProvider(TestNow));
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
            accountCommand.Parameters.AddWithValue("display_name", $"{accountKind} search actor");
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
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-5));
        command.Parameters.AddWithValue("created_by_account_id", actorAccountId);
        command.Parameters.AddWithValue("updated_at", TestNow.AddDays(-5));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertCardAsync(
        PostgreSqlTestDatabase database,
        Guid clientId,
        Guid actorAccountId,
        string cardNumber,
        bool isCurrent = true)
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
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("card_number_raw", cardNumber);
        command.Parameters.AddWithValue(
            "card_number_normalized",
            ClientSearchNormalizer.NormalizeCardNumber(cardNumber));
        command.Parameters.AddWithValue("assigned_at", TestNow.AddDays(-2));
        command.Parameters.AddWithValue("assigned_by_account_id", actorAccountId);
        command.Parameters.Add("ended_at", NpgsqlDbType.TimestampTz).Value = isCurrent
            ? DBNull.Value
            : TestNow.AddDays(-1);
        command.Parameters.Add("ended_by_account_id", NpgsqlDbType.Uuid).Value = isCurrent
            ? DBNull.Value
            : actorAccountId;
        command.Parameters.Add("end_reason", NpgsqlDbType.Text).Value = isCurrent
            ? DBNull.Value
            : "Historical test card";
        command.Parameters.AddWithValue("is_current", isCurrent);
        await command.ExecuteNonQueryAsync();
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>($"select count(*) from bodylife.{tableName}");
    }

    private static void AssertSuccessful(SearchClientsResult result)
    {
        Assert.Equal(SearchClientsStatus.Success, result.Status);
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
