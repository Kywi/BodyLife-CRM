using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Reports;

public sealed class NegativeClientsPage
{
    private NegativeClientsPage(
        DateOnly asOfDate,
        int offset,
        IReadOnlyList<NegativeClientRow> items,
        bool hasMore,
        int? nextOffset)
    {
        AsOfDate = asOfDate;
        Offset = offset;
        Items = items;
        HasMore = hasMore;
        NextOffset = nextOffset;
    }

    public DateOnly AsOfDate { get; }

    public int Offset { get; }

    public IReadOnlyList<NegativeClientRow> Items { get; }

    public bool HasMore { get; }

    public int? NextOffset { get; }

    public static bool TryCreate(
        ListNegativeClientsQuery query,
        NegativeMembershipStateRowsPage source,
        out NegativeClientsPage? page)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(source);

        page = null;
        if (source.AsOfDate != query.AsOfDate
            || source.Offset != query.Offset
            || query.Limit is < 1 or > GetNegativeMembershipStateRowsQuery.MaxLimit
            || source.Items.Count > query.Limit
            || (source.HasMore && source.Items.Count != query.Limit))
        {
            return false;
        }

        var items = source.Items
            .Select(item => new NegativeClientRow(item))
            .ToArray();
        var nextOffset = source.HasMore
            ? query.Offset + items.Length
            : (int?)null;
        page = new NegativeClientsPage(
            query.AsOfDate,
            query.Offset,
            Array.AsReadOnly(items),
            source.HasMore,
            nextOffset);
        return true;
    }
}
