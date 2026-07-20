namespace BodyLife.Crm.Modules.Audit;

public sealed class ClientAuditEntriesPage
{
    private ClientAuditEntriesPage(
        Guid clientId,
        DateTimeOffset? occurredFromInclusive,
        DateTimeOffset? occurredBeforeExclusive,
        IReadOnlyList<ClientAuditEntityFilter> entityFilters,
        int offset,
        IReadOnlyList<ClientAuditEntry> items,
        bool hasMore)
    {
        ClientId = clientId;
        OccurredFromInclusive = occurredFromInclusive;
        OccurredBeforeExclusive = occurredBeforeExclusive;
        EntityFilters = entityFilters;
        Offset = offset;
        Items = items;
        HasMore = hasMore;
        NextOffset = hasMore ? offset + items.Count : null;
    }

    public Guid ClientId { get; }

    public DateTimeOffset? OccurredFromInclusive { get; }

    public DateTimeOffset? OccurredBeforeExclusive { get; }

    public IReadOnlyList<ClientAuditEntityFilter> EntityFilters { get; }

    public int Offset { get; }

    public IReadOnlyList<ClientAuditEntry> Items { get; }

    public bool HasMore { get; }

    public int? NextOffset { get; }

    public static ClientAuditEntriesPage Create(
        Guid clientId,
        DateTimeOffset? occurredFromInclusive,
        DateTimeOffset? occurredBeforeExclusive,
        IEnumerable<ClientAuditEntityFilter> entityFilters,
        int offset,
        IEnumerable<ClientAuditEntry> items,
        bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(entityFilters);
        ArgumentNullException.ThrowIfNull(items);

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var filterSnapshot = entityFilters.Distinct().ToArray();
        var itemSnapshot = items.ToArray();
        return new ClientAuditEntriesPage(
            clientId,
            occurredFromInclusive,
            occurredBeforeExclusive,
            Array.AsReadOnly(filterSnapshot),
            offset,
            Array.AsReadOnly(itemSnapshot),
            hasMore);
    }
}
