using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public sealed class StaffAccountLifecycleService(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    TimeProvider timeProvider)
{
    private const int DisplayNameMaxLength = 160;
    private const string NamedAdminAccountType = "named_admin";
    private const string SharedReceptionAdminAccountType = "shared_reception_admin";
    private const string OwnerAccountType = "owner";
    private const string AdminRole = "admin";

    public async Task<StaffAccountLifecycleResult> CreateStaffAccountAsync(
        CommandEnvelope envelope,
        AccountKind accountKind,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!IsOwner(envelope.Actor))
        {
            return StaffAccountLifecycleResult.PermissionDenied();
        }

        if (!TryMapStaffAccountKind(accountKind, out var accountType))
        {
            return StaffAccountLifecycleResult.ValidationFailed(
                "Only named Admin and shared Reception/Admin accounts can be managed by this service.");
        }

        if (!TryNormalizeDisplayName(displayName, out var normalizedDisplayName, out var validationResult))
        {
            return validationResult;
        }

        var recordedAt = timeProvider.GetUtcNow();
        var account = new AccountRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = normalizedDisplayName,
            AccountType = accountType,
            Role = AdminRole,
            IsActive = true,
            CreatedAt = recordedAt,
            DeactivatedAt = null,
        };

        dbContext.Set<AccountRecord>().Add(account);
        var auditEntryId = auditAppender.Append(
            envelope,
            StaffAccountAuditActions.Created,
            StaffAccountAuditActions.EntityType,
            account.Id,
            recordedAt,
            afterSummary: new
            {
                account.DisplayName,
                account.AccountType,
                account.Role,
                account.IsActive,
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        return StaffAccountLifecycleResult.Created(account.Id, auditEntryId);
    }

    public async Task<StaffAccountLifecycleResult> UpdateStaffAccountDisplayNameAsync(
        CommandEnvelope envelope,
        Guid accountId,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!IsOwner(envelope.Actor))
        {
            return StaffAccountLifecycleResult.PermissionDenied();
        }

        if (!TryNormalizeDisplayName(displayName, out var normalizedDisplayName, out var validationResult))
        {
            return validationResult;
        }

        var account = await FindAccountAsync(accountId, cancellationToken);
        var accountGuard = GuardManageableStaffAccount(account);

        if (accountGuard is not null)
        {
            return accountGuard;
        }

        var previousDisplayName = account!.DisplayName;
        var recordedAt = timeProvider.GetUtcNow();
        account.DisplayName = normalizedDisplayName;
        var auditEntryId = auditAppender.Append(
            envelope,
            StaffAccountAuditActions.DisplayNameUpdated,
            StaffAccountAuditActions.EntityType,
            account.Id,
            recordedAt,
            beforeSummary: new { DisplayName = previousDisplayName },
            afterSummary: new { account.DisplayName });
        await dbContext.SaveChangesAsync(cancellationToken);

        return StaffAccountLifecycleResult.DisplayNameUpdated(account.Id, auditEntryId);
    }

    public async Task<StaffAccountLifecycleResult> SetStaffAccountActiveStateAsync(
        CommandEnvelope envelope,
        Guid accountId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!IsOwner(envelope.Actor))
        {
            return StaffAccountLifecycleResult.PermissionDenied();
        }

        var account = await FindAccountAsync(accountId, cancellationToken);
        var accountGuard = GuardManageableStaffAccount(account);

        if (accountGuard is not null)
        {
            return accountGuard;
        }

        if (account!.IsActive == isActive)
        {
            return isActive
                ? StaffAccountLifecycleResult.AlreadyActive(account.Id)
                : StaffAccountLifecycleResult.AlreadyInactive(account.Id);
        }

        if (!isActive && string.IsNullOrWhiteSpace(envelope.Reason))
        {
            return StaffAccountLifecycleResult.ValidationFailed(
                "Reason is required to deactivate a staff account.");
        }

        var wasActive = account.IsActive;
        var now = timeProvider.GetUtcNow();
        account.IsActive = isActive;
        account.DeactivatedAt = isActive ? null : now;
        var endedSessionCount = 0;

        if (!isActive)
        {
            var activeSessions = await dbContext.Set<SessionRecord>()
                .Where(session => session.AccountId == account.Id && session.EndedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var session in activeSessions)
            {
                session.EndedAt = now;
                session.LastSeenAt = now;
            }

            endedSessionCount = activeSessions.Count;
        }

        var auditEntryId = auditAppender.Append(
            envelope,
            isActive ? StaffAccountAuditActions.Activated : StaffAccountAuditActions.Deactivated,
            StaffAccountAuditActions.EntityType,
            account.Id,
            now,
            beforeSummary: new { IsActive = wasActive },
            afterSummary: new
            {
                account.IsActive,
                EndedSessionCount = endedSessionCount,
            });

        await dbContext.SaveChangesAsync(cancellationToken);

        return isActive
            ? StaffAccountLifecycleResult.Activated(account.Id, auditEntryId)
            : StaffAccountLifecycleResult.Deactivated(account.Id, auditEntryId);
    }

    private Task<AccountRecord?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return dbContext.Set<AccountRecord>()
            .SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken);
    }

    private static StaffAccountLifecycleResult? GuardManageableStaffAccount(AccountRecord? account)
    {
        if (account is null)
        {
            return StaffAccountLifecycleResult.NotFound();
        }

        if (account.AccountType == OwnerAccountType)
        {
            return StaffAccountLifecycleResult.OwnerAccountProtected(account.Id);
        }

        return IsStaffAccountType(account.AccountType)
            ? null
            : StaffAccountLifecycleResult.ValidationFailed("Account type is not manageable in this workflow.");
    }

    private static bool TryNormalizeDisplayName(
        string? displayName,
        out string normalizedDisplayName,
        out StaffAccountLifecycleResult validationResult)
    {
        normalizedDisplayName = string.Empty;
        validationResult = StaffAccountLifecycleResult.ValidationFailed("Display name is required.");

        var trimmedDisplayName = displayName?.Trim();

        if (string.IsNullOrWhiteSpace(trimmedDisplayName))
        {
            return false;
        }

        if (trimmedDisplayName.Length > DisplayNameMaxLength)
        {
            validationResult = StaffAccountLifecycleResult.ValidationFailed(
                $"Display name must be {DisplayNameMaxLength} characters or fewer.");
            return false;
        }

        normalizedDisplayName = trimmedDisplayName;
        return true;
    }

    private static bool TryMapStaffAccountKind(AccountKind accountKind, out string accountType)
    {
        accountType = accountKind switch
        {
            AccountKind.NamedAdmin => NamedAdminAccountType,
            AccountKind.SharedReceptionAdmin => SharedReceptionAdminAccountType,
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(accountType);
    }

    private static bool IsStaffAccountType(string accountType)
    {
        return accountType is NamedAdminAccountType or SharedReceptionAdminAccountType;
    }

    private static bool IsOwner(ActorContext actor)
    {
        return actor is { Role: ActorRole.Owner, AccountKind: AccountKind.Owner };
    }
}

