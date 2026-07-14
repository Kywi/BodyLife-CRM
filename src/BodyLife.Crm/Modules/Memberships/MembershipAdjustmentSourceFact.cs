namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipAdjustmentSourceFact
{
    public const int MaxAdjustmentTypeLength = 64;

    public MembershipAdjustmentSourceFact(
        Guid membershipId,
        Guid adjustmentId,
        string? adjustmentType,
        int? daysDelta,
        int? visitsDelta,
        decimal? moneyDelta,
        DateOnly effectiveDate,
        MembershipAdjustmentSourceStatus status)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        if (adjustmentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership adjustment id is required.",
                nameof(adjustmentId));
        }

        var normalizedType = adjustmentType?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedType))
        {
            throw new ArgumentException(
                "Membership adjustment type is required.",
                nameof(adjustmentType));
        }

        if (normalizedType.Length > MaxAdjustmentTypeLength)
        {
            throw new ArgumentException(
                "Membership adjustment type is too long.",
                nameof(adjustmentType));
        }

        if (daysDelta.GetValueOrDefault() == 0
            && visitsDelta.GetValueOrDefault() == 0
            && moneyDelta.GetValueOrDefault() == 0m)
        {
            throw new ArgumentException(
                "Membership adjustment must contain a non-zero delta.",
                nameof(daysDelta));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Membership adjustment source status is not supported.");
        }

        MembershipId = membershipId;
        AdjustmentId = adjustmentId;
        AdjustmentType = normalizedType;
        DaysDelta = daysDelta;
        VisitsDelta = visitsDelta;
        MoneyDelta = moneyDelta;
        EffectiveDate = effectiveDate;
        Status = status;
    }

    public Guid MembershipId { get; }

    public Guid AdjustmentId { get; }

    public string AdjustmentType { get; }

    public int? DaysDelta { get; }

    public int? VisitsDelta { get; }

    public decimal? MoneyDelta { get; }

    public DateOnly EffectiveDate { get; }

    public MembershipAdjustmentSourceStatus Status { get; }

    public bool IsActive => Status == MembershipAdjustmentSourceStatus.Active;
}
