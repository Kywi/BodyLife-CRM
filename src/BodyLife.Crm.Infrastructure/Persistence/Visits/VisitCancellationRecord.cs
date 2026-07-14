namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

internal sealed class VisitCancellationRecord
{
    public Guid Id { get; set; }

    public Guid VisitId { get; set; }

    public VisitRecord? Visit { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public Guid SessionId { get; set; }

    public required string EntryOrigin { get; set; }

    public Guid? EntryBatchId { get; set; }
}
