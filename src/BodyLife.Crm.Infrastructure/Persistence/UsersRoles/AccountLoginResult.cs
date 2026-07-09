namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public sealed record AccountLoginResult(
    AccountLoginStatus Status,
    AccountSessionSnapshot? Session)
{
    public static AccountLoginResult Success(AccountSessionSnapshot session)
    {
        return new AccountLoginResult(AccountLoginStatus.Success, session);
    }

    public static AccountLoginResult InvalidCredentials()
    {
        return new AccountLoginResult(AccountLoginStatus.InvalidCredentials, null);
    }
}
