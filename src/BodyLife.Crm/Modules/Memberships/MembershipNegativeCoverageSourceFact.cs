namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipNegativeCoverageSourceFact
{
    public MembershipNegativeCoverageSourceFact(
        Guid closureItemId,
        Guid visitId,
        Guid sourceMembershipId,
        Guid? coveringMembershipId,
        DateOnly businessDate,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        MembershipNegativeCoverageSourceStatus status)
    {
        if (closureItemId == Guid.Empty)
        {
            throw new ArgumentException("Closure item id is required.", nameof(closureItemId));
        }

        if (visitId == Guid.Empty)
        {
            throw new ArgumentException("Visit id is required.", nameof(visitId));
        }

        if (sourceMembershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Source Membership id is required.",
                nameof(sourceMembershipId));
        }

        if (coveringMembershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Covering Membership id must be non-empty when supplied.",
                nameof(coveringMembershipId));
        }

        if (coveringMembershipId == sourceMembershipId)
        {
            throw new ArgumentException(
                "A Membership cannot cover its own negative Visit.",
                nameof(coveringMembershipId));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Negative coverage source status is not supported.");
        }

        ClosureItemId = closureItemId;
        VisitId = visitId;
        SourceMembershipId = sourceMembershipId;
        CoveringMembershipId = coveringMembershipId;
        BusinessDate = businessDate;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
        Status = status;
    }

    public Guid ClosureItemId { get; }

    public Guid VisitId { get; }

    public Guid SourceMembershipId { get; }

    public Guid? CoveringMembershipId { get; }

    public DateOnly BusinessDate { get; }

    public DateTimeOffset OccurredAt { get; }

    public DateTimeOffset RecordedAt { get; }

    public MembershipNegativeCoverageSourceStatus Status { get; }

    public bool IsActive => Status == MembershipNegativeCoverageSourceStatus.Active;
}

public enum MembershipNegativeCoverageSourceStatus
{
    Active = 1,
    Canceled,
    Replaced,
}
