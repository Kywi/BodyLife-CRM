using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

internal static class PostgreSqlPaperFallbackTestData
{
    internal static async Task<PaperFallbackRowFixture> SeedRowAsync(
        PostgreSqlTestDatabase database,
        ActorContext actor,
        DateTimeOffset occurredAt,
        string eventType,
        DateTimeOffset recordedAt,
        int lineNumber = 1,
        string? explanation = null)
    {
        var batchId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var sheetNumber = $"SHEET-{batchId:N}".ToUpperInvariant();
        var normalizedExplanation = explanation
            ?? $"Recovered {eventType} from paper line {lineNumber}";
        var businessDate = BusinessTimeZone.GetBusinessDate(occurredAt);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.entry_batches (
                id, batch_type, paper_sheet_number, business_date_start,
                business_date_end, recorded_at, recorded_by_account_id,
                reconciled_at, reconciled_by_account_id, note)
            values (
                @batch_id, 'paper_fallback', @sheet_number,
                @business_date, @business_date, @batch_recorded_at,
                @account_id, null, null, 'Paper command test fixture');

            insert into bodylife.entry_batch_rows (
                id, entry_batch_id, line_number, event_type, occurred_at,
                explanation, recorded_at, recorded_by_account_id, session_id)
            values (
                @row_id, @batch_id, @line_number, @event_type, @occurred_at,
                @explanation, @row_recorded_at, @account_id, @session_id);
            """;
        command.Parameters.AddWithValue("batch_id", batchId);
        command.Parameters.AddWithValue("sheet_number", sheetNumber);
        command.Parameters.AddWithValue("business_date", NpgsqlDbType.Date, businessDate);
        command.Parameters.AddWithValue("batch_recorded_at", recordedAt.AddMinutes(-10));
        command.Parameters.AddWithValue("account_id", actor.AccountId.Value);
        command.Parameters.AddWithValue("row_id", rowId);
        command.Parameters.AddWithValue("line_number", lineNumber);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("explanation", normalizedExplanation);
        command.Parameters.AddWithValue("row_recorded_at", recordedAt.AddMinutes(-5));
        command.Parameters.AddWithValue("session_id", actor.SessionId.Value);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        return new PaperFallbackRowFixture(
            batchId,
            rowId,
            sheetNumber,
            lineNumber,
            eventType,
            normalizedExplanation);
    }

    internal static async Task<PaperFallbackRowFixture> SeedRowInBatchAsync(
        PostgreSqlTestDatabase database,
        PaperFallbackRowFixture batchRow,
        ActorContext actor,
        DateTimeOffset occurredAt,
        string eventType,
        DateTimeOffset recordedAt,
        int lineNumber,
        string? explanation = null)
    {
        var rowId = Guid.NewGuid();
        var normalizedExplanation = explanation
            ?? $"Recovered {eventType} from paper line {lineNumber}";

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.entry_batch_rows (
                id, entry_batch_id, line_number, event_type, occurred_at,
                explanation, recorded_at, recorded_by_account_id, session_id)
            values (
                @row_id, @batch_id, @line_number, @event_type, @occurred_at,
                @explanation, @row_recorded_at, @account_id, @session_id);
            """;
        command.Parameters.AddWithValue("row_id", rowId);
        command.Parameters.AddWithValue("batch_id", batchRow.EntryBatchId);
        command.Parameters.AddWithValue("line_number", lineNumber);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("explanation", normalizedExplanation);
        command.Parameters.AddWithValue("row_recorded_at", recordedAt.AddMinutes(-5));
        command.Parameters.AddWithValue("account_id", actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", actor.SessionId.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return new PaperFallbackRowFixture(
            batchRow.EntryBatchId,
            rowId,
            batchRow.PaperSheetNumber,
            lineNumber,
            eventType,
            normalizedExplanation);
    }

    internal static async Task<IReadOnlyList<PaperFallbackEntityLink>> ReadLinksAsync(
        PostgreSqlTestDatabase database,
        Guid entryBatchRowId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select entity_type, entity_id
            from bodylife.entry_batch_row_entities
            where entry_batch_row_id = @row_id
            order by entity_type, entity_id
            """;
        command.Parameters.AddWithValue("row_id", entryBatchRowId);
        await using var reader = await command.ExecuteReaderAsync();
        var links = new List<PaperFallbackEntityLink>();
        while (await reader.ReadAsync())
        {
            links.Add(new PaperFallbackEntityLink(
                reader.GetString(0),
                reader.GetGuid(1)));
        }

        return links.AsReadOnly();
    }

    internal static async Task LinkRowAsync(
        PostgreSqlTestDatabase database,
        Guid entryBatchRowId,
        params PaperFallbackEntityLink[] links)
    {
        ArgumentNullException.ThrowIfNull(links);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var link in links)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                insert into bodylife.entry_batch_row_entities (
                    entry_batch_row_id, entity_type, entity_id)
                values (@row_id, @entity_type, @entity_id)
                """;
            command.Parameters.AddWithValue("row_id", entryBatchRowId);
            command.Parameters.AddWithValue("entity_type", link.EntityType);
            command.Parameters.AddWithValue("entity_id", link.EntityId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
    }
}

internal sealed record PaperFallbackRowFixture(
    Guid EntryBatchId,
    Guid EntryBatchRowId,
    string PaperSheetNumber,
    int LineNumber,
    string EventType,
    string Explanation);

internal sealed record PaperFallbackEntityLink(
    string EntityType,
    Guid EntityId);
