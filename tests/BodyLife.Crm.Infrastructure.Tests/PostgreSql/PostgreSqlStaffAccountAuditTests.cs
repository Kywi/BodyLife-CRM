using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlStaffAccountAuditTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 9, 18, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task StaffLifecycleMutationsAppendAccountableAuditEntries()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var envelope = OwnerEnvelope(
            reason: "Owner account maintenance",
            comment: "Scheduled staff change");
        var service = LifecycleService(dbContext);

        var createResult = await service.CreateStaffAccountAsync(
            envelope,
            AccountKind.NamedAdmin,
            "Main Admin");
        var accountId = createResult.AccountId!.Value;
        var updateResult = await service.UpdateStaffAccountDisplayNameAsync(
            envelope,
            accountId,
            "Updated Admin");
        var deactivateResult = await service.SetStaffAccountActiveStateAsync(
            envelope,
            accountId,
            isActive: false);
        var activateResult = await service.SetStaffAccountActiveStateAsync(
            envelope,
            accountId,
            isActive: true);

        Assert.NotNull(createResult.AuditEntryId);
        Assert.NotNull(updateResult.AuditEntryId);
        Assert.NotNull(deactivateResult.AuditEntryId);
        Assert.NotNull(activateResult.AuditEntryId);
        Assert.Equal(4L, await CountAuditEntriesAsync(database));
        Assert.Equal(1L, await CountAuditEntryAsync(database, createResult.AuditEntryId!.Value));
        Assert.Equal(1L, await CountAuditEntryAsync(database, updateResult.AuditEntryId!.Value));
        Assert.Equal(1L, await CountAuditEntryAsync(database, deactivateResult.AuditEntryId!.Value));
        Assert.Equal(1L, await CountAuditEntryAsync(database, activateResult.AuditEntryId!.Value));

        var actions = await ReadAuditActionsAsync(database);
        Assert.Equal(
            [
                StaffAccountAuditActions.Activated,
                StaffAccountAuditActions.Created,
                StaffAccountAuditActions.Deactivated,
                StaffAccountAuditActions.DisplayNameUpdated,
            ],
            actions);

        var createdAudit = await ReadAuditEntryAsync(database, createResult.AuditEntryId.Value);
        Assert.Equal(StaffAccountAuditActions.Created, createdAudit.ActionType);
        Assert.Equal(StaffAccountAuditActions.EntityType, createdAudit.EntityType);
        Assert.Equal(accountId, createdAudit.EntityId);
        Assert.Equal(envelope.Actor.AccountId.Value, createdAudit.ActorAccountId);
        Assert.Equal("owner", createdAudit.ActorAccountType);
        Assert.Equal("owner", createdAudit.ActorRole);
        Assert.Equal(envelope.Actor.SessionId.Value, createdAudit.SessionId);
        Assert.Equal("owner phone", createdAudit.DeviceLabel);
        Assert.Equal("audit-correlation", createdAudit.RequestCorrelationId);
        Assert.Equal("normal", createdAudit.EntryOrigin);
        Assert.Equal("Owner account maintenance", createdAudit.Reason);
        Assert.Equal("Scheduled staff change", createdAudit.Comment);
        Assert.Equal("{}", createdAudit.BeforeSummary);
        using var createdSummary = JsonDocument.Parse(createdAudit.AfterSummary);
        Assert.Equal(JsonValueKind.Object, createdSummary.RootElement.ValueKind);
        Assert.Equal(4, createdSummary.RootElement.EnumerateObject().Count());
        Assert.Equal(
            "Main Admin",
            createdSummary.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(
            "named_admin",
            createdSummary.RootElement.GetProperty("accountType").GetString());
        Assert.Equal("admin", createdSummary.RootElement.GetProperty("role").GetString());
        Assert.True(createdSummary.RootElement.GetProperty("isActive").GetBoolean());
        Assert.False(createdSummary.RootElement.TryGetProperty("loginName", out _));
        Assert.False(createdSummary.RootElement.TryGetProperty("password", out _));
        Assert.False(createdSummary.RootElement.TryGetProperty("passwordHash", out _));
    }

    [PostgreSqlFact]
    public async Task StaffCredentialAuditOmitsLoginPasswordAndHash()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var envelope = OwnerEnvelope();
        var hashingService = new PasswordHashingService();
        var accountId = (await LifecycleService(dbContext).CreateStaffAccountAsync(
            envelope,
            AccountKind.SharedReceptionAdmin,
            "Front desk shared")).AccountId!.Value;
        var credentialsService = CredentialsService(dbContext, hashingService);

        var configuredResult = await credentialsService.SetStaffCredentialsAsync(
            envelope,
            accountId,
            "front.desk",
            "initial shared password");
        dbContext.ChangeTracker.Clear();
        var loginResult = await new AccountLoginService(dbContext, hashingService, FixedClock())
            .LoginAsync("front.desk", "initial shared password", "front desk tablet");
        Assert.Equal(AccountLoginStatus.Success, loginResult.Status);
        var resetResult = await credentialsService.SetStaffCredentialsAsync(
            OwnerEnvelope(reason: "Rotate shared credentials"),
            accountId,
            "renamed.front.desk",
            "replacement shared password");

        Assert.NotNull(configuredResult.AuditEntryId);
        Assert.NotNull(resetResult.AuditEntryId);
        Assert.Equal(1, resetResult.EndedSessionCount);
        Assert.Equal(2L, await CountCredentialAuditEntriesAsync(database));

        var credentialAuditText = await ReadCredentialAuditTextAsync(database);
        var storedPasswordHash = await database.ExecuteScalarAsync<string>(
            $"select password_hash from bodylife.account_credentials where account_id = '{accountId}'::uuid");

        Assert.DoesNotContain("front.desk", credentialAuditText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("initial shared password", credentialAuditText, StringComparison.Ordinal);
        Assert.DoesNotContain("replacement shared password", credentialAuditText, StringComparison.Ordinal);
        Assert.DoesNotContain(storedPasswordHash!, credentialAuditText, StringComparison.Ordinal);
        Assert.Contains("credentialsConfigured", credentialAuditText, StringComparison.Ordinal);
        Assert.Contains("endedSessionCount", credentialAuditText, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task DeniedNoOpValidationAndConflictPathsDoNotAppendAudit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var lifecycleService = LifecycleService(dbContext);

        var deniedResult = await lifecycleService.CreateStaffAccountAsync(
            AdminEnvelope(),
            AccountKind.NamedAdmin,
            "Denied Admin");

        Assert.Equal(StaffAccountLifecycleStatus.PermissionDenied, deniedResult.Status);
        Assert.Null(deniedResult.AuditEntryId);
        Assert.Equal(0L, await CountAuditEntriesAsync(database));

        var firstAccountId = (await lifecycleService.CreateStaffAccountAsync(
            OwnerEnvelope(),
            AccountKind.NamedAdmin,
            "Main Admin")).AccountId!.Value;
        var secondAccountId = (await lifecycleService.CreateStaffAccountAsync(
            OwnerEnvelope(),
            AccountKind.SharedReceptionAdmin,
            "Front desk shared")).AccountId!.Value;
        var noOpResult = await lifecycleService.SetStaffAccountActiveStateAsync(
            OwnerEnvelope(),
            firstAccountId,
            isActive: true);
        var credentialsService = CredentialsService(dbContext, new PasswordHashingService());
        var invalidResult = await credentialsService.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            firstAccountId,
            "main.admin",
            "short");
        await credentialsService.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            firstAccountId,
            "staff.login",
            "named admin password");
        var conflictResult = await credentialsService.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            secondAccountId,
            " STAFF.LOGIN ",
            "shared desk password");

        Assert.Equal(StaffAccountLifecycleStatus.AlreadyActive, noOpResult.Status);
        Assert.Null(noOpResult.AuditEntryId);
        Assert.Equal(StaffCredentialsStatus.ValidationFailed, invalidResult.Status);
        Assert.Null(invalidResult.AuditEntryId);
        Assert.Equal(StaffCredentialsStatus.LoginNameAlreadyInUse, conflictResult.Status);
        Assert.Null(conflictResult.AuditEntryId);
        Assert.Equal(3L, await CountAuditEntriesAsync(database));
    }

    [PostgreSqlFact]
    public async Task BusinessAuditTableRejectsUnknownAccountType()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => InsertInvalidAccountTypeAuditAsync(database.ConnectionString));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_business_audit_entries_actor_account_type", exception.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task StaffMutationRollsBackWhenAuditEntryCannotBePersisted()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var envelope = OwnerEnvelope() with
        {
            RequestCorrelationId = new RequestCorrelationId(new string('x', 129)),
        };

        await Assert.ThrowsAsync<DbUpdateException>(
            () => LifecycleService(dbContext).CreateStaffAccountAsync(
                envelope,
                AccountKind.NamedAdmin,
                "Main Admin"));

        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>("select count(*) from bodylife.accounts"));
        Assert.Equal(0L, await CountAuditEntriesAsync(database));
    }

    private static StaffAccountLifecycleService LifecycleService(BodyLifeDbContext dbContext)
    {
        return new StaffAccountLifecycleService(
            dbContext,
            new BusinessAuditAppender(dbContext),
            FixedClock());
    }

    private static StaffCredentialsService CredentialsService(
        BodyLifeDbContext dbContext,
        PasswordHashingService passwordHashingService)
    {
        return new StaffCredentialsService(
            dbContext,
            passwordHashingService,
            new BusinessAuditAppender(dbContext),
            FixedClock());
    }

    private static CommandEnvelope OwnerEnvelope(string? reason = null, string? comment = null)
    {
        return Envelope(ActorRole.Owner, AccountKind.Owner, reason, comment);
    }

    private static CommandEnvelope AdminEnvelope()
    {
        return Envelope(ActorRole.Admin, AccountKind.NamedAdmin, reason: null, comment: null);
    }

    private static CommandEnvelope Envelope(
        ActorRole role,
        AccountKind accountKind,
        string? reason,
        string? comment)
    {
        return new CommandEnvelope(
            new ActorContext(
                new AccountId(Guid.Parse("04aee40c-1952-4dfe-8474-53a74a382f18")),
                role,
                accountKind,
                new SessionId(Guid.Parse("fc10efdc-0f6a-444f-962b-266c22cf4fdc")),
                "owner phone"),
            new RequestCorrelationId("audit-correlation"),
            EntryOrigin.Normal,
            OccurredAt: TestNow.AddMinutes(-5),
            IdempotencyKey: null,
            Reason: reason,
            Comment: comment);
    }

    private static TimeProvider FixedClock()
    {
        return new FixedTimeProvider(TestNow);
    }

    private static Task<long> CountAuditEntriesAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>("select count(*) from bodylife.business_audit_entries");
    }

    private static Task<long> CountCredentialAuditEntriesAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.business_audit_entries where action_type like 'staff_credentials.%'");
    }

    private static Task<long> CountAuditEntryAsync(
        PostgreSqlTestDatabase database,
        AuditEntryId auditEntryId)
    {
        return database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.business_audit_entries where id = '{auditEntryId.Value}'::uuid");
    }

    private static async Task<string[]> ReadAuditActionsAsync(PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select action_type from bodylife.business_audit_entries order by action_type",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var actions = new List<string>();

        while (await reader.ReadAsync())
        {
            actions.Add(reader.GetString(0));
        }

        return [.. actions];
    }

    private static async Task<AuditRow> ReadAuditEntryAsync(
        PostgreSqlTestDatabase database,
        AuditEntryId auditEntryId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select action_type,
                   entity_type,
                   entity_id,
                   actor_account_id,
                   actor_account_type,
                   actor_role,
                   session_id,
                   device_label,
                   request_correlation_id,
                   entry_origin,
                   reason,
                   comment,
                   before_summary::text,
                   after_summary::text
            from bodylife.business_audit_entries
            where id = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", auditEntryId.Value);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new AuditRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetGuid(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13));
    }

    private static Task<string?> ReadCredentialAuditTextAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<string>(
            """
            select string_agg(
                action_type || ' ' ||
                related_entity_refs::text || ' ' ||
                before_summary::text || ' ' ||
                after_summary::text || ' ' ||
                coalesce(reason, '') || ' ' ||
                coalesce(comment, ''),
                ' ')
            from bodylife.business_audit_entries
            where action_type like 'staff_credentials.%'
            """);
    }

    private static async Task InsertInvalidAccountTypeAuditAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            insert into bodylife.business_audit_entries (
                id,
                action_type,
                entity_type,
                entity_id,
                related_entity_refs,
                actor_account_id,
                actor_account_type,
                actor_role,
                session_id,
                device_label,
                occurred_at,
                recorded_at,
                reason,
                comment,
                before_summary,
                after_summary,
                request_correlation_id,
                entry_origin,
                idempotency_key,
                changed_after_close)
            values (
                @id,
                'staff_account.created',
                'staff_account',
                @entity_id,
                '{}'::jsonb,
                @actor_account_id,
                'trainer',
                'admin',
                @session_id,
                null,
                @occurred_at,
                @recorded_at,
                null,
                null,
                '{}'::jsonb,
                '{}'::jsonb,
                'constraint-test',
                'normal',
                null,
                false)
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("entity_id", Guid.NewGuid());
        command.Parameters.AddWithValue("actor_account_id", Guid.NewGuid());
        command.Parameters.AddWithValue("session_id", Guid.NewGuid());
        command.Parameters.AddWithValue("occurred_at", TestNow);
        command.Parameters.AddWithValue("recorded_at", TestNow);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record AuditRow(
        string ActionType,
        string EntityType,
        Guid EntityId,
        Guid ActorAccountId,
        string ActorAccountType,
        string ActorRole,
        Guid SessionId,
        string DeviceLabel,
        string RequestCorrelationId,
        string EntryOrigin,
        string Reason,
        string Comment,
        string BeforeSummary,
        string AfterSummary);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
