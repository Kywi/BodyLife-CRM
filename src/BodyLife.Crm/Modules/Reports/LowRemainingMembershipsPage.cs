using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Reports;

public sealed class LowRemainingMembershipsPage
{
    private LowRemainingMembershipsPage(
        DateOnly asOfDate,
        int remainingVisitsThreshold,
        int offset,
        IReadOnlyList<LowRemainingMembershipRow> items,
        bool hasMore,
        int? nextOffset)
    {
        AsOfDate = asOfDate;
        RemainingVisitsThreshold = remainingVisitsThreshold;
        Offset = offset;
        Items = items;
        HasMore = hasMore;
        NextOffset = nextOffset;
    }

    public DateOnly AsOfDate { get; }

    public int RemainingVisitsThreshold { get; }

    public int Offset { get; }

    public IReadOnlyList<LowRemainingMembershipRow> Items { get; }

    public bool HasMore { get; }

    public int? NextOffset { get; }

    public static bool TryCreate(
        ListLowRemainingMembershipsQuery query,
        LowRemainingMembershipStateRowsPage source,
        out LowRemainingMembershipsPage? page)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(source);

        page = null;
        if (source.AsOfDate != query.AsOfDate
            || source.RemainingVisitsThreshold != query.RemainingVisitsThreshold
            || source.Offset != query.Offset
            || query.Limit is < 1 or > GetLowRemainingMembershipStateRowsQuery.MaxLimit
            || source.Items.Count > query.Limit
            || (source.HasMore && source.Items.Count != query.Limit))
        {
            return false;
        }

        var items = source.Items
            .Select(item => new LowRemainingMembershipRow(item))
            .ToArray();
        var nextOffset = source.HasMore
            ? query.Offset + items.Length
            : (int?)null;
        page = new LowRemainingMembershipsPage(
            query.AsOfDate,
            query.RemainingVisitsThreshold,
            query.Offset,
            Array.AsReadOnly(items),
            source.HasMore,
            nextOffset);
        return true;
    }
}
