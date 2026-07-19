namespace BodyLife.Crm.Modules.Memberships;

public sealed class LowRemainingMembershipStateRowsPage
{
    public LowRemainingMembershipStateRowsPage(
        DateOnly asOfDate,
        int remainingVisitsThreshold,
        int offset,
        IEnumerable<LowRemainingMembershipStateSourceRow> items,
        bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (asOfDate == default)
        {
            throw new ArgumentException("As-of date is required.", nameof(asOfDate));
        }

        if (remainingVisitsThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainingVisitsThreshold),
                remainingVisitsThreshold,
                "Remaining-visits threshold cannot be negative.");
        }

        if (offset is < 0 or > GetLowRemainingMembershipStateRowsQuery.MaxOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                offset,
                $"Offset must be between 0 and {GetLowRemainingMembershipStateRowsQuery.MaxOffset}.");
        }

        var sourceItems = items.ToArray();
        if (sourceItems.Any(item => item is null))
        {
            throw new ArgumentException(
                "Low-remaining source rows cannot contain a missing item.",
                nameof(items));
        }

        if (sourceItems.Select(item => item.State.MembershipId)
            .Distinct()
            .Count() != sourceItems.Length)
        {
            throw new ArgumentException(
                "Low-remaining source rows cannot contain duplicate Memberships.",
                nameof(items));
        }

        foreach (var item in sourceItems)
        {
            if (item.LifecycleStatus != IssuedMembershipLifecycleStatus.Active
                || item.State.AsOfDate != asOfDate
                || item.State.RemainingVisits > remainingVisitsThreshold)
            {
                throw new ArgumentException(
                    "Every low-remaining source row must be lifecycle-active and at or below the requested remaining-visits threshold.",
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
        RemainingVisitsThreshold = remainingVisitsThreshold;
        Offset = offset;
        Items = Array.AsReadOnly(sourceItems);
        HasMore = hasMore;
    }

    public DateOnly AsOfDate { get; }

    public int RemainingVisitsThreshold { get; }

    public int Offset { get; }

    public IReadOnlyList<LowRemainingMembershipStateSourceRow> Items { get; }

    public bool HasMore { get; }
}
