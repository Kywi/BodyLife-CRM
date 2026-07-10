namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public static class AccountSessionPolicy
{
    public static TimeSpan IdleTimeout { get; } = TimeSpan.FromHours(12);
}
