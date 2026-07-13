namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class MembershipExtensionDayRecord
{
    public Guid Id { get; set; }

    public Guid MembershipId { get; set; }

    public IssuedMembershipRecord? Membership { get; set; }

    public DateOnly ExtensionDate { get; set; }

    public required string SourceType { get; set; }

    public Guid SourceId { get; set; }

    public required string SourceLabel { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset RecalculatedAt { get; set; }
}
