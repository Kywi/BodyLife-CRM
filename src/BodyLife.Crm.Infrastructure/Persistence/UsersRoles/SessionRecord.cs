namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

internal sealed class SessionRecord
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public AccountRecord? Account { get; set; }

    public string? DeviceLabel { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
