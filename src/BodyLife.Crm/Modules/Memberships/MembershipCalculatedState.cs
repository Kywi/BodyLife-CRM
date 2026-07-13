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

    public static MembershipCalculatedState FromStoredCache(
        MembershipIssueTerms? issueTerms,
        int countedVisits,
        int remainingVisits,
        int negativeBalance,
        Guid? firstNegativeVisitId,
        DateOnly? firstNegativeVisitDate,
        int extensionDays,
        DateOnly effectiveEndDate,
        DateTimeOffset? lastCountedVisitAt)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);

        if (countedVisits < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(countedVisits),
                countedVisits,
                "Counted visits cannot be negative.");
        }

        var expectedNegativeBalance = Math.Max(0L, -(long)remainingVisits);
        if (expectedNegativeBalance > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainingVisits),
                remainingVisits,
                "Remaining visits exceed the supported negative balance range.");
        }

        if (negativeBalance != expectedNegativeBalance)
        {
            throw new ArgumentException(
                "Negative balance must match signed remaining visits.",
                nameof(negativeBalance));
        }

        if (extensionDays < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(extensionDays),
                extensionDays,
                "Extension days cannot be negative.");
        }

        var effectiveEndDayNumber = (long)issueTerms.BaseEndDate.DayNumber + extensionDays;
        if (effectiveEndDayNumber > DateOnly.MaxValue.DayNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(extensionDays),
                extensionDays,
                "Extension days exceed the supported calendar range.");
        }

        var expectedEffectiveEndDate = DateOnly.FromDayNumber((int)effectiveEndDayNumber);
        if (effectiveEndDate != expectedEffectiveEndDate)
        {
            throw new ArgumentException(
                "Effective end date must match the canonical base end date and extension days.",
                nameof(effectiveEndDate));
        }

        return new MembershipCalculatedState(
            countedVisits,
            remainingVisits,
            negativeBalance,
            firstNegativeVisitId,
            firstNegativeVisitDate,
            extensionDays,
            effectiveEndDate,
            lastCountedVisitAt);
    }

    public bool IsActiveByDate(DateOnly asOfDate)
    {
        return MembershipDateRules.IsActiveByDate(asOfDate, EffectiveEndDate);
    }
}
