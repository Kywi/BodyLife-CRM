namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

internal sealed class BusinessAuditEntryRecord
{
    public Guid Id { get; set; }

    public required string ActionType { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }

    public required string RelatedEntityRefsJson { get; set; }

    public Guid ActorAccountId { get; set; }

    public required string ActorAccountType { get; set; }

    public required string ActorRole { get; set; }

    public Guid SessionId { get; set; }

    public string? DeviceLabel { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public string? Reason { get; set; }

    public string? Comment { get; set; }

    public required string BeforeSummaryJson { get; set; }

    public required string AfterSummaryJson { get; set; }

    public required string RequestCorrelationId { get; set; }

    public required string EntryOrigin { get; set; }

    public string? IdempotencyKey { get; set; }

    public bool ChangedAfterClose { get; set; }
}
