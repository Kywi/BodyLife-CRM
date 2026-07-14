using BodyLife.Crm.Infrastructure.Persistence.Memberships;

namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

internal sealed class FreezeRecord
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }

    public Guid MembershipId { get; set; }

    public IssuedMembershipRecord? Membership { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public Guid SessionId { get; set; }

    public required string EntryOrigin { get; set; }

    public Guid? EntryBatchId { get; set; }

    public required string Status { get; set; }
}
