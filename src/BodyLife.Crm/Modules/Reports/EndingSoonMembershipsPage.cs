using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Reports;

public sealed class EndingSoonMembershipsPage
{
    private EndingSoonMembershipsPage(
        DateOnly asOfDate,
        int daysThreshold,
        int offset,
        IReadOnlyList<EndingSoonMembershipRow> items,
        bool hasMore,
        int? nextOffset)
    {
        AsOfDate = asOfDate;
        DaysThreshold = daysThreshold;
        Offset = offset;
        Items = items;
        HasMore = hasMore;
        NextOffset = nextOffset;
    }

    public DateOnly AsOfDate { get; }

    public int DaysThreshold { get; }

    public int Offset { get; }

    public IReadOnlyList<EndingSoonMembershipRow> Items { get; }

    public bool HasMore { get; }

    public int? NextOffset { get; }

    public static bool TryCreate(
        ListEndingSoonMembershipsQuery query,
        EndingSoonMembershipStateRowsPage source,
        out EndingSoonMembershipsPage? page)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(source);

        page = null;
        if (source.AsOfDate != query.AsOfDate
            || source.DaysThreshold != query.DaysThreshold
            || source.Offset != query.Offset
            || query.Limit is < 1 or > GetEndingSoonMembershipStateRowsQuery.MaxLimit
            || source.Items.Count > query.Limit
            || (source.HasMore && source.Items.Count != query.Limit))
        {
            return false;
        }

        var items = source.Items
            .Select(item => new EndingSoonMembershipRow(
                item,
                item.State.EffectiveEndDate.DayNumber - query.AsOfDate.DayNumber))
            .ToArray();
        var nextOffset = source.HasMore
            ? query.Offset + items.Length
            : (int?)null;
        page = new EndingSoonMembershipsPage(
            query.AsOfDate,
            query.DaysThreshold,
            query.Offset,
            Array.AsReadOnly(items),
            source.HasMore,
            nextOffset);
        return true;
    }
}
