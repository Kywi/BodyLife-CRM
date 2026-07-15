namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

internal sealed class PaymentCorrectionRecord
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }

    public Guid OriginalPaymentId { get; set; }

    public PaymentRecord? OriginalPayment { get; set; }

    public Guid ReplacementPaymentId { get; set; }

    public PaymentRecord? ReplacementPayment { get; set; }

    public required string ChangedFieldsJson { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public Guid SessionId { get; set; }

    public required string EntryOrigin { get; set; }

    public Guid? EntryBatchId { get; set; }
}
