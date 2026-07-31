using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMembershipSalesFoundationConstraintTests
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
    public async Task ExactSaleConstraintRejectsMissingMismatchedAndDuplicatePayments()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            await dbContext.Database.MigrateAsync();
        }

        var fixture = await SeedBaseAsync(database);
        SaleShape[] invalidShapes =
        [
            SaleShape.Missing,
            SaleShape.Under,
            SaleShape.Over,
            SaleShape.WrongCurrency,
            SaleShape.Duplicate,
        ];

        foreach (var shape in invalidShapes)
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => InsertSaleAsync(database, fixture, shape));

            Assert.Contains(
                shape == SaleShape.Duplicate
                    ? "ux_payments_membership_sale_membership"
                    : "ck_issued_memberships_exact_sale_payment",
                exception.ToString(),
                StringComparison.Ordinal);
        }

        var membershipId = await InsertSaleAsync(database, fixture, SaleShape.Exact);
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.payments where membership_id = '"
                + membershipId
                + "'"));
    }

    [PostgreSqlFact]
    public async Task OpeningModeRequiresOneSourceAndForbidsSalePayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            await dbContext.Database.MigrateAsync();
        }

        var fixture = await SeedBaseAsync(database);
        var missingSource = await Assert.ThrowsAnyAsync<Exception>(
            () => InsertOpeningAsync(database, fixture, includeSource: false, includePayment: false));
        Assert.Contains(
            "ck_issued_memberships_opening_state_source",
            missingSource.ToString(),
            StringComparison.Ordinal);

        var forgedSale = await Assert.ThrowsAnyAsync<Exception>(
            () => InsertOpeningAsync(database, fixture, includeSource: true, includePayment: true));
        Assert.Contains(
            "ck_issued_memberships_opening_state_source",
            forgedSale.ToString(),
            StringComparison.Ordinal);

        var membershipId = await InsertOpeningAsync(
            database,
            fixture,
            includeSource: true,
            includePayment: false);
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.payments where membership_id = '"
                + membershipId
                + "'"));
    }

    [PostgreSqlFact]
    public async Task CatalogAndIssuanceDiscriminatorsAreConstrainedAndImmutable()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var dbContext = database.CreateDbContext())
        {
            await dbContext.Database.MigrateAsync();
        }

        var fixture = await SeedBaseAsync(database);
        var saleMembershipId = await InsertSaleAsync(database, fixture, SaleShape.Exact);

        await AssertConstraintAsync(
            database,
            $"update bodylife.membership_types set kind = 'one_off' where id = '{fixture.MembershipTypeId}'",
            "ck_membership_types_kind_immutable");
        await AssertConstraintAsync(
            database,
            $"update bodylife.issued_memberships set issuance_mode = 'opening_state' where id = '{saleMembershipId}'",
            "ck_issued_memberships_issuance_mode_immutable");
        await AssertConstraintAsync(
            database,
            """
            insert into bodylife.membership_types (
                id, name, kind, duration_days, visits_limit, price_amount,
                price_currency, is_active, comment, created_at, updated_at, deactivated_at)
            values (
                gen_random_uuid(), 'Invalid one off', 'one_off', 1, 2, 100,
                'UAH', true, null, now(), now(), null)
            """,
            "ck_membership_types_one_off_visits");
        await AssertConstraintAsync(
            database,
            """
            insert into bodylife.membership_types (
                id, name, kind, duration_days, visits_limit, price_amount,
                price_currency, is_active, comment, created_at, updated_at, deactivated_at)
            values (
                gen_random_uuid(), 'Free active type', 'ordinary', 1, 1, 0,
                'UAH', true, null, now(), now(), null)
            """,
            "ck_membership_types_active_sale_terms");
    }

    private static async Task<BaseFixture> SeedBaseAsync(
        PostgreSqlTestDatabase database)
    {
        var fixture = new BaseFixture(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.accounts (
                id, display_name, account_type, role, is_active, created_at, deactivated_at)
            values (@account_id, 'Constraint owner', 'owner', 'owner', true, @now, null);

            insert into bodylife.sessions (
                id, account_id, device_label, started_at, expires_at, ended_at, last_seen_at)
            values (@session_id, @account_id, 'constraint test', @now, @expires_at, null, @now);

            insert into bodylife.clients (
                id, surname, name, patronymic, normalized_full_name,
                phone_raw, phone_normalized, phone_last4, comment,
                operational_status, created_at, created_by_account_id, updated_at)
            values (
                @client_id, 'Constraint', 'Client', null, 'CONSTRAINT CLIENT',
                null, null, null, null, 'active', @now, @account_id, @now);

            insert into bodylife.membership_types (
                id, name, kind, duration_days, visits_limit, price_amount,
                price_currency, is_active, comment, created_at, updated_at, deactivated_at)
            values (
                @membership_type_id, 'Eight visits', 'ordinary', 30, 8, 1200,
                'UAH', true, null, @now, @now, null)
            """;
        command.Parameters.AddWithValue("account_id", fixture.AccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("now", TestNow);
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(12));
        Assert.Equal(4, await command.ExecuteNonQueryAsync());
        return fixture;
    }

    private static async Task<Guid> InsertSaleAsync(
        PostgreSqlTestDatabase database,
        BaseFixture fixture,
        SaleShape shape)
    {
        var membershipId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await InsertMembershipAsync(connection, transaction, fixture, membershipId, "sale");

        if (shape != SaleShape.Missing)
        {
            var amount = shape switch
            {
                SaleShape.Under => 1199m,
                SaleShape.Over => 1201m,
                _ => 1200m,
            };
            var currency = shape == SaleShape.WrongCurrency ? "EUR" : "UAH";
            await InsertPaymentAsync(
                connection,
                transaction,
                fixture,
                membershipId,
                amount,
                currency);
        }

        if (shape == SaleShape.Duplicate)
        {
            await InsertPaymentAsync(
                connection,
                transaction,
                fixture,
                membershipId,
                1200m,
                "UAH");
        }

        await transaction.CommitAsync();
        return membershipId;
    }

    private static async Task<Guid> InsertOpeningAsync(
        PostgreSqlTestDatabase database,
        BaseFixture fixture,
        bool includeSource,
        bool includePayment)
    {
        var membershipId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await InsertMembershipAsync(
            connection,
            transaction,
            fixture,
            membershipId,
            "opening_state");
        if (includeSource)
        {
            await using var opening = connection.CreateCommand();
            opening.Transaction = transaction;
            opening.CommandText =
                """
                insert into bodylife.membership_opening_states (
                    id, membership_id, opening_as_of_date, declared_remaining_visits,
                    declared_negative_balance, known_effective_end_date,
                    known_extension_days, source_reference, reason, recorded_at,
                    recorded_by_account_id, recorded_session_id, entry_origin,
                    entry_batch_id, status)
                values (
                    @id, @membership_id, date '2026-07-15', 3, 0, date '2026-07-30',
                    0, 'Register', 'Opening source', @now,
                    @account_id, @session_id, 'manual_backfill', null, 'active')
                """;
            opening.Parameters.AddWithValue("id", Guid.NewGuid());
            opening.Parameters.AddWithValue("membership_id", membershipId);
            opening.Parameters.AddWithValue("now", TestNow);
            opening.Parameters.AddWithValue("account_id", fixture.AccountId);
            opening.Parameters.AddWithValue("session_id", fixture.SessionId);
            Assert.Equal(1, await opening.ExecuteNonQueryAsync());
        }

        if (includePayment)
        {
            await InsertPaymentAsync(
                connection,
                transaction,
                fixture,
                membershipId,
                1200m,
                "UAH");
        }

        await transaction.CommitAsync();
        return membershipId;
    }

    private static async Task InsertMembershipAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BaseFixture fixture,
        Guid membershipId,
        string issuanceMode)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into bodylife.issued_memberships (
                id, client_id, membership_type_id, issuance_mode, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot,
                price_currency_snapshot, start_date, base_end_date, issued_at,
                issued_by_account_id, status, entry_origin, entry_batch_id, comment)
            values (
                @id, @client_id, @membership_type_id, @issuance_mode, 'Eight visits',
                30, 8, 1200, 'UAH', date '2026-07-01', date '2026-07-30', @now,
                @account_id, 'active', 'normal', null, null)
            """;
        command.Parameters.AddWithValue("id", membershipId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("issuance_mode", issuanceMode);
        command.Parameters.AddWithValue("now", TestNow);
        command.Parameters.AddWithValue("account_id", fixture.AccountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertPaymentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BaseFixture fixture,
        Guid membershipId,
        decimal amount,
        string currency)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into bodylife.payments (
                id, client_id, membership_id, amount, currency, method,
                payment_context, occurred_at, recorded_at, recorded_by_account_id,
                session_id, entry_origin, entry_batch_id, comment, status)
            values (
                @id, @client_id, @membership_id, @amount, @currency, 'cash',
                'membership_sale', @now, @now, @account_id,
                @session_id, 'normal', null, null, 'active')
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("currency", currency);
        command.Parameters.AddWithValue("now", TestNow);
        command.Parameters.AddWithValue("account_id", fixture.AccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertConstraintAsync(
        PostgreSqlTestDatabase database,
        string sql,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        });
        Assert.Equal(constraintName, exception.ConstraintName);
    }

    private enum SaleShape
    {
        Exact,
        Missing,
        Under,
        Over,
        WrongCurrency,
        Duplicate,
    }

    private sealed record BaseFixture(
        Guid AccountId,
        Guid SessionId,
        Guid ClientId,
        Guid MembershipTypeId);
}
