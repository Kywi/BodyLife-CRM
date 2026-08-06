namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class IssuedMembershipSaleCorrectionRecord
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid OriginalMembershipId { get; set; }
    public Guid OriginalPaymentId { get; set; }
    public Guid? ReplacementMembershipId { get; set; }
    public Guid? ReplacementPaymentId { get; set; }
    public required string CorrectionMode { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public Guid RecordedByAccountId { get; set; }
    public Guid SessionId { get; set; }
    public required string EntryOrigin { get; set; }
    public Guid? EntryBatchId { get; set; }
    public required string Status { get; set; }
    public required string DependencyToken { get; set; }
}

internal sealed class MembershipReplacementDependencyItemRecord
{
    public Guid Id { get; set; }
    public Guid SaleCorrectionId { get; set; }
    public required string DependencyType { get; set; }
    public Guid OriginalFactId { get; set; }
    public Guid? ReplacementFactId { get; set; }
    public required string ValidationSummary { get; set; }
    public required string Status { get; set; }
}
