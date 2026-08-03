namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

internal sealed class EntryBatchRecord
{
    public Guid Id { get; set; }

    public required string BatchType { get; set; }

    public required string PaperSheetNumber { get; set; }

    public DateOnly BusinessDateStart { get; set; }

    public DateOnly BusinessDateEnd { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public DateTimeOffset? ReconciledAt { get; set; }

    public Guid? ReconciledByAccountId { get; set; }

    public string? Note { get; set; }
}

internal sealed class EntryBatchRowRecord
{
    public Guid Id { get; set; }

    public Guid EntryBatchId { get; set; }

    public int LineNumber { get; set; }

    public required string EventType { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string Explanation { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public Guid SessionId { get; set; }
}

internal sealed class EntryBatchRowEntityRecord
{
    public Guid EntryBatchRowId { get; set; }

    public required string EntityType { get; set; }

    public Guid EntityId { get; set; }
}
