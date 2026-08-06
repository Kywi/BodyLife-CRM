using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlInitialBaselinePaperLinkTests
{
    [PostgreSqlFact]
    public async Task CompletePaperVisitLinkIsAcceptedAndImmutable()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedPaperVisitAsync(database, includeLink: true);

        var deleteException = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteNonQueryAsync(
                database,
                """
                delete from bodylife.entry_batch_row_entities
                where entry_batch_row_id = @row_id
                  and entity_type = 'visit'
                  and entity_id = @visit_id
                """,
                new NpgsqlParameter<Guid>("row_id", fixture.RowId),
                new NpgsqlParameter<Guid>("visit_id", fixture.VisitId)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, deleteException.SqlState);
        Assert.Equal(
            "ck_entry_batch_row_entities_immutable",
            deleteException.ConstraintName);

        var moveException = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteNonQueryAsync(
                database,
                """
                update bodylife.visits
                set entry_batch_id = @other_batch_id
                where id = @visit_id
                """,
                new NpgsqlParameter<Guid>("other_batch_id", Guid.NewGuid()),
                new NpgsqlParameter<Guid>("visit_id", fixture.VisitId)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, moveException.SqlState);
        Assert.Equal("ck_paper_source_exact_row_link", moveException.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task PaperSourceWithoutRowLinkIsRejected()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => SeedPaperVisitAsync(database, includeLink: false));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_paper_source_exact_row_link", exception.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task NonPaperSourceCannotBeLinkedToPaperRow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedPaperVisitAsync(
            database,
            includeLink: false,
            entryOrigin: "normal");

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteNonQueryAsync(
                database,
                """
                insert into bodylife.entry_batch_row_entities (
                    entry_batch_row_id, entity_type, entity_id)
                values (@row_id, 'visit', @visit_id)
                """,
                new NpgsqlParameter<Guid>("row_id", fixture.RowId),
                new NpgsqlParameter<Guid>("visit_id", fixture.VisitId)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(
            "ck_entry_batch_row_entities_paper_source",
            exception.ConstraintName);
    }

    private static async Task<PaperVisitFixture> SeedPaperVisitAsync(
        PostgreSqlTestDatabase database,
        bool includeLink,
        string entryOrigin = "paper_fallback")
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        var recordedAt = occurredAt.AddMinutes(10);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into bodylife.accounts (
                id, display_name, account_type, role, is_active,
                created_at, deactivated_at)
            values (
                @account_id, 'Paper owner', 'owner', 'owner', true,
                @recorded_at, null);

            insert into bodylife.sessions (
                id, account_id, device_label, started_at, expires_at,
                ended_at, last_seen_at)
            values (
                @session_id, @account_id, 'paper-test', @occurred_at,
                @expires_at, null, @recorded_at);

            insert into bodylife.clients (
                id, surname, name, patronymic, normalized_full_name,
                phone_raw, phone_normalized, phone_last4, comment,
                operational_status, created_at, created_by_account_id,
                updated_at)
            values (
                @client_id, 'Paper', 'Visitor', null, 'PAPER VISITOR',
                null, null, null, null, 'active', @recorded_at,
                @account_id, @recorded_at);

            insert into bodylife.entry_batches (
                id, batch_type, paper_sheet_number, business_date_start,
                business_date_end, recorded_at, recorded_by_account_id,
                reconciled_at, reconciled_by_account_id, note)
            values (
                @batch_id, 'paper_fallback', @sheet_number, @business_date,
                @business_date, @recorded_at, @account_id,
                null, null, 'Baseline paper invariant test');

            insert into bodylife.entry_batch_rows (
                id, entry_batch_id, line_number, event_type, occurred_at,
                explanation, recorded_at, recorded_by_account_id, session_id)
            values (
                @row_id, @batch_id, 1, 'visit', @occurred_at,
                'Recovered trial visit', @recorded_at,
                @account_id, @session_id);

            insert into bodylife.visits (
                id, client_id, occurred_at, recorded_at,
                recorded_by_account_id, session_id, visit_kind,
                entry_origin, entry_batch_id, comment, status)
            values (
                @visit_id, @client_id, @occurred_at, @recorded_at,
                @account_id, @session_id, 'trial',
                @entry_origin, @batch_id, 'Recovered from paper', 'active');
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("batch_id", batchId);
        command.Parameters.AddWithValue("row_id", rowId);
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("expires_at", recordedAt.AddHours(12));
        command.Parameters.AddWithValue(
            "business_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 8, 6));
        command.Parameters.AddWithValue(
            "sheet_number",
            $"SHEET-{batchId:N}".ToUpperInvariant());
        Assert.Equal(6, await command.ExecuteNonQueryAsync());

        if (includeLink)
        {
            await using var linkCommand = connection.CreateCommand();
            linkCommand.Transaction = transaction;
            linkCommand.CommandText =
                """
                insert into bodylife.entry_batch_row_entities (
                    entry_batch_row_id, entity_type, entity_id)
                values (@row_id, 'visit', @visit_id)
                """;
            linkCommand.Parameters.AddWithValue("row_id", rowId);
            linkCommand.Parameters.AddWithValue("visit_id", visitId);
            Assert.Equal(1, await linkCommand.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
        return new PaperVisitFixture(rowId, visitId);
    }

    private static async Task ExecuteNonQueryAsync(
        PostgreSqlTestDatabase database,
        string commandText,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record PaperVisitFixture(Guid RowId, Guid VisitId);
}
