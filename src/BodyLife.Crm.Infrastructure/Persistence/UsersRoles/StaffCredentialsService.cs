using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public sealed class StaffCredentialsService(
    BodyLifeDbContext dbContext,
    PasswordHashingService passwordHashingService,
    BusinessAuditAppender auditAppender,
    TimeProvider timeProvider)
{
    private const int LoginNameMaxLength = 120;
    private const int MinimumPasswordLength = 12;
    private const string LoginNameUniqueConstraint = "ux_account_credentials_normalized_login_name";
    private const string NamedAdminAccountType = "named_admin";
    private const string OwnerAccountType = "owner";
    private const string SharedReceptionAdminAccountType = "shared_reception_admin";

    public async Task<StaffCredentialsResult> SetStaffCredentialsAsync(
        CommandEnvelope envelope,
        Guid accountId,
        string? loginName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!IsOwner(envelope.Actor))
        {
            return StaffCredentialsResult.PermissionDenied();
        }

        if (!TryNormalizeLoginName(loginName, out var normalizedLogin, out var validationResult))
        {
            return validationResult;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return StaffCredentialsResult.ValidationFailed("Staff password is required.");
        }

        if (password.Length < MinimumPasswordLength)
        {
            return StaffCredentialsResult.ValidationFailed(
                $"Staff password must be at least {MinimumPasswordLength} characters.");
        }

        var account = await dbContext.Set<AccountRecord>()
            .SingleOrDefaultAsync(value => value.Id == accountId, cancellationToken);

        if (account is null)
        {
            return StaffCredentialsResult.NotFound();
        }

        if (account.AccountType == OwnerAccountType)
        {
            return StaffCredentialsResult.OwnerAccountProtected(account.Id);
        }

        if (!IsStaffAccountType(account.AccountType))
        {
            return StaffCredentialsResult.ValidationFailed(
                "Account type is not manageable in this workflow.");
        }

        var loginNameInUse = await dbContext.Set<AccountCredentialRecord>()
            .AnyAsync(
                credential => credential.NormalizedLoginName == normalizedLogin.NormalizedLoginName
                    && credential.AccountId != account.Id,
                cancellationToken);

        if (loginNameInUse)
        {
            return StaffCredentialsResult.LoginNameAlreadyInUse();
        }

        var credential = await dbContext.Set<AccountCredentialRecord>()
            .SingleOrDefaultAsync(value => value.AccountId == account.Id, cancellationToken);
        var isReset = credential is not null;
        var now = timeProvider.GetUtcNow();

        if (credential is null)
        {
            credential = new AccountCredentialRecord
            {
                AccountId = account.Id,
                LoginName = normalizedLogin.LoginName,
                NormalizedLoginName = normalizedLogin.NormalizedLoginName,
                PasswordHash = passwordHashingService.HashPassword(password),
                PasswordChangedAt = now,
            };
            dbContext.Set<AccountCredentialRecord>().Add(credential);
        }
        else
        {
            credential.LoginName = normalizedLogin.LoginName;
            credential.NormalizedLoginName = normalizedLogin.NormalizedLoginName;
            credential.PasswordHash = passwordHashingService.HashPassword(password);
            credential.PasswordChangedAt = now;
        }

        var activeSessions = await dbContext.Set<SessionRecord>()
            .Where(session => session.AccountId == account.Id && session.EndedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in activeSessions)
        {
            session.EndedAt = now;
            session.LastSeenAt = now;
        }

        var auditEntryId = auditAppender.Append(
            envelope,
            isReset
                ? StaffAccountAuditActions.CredentialsReset
                : StaffAccountAuditActions.CredentialsConfigured,
            StaffAccountAuditActions.EntityType,
            account.Id,
            now,
            beforeSummary: new { CredentialsConfigured = isReset },
            afterSummary: new
            {
                CredentialsConfigured = true,
                EndedSessionCount = activeSessions.Count,
            });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsLoginNameUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            return StaffCredentialsResult.LoginNameAlreadyInUse();
        }

        return isReset
            ? StaffCredentialsResult.Reset(account.Id, auditEntryId, activeSessions.Count)
            : StaffCredentialsResult.Configured(account.Id, auditEntryId);
    }

    private static bool TryNormalizeLoginName(
        string? loginName,
        out NormalizedLogin normalizedLogin,
        out StaffCredentialsResult validationResult)
    {
        normalizedLogin = new NormalizedLogin(string.Empty, string.Empty);
        validationResult = StaffCredentialsResult.ValidationFailed("Staff login name is required.");

        var trimmedLoginName = loginName?.Trim();

        if (string.IsNullOrWhiteSpace(trimmedLoginName))
        {
            return false;
        }

        if (trimmedLoginName.Length > LoginNameMaxLength)
        {
            validationResult = StaffCredentialsResult.ValidationFailed(
                $"Staff login name must be {LoginNameMaxLength} characters or fewer.");
            return false;
        }

        normalizedLogin = new NormalizedLogin(
            trimmedLoginName,
            PasswordHashingService.NormalizeLoginName(trimmedLoginName));
        return true;
    }

    private static bool IsLoginNameUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: LoginNameUniqueConstraint,
        };
    }

    private static bool IsStaffAccountType(string accountType)
    {
        return accountType is NamedAdminAccountType or SharedReceptionAdminAccountType;
    }

    private static bool IsOwner(ActorContext actor)
    {
        return actor is { Role: ActorRole.Owner, AccountKind: AccountKind.Owner };
    }

    private sealed record NormalizedLogin(string LoginName, string NormalizedLoginName);
}

