using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Tests.Architecture;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlBusinessAuditMatrixTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        20,
        12,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task EveryCanonicalEventPersistsTheCompleteAuditContract()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var appender = new BusinessAuditAppender(dbContext);
        var envelope = BusinessAuditMatrixTests.CompleteEnvelope();

        foreach (var item in BusinessAuditMatrixTestCases.All)
        {
            appender.Append(
                envelope,
                item.ActionType,
                item.EntityType,
                Guid.NewGuid(),
                TestNow,
                BusinessAuditMatrixTests.CompleteRelatedEntityRefs(),
                BusinessAuditMatrixTests.CompleteSummary("before"),
                BusinessAuditMatrixTests.CompleteSummary("after"),
                changedAfterClose: true);
        }

        await dbContext.SaveChangesAsync();

        var expectedActions = BusinessAuditMatrixTestCases.All
            .Select(item => $"{item.ActionType}|{item.EntityType}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedActions, await ReadActionEntitiesAsync(database.ConnectionString));
        Assert.Equal(
            0L,
            await CountContractViolationsAsync(database.ConnectionString, envelope));
    }

    private static async Task<string[]> ReadActionEntitiesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select action_type, entity_type
            from bodylife.business_audit_entries
            order by action_type
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();

        while (await reader.ReadAsync())
        {
            rows.Add($"{reader.GetString(0)}|{reader.GetString(1)}");
        }

        return [.. rows];
    }

    private static async Task<long> CountContractViolationsAsync(
        string connectionString,
        BodyLife.Crm.Application.Commands.CommandEnvelope envelope)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select count(*)
            from bodylife.business_audit_entries
            where id = '00000000-0000-0000-0000-000000000000'::uuid
               or entity_id = '00000000-0000-0000-0000-000000000000'::uuid
               or actor_account_id <> @actor_account_id
               or actor_account_type <> 'shared_reception_admin'
               or actor_role <> 'admin'
               or session_id <> @session_id
               or device_label is distinct from 'reception tablet'
               or occurred_at <> @occurred_at
               or recorded_at <> @recorded_at
               or reason is distinct from 'matrix reason'
               or comment is distinct from 'matrix comment'
               or request_correlation_id <> 'matrix-correlation'
               or entry_origin <> 'normal'
               or idempotency_key is distinct from 'matrix-idempotency'
               or changed_after_close is not true
               or not (related_entity_refs ? 'clientId')
               or not (before_summary ? 'state')
               or not (after_summary ? 'state')
            """,
            connection);
        command.Parameters.AddWithValue("actor_account_id", envelope.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", envelope.Actor.SessionId.Value);
        command.Parameters.AddWithValue("occurred_at", envelope.OccurredAt!.Value);
        command.Parameters.AddWithValue("recorded_at", TestNow);

        return (long)(await command.ExecuteScalarAsync())!;
    }
}
