namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

internal sealed class AccountRecord
{
    public Guid Id { get; set; }

    public required string DisplayName { get; set; }

    public required string AccountType { get; set; }

    public required string Role { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DeactivatedAt { get; set; }
}
