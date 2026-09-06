namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class MembershipLifecycleClosureRecord
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid SourceMembershipId { get; set; }
    public Guid? SuccessorMembershipId { get; set; }
    public Guid? NegativeClosureId { get; set; }
    public string ReasonCode { get; set; } = null!;
    public Guid RecordedByAccountId { get; set; }
    public Guid SessionId { get; set; }
    public string CorrelationId { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public string EntryOrigin { get; set; } = null!;
    public Guid? EntryBatchId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string? Explanation { get; set; }
}
