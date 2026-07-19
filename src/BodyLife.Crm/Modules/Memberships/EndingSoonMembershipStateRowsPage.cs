namespace BodyLife.Crm.Modules.Memberships;

public sealed class EndingSoonMembershipStateRowsPage
{
    public EndingSoonMembershipStateRowsPage(
        DateOnly asOfDate,
        int daysThreshold,
        int offset,
        IEnumerable<EndingSoonMembershipStateSourceRow> items,
        bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (asOfDate == default)
        {
            throw new ArgumentException("As-of date is required.", nameof(asOfDate));
        }

        if (daysThreshold is < 0
            or > GetEndingSoonMembershipStateRowsQuery.MaxDaysThreshold)
        {
            throw new ArgumentOutOfRangeException(
                nameof(daysThreshold),
                daysThreshold,
                $"Days threshold must be between 0 and {GetEndingSoonMembershipStateRowsQuery.MaxDaysThreshold}.");
        }

        if (asOfDate.DayNumber > DateOnly.MaxValue.DayNumber - daysThreshold)
        {
            throw new ArgumentOutOfRangeException(
                nameof(asOfDate),
                asOfDate,
                "As-of date and days threshold exceed the supported calendar range.");
        }

        if (offset is < 0 or > GetEndingSoonMembershipStateRowsQuery.MaxOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                offset,
                $"Offset must be between 0 and {GetEndingSoonMembershipStateRowsQuery.MaxOffset}.");
        }

        var sourceItems = items.ToArray();
        if (sourceItems.Any(item => item is null))
        {
            throw new ArgumentException(
                "Ending-soon source rows cannot contain a missing item.",
                nameof(items));
        }

        if (sourceItems.Select(item => item.State.MembershipId)
            .Distinct()
            .Count() != sourceItems.Length)
        {
            throw new ArgumentException(
                "Ending-soon source rows cannot contain duplicate Memberships.",
                nameof(items));
        }

        foreach (var item in sourceItems)
        {
            var daysLeft = item.State.EffectiveEndDate.DayNumber - asOfDate.DayNumber;
            if (item.LifecycleStatus != IssuedMembershipLifecycleStatus.Active
                || item.State.AsOfDate != asOfDate
                || daysLeft < 0
                || daysLeft > daysThreshold)
            {
                throw new ArgumentException(
                    "Every ending-soon source row must be lifecycle-active and within the requested effective-end range.",
                    nameof(items));
            }
        }

        if (hasMore && sourceItems.Length == 0)
        {
            throw new ArgumentException(
                "A page with more rows must include at least one visible row.",
                nameof(hasMore));
        }

        AsOfDate = asOfDate;
        DaysThreshold = daysThreshold;
        Offset = offset;
        Items = Array.AsReadOnly(sourceItems);
        HasMore = hasMore;
    }

    public DateOnly AsOfDate { get; }

    public int DaysThreshold { get; }

    public int Offset { get; }

    public IReadOnlyList<EndingSoonMembershipStateSourceRow> Items { get; }

    public bool HasMore { get; }
}
