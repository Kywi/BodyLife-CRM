namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipCalculatedState
{
    internal MembershipCalculatedState(
        int countedVisits,
        int remainingVisits,
        int negativeBalance,
        Guid? firstNegativeVisitId,
        DateOnly? firstNegativeVisitDate,
        int extensionDays,
        DateOnly effectiveEndDate,
        DateTimeOffset? lastCountedVisitAt)
    {
        CountedVisits = countedVisits;
        RemainingVisits = remainingVisits;
        NegativeBalance = negativeBalance;
        FirstNegativeVisitId = firstNegativeVisitId;
        FirstNegativeVisitDate = firstNegativeVisitDate;
        ExtensionDays = extensionDays;
        EffectiveEndDate = effectiveEndDate;
        LastCountedVisitAt = lastCountedVisitAt;
    }

    public int CountedVisits { get; }

    public int RemainingVisits { get; }

    public int NegativeBalance { get; }

    public Guid? FirstNegativeVisitId { get; }

    public DateOnly? FirstNegativeVisitDate { get; }

    public int ExtensionDays { get; }

    public DateOnly EffectiveEndDate { get; }

    public DateTimeOffset? LastCountedVisitAt { get; }

    public bool IsActiveByDate(DateOnly asOfDate)
    {
        return MembershipDateRules.IsActiveByDate(asOfDate, EffectiveEndDate);
    }
}
