namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class MembershipOpeningStateRecord
{
    public Guid Id { get; set; }

    public Guid MembershipId { get; set; }

    public IssuedMembershipRecord? Membership { get; set; }

    public DateOnly OpeningAsOfDate { get; set; }

    public int DeclaredRemainingVisits { get; set; }

    public int DeclaredNegativeBalance { get; set; }

    public DateOnly? KnownEffectiveEndDate { get; set; }

    public int? KnownExtensionDays { get; set; }

    public required string SourceReference { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public Guid RecordedSessionId { get; set; }

    public required string EntryOrigin { get; set; }

    public Guid? EntryBatchId { get; set; }

    public required string Status { get; set; }
}