public sealed record StaffCredentialsResult(
    StaffCredentialsStatus Status,
    Guid? AccountId,
    AuditEntryId? AuditEntryId,
    int EndedSessionCount,
    string Message)
{
    public bool Succeeded => Status is StaffCredentialsStatus.Configured or StaffCredentialsStatus.Reset;

    public static StaffCredentialsResult Configured(Guid accountId, AuditEntryId auditEntryId)
    {
        return new StaffCredentialsResult(
            StaffCredentialsStatus.Configured,
            accountId,
            auditEntryId,
            0,
            "Staff credentials configured.");
    }

    public static StaffCredentialsResult Reset(
        Guid accountId,
        AuditEntryId auditEntryId,
        int endedSessionCount)
    {
        return new StaffCredentialsResult(
            StaffCredentialsStatus.Reset,
            accountId,
            auditEntryId,
            endedSessionCount,
            "Staff credentials reset.");
    }

    public static StaffCredentialsResult PermissionDenied()
    {
        return Failure(StaffCredentialsStatus.PermissionDenied, "Owner account is required.");
    }

    public static StaffCredentialsResult ValidationFailed(string message)
    {
        return Failure(StaffCredentialsStatus.ValidationFailed, message);
    }

    public static StaffCredentialsResult NotFound()
    {
        return Failure(StaffCredentialsStatus.NotFound, "Account was not found.");
    }

    public static StaffCredentialsResult OwnerAccountProtected(Guid accountId)
    {
        return new StaffCredentialsResult(
            StaffCredentialsStatus.OwnerAccountProtected,
            accountId,
            null,
            0,
            "Owner credentials are protected by the bootstrap workflow.");
    }

    public static StaffCredentialsResult LoginNameAlreadyInUse()
    {
        return Failure(
            StaffCredentialsStatus.LoginNameAlreadyInUse,
            "Login name is already in use.");
    }

    private static StaffCredentialsResult Failure(StaffCredentialsStatus status, string message)
    {
        return new StaffCredentialsResult(status, null, null, 0, message);
    }
}

public enum StaffCredentialsStatus
{
    Configured = 1,
    Reset,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    OwnerAccountProtected,
    LoginNameAlreadyInUse,
}
