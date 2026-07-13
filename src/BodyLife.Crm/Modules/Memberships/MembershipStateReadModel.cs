namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipStateReadModel
{
    public MembershipStateReadModel(
        Guid membershipId,
        Guid clientId,
        Guid membershipTypeId,
        IssuedMembershipSnapshot snapshot,
        DateOnly startDate,
        DateOnly baseEndDate,
        DateOnly effectiveEndDate,
        int countedVisits,
        int remainingVisits,
        int negativeBalance,
        Guid? firstNegativeVisitId,
        DateOnly? firstNegativeVisitDate,
        int extensionDays,
        DateTimeOffset? lastCountedVisitAt,
        DateOnly asOfDate)
    {
        MembershipId = membershipId;
        ClientId = clientId;
        MembershipTypeId = membershipTypeId;
        Snapshot = snapshot;
        StartDate = startDate;
        BaseEndDate = baseEndDate;
        EffectiveEndDate = effectiveEndDate;
        CountedVisits = countedVisits;
        RemainingVisits = remainingVisits;
        NegativeBalance = negativeBalance;
        FirstNegativeVisitId = firstNegativeVisitId;
        FirstNegativeVisitDate = firstNegativeVisitDate;
        ExtensionDays = extensionDays;
        LastCountedVisitAt = lastCountedVisitAt;
        AsOfDate = asOfDate;
    }

    public Guid MembershipId { get; }

    public Guid ClientId { get; }

    public Guid MembershipTypeId { get; }

    public IssuedMembershipSnapshot Snapshot { get; }

    public DateOnly StartDate { get; }

    public DateOnly BaseEndDate { get; }

    public DateOnly EffectiveEndDate { get; }

    public int CountedVisits { get; }

    public int RemainingVisits { get; }

    public int NegativeBalance { get; }

    public Guid? FirstNegativeVisitId { get; }

    public DateOnly? FirstNegativeVisitDate { get; }

    public int ExtensionDays { get; }

    public DateTimeOffset? LastCountedVisitAt { get; }

    public DateOnly AsOfDate { get; }

    public bool IsActiveByDate => MembershipDateRules.IsActiveByDate(
        AsOfDate,
        EffectiveEndDate);
}
