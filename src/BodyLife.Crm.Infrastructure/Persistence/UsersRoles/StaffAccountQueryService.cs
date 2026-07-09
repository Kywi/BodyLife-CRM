using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public sealed class StaffAccountQueryService(BodyLifeDbContext dbContext)
{
    private const string NamedAdminAccountType = "named_admin";
    private const string SharedReceptionAdminAccountType = "shared_reception_admin";

    public async Task<IReadOnlyList<StaffAccountSummary>> ListStaffAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await dbContext.Set<AccountRecord>()
            .AsNoTracking()
            .Where(account => account.AccountType == NamedAdminAccountType
                || account.AccountType == SharedReceptionAdminAccountType)
            .OrderBy(account => account.AccountType)
            .ThenBy(account => account.DisplayName)
            .Select(account => new
            {
                account.Id,
                account.DisplayName,
                account.AccountType,
                account.IsActive,
                LoginName = dbContext.Set<AccountCredentialRecord>()
                    .Where(credential => credential.AccountId == account.Id)
                    .Select(credential => credential.LoginName)
                    .SingleOrDefault(),
                ActiveSessionCount = dbContext.Set<SessionRecord>()
                    .Count(session => session.AccountId == account.Id && session.EndedAt == null),
            })
            .ToListAsync(cancellationToken);

        return records
            .Select(record => new StaffAccountSummary(
                record.Id,
                record.DisplayName,
                MapAccountKind(record.AccountType),
                record.IsActive,
                record.LoginName,
                record.ActiveSessionCount))
            .ToArray();
    }

    private static AccountKind MapAccountKind(string accountType)
    {
        return accountType switch
        {
            NamedAdminAccountType => AccountKind.NamedAdmin,
            SharedReceptionAdminAccountType => AccountKind.SharedReceptionAdmin,
            _ => throw new InvalidOperationException($"Unsupported staff account type '{accountType}'."),
        };
    }
}

public sealed record StaffAccountSummary(
    Guid AccountId,
    string DisplayName,
    AccountKind AccountKind,
    bool IsActive,
    string? LoginName,
    int ActiveSessionCount)
{
    public bool HasCredentials => !string.IsNullOrWhiteSpace(LoginName);
}
