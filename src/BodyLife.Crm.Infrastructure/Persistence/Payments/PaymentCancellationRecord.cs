namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

internal sealed class PaymentCancellationRecord
{
    public Guid Id { get; set; }

    public Guid PaymentId { get; set; }

    public PaymentRecord? Payment { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public Guid SessionId { get; set; }

    public required string EntryOrigin { get; set; }

    public Guid? EntryBatchId { get; set; }
}
