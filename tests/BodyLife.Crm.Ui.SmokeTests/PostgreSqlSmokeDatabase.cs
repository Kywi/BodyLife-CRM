using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Modules.Clients.Search;
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

    public async Task SeedClientAsync(
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

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
