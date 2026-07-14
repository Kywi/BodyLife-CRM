namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipVisitSourceFact
{
    public MembershipVisitSourceFact(
        Guid membershipId,
        Guid visitId,
        DateOnly businessDate,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        MembershipVisitSourceStatus status)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        if (visitId == Guid.Empty)
        {
            throw new ArgumentException(
                "Visit id is required.",
                nameof(visitId));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Membership Visit source status is not supported.");
        }

        MembershipId = membershipId;
        VisitId = visitId;
        BusinessDate = businessDate;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
        Status = status;
    }

    public Guid MembershipId { get; }

    public Guid VisitId { get; }

    public DateOnly BusinessDate { get; }

    public DateTimeOffset OccurredAt { get; }

    public DateTimeOffset RecordedAt { get; }

    public MembershipVisitSourceStatus Status { get; }

    public bool IsActiveCounted => Status == MembershipVisitSourceStatus.Active;
}
