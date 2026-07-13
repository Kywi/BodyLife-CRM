namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipOpeningState
{
    private MembershipOpeningState(
        DateOnly openingAsOfDate,
        int declaredRemainingVisits,
        int declaredNegativeBalance,
        DateOnly? knownEffectiveEndDate,
        int? knownExtensionDays)
    {
        if (knownExtensionDays < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(knownExtensionDays),
                knownExtensionDays,
                "Known extension days cannot be negative.");
        }

        if (knownEffectiveEndDate < openingAsOfDate)
        {
            throw new ArgumentException(
                "Known effective end date cannot precede the opening date.",
                nameof(knownEffectiveEndDate));
        }

        var expectedNegativeBalance = CalculateNegativeBalance(declaredRemainingVisits);
        if (declaredNegativeBalance != expectedNegativeBalance)
        {
            throw new ArgumentException(
                "Declared negative balance must match signed remaining visits.",
                nameof(declaredNegativeBalance));
        }

        OpeningAsOfDate = openingAsOfDate;
        DeclaredRemainingVisits = declaredRemainingVisits;
        DeclaredNegativeBalance = declaredNegativeBalance;
        KnownEffectiveEndDate = knownEffectiveEndDate;
        KnownExtensionDays = knownExtensionDays;
    }

    public DateOnly OpeningAsOfDate { get; }

    public int DeclaredRemainingVisits { get; }

    public int DeclaredNegativeBalance { get; }

    public DateOnly? KnownEffectiveEndDate { get; }

    public int? KnownExtensionDays { get; }

    public static MembershipOpeningState FromDeclaration(
        DateOnly openingAsOfDate,
        int declaredRemainingVisits,
        DateOnly? knownEffectiveEndDate = null,
        int? knownExtensionDays = null)
    {
        return new MembershipOpeningState(
            openingAsOfDate,
            declaredRemainingVisits,
            CalculateNegativeBalance(declaredRemainingVisits),
            knownEffectiveEndDate,
            knownExtensionDays);
    }

    public static MembershipOpeningState FromStoredSource(
        DateOnly openingAsOfDate,
        int declaredRemainingVisits,
        int declaredNegativeBalance,
        DateOnly? knownEffectiveEndDate,
        int? knownExtensionDays)
    {
        return new MembershipOpeningState(
            openingAsOfDate,
            declaredRemainingVisits,
            declaredNegativeBalance,
            knownEffectiveEndDate,
            knownExtensionDays);
    }

    private static int CalculateNegativeBalance(int declaredRemainingVisits)
    {
        var negativeBalance = Math.Max(0L, -(long)declaredRemainingVisits);
        if (negativeBalance > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(declaredRemainingVisits),
                declaredRemainingVisits,
                "Declared remaining visits exceed the supported negative balance range.");
        }

        return (int)negativeBalance;
    }
}
