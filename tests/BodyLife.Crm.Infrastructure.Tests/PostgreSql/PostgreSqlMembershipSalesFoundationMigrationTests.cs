using BodyLife.Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlMembershipSalesFoundationMigrationTests
{
    private const string PreviousMigration =
        "20260720173659_AddBusinessAuditRecordedTimelineIndex";

    [PostgreSqlFact]
    public async Task ValidLegacySaleAndOpeningStateAreClassifiedWithoutRewritingCash()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var fixture = await SeedLegacyAsync(database, LegacyShape.ValidSaleAndOpening);

        await migrator.MigrateAsync();

        Assert.Equal(
            "sale",
            await ReadIssuanceModeAsync(database, fixture.SaleMembershipId));
        Assert.Equal(
            "opening_state",
            await ReadIssuanceModeAsync(database, fixture.OpeningMembershipId));
        Assert.Equal(
            1200m,
            await database.ExecuteScalarAsync<decimal>(
                "select amount from bodylife.payments where id = '"
                + fixture.FirstPaymentId
                + "'"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.payments"));
    }

    [PostgreSqlFact]
    public async Task InvalidLegacySaleShapesStopMigrationPreflight()
    {
        LegacyShape[] invalidShapes =
        [
            LegacyShape.MissingPayment,
            LegacyShape.MismatchedPayment,
            LegacyShape.DuplicatePayment,
            LegacyShape.AmbiguousOpeningWithPayment,
        ];

        foreach (var shape in invalidShapes)
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync();
            await using var dbContext = database.CreateDbContext();
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            await SeedLegacyAsync(database, shape);

            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => migrator.MigrateAsync());

            Assert.Contains(
                "ADR-018 migration refused",
                exception.ToString(),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                await dbContext.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith(
                    "_AddMembershipSalesFoundation",
                    StringComparison.Ordinal));
        }
    }

    private static async Task<LegacyFixture> SeedLegacyAsync(
        PostgreSqlTestDatabase database,
        LegacyShape shape)
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var saleMembershipId = Guid.NewGuid();
        var openingMembershipId = shape == LegacyShape.ValidSaleAndOpening
            ? Guid.NewGuid()
            : saleMembershipId;
        var firstPaymentId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.accounts (
                id, display_name, account_type, role, is_active, created_at, deactivated_at)
            values (
                @account_id, 'Migration owner', 'owner', 'owner', true, @now, null);

            insert into bodylife.sessions (
                id, account_id, device_label, started_at, expires_at, ended_at, last_seen_at)
            values (
                @session_id, @account_id, 'migration test', @now, @expires_at, null, @now);

            insert into bodylife.clients (
                id, surname, name, patronymic, normalized_full_name,
                phone_raw, phone_normalized, phone_last4, comment,
                operational_status, created_at, created_by_account_id, updated_at)
            values (
                @client_id, 'Legacy', 'Client', null, 'LEGACY CLIENT',
                null, null, null, null, 'active', @now, @account_id, @now);

            insert into bodylife.membership_types (
                id, name, duration_days, visits_limit, price_amount, price_currency,
                is_active, comment, created_at, updated_at, deactivated_at)
            values (
                @membership_type_id, 'Legacy eight visits', 30, 8, 1200, 'UAH',
                true, null, @now, @now, null);

            insert into bodylife.issued_memberships (
                id, client_id, membership_type_id, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot,
                price_currency_snapshot, start_date, base_end_date, issued_at,
                issued_by_account_id, status, entry_origin, entry_batch_id, comment)
            values (
                @sale_membership_id, @client_id, @membership_type_id,
                'Legacy eight visits', 30, 8, 1200, 'UAH',
                date '2026-07-01', date '2026-07-30', @now,
                @account_id, 'active', 'normal', null, null)
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("sale_membership_id", saleMembershipId);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("expires_at", now.AddHours(12));
        Assert.Equal(5, await command.ExecuteNonQueryAsync());

        if (shape is not LegacyShape.MissingPayment)
        {
            await InsertLegacyPaymentAsync(
                connection,
                firstPaymentId,
                clientId,
                saleMembershipId,
                accountId,
                sessionId,
                shape == LegacyShape.MismatchedPayment ? 1199m : 1200m,
                now);
        }

        if (shape == LegacyShape.DuplicatePayment)
        {
            await InsertLegacyPaymentAsync(
                connection,
                Guid.NewGuid(),
                clientId,
                saleMembershipId,
                accountId,
                sessionId,
                1200m,
                now);
        }

        if (shape is LegacyShape.ValidSaleAndOpening
            or LegacyShape.AmbiguousOpeningWithPayment)
        {
            if (shape == LegacyShape.ValidSaleAndOpening)
            {
                await InsertLegacyMembershipAsync(
                    connection,
                    openingMembershipId,
                    clientId,
                    membershipTypeId,
                    accountId,
                    now);
            }

            await InsertLegacyOpeningStateAsync(
                connection,
                openingMembershipId,
                accountId,
                sessionId,
                now);
        }

        return new LegacyFixture(
            saleMembershipId,
            openingMembershipId,
            firstPaymentId);
    }

    private static async Task InsertLegacyPaymentAsync(
        NpgsqlConnection connection,
        Guid paymentId,
        Guid clientId,
        Guid membershipId,
        Guid accountId,
        Guid sessionId,
        decimal amount,
        DateTimeOffset now)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.payments (
                id, client_id, membership_id, amount, currency, method,
                payment_context, occurred_at, recorded_at, recorded_by_account_id,
                session_id, entry_origin, entry_batch_id, comment, status)
            values (
                @id, @client_id, @membership_id, @amount, 'UAH', 'cash',
                'membership_sale', @now, @now, @account_id,
                @session_id, 'normal', null, null, 'active')
            """;
        command.Parameters.AddWithValue("id", paymentId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("session_id", sessionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertLegacyMembershipAsync(
        NpgsqlConnection connection,
        Guid membershipId,
        Guid clientId,
        Guid membershipTypeId,
        Guid accountId,
        DateTimeOffset now)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.issued_memberships (
                id, client_id, membership_type_id, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot,
                price_currency_snapshot, start_date, base_end_date, issued_at,
                issued_by_account_id, status, entry_origin, entry_batch_id, comment)
            values (
                @id, @client_id, @membership_type_id, 'Legacy opening',
                30, 8, 1200, 'UAH', date '2026-07-01', date '2026-07-30', @now,
                @account_id, 'active', 'manual_backfill', null, null)
            """;
        command.Parameters.AddWithValue("id", membershipId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("account_id", accountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertLegacyOpeningStateAsync(
        NpgsqlConnection connection,
        Guid membershipId,
        Guid accountId,
        Guid sessionId,
        DateTimeOffset now)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.membership_opening_states (
                id, membership_id, opening_as_of_date, declared_remaining_visits,
                declared_negative_balance, known_effective_end_date,
                known_extension_days, source_reference, reason, recorded_at,
                recorded_by_account_id, recorded_session_id, entry_origin,
                entry_batch_id, status)
            values (
                @id, @membership_id, date '2026-07-15', 3, 0, date '2026-07-30',
                0, 'Legacy register', 'Migration classification', @now,
                @account_id, @session_id, 'manual_backfill', null, 'active')
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("session_id", sessionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<string?> ReadIssuanceModeAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        return await database.ExecuteScalarAsync<string>(
            "select issuance_mode from bodylife.issued_memberships where id = '"
            + membershipId
            + "'");
    }

    private enum LegacyShape
    {
        ValidSaleAndOpening,
        MissingPayment,
        MismatchedPayment,
        DuplicatePayment,
        AmbiguousOpeningWithPayment,
    }

    private sealed record LegacyFixture(
        Guid SaleMembershipId,
        Guid OpeningMembershipId,
        Guid FirstPaymentId);
}