public sealed record StaffAccountLifecycleResult(
    StaffAccountLifecycleStatus Status,
    Guid? AccountId,
    AuditEntryId? AuditEntryId,
    string Message)
{
    public bool Succeeded => Status is
        StaffAccountLifecycleStatus.Created
        or StaffAccountLifecycleStatus.DisplayNameUpdated
        or StaffAccountLifecycleStatus.Activated
        or StaffAccountLifecycleStatus.Deactivated
        or StaffAccountLifecycleStatus.AlreadyActive
        or StaffAccountLifecycleStatus.AlreadyInactive;

    public static StaffAccountLifecycleResult Created(Guid accountId, AuditEntryId auditEntryId)
    {
        return new StaffAccountLifecycleResult(
            StaffAccountLifecycleStatus.Created,
            accountId,
            auditEntryId,
            "Staff account created.");
    }

    public static StaffAccountLifecycleResult DisplayNameUpdated(Guid accountId, AuditEntryId auditEntryId)
    {
        return new StaffAccountLifecycleResult(
            StaffAccountLifecycleStatus.DisplayNameUpdated,
            accountId,
            auditEntryId,
            "Staff account display name updated.");
    }

    public static StaffAccountLifecycleResult Activated(Guid accountId, AuditEntryId auditEntryId)
    {
        return new StaffAccountLifecycleResult(
            StaffAccountLifecycleStatus.Activated,
            accountId,
            auditEntryId,
            "Staff account activated.");
    }

    public static StaffAccountLifecycleResult Deactivated(Guid accountId, AuditEntryId auditEntryId)
    {
        return new StaffAccountLifecycleResult(
            StaffAccountLifecycleStatus.Deactivated,
            accountId,
            auditEntryId,
            "Staff account deactivated.");
    }

    public static StaffAccountLifecycleResult AlreadyActive(Guid accountId)
    {
        return new StaffAccountLifecycleResult(
            StaffAccountLifecycleStatus.AlreadyActive,
            accountId,
            null,
            "Staff account is already active.");
    }

    public static StaffAccountLifecycleResult AlreadyInactive(Guid accountId)
    {
        return new StaffAccountLifecycleResult(
            StaffAccountLifecycleStatus.AlreadyInactive,
            accountId,
            null,
            "Staff account is already inactive.");
    }

    public static StaffAccountLifecycleResult PermissionDenied()
    {
        return new StaffAccountLifecycleResult(
            StaffAccountLifecycleStatus.PermissionDenied,
            null,
            null,
            "Owner account is required.");
    }

    public static StaffAccountLifecycleResult ValidationFailed(string message)
    {
        return new StaffAccountLifecycleResult(
            StaffAccountLifecycleStatus.ValidationFailed,
            null,
            null,
            message);
    }

    public static StaffAccountLifecycleResult NotFound()
    {
        return new StaffAccountLifecycleResult(
            StaffAccountLifecycleStatus.NotFound,
            null,
            null,
            "Account was not found.");
    }

    public static StaffAccountLifecycleResult OwnerAccountProtected(Guid accountId)
    {
        return new StaffAccountLifecycleResult(
            StaffAccountLifecycleStatus.OwnerAccountProtected,
            accountId,
            null,
            "Owner account is protected by the bootstrap workflow.");
    }
}

public enum StaffAccountLifecycleStatus
{
    Created = 1,
    DisplayNameUpdated,
    Activated,
    Deactivated,
    AlreadyActive,
    AlreadyInactive,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    OwnerAccountProtected,
}
