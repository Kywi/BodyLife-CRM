namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipDateRules
{
    public static DateOnly CalculateBaseEndDate(DateOnly startDate, int durationDays)
    {
        if (durationDays <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationDays),
                durationDays,
                "Duration days must be greater than zero.");
        }

        var endDayNumber = (long)startDate.DayNumber + durationDays - 1;
        if (endDayNumber > DateOnly.MaxValue.DayNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationDays),
                durationDays,
                "Duration extends beyond the supported calendar range.");
        }

        return DateOnly.FromDayNumber((int)endDayNumber);
    }
}
