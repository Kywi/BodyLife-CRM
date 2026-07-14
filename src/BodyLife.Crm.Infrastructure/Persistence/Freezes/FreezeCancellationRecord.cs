namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

internal sealed class FreezeCancellationRecord
{
    public Guid Id { get; set; }

    public Guid FreezeId { get; set; }

    public FreezeRecord? Freeze { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public Guid SessionId { get; set; }

    public required string EntryOrigin { get; set; }

    public Guid? EntryBatchId { get; set; }
}
