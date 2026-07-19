namespace BodyLife.Crm.Modules.Memberships;

public sealed class NegativeMembershipStateRowsPage
{
    public NegativeMembershipStateRowsPage(
        DateOnly asOfDate,
        int offset,
        IEnumerable<NegativeMembershipStateSourceRow> items,
        bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (asOfDate == default)
        {
            throw new ArgumentException("As-of date is required.", nameof(asOfDate));
        }

        if (offset is < 0 or > GetNegativeMembershipStateRowsQuery.MaxOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                offset,
                $"Offset must be between 0 and {GetNegativeMembershipStateRowsQuery.MaxOffset}.");
        }

        var sourceItems = items.ToArray();
        if (sourceItems.Any(item => item is null))
        {
            throw new ArgumentException(
                "Negative Membership source rows cannot contain a missing item.",
                nameof(items));
        }

        if (sourceItems.Select(item => item.State.MembershipId)
            .Distinct()
            .Count() != sourceItems.Length)
        {
            throw new ArgumentException(
                "Negative Membership source rows cannot contain duplicate Memberships.",
                nameof(items));
        }

        foreach (var item in sourceItems)
        {
            if (item.LifecycleStatus != IssuedMembershipLifecycleStatus.Active
                || item.State.AsOfDate != asOfDate
                || item.State.NegativeBalance <= 0)
            {
                throw new ArgumentException(
                    "Every negative Membership source row must be lifecycle-active and have a positive negative balance.",
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
        Offset = offset;
        Items = Array.AsReadOnly(sourceItems);
        HasMore = hasMore;
    }

    public DateOnly AsOfDate { get; }

    public int Offset { get; }

    public IReadOnlyList<NegativeMembershipStateSourceRow> Items { get; }

    public bool HasMore { get; }
}
