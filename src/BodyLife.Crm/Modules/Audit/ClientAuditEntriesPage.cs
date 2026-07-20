namespace BodyLife.Crm.Modules.Audit;

public sealed class ClientAuditEntriesPage
{
    private ClientAuditEntriesPage(
        Guid clientId,
        DateTimeOffset? occurredFromInclusive,
        DateTimeOffset? occurredBeforeExclusive,
        IReadOnlyList<ClientAuditEntityFilter> entityFilters,
        IReadOnlyList<string> actionTypes,
        int offset,
        IReadOnlyList<ClientAuditEntry> items,
        bool hasMore)
    {
        ClientId = clientId;
        OccurredFromInclusive = occurredFromInclusive;
        OccurredBeforeExclusive = occurredBeforeExclusive;
        EntityFilters = entityFilters;
        ActionTypes = actionTypes;
        Offset = offset;
        Items = items;
        HasMore = hasMore;
        NextOffset = hasMore ? offset + items.Count : null;
    }

    public Guid ClientId { get; }

    public DateTimeOffset? OccurredFromInclusive { get; }

    public DateTimeOffset? OccurredBeforeExclusive { get; }

    public IReadOnlyList<ClientAuditEntityFilter> EntityFilters { get; }

    public IReadOnlyList<string> ActionTypes { get; }

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
        return Create(
            clientId,
            occurredFromInclusive,
            occurredBeforeExclusive,
            entityFilters,
            actionTypes: [],
            offset,
            items,
            hasMore);
    }

    public static ClientAuditEntriesPage Create(
        Guid clientId,
        DateTimeOffset? occurredFromInclusive,
        DateTimeOffset? occurredBeforeExclusive,
        IEnumerable<ClientAuditEntityFilter> entityFilters,
        IEnumerable<string> actionTypes,
        int offset,
        IEnumerable<ClientAuditEntry> items,
        bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(entityFilters);
        ArgumentNullException.ThrowIfNull(actionTypes);
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
        var rawActionTypes = actionTypes.ToArray();
        if (rawActionTypes.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Action types cannot contain blank values.",
                nameof(actionTypes));
        }

        var actionTypeSnapshot = rawActionTypes
            .Select(actionType => actionType.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var itemSnapshot = items.ToArray();
        return new ClientAuditEntriesPage(
            clientId,
            occurredFromInclusive,
            occurredBeforeExclusive,
            Array.AsReadOnly(filterSnapshot),
            Array.AsReadOnly(actionTypeSnapshot),
            offset,
            Array.AsReadOnly(itemSnapshot),
            hasMore);
    }
}
