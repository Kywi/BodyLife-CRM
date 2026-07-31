using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlNegativeCoverageFoundationTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        20,
        8,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task OneOffClosureRequiresExactPaymentAndKeepsCatalogSnapshot()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedBaseAsync(database);

        var mismatch = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertOneOffClosureAsync(
                database,
                fixture,
                [new OneOffLine(fixture.OneOffTypeAId, Quantity: 1, Price: 50m)],
                visitIndexes: [2],
                paymentAmount: 49m));
        Assert.Equal(
            "ck_negative_closure_exact_payment",
            mismatch.ConstraintName);

        var closureId = await InsertOneOffClosureAsync(
            database,
            fixture,
            [
                new OneOffLine(fixture.OneOffTypeAId, Quantity: 1, Price: 50m),
                new OneOffLine(fixture.OneOffTypeBId, Quantity: 1, Price: 75m),
            ],
            visitIndexes: [2, 3],
            paymentAmount: 125m);

        await database.ExecuteScalarAsync<int>(
            $"update bodylife.membership_types set price_amount = 99, updated_at = now() where id = '{fixture.OneOffTypeAId}'; select 1;");

        Assert.Equal(
            50m,
            await database.ExecuteScalarAsync<decimal>(
                $"select unit_price_amount_snapshot from bodylife.membership_negative_closure_lines where negative_closure_id = '{closureId}' and sequence = 1"));
        Assert.Equal(
            125m,
            await database.ExecuteScalarAsync<decimal>(
                $"select amount from bodylife.payments where negative_closure_id = '{closureId}'"));
        Assert.Equal(
            2L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.membership_negative_closure_items where negative_closure_id = '{closureId}'"));
    }

    [PostgreSqlFact]
    public async Task ActiveVisitCannotBeCoveredTwice()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedBaseAsync(database);
        await InsertOneOffClosureAsync(
            database,
            fixture,
            [new OneOffLine(fixture.OneOffTypeAId, Quantity: 1, Price: 50m)],
            visitIndexes: [2],
            paymentAmount: 50m);

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertOneOffClosureAsync(
                database,
                fixture,
                [new OneOffLine(fixture.OneOffTypeAId, Quantity: 1, Price: 50m)],
                visitIndexes: [2],
                paymentAmount: 50m));

        Assert.Equal(
            "ix_negative_closure_items_visit",
            duplicate.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task NewMembershipCoverageEnforcesLimitAndRebuildsBothMemberships()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedBaseAsync(database);

        var overLimit = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertNewMembershipCoverageAsync(
                database,
                fixture,
                visitIndexes: [2, 3]));
        Assert.Equal(
            "ck_negative_closure_membership_allocation",
            overLimit.ConstraintName);

        var coverage = await InsertNewMembershipCoverageAsync(
            database,
            fixture,
            visitIndexes: [2]);
        await using var dbContext = database.CreateDbContext();
        var rebuilder = new MembershipStateCacheRebuilder(
            dbContext,
            new FixedTimeProvider(TestNow.AddHours(1)));

        var sourceResult = await rebuilder.RebuildAsync(fixture.SourceMembershipId);
        var coveringResult = await rebuilder.RebuildAsync(coverage.CoveringMembershipId);

        Assert.True(sourceResult.Succeeded);
        Assert.Equal(3, sourceResult.State!.CountedVisits);
        Assert.Equal(-1, sourceResult.State.RemainingVisits);
        Assert.Equal(fixture.VisitIds[3], sourceResult.State.FirstNegativeVisitId);
        Assert.True(coveringResult.Succeeded);
        Assert.Equal(1, coveringResult.State!.CountedVisits);
        Assert.Equal(0, coveringResult.State.RemainingVisits);
        Assert.Equal(0, coveringResult.State.NegativeBalance);
        Assert.Equal(8, sourceResult.RecalculationVersion);
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        return database;
    }

    private static async Task<CoverageFixture> SeedBaseAsync(
        PostgreSqlTestDatabase database)
    {
        var fixture = new CoverageFixture(
            AccountId: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            ClientId: Guid.NewGuid(),
            SourceTypeId: Guid.NewGuid(),
            OneOffTypeAId: Guid.NewGuid(),
            OneOffTypeBId: Guid.NewGuid(),
            CoveringTypeId: Guid.NewGuid(),
            SourceMembershipId: Guid.NewGuid(),
            VisitIds: Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray(),
            ConsumptionIds: Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray());

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into bodylife.accounts (
                id, display_name, account_type, role, is_active, created_at, deactivated_at)
            values (@account_id, 'Coverage owner', 'owner', 'owner', true, @now, null);

            insert into bodylife.sessions (
                id, account_id, device_label, started_at, expires_at, ended_at, last_seen_at)
            values (@session_id, @account_id, 'coverage test', @now, @expires_at, null, @now);

            insert into bodylife.clients (
                id, surname, name, patronymic, normalized_full_name,
                phone_raw, phone_normalized, phone_last4, comment,
                operational_status, created_at, created_by_account_id, updated_at)
            values (
                @client_id, 'Coverage', 'Client', null, 'COVERAGE CLIENT',
                null, null, null, null, 'active', @now, @account_id, @now);

            insert into bodylife.membership_types (
                id, name, kind, duration_days, visits_limit, price_amount,
                price_currency, is_active, comment, created_at, updated_at, deactivated_at)
            values
                (@source_type_id, 'Two visits', 'ordinary', 30, 2, 1000, 'UAH', true, null, @now, @now, null),
                (@one_off_a_id, 'Single A', 'one_off', 1, 1, 50, 'UAH', true, null, @now, @now, null),
                (@one_off_b_id, 'Single B', 'one_off', 1, 1, 75, 'UAH', true, null, @now, @now, null),
                (@covering_type_id, 'One visit cover', 'ordinary', 30, 1, 600, 'UAH', true, null, @now, @now, null);

            insert into bodylife.issued_memberships (
                id, client_id, membership_type_id, issuance_mode, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot,
                price_currency_snapshot, start_date, base_end_date, issued_at,
                issued_by_account_id, status, entry_origin, entry_batch_id, comment)
            values (
                @source_membership_id, @client_id, @source_type_id, 'sale', 'Two visits',
                30, 2, 1000, 'UAH', date '2026-07-01', date '2026-07-30', @now,
                @account_id, 'active', 'normal', null, null);

            insert into bodylife.payments (
                id, client_id, membership_id, negative_closure_id, amount, currency,
                method, payment_context, occurred_at, recorded_at,
                recorded_by_account_id, session_id, entry_origin, entry_batch_id,
                comment, status)
            values (
                gen_random_uuid(), @client_id, @source_membership_id, null, 1000, 'UAH',
                'cash', 'membership_sale', @now, @now,
                @account_id, @session_id, 'normal', null, null, 'active');
            """,
            ("account_id", fixture.AccountId),
            ("session_id", fixture.SessionId),
            ("client_id", fixture.ClientId),
            ("source_type_id", fixture.SourceTypeId),
            ("one_off_a_id", fixture.OneOffTypeAId),
            ("one_off_b_id", fixture.OneOffTypeBId),
            ("covering_type_id", fixture.CoveringTypeId),
            ("source_membership_id", fixture.SourceMembershipId),
            ("now", TestNow),
            ("expires_at", TestNow.AddHours(12)));

        for (var index = 0; index < fixture.VisitIds.Length; index++)
        {
            var occurredAt = new DateTimeOffset(
                2026,
                7,
                index + 1,
                10,
                0,
                0,
                TimeSpan.Zero);
            await ExecuteAsync(
                connection,
                transaction,
                """
                insert into bodylife.visits (
                    id, client_id, occurred_at, recorded_at, recorded_by_account_id,
                    session_id, visit_kind, entry_origin, entry_batch_id, comment, status)
                values (
                    @visit_id, @client_id, @occurred_at, @recorded_at, @account_id,
                    @session_id, 'membership', 'normal', null, null, 'active');

                insert into bodylife.visit_consumptions (
                    id, visit_id, client_id, visit_kind, membership_id,
                    consumption_type, source_fact_type, source_fact_id, recorded_at,
                    recorded_by_account_id, recorded_session_id, status)
                values (
                    @consumption_id, @visit_id, @client_id, 'membership', @membership_id,
                    'counted', 'visit', @visit_id, @recorded_at,
                    @account_id, @session_id, 'active');
                """,
                ("visit_id", fixture.VisitIds[index]),
                ("consumption_id", fixture.ConsumptionIds[index]),
                ("client_id", fixture.ClientId),
                ("membership_id", fixture.SourceMembershipId),
                ("occurred_at", occurredAt),
                ("recorded_at", TestNow.AddMinutes(index + 1)),
                ("account_id", fixture.AccountId),
                ("session_id", fixture.SessionId));
        }

        await transaction.CommitAsync();
        return fixture;
    }

    private static async Task<Guid> InsertOneOffClosureAsync(
        PostgreSqlTestDatabase database,
        CoverageFixture fixture,
        IReadOnlyList<OneOffLine> lines,
        IReadOnlyList<int> visitIndexes,
        decimal paymentAmount)
    {
        var closureId = Guid.NewGuid();
        var lineIds = lines.Select(_ => Guid.NewGuid()).ToArray();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into bodylife.membership_negative_closures (
                id, client_id, closure_type, covering_membership_id,
                oldest_open_negative_visit_id, visits_count, comment,
                occurred_at, recorded_at, recorded_by_account_id, session_id,
                entry_origin, entry_batch_id, idempotency_key, status)
            values (
                @closure_id, @client_id, 'one_off', null,
                @oldest_visit_id, @visits_count, null,
                @now, @now, @account_id, @session_id,
                'normal', null, @idempotency_key, 'active');
            """,
            ("closure_id", closureId),
            ("client_id", fixture.ClientId),
            ("oldest_visit_id", fixture.VisitIds[visitIndexes[0]]),
            ("visits_count", visitIndexes.Count),
            ("now", TestNow.AddMinutes(30)),
            ("account_id", fixture.AccountId),
            ("session_id", fixture.SessionId),
            ("idempotency_key", Guid.NewGuid().ToString("N")));

        var itemOffset = 0;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            await ExecuteAsync(
                connection,
                transaction,
                """
                insert into bodylife.membership_negative_closure_lines (
                    id, negative_closure_id, membership_type_id, type_name_snapshot,
                    duration_days_snapshot, visits_limit_snapshot, quantity,
                    unit_price_amount_snapshot, currency_snapshot, line_total, sequence)
                values (
                    @line_id, @closure_id, @membership_type_id, @type_name,
                    1, 1, @quantity, @price, 'UAH', @line_total, @sequence);
                """,
                ("line_id", lineIds[lineIndex]),
                ("closure_id", closureId),
                ("membership_type_id", line.MembershipTypeId),
                ("type_name", $"Single {lineIndex + 1}"),
                ("quantity", line.Quantity),
                ("price", line.Price),
                ("line_total", line.Price * line.Quantity),
                ("sequence", lineIndex + 1));

            for (var quantityIndex = 0; quantityIndex < line.Quantity; quantityIndex++)
            {
                var visitIndex = visitIndexes[itemOffset];
                await InsertClosureItemAsync(
                    connection,
                    transaction,
                    fixture,
                    closureId,
                    lineIds[lineIndex],
                    itemOffset + 1,
                    visitIndex,
                    coveringMembershipId: null,
                    itemId: Guid.NewGuid(),
                    newConsumptionId: null);
                itemOffset++;
            }
        }

        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into bodylife.payments (
                id, client_id, membership_id, negative_closure_id, amount, currency,
                method, payment_context, occurred_at, recorded_at,
                recorded_by_account_id, session_id, entry_origin, entry_batch_id,
                comment, status)
            values (
                gen_random_uuid(), @client_id, null, @closure_id, @amount, 'UAH',
                'cash', 'negative_closure', @now, @now,
                @account_id, @session_id, 'normal', null, null, 'active');
            """,
            ("client_id", fixture.ClientId),
            ("closure_id", closureId),
            ("amount", paymentAmount),
            ("now", TestNow.AddMinutes(30)),
            ("account_id", fixture.AccountId),
            ("session_id", fixture.SessionId));

        await transaction.CommitAsync();
        return closureId;
    }

    private static async Task<NewMembershipCoverage> InsertNewMembershipCoverageAsync(
        PostgreSqlTestDatabase database,
        CoverageFixture fixture,
        IReadOnlyList<int> visitIndexes)
    {
        var coveringMembershipId = Guid.NewGuid();
        var closureId = Guid.NewGuid();
        var itemIds = visitIndexes.Select(_ => Guid.NewGuid()).ToArray();
        var newConsumptionIds = visitIndexes.Select(_ => Guid.NewGuid()).ToArray();
        var startDate = new DateOnly(2026, 7, visitIndexes[0] + 1);
        var baseEndDate = startDate.AddDays(29);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into bodylife.issued_memberships (
                id, client_id, membership_type_id, issuance_mode, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot,
                price_currency_snapshot, start_date, base_end_date, issued_at,
                issued_by_account_id, status, entry_origin, entry_batch_id, comment)
            values (
                @membership_id, @client_id, @membership_type_id, 'sale', 'One visit cover',
                30, 1, 600, 'UAH', @start_date, @base_end_date, @now,
                @account_id, 'active', 'normal', null, null);

            insert into bodylife.payments (
                id, client_id, membership_id, negative_closure_id, amount, currency,
                method, payment_context, occurred_at, recorded_at,
                recorded_by_account_id, session_id, entry_origin, entry_batch_id,
                comment, status)
            values (
                gen_random_uuid(), @client_id, @membership_id, null, 600, 'UAH',
                'cash', 'membership_sale', @now, @now,
                @account_id, @session_id, 'normal', null, null, 'active');

            insert into bodylife.membership_negative_closures (
                id, client_id, closure_type, covering_membership_id,
                oldest_open_negative_visit_id, visits_count, comment,
                occurred_at, recorded_at, recorded_by_account_id, session_id,
                entry_origin, entry_batch_id, idempotency_key, status)
            values (
                @closure_id, @client_id, 'new_membership', @membership_id,
                @oldest_visit_id, @visits_count, null,
                @now, @now, @account_id, @session_id,
                'normal', null, @idempotency_key, 'active');
            """,
            ("membership_id", coveringMembershipId),
            ("client_id", fixture.ClientId),
            ("membership_type_id", fixture.CoveringTypeId),
            ("start_date", startDate),
            ("base_end_date", baseEndDate),
            ("closure_id", closureId),
            ("oldest_visit_id", fixture.VisitIds[visitIndexes[0]]),
            ("visits_count", visitIndexes.Count),
            ("now", TestNow.AddMinutes(40)),
            ("account_id", fixture.AccountId),
            ("session_id", fixture.SessionId),
            ("idempotency_key", Guid.NewGuid().ToString("N")));

        for (var index = 0; index < visitIndexes.Count; index++)
        {
            var visitIndex = visitIndexes[index];
            await ExecuteAsync(
                connection,
                transaction,
                """
                insert into bodylife.visit_consumptions (
                    id, visit_id, client_id, visit_kind, membership_id,
                    consumption_type, source_fact_type, source_fact_id, recorded_at,
                    recorded_by_account_id, recorded_session_id, status)
                values (
                    @consumption_id, @visit_id, @client_id, 'membership', @membership_id,
                    'negative_coverage', 'negative_closure_item', @item_id, @now,
                    @account_id, @session_id, 'active');
                """,
                ("consumption_id", newConsumptionIds[index]),
                ("visit_id", fixture.VisitIds[visitIndex]),
                ("client_id", fixture.ClientId),
                ("membership_id", coveringMembershipId),
                ("item_id", itemIds[index]),
                ("now", TestNow.AddMinutes(40)),
                ("account_id", fixture.AccountId),
                ("session_id", fixture.SessionId));
            await InsertClosureItemAsync(
                connection,
                transaction,
                fixture,
                closureId,
                closureLineId: null,
                sequence: index + 1,
                visitIndex,
                coveringMembershipId,
                itemIds[index],
                newConsumptionIds[index]);
        }

        await transaction.CommitAsync();
        return new NewMembershipCoverage(closureId, coveringMembershipId);
    }

    private static Task<int> InsertClosureItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CoverageFixture fixture,
        Guid closureId,
        Guid? closureLineId,
        int sequence,
        int visitIndex,
        Guid? coveringMembershipId,
        Guid itemId,
        Guid? newConsumptionId)
    {
        return ExecuteAsync(
            connection,
            transaction,
            """
            insert into bodylife.membership_negative_closure_items (
                id, negative_closure_id, client_id, closure_line_id, sequence,
                visit_id, source_membership_id, old_consumption_id,
                covering_membership_id, new_consumption_id, status)
            values (
                @item_id, @closure_id, @client_id, @line_id, @sequence,
                @visit_id, @source_membership_id, @old_consumption_id,
                @covering_membership_id, @new_consumption_id, 'active');
            """,
            ("item_id", itemId),
            ("closure_id", closureId),
            ("client_id", fixture.ClientId),
            ("line_id", closureLineId),
            ("sequence", sequence),
            ("visit_id", fixture.VisitIds[visitIndex]),
            ("source_membership_id", fixture.SourceMembershipId),
            ("old_consumption_id", fixture.ConsumptionIds[visitIndex]),
            ("covering_membership_id", coveringMembershipId),
            ("new_consumption_id", newConsumptionId));
    }

    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            if (parameter.Value is null)
            {
                command.Parameters.Add(parameter.Name, NpgsqlDbType.Uuid).Value =
                    DBNull.Value;
            }
            else
            {
                command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            }
        }

        return await command.ExecuteNonQueryAsync();
    }

    private sealed record CoverageFixture(
        Guid AccountId,
        Guid SessionId,
        Guid ClientId,
        Guid SourceTypeId,
        Guid OneOffTypeAId,
        Guid OneOffTypeBId,
        Guid CoveringTypeId,
        Guid SourceMembershipId,
        Guid[] VisitIds,
        Guid[] ConsumptionIds);

    private sealed record OneOffLine(
        Guid MembershipTypeId,
        int Quantity,
        decimal Price);

    private sealed record NewMembershipCoverage(
        Guid ClosureId,
        Guid CoveringMembershipId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
