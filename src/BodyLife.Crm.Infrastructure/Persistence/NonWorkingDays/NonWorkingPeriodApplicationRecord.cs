using BodyLife.Crm.Infrastructure.Persistence.Memberships;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal sealed class NonWorkingPeriodApplicationRecord
{
    public Guid Id { get; set; }

    public Guid NonWorkingPeriodId { get; set; }

    public NonWorkingPeriodRecord? NonWorkingPeriod { get; set; }

    public Guid MembershipId { get; set; }

    public Guid ClientId { get; set; }

    public IssuedMembershipRecord? Membership { get; set; }

    public DateOnly AppliedStartDate { get; set; }

    public DateOnly AppliedEndDate { get; set; }

    public DateTimeOffset PreviewedAt { get; set; }

    public DateTimeOffset ConfirmedAt { get; set; }

    public required string Status { get; set; }
}
