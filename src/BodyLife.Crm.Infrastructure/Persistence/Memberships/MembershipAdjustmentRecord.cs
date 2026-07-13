namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class MembershipAdjustmentRecord
{
    public Guid Id { get; set; }

    public Guid MembershipId { get; set; }

    public IssuedMembershipRecord? Membership { get; set; }

    public required string AdjustmentType { get; set; }

    public int? DaysDelta { get; set; }

    public int? VisitsDelta { get; set; }

    public decimal? MoneyDelta { get; set; }

    public DateOnly EffectiveDate { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public Guid RecordedSessionId { get; set; }

    public required string EntryOrigin { get; set; }

    public Guid? EntryBatchId { get; set; }

    public required string Status { get; set; }
}
