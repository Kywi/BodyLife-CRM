namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class ClientNonWorkingDayHistorySourceRowsPage
{
    private ClientNonWorkingDayHistorySourceRowsPage(
        Guid clientId,
        DateTimeOffset? occurredFromInclusive,
        DateTimeOffset? occurredBeforeExclusive,
        int offset,
        IReadOnlyList<ClientNonWorkingDayHistorySourceRow> items,
        bool hasMore)
    {
        ClientId = clientId;
        OccurredFromInclusive = occurredFromInclusive;
        OccurredBeforeExclusive = occurredBeforeExclusive;
        Offset = offset;
        Items = items;
        HasMore = hasMore;
        NextOffset = hasMore ? offset + items.Count : null;
    }

    public Guid ClientId { get; }

    public DateTimeOffset? OccurredFromInclusive { get; }

    public DateTimeOffset? OccurredBeforeExclusive { get; }

    public int Offset { get; }

    public IReadOnlyList<ClientNonWorkingDayHistorySourceRow> Items { get; }

    public bool HasMore { get; }

    public int? NextOffset { get; }

    public static ClientNonWorkingDayHistorySourceRowsPage Create(
        Guid clientId,
        DateTimeOffset? occurredFromInclusive,
        DateTimeOffset? occurredBeforeExclusive,
        int offset,
        IEnumerable<ClientNonWorkingDayHistorySourceRow> items,
        bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var itemSnapshot = items.ToArray();
        if (itemSnapshot.Any(item => item.ClientId != clientId))
        {
            throw new ArgumentException(
                "Every NonWorkingDay history source row must belong to the requested client.",
                nameof(items));
        }

        return new ClientNonWorkingDayHistorySourceRowsPage(
            clientId,
            occurredFromInclusive,
            occurredBeforeExclusive,
            offset,
            Array.AsReadOnly(itemSnapshot),
            hasMore);
    }
}
