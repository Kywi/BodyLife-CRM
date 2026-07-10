namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public sealed record AccountSessionSnapshot(
    Guid AccountId,
    Guid SessionId,
    string DisplayName,
    string AccountType,
    string Role,
    string? DeviceLabel,
    DateTimeOffset ExpiresAt);
