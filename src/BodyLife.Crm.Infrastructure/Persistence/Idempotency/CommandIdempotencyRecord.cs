namespace BodyLife.Crm.Infrastructure.Persistence.Idempotency;

internal sealed class CommandIdempotencyRecord
{
    public Guid Id { get; set; }

    public required string CommandName { get; set; }

    public required string IdempotencyKey { get; set; }

    public required string RequestCorrelationId { get; set; }

    public Guid? AccountId { get; set; }

    public required string ActorRole { get; set; }

    public required string AccountKind { get; set; }

    public Guid? SessionId { get; set; }

    public string? DeviceLabel { get; set; }

    public required string EntryOrigin { get; set; }

    public required string Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid? PrimaryEntityId { get; set; }

    public Guid? RereadTargetId { get; set; }

    public Guid? AuditEntryId { get; set; }

    public string? ResultFingerprint { get; set; }
}
