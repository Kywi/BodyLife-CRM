namespace BodyLife.Crm.Modules.Memberships;

public sealed class ClientNegativeVisitCoverageHistorySourceRowsPage
{
    private ClientNegativeVisitCoverageHistorySourceRowsPage(
        Guid clientId,
        DateTimeOffset? occurredFromInclusive,
        DateTimeOffset? occurredBeforeExclusive,
        int offset,
        IReadOnlyList<ClientNegativeVisitCoverageHistorySourceRow> items,
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
    public IReadOnlyList<ClientNegativeVisitCoverageHistorySourceRow> Items { get; }
    public bool HasMore { get; }
    public int? NextOffset { get; }

    public static ClientNegativeVisitCoverageHistorySourceRowsPage Create(
        Guid clientId,
        DateTimeOffset? occurredFromInclusive,
        DateTimeOffset? occurredBeforeExclusive,
        int offset,
        IEnumerable<ClientNegativeVisitCoverageHistorySourceRow> items,
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

        var snapshot = items.ToArray();
        if (snapshot.Any(item => item is null || item.ClientId != clientId))
        {
            throw new ArgumentException(
                "Every history source row must belong to the requested client.",
                nameof(items));
        }

        return new ClientNegativeVisitCoverageHistorySourceRowsPage(
            clientId,
            occurredFromInclusive,
            occurredBeforeExclusive,
            offset,
            Array.AsReadOnly(snapshot),
            hasMore);
    }
}
