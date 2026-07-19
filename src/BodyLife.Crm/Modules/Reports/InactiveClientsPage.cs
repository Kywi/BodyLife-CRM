namespace BodyLife.Crm.Modules.Reports;

public sealed class InactiveClientsPage
{
    private InactiveClientsPage(
        DateOnly asOfDate,
        int thresholdDays,
        bool includesClientsWithNoVisits,
        int offset,
        IReadOnlyList<InactiveClientRow> items,
        bool hasMore,
        int? nextOffset)
    {
        AsOfDate = asOfDate;
        ThresholdDays = thresholdDays;
        IncludesClientsWithNoVisits = includesClientsWithNoVisits;
        Offset = offset;
        Items = items;
        HasMore = hasMore;
        NextOffset = nextOffset;
    }

    public DateOnly AsOfDate { get; }

    public int ThresholdDays { get; }

    public bool IncludesClientsWithNoVisits { get; }

    public int Offset { get; }

    public IReadOnlyList<InactiveClientRow> Items { get; }

    public bool HasMore { get; }

    public int? NextOffset { get; }

    public static bool TryCreate(
        ListInactiveClientsQuery query,
        IEnumerable<InactiveClientSourceRow> sourceRows,
        bool hasMore,
        out InactiveClientsPage? page)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sourceRows);

        page = null;
        var sources = sourceRows.ToArray();
        if (query.AsOfDate == default
            || !ListInactiveClientsQuery.IsSupportedThreshold(query.ThresholdDays)
            || query.Limit is < 1 or > ListInactiveClientsQuery.MaxLimit
            || query.Offset is < 0 or > ListInactiveClientsQuery.MaxOffset
            || sources.Length > query.Limit
            || (hasMore && sources.Length != query.Limit)
            || sources.Any(source => source is null)
            || sources.Select(source => source.ClientId).Distinct().Count()
                != sources.Length)
        {
            return false;
        }

        foreach (var source in sources)
        {
            if (source.MembershipStates.AsOfDate != query.AsOfDate)
            {
                return false;
            }

            if (source.LastCountedVisit is null)
            {
                if (!query.IncludeClientsWithNoVisits)
                {
                    return false;
                }

                continue;
            }

            var daysInactive = query.AsOfDate.DayNumber
                - source.LastCountedVisit.OccurredDateUtc.DayNumber;
            if (daysInactive < query.ThresholdDays)
            {
                return false;
            }
        }

        var items = sources
            .Select(source => new InactiveClientRow(query, source))
            .ToArray();
        var nextOffset = hasMore
            ? query.Offset + items.Length
            : (int?)null;
        page = new InactiveClientsPage(
            query.AsOfDate,
            query.ThresholdDays,
            query.IncludeClientsWithNoVisits,
            query.Offset,
            Array.AsReadOnly(items),
            hasMore,
            nextOffset);
        return true;
    }
}
