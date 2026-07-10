using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Clients.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlClientDuplicateCandidateQueryTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 10, 12, 15, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryReturnsPhoneAndNameWarningsWithoutWritingHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var bothMatchId = Guid.Parse("00000000-0000-0000-0000-000000000101");
        var phoneMatchId = Guid.Parse("00000000-0000-0000-0000-000000000102");
        var nameMatchId = Guid.Parse("00000000-0000-0000-0000-000000000103");

        await InsertClientAsync(
            database.ConnectionString,
            bothMatchId,
            actorAccountId,
            "Іваненко",
            "Олексій",
            "Петрович",
            "+38 (067) 123-45-67",
            isActive: true);
        await InsertClientAsync(
            database.ConnectionString,
            phoneMatchId,
            actorAccountId,
            "Петренко",
            "Марія",
            patronymic: null,
            "38 067 123 45 67",
            isActive: false);
        await InsertClientAsync(
            database.ConnectionString,
            nameMatchId,
            actorAccountId,
            "Іваненко",
            "Олексій",
            "Петрович",
            "050 000 99 99",
            isActive: true);
        await InsertClientAsync(
            database.ConnectionString,
            Guid.Parse("00000000-0000-0000-0000-000000000104"),
            actorAccountId,
            "Коваль",
            "Ірина",
            patronymic: null,
            "063 111 22 33",
            isActive: true);
        var acknowledgementCountBefore = await CountAcknowledgementsAsync(database);
        var auditCountBefore = await CountAuditEntriesAsync(database);
        var handler = new FindClientDuplicateCandidatesQueryHandler(dbContext);

        var candidates = await handler.ExecuteAsync(
            new FindClientDuplicateCandidatesQuery(
                Surname: "  іваненко ",
                Name: "олексій",
                Patronymic: "петрович",
                Phone: "+38 067 123 45 67"),
            CancellationToken.None);

        Assert.Collection(
            candidates,
            candidate => AssertCandidate(
                candidate,
                bothMatchId,
                ClientDuplicateWarningType.DuplicatePhone,
                isActive: true),
            candidate => AssertCandidate(
                candidate,
                phoneMatchId,
                ClientDuplicateWarningType.DuplicatePhone,
                isActive: false),
            candidate => AssertCandidate(
                candidate,
                bothMatchId,
                ClientDuplicateWarningType.SimilarName,
                isActive: true),
            candidate => AssertCandidate(
                candidate,
                nameMatchId,
                ClientDuplicateWarningType.SimilarName,
                isActive: true));
        Assert.Equal(acknowledgementCountBefore, await CountAcknowledgementsAsync(database));
        Assert.Equal(auditCountBefore, await CountAuditEntriesAsync(database));
    }

    [PostgreSqlFact]
    public async Task QueryExcludesTargetClientDuringUpdateCheck()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var targetClientId = Guid.Parse("00000000-0000-0000-0000-000000000201");
        var matchedClientId = Guid.Parse("00000000-0000-0000-0000-000000000202");
        await InsertClientAsync(
            database.ConnectionString,
            targetClientId,
            actorAccountId,
            "Іваненко",
            "Олексій",
            patronymic: null,
            "067 123 45 67",
            isActive: true);
        await InsertClientAsync(
            database.ConnectionString,
            matchedClientId,
            actorAccountId,
            "Іваненко",
            "Олексій",
            patronymic: null,
            "067 123 45 67",
            isActive: true);
        var handler = new FindClientDuplicateCandidatesQueryHandler(dbContext);

        var candidates = await handler.ExecuteAsync(
            new FindClientDuplicateCandidatesQuery(
                "Іваненко",
                "Олексій",
                Patronymic: null,
                "0671234567",
                ExcludedClientId: targetClientId),
            CancellationToken.None);

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Equal(matchedClientId, candidate.MatchedClientId));
        Assert.Equal(
            [ClientDuplicateWarningType.DuplicatePhone, ClientDuplicateWarningType.SimilarName],
            candidates.Select(candidate => candidate.WarningType));
    }

    [PostgreSqlFact]
    public async Task QueryWithoutPhoneEvaluatesOnlyNormalizedName()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        var nameMatchId = Guid.Parse("00000000-0000-0000-0000-000000000301");
        await InsertClientAsync(
            database.ConnectionString,
            nameMatchId,
            actorAccountId,
            "Іваненко",
            "Олексій",
            patronymic: null,
            "067 123 45 67",
            isActive: true);
        await InsertClientAsync(
            database.ConnectionString,
            Guid.Parse("00000000-0000-0000-0000-000000000302"),
            actorAccountId,
            "Петренко",
            "Марія",
            patronymic: null,
            "067 123 45 67",
            isActive: true);
        var handler = new FindClientDuplicateCandidatesQueryHandler(dbContext);

        var candidates = await handler.ExecuteAsync(
            new FindClientDuplicateCandidatesQuery(
                "Іваненко",
                "Олексій",
                Patronymic: null,
                Phone: null),
            CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal(nameMatchId, candidate.MatchedClientId);
        Assert.Equal(ClientDuplicateWarningType.SimilarName, candidate.WarningType);
    }

    [PostgreSqlFact]
    public async Task QueryDoesNotUsePrefixOrFuzzyNameMatching()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actorAccountId = await BootstrapOwnerAsync(dbContext);
        await InsertClientAsync(
            database.ConnectionString,
            Guid.NewGuid(),
            actorAccountId,
            "Іваненко",
            "Олексій",
            patronymic: null,
            "067 123 45 67",
            isActive: true);
        var handler = new FindClientDuplicateCandidatesQueryHandler(dbContext);

        var candidates = await handler.ExecuteAsync(
            new FindClientDuplicateCandidatesQuery(
                "Іванен",
                "Олексій",
                Patronymic: null,
                Phone: null),
            CancellationToken.None);

        Assert.Empty(candidates);
    }

    [PostgreSqlFact]
    public async Task QueryUsesCanonicalNormalizerValidation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var handler = new FindClientDuplicateCandidatesQueryHandler(dbContext);

        var invalidPhoneException = await Assert.ThrowsAsync<ArgumentException>(
            () => handler.ExecuteAsync(
                new FindClientDuplicateCandidatesQuery(
                    "Іваненко",
                    "Олексій",
                    Patronymic: null,
                    Phone: "12"),
                CancellationToken.None));
        var invalidNameException = await Assert.ThrowsAsync<ArgumentException>(
            () => handler.ExecuteAsync(
                new FindClientDuplicateCandidatesQuery(
                    Surname: "   ",
                    Name: "Олексій",
                    Patronymic: null,
                    Phone: null),
                CancellationToken.None));

        Assert.Equal("phone", invalidPhoneException.ParamName);
        Assert.Equal("surname", invalidNameException.ParamName);
    }

    private static async Task<Guid> BootstrapOwnerAsync(BodyLifeDbContext dbContext)
    {
        var result = await new OwnerBootstrapper(dbContext, new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, result.Status);
        return result.AccountId!.Value;
    }

    private static async Task InsertClientAsync(
        string connectionString,
        Guid clientId,
        Guid actorAccountId,
        string surname,
        string name,
        string? patronymic,
        string phoneRaw,
        bool isActive)
    {
        var normalizedFullName = ClientSearchNormalizer.NormalizeFullName(surname, name, patronymic);
        var normalizedPhone = ClientSearchNormalizer.NormalizePhone(phoneRaw);
        var phoneLastFour = ClientSearchNormalizer.ExtractPhoneLastFour(normalizedPhone);
        await using var connection = new NpgsqlConnection(connectionString);
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
        command.Parameters.AddWithValue("normalized_full_name", normalizedFullName);
        command.Parameters.AddWithValue("phone_raw", phoneRaw);
        command.Parameters.AddWithValue("phone_normalized", normalizedPhone);
        command.Parameters.AddWithValue("phone_last4", phoneLastFour);
        command.Parameters.AddWithValue("operational_status", isActive ? "active" : "inactive");
        command.Parameters.AddWithValue("created_at", TestNow);
        command.Parameters.AddWithValue("created_by_account_id", actorAccountId);
        command.Parameters.AddWithValue("updated_at", TestNow);
        await command.ExecuteNonQueryAsync();
    }

    private static Task<long> CountAcknowledgementsAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.duplicate_warning_acknowledgements");
    }

    private static Task<long> CountAuditEntriesAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.business_audit_entries");
    }

    private static void AssertCandidate(
        ClientDuplicateCandidate candidate,
        Guid expectedClientId,
        ClientDuplicateWarningType expectedWarningType,
        bool isActive)
    {
        Assert.Equal(expectedClientId, candidate.MatchedClientId);
        Assert.Equal(expectedWarningType, candidate.WarningType);
        Assert.Equal(isActive, candidate.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(candidate.Surname));
        Assert.False(string.IsNullOrWhiteSpace(candidate.Name));
        Assert.False(string.IsNullOrWhiteSpace(candidate.Phone));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
