namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class MembershipStateCacheRecord
{
    public Guid MembershipId { get; set; }

    public IssuedMembershipRecord? Membership { get; set; }

    public int CountedVisits { get; set; }

    public int RemainingVisits { get; set; }

    public int NegativeBalance { get; set; }

    public Guid? FirstNegativeVisitId { get; set; }

    public DateOnly? FirstNegativeVisitDate { get; set; }

    public int ExtensionDays { get; set; }

    public DateOnly EffectiveEndDate { get; set; }

    public DateTimeOffset? LastCountedVisitAt { get; set; }

    public DateTimeOffset RecalculatedAt { get; set; }

    public int RecalculationVersion { get; set; }
}
