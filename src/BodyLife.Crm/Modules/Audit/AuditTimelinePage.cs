namespace BodyLife.Crm.Modules.Audit;

public sealed class AuditTimelinePage
{
    private AuditTimelinePage(
        Guid? clientId,
        AuditTimelineEntityType? entityType,
        Guid? entityId,
        DateTimeOffset? recordedFromInclusive,
        DateTimeOffset? recordedBeforeExclusive,
        IReadOnlyList<string> actionTypes,
        int offset,
        IReadOnlyList<AuditTimelineEntry> items,
        bool hasMore)
    {
        ClientId = clientId;
        EntityType = entityType;
        EntityId = entityId;
        RecordedFromInclusive = recordedFromInclusive;
        RecordedBeforeExclusive = recordedBeforeExclusive;
        ActionTypes = actionTypes;
        Offset = offset;
        Items = items;
        HasMore = hasMore;
        NextOffset = hasMore ? offset + items.Count : null;
    }

    public Guid? ClientId { get; }

    public AuditTimelineEntityType? EntityType { get; }

    public Guid? EntityId { get; }

    public DateTimeOffset? RecordedFromInclusive { get; }

    public DateTimeOffset? RecordedBeforeExclusive { get; }

    public IReadOnlyList<string> ActionTypes { get; }

    public int Offset { get; }

    public IReadOnlyList<AuditTimelineEntry> Items { get; }

    public bool HasMore { get; }

    public int? NextOffset { get; }

    public static AuditTimelinePage Create(
        Guid? clientId,
        AuditTimelineEntityType? entityType,
        Guid? entityId,
        DateTimeOffset? recordedFromInclusive,
        DateTimeOffset? recordedBeforeExclusive,
        IEnumerable<string> actionTypes,
        int offset,
        IEnumerable<AuditTimelineEntry> items,
        bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(actionTypes);
        ArgumentNullException.ThrowIfNull(items);

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id cannot be empty.", nameof(clientId));
        }

        if (entityType is { } selectedEntityType
            && !Enum.IsDefined(selectedEntityType))
        {
            throw new ArgumentException("Entity type is invalid.", nameof(entityType));
        }

        if (entityId == Guid.Empty
            || (entityId is not null && entityType is null))
        {
            throw new ArgumentException(
                "Entity id must be non-empty and paired with an entity type.",
                nameof(entityId));
        }

        if (recordedFromInclusive is not null
            && recordedBeforeExclusive is not null
            && recordedFromInclusive >= recordedBeforeExclusive)
        {
            throw new ArgumentException(
                "Recorded-from time must be earlier than recorded-before time.",
                nameof(recordedBeforeExclusive));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

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
        if (itemSnapshot.Any(item => !IsCanonicalEntry(
                item,
                entityType,
                entityId,
                recordedFromInclusive,
                recordedBeforeExclusive,
                actionTypeSnapshot))
            || itemSnapshot.Select(item => item.AuditEntryId).Distinct().Count()
                != itemSnapshot.Length
            || !itemSnapshot.SequenceEqual(itemSnapshot
                .OrderByDescending(item => item.RecordedAt)
                .ThenByDescending(item => item.AuditEntryId.Value)))
        {
            throw new ArgumentException(
                "Audit timeline entries must be canonical, unique and stably ordered.",
                nameof(items));
        }

        return new AuditTimelinePage(
            clientId,
            entityType,
            entityId,
            recordedFromInclusive,
            recordedBeforeExclusive,
            Array.AsReadOnly(actionTypeSnapshot),
            offset,
            Array.AsReadOnly(itemSnapshot),
            hasMore);
    }

    private static bool IsCanonicalEntry(
        AuditTimelineEntry? item,
        AuditTimelineEntityType? entityType,
        Guid? entityId,
        DateTimeOffset? recordedFromInclusive,
        DateTimeOffset? recordedBeforeExclusive,
        IReadOnlyCollection<string> actionTypes)
    {
        return item is not null
            && item.AuditEntryId.Value != Guid.Empty
            && !string.IsNullOrWhiteSpace(item.ActionType)
            && Enum.IsDefined(item.EntityType)
            && item.EntityId != Guid.Empty
            && item.ActorAccountId.Value != Guid.Empty
            && Enum.IsDefined(item.ActorAccountKind)
            && Enum.IsDefined(item.ActorRole)
            && item.SessionId.Value != Guid.Empty
            && Enum.IsDefined(item.EntryOrigin)
            && !string.IsNullOrWhiteSpace(item.RelatedEntityRefsJson)
            && !string.IsNullOrWhiteSpace(item.BeforeSummaryJson)
            && !string.IsNullOrWhiteSpace(item.AfterSummaryJson)
            && !string.IsNullOrWhiteSpace(item.RequestCorrelationId.Value)
            && (entityType is null || item.EntityType == entityType)
            && (entityId is null || item.EntityId == entityId)
            && (recordedFromInclusive is null
                || item.RecordedAt >= recordedFromInclusive)
            && (recordedBeforeExclusive is null
                || item.RecordedAt < recordedBeforeExclusive)
            && (actionTypes.Count == 0 || actionTypes.Contains(item.ActionType));
    }
}
