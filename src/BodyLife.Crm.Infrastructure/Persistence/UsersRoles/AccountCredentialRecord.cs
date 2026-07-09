namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

internal sealed class AccountCredentialRecord
{
    public Guid AccountId { get; set; }

    public AccountRecord? Account { get; set; }

    public required string LoginName { get; set; }

    public required string NormalizedLoginName { get; set; }

    public required string PasswordHash { get; set; }

    public DateTimeOffset PasswordChangedAt { get; set; }
}
