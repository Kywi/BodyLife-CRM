using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMembershipLifecycleStorageTests
{
    [PostgreSqlFact]
    public async Task ConcurrentActiveInsertWaitsThenRejectsAndRollsBackLoser()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fixture = await SeedAsync(database);
        await using var first = new NpgsqlConnection(database.ConnectionString);
        await using var second = new NpgsqlConnection(database.ConnectionString);
        await first.OpenAsync();
        await second.OpenAsync();
        await using var firstTransaction = await first.BeginTransactionAsync();
        await using var secondTransaction = await second.BeginTransactionAsync();
        await RunAsync(first, firstTransaction, OpeningSql(fixture, Guid.NewGuid()));

        var losingInsert = RunAsync(second, secondTransaction, OpeningSql(fixture, Guid.NewGuid()));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await database.ExecuteScalarAsync<bool>(
            $"select {first.ProcessID} = any(pg_blocking_pids({second.ProcessID}))"))
        {
            timeout.Token.ThrowIfCancellationRequested();
            Assert.False(losingInsert.IsCompleted, "The competing insert must wait for the first transaction.");
            await Task.Delay(20, timeout.Token);
        }

        await firstTransaction.CommitAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => losingInsert);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("ux_issued_memberships_active_client", error.ConstraintName);
        await secondTransaction.RollbackAsync();
        Assert.Equal(1L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.issued_memberships"));
        Assert.Equal(1L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.membership_opening_states"));
    }

    [PostgreSqlFact]
    public async Task ClosedHistoryKeepsOneCurrentAndDoesNotLimitOtherClients()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fixture = await SeedAsync(database);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var current = Guid.NewGuid();
        await ExecuteAsync(database, OpeningSql(fixture, first));
        await ExecuteAsync(database, RolloverSql(fixture, first, second));
        await ExecuteAsync(database, RolloverSql(fixture, second, current));
        await ExecuteAsync(database, OpeningSql(fixture with { ClientId = fixture.OtherClientId }, Guid.NewGuid()));

        Assert.Equal(2L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.issued_memberships where status = 'closed'"));
        Assert.Equal(2L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.issued_memberships where status = 'active'"));
        Assert.Equal(2L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.membership_lifecycle_closures"));
        await AssertRejectedAsync(database, OpeningSql(fixture, Guid.NewGuid()),
            "ux_issued_memberships_active_client", PostgresErrorCodes.UniqueViolation);
    }

    [PostgreSqlFact]
    public async Task StatusAndFactMustAgreeAtCommitAndClosureHistoryIsAppendOnly()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fixture = await SeedAsync(database);
        var source = Guid.NewGuid();
        var successor = Guid.NewGuid();
        await ExecuteAsync(database, OpeningSql(fixture, source));
        await AssertRejectedAsync(database,
            $"update bodylife.issued_memberships set status = 'closed' where id = '{source}'",
            "ck_issued_memberships_lifecycle_closure");
        await ExecuteAsync(database, RolloverSql(fixture, source, successor));

        foreach (var status in new[] { "active", "canceled", "corrected" })
        {
            await AssertRejectedAsync(database,
                $"update bodylife.issued_memberships set status = 'canceled' where id = '{successor}'; "
                + $"update bodylife.issued_memberships set status = '{status}' where id = '{source}'",
                "ck_issued_memberships_lifecycle_closure");
        }

        foreach (var sql in new[]
        {
            "update bodylife.membership_lifecycle_closures set explanation = 'rewritten'",
            "delete from bodylife.membership_lifecycle_closures",
            "truncate bodylife.membership_lifecycle_closures",
            "truncate bodylife.issued_memberships cascade",
        })
        {
            await AssertRejectedAsync(database, sql, "ck_membership_lifecycle_closures_append_only");
        }

        await AssertRejectedAsync(database, ClosureSql(fixture, source, successor),
            "ux_membership_lifecycle_closures_source_membership", PostgresErrorCodes.UniqueViolation);
        Assert.Equal(1L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.membership_lifecycle_closures"));
    }

    [PostgreSqlFact]
    public async Task InvalidClosureShapeAndCrossClientSuccessorRollBackWholeTransition()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fixture = await SeedAsync(database);
        var source = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var otherMembership = Guid.NewGuid();
        await ExecuteAsync(database, OpeningSql(fixture, source));
        await ExecuteAsync(database, OpeningSql(fixture with { ClientId = fixture.OtherClientId }, otherMembership));
        var prefix = $"update bodylife.issued_memberships set status = 'closed' where id = '{source}'; "
            + OpeningSql(fixture, successor);

        await AssertRejectedAsync(database, prefix + ClosureSql(fixture, source, source),
            "ck_membership_lifecycle_closures_distinct_memberships");
        await AssertRejectedAsync(database, prefix + ClosureSql(fixture, source, otherMembership),
            null, PostgresErrorCodes.ForeignKeyViolation);
        await AssertRejectedAsync(database, prefix + ClosureSql(fixture with { ClientId = fixture.OtherClientId }, source, otherMembership),
            null, PostgresErrorCodes.ForeignKeyViolation);
        await AssertRejectedAsync(database, prefix + ClosureSql(fixture, source, null),
            "ck_membership_lifecycle_closures_shape");
        await AssertRejectedAsync(database, prefix + ClosureSql(fixture, source, successor, reason: "invented"),
            "ck_membership_lifecycle_closures_reason");
        await AssertRejectedAsync(database, prefix + ClosureSql(fixture, source, successor, reason: "one_off_zero_balance"),
            "ck_membership_lifecycle_closures_shape");
        await AssertRejectedAsync(database, prefix + ClosureSql(fixture, source, successor, correlation: " "),
            "ck_membership_lifecycle_closures_correlation");
        Assert.Equal("active", await database.ExecuteScalarAsync<string>($"select status from bodylife.issued_memberships where id = '{source}'"));
        Assert.Equal(0L, await database.ExecuteScalarAsync<long>($"select count(*) from bodylife.issued_memberships where id = '{successor}'"));
    }

    [PostgreSqlFact]
    public async Task PaperRolloverRequiresClosureOnTheExactSaleRow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fixture = await SeedAsync(database);
        var source = Guid.NewGuid();
        await ExecuteAsync(database, OpeningSql(fixture, source));
        await AssertRejectedAsync(database, PaperRolloverSql(fixture, source, includeClosureLink: false),
            "ck_entry_batch_row_entities_exact_shape");
        await AssertRejectedAsync(database, PaperRolloverSql(fixture, source, includeClosureLink: true, wrongRow: true),
            "ck_entry_batch_row_entities_exact_shape");
        await AssertRejectedAsync(database, PaperRolloverSql(fixture, source, includeClosureLink: true, nonPaperClosure: true),
            "ck_entry_batch_row_entities_source_batch");
        await ExecuteAsync(database, PaperRolloverSql(fixture, source, includeClosureLink: true));
        Assert.Equal(3L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.entry_batch_row_entities"));
        Assert.Equal(1L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.payments where status = 'active'"));
    }

    private static async Task<Fixture> SeedAsync(PostgreSqlTestDatabase database)
    {
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();
        var fixture = new Fixture(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await ExecuteAsync(database, $"""
            insert into bodylife.accounts (id, display_name, account_type, role, is_active, created_at)
            values ('{fixture.AccountId}', 'Lifecycle owner', 'owner', 'owner', true, now());
            insert into bodylife.sessions (id, account_id, device_label, started_at, expires_at, last_seen_at)
            values ('{fixture.SessionId}', '{fixture.AccountId}', 'Lifecycle storage', now(), now() + interval '1 hour', now());
            insert into bodylife.clients (id, surname, name, normalized_full_name, operational_status, created_at, created_by_account_id, updated_at)
            values ('{fixture.ClientId}', 'Lifecycle', 'Client', 'LIFECYCLE CLIENT', 'active', now(), '{fixture.AccountId}', now()),
                   ('{fixture.OtherClientId}', 'Other', 'Client', 'OTHER CLIENT', 'active', now(), '{fixture.AccountId}', now());
            insert into bodylife.membership_types (id, name, kind, duration_days, visits_limit, price_amount, price_currency, is_active, created_at, updated_at)
            values ('{fixture.TypeId}', 'One visit', 'ordinary', 30, 1, 100, 'UAH', true, now(), now());
            """);
        return fixture;
    }

    private static string OpeningSql(Fixture fixture, Guid id) => $"""
        insert into bodylife.issued_memberships (id, client_id, membership_type_id, issuance_mode,
            type_name_snapshot, duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot,
            price_currency_snapshot, start_date, base_end_date, issued_at, issued_by_account_id, status, entry_origin)
        values ('{id}', '{fixture.ClientId}', '{fixture.TypeId}', 'opening_state', 'One visit', 30, 1, 100,
            'UAH', '2026-07-01', '2026-07-30', now(), '{fixture.AccountId}', 'active', 'manual_backfill');
        insert into bodylife.membership_opening_states (id, membership_id, opening_as_of_date,
            declared_remaining_visits, declared_negative_balance, known_effective_end_date, known_extension_days,
            source_reference, reason, recorded_at, recorded_by_account_id, recorded_session_id, entry_origin, status)
        values (gen_random_uuid(), '{id}', '2026-07-01', 0, 0, '2026-07-30', 0, 'Zero opening balance',
            'Known historical zero', now(), '{fixture.AccountId}', '{fixture.SessionId}', 'manual_backfill', 'active');
        """;

    private static string ClosureSql(Fixture fixture, Guid source, Guid? successor,
        string reason = "zero_balance_rollover", string correlation = "lifecycle-storage") => $"""
        insert into bodylife.membership_lifecycle_closures (id, client_id, source_membership_id,
            successor_membership_id, reason_code, recorded_by_account_id, session_id, correlation_id,
            idempotency_key, entry_origin, occurred_at, recorded_at)
        values (gen_random_uuid(), '{fixture.ClientId}', '{source}', {(successor is null ? "null" : $"'{successor}'")},
            '{reason}', '{fixture.AccountId}', '{fixture.SessionId}', '{correlation}',
            '{Guid.NewGuid()}', 'normal', now(), now());
        """;

    private static string RolloverSql(Fixture fixture, Guid source, Guid successor) =>
        $"update bodylife.issued_memberships set status = 'closed' where id = '{source}'; "
        + OpeningSql(fixture, successor) + ClosureSql(fixture, source, successor);

    private static string PaperRolloverSql(Fixture fixture, Guid source, bool includeClosureLink,
        bool wrongRow = false, bool nonPaperClosure = false)
    {
        var batch = Guid.NewGuid();
        var row = Guid.NewGuid();
        var otherRow = Guid.NewGuid();
        var membership = Guid.NewGuid();
        var payment = Guid.NewGuid();
        var closure = Guid.NewGuid();
        var sql = $"""
            update bodylife.issued_memberships set status = 'closed' where id = '{source}';
            insert into bodylife.entry_batches (id, batch_type, paper_sheet_number, business_date_start,
                business_date_end, recorded_at, recorded_by_account_id)
            values ('{batch}', 'paper_fallback', upper('{batch}'), '2026-07-01', '2026-07-01', now(), '{fixture.AccountId}');
            insert into bodylife.entry_batch_rows (id, entry_batch_id, line_number, event_type, occurred_at,
                explanation, recorded_at, recorded_by_account_id, session_id)
            values ('{row}', '{batch}', 1, 'membership_sale', '2026-07-01T09:00:00Z', 'Recovered sale', now(), '{fixture.AccountId}', '{fixture.SessionId}');
            insert into bodylife.issued_memberships (id, client_id, membership_type_id, issuance_mode,
                type_name_snapshot, duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot,
                price_currency_snapshot, start_date, base_end_date, issued_at, issued_by_account_id, status, entry_origin, entry_batch_id)
            values ('{membership}', '{fixture.ClientId}', '{fixture.TypeId}', 'sale', 'One visit', 30, 1, 100,
                'UAH', '2026-07-01', '2026-07-30', '2026-07-01T09:00:00Z', '{fixture.AccountId}', 'active', 'paper_fallback', '{batch}');
            insert into bodylife.payments (id, client_id, membership_id, amount, currency, method, payment_context,
                occurred_at, recorded_at, recorded_by_account_id, session_id, entry_origin, entry_batch_id, status)
            values ('{payment}', '{fixture.ClientId}', '{membership}', 100, 'UAH', 'cash', 'membership_sale',
                '2026-07-01T09:00:00Z', now(), '{fixture.AccountId}', '{fixture.SessionId}', 'paper_fallback', '{batch}', 'active');
            insert into bodylife.membership_lifecycle_closures (id, client_id, source_membership_id,
                successor_membership_id, reason_code, recorded_by_account_id, session_id, correlation_id,
                idempotency_key, entry_origin, entry_batch_id, occurred_at, recorded_at)
            values ('{closure}', '{fixture.ClientId}', '{source}', '{membership}', 'zero_balance_rollover',
                '{fixture.AccountId}', '{fixture.SessionId}', 'paper-lifecycle', '{closure}',
                '{(nonPaperClosure ? "normal" : "paper_fallback")}', {(nonPaperClosure ? "null" : $"'{batch}'")}, '2026-07-01T09:00:00Z', now());
            insert into bodylife.entry_batch_row_entities (entry_batch_row_id, entity_type, entity_id)
            values ('{row}', 'membership', '{membership}'), ('{row}', 'payment', '{payment}');
            """;
        if (wrongRow)
        {
            sql += $"""
                insert into bodylife.entry_batch_rows (id, entry_batch_id, line_number, event_type, occurred_at,
                    explanation, recorded_at, recorded_by_account_id, session_id)
                values ('{otherRow}', '{batch}', 2, 'membership_sale', '2026-07-01T09:00:00Z', 'Wrong row', now(), '{fixture.AccountId}', '{fixture.SessionId}');
                """;
        }

        if (includeClosureLink)
        {
            sql += $"insert into bodylife.entry_batch_row_entities values ('{(wrongRow ? otherRow : row)}', 'membership_lifecycle_closure', '{closure}');";
        }

        return sql;
    }

    private static async Task AssertRejectedAsync(PostgreSqlTestDatabase database, string sql,
        string? constraint, string code = PostgresErrorCodes.CheckViolation)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database, sql));
        Assert.Equal(code, error.SqlState);
        if (constraint is not null)
        {
            Assert.Equal(constraint, error.ConstraintName);
        }
    }

    private static async Task ExecuteAsync(PostgreSqlTestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await RunAsync(connection, transaction, sql);
        await transaction.CommitAsync();
    }

    private static async Task RunAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record Fixture(Guid AccountId, Guid SessionId, Guid ClientId, Guid OtherClientId, Guid TypeId);
}
