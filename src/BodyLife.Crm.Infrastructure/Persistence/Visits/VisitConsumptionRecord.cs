using BodyLife.Crm.Infrastructure.Persistence.Memberships;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

internal sealed class VisitConsumptionRecord
{
    public Guid Id { get; set; }

    public Guid VisitId { get; set; }

    public Guid ClientId { get; set; }

    public required string VisitKind { get; set; }

    public VisitRecord? Visit { get; set; }

    public Guid MembershipId { get; set; }

    public IssuedMembershipRecord? Membership { get; set; }

    public required string ConsumptionType { get; set; }

    public required string SourceFactType { get; set; }

    public Guid SourceFactId { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public Guid RecordedSessionId { get; set; }

    public required string Status { get; set; }
}
