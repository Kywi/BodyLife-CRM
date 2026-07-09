namespace BodyLife.Crm.Application.Queries;

public sealed class QueryPermissionSet
{
    private readonly IReadOnlyDictionary<string, QueryPermissionResult> _permissionsByActionKey;

    public QueryPermissionSet(IEnumerable<QueryPermissionResult> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var permissionItems = permissions.ToArray();
        var duplicateActionKey = permissionItems
            .GroupBy(permission => permission.ActionKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateActionKey is not null)
        {
            throw new ArgumentException($"Duplicate permission action key '{duplicateActionKey}'.", nameof(permissions));
        }

        Items = permissionItems;
        _permissionsByActionKey = permissionItems.ToDictionary(
            permission => permission.ActionKey,
            StringComparer.Ordinal);
    }

    public static QueryPermissionSet Empty { get; } = new([]);

    public IReadOnlyList<QueryPermissionResult> Items { get; }

    public bool IsAllowed(string actionKey)
    {
        return TryGet(actionKey, out var permission) && permission.IsAllowed;
    }

    public bool TryGet(string actionKey, out QueryPermissionResult permission)
    {
        return _permissionsByActionKey.TryGetValue(actionKey, out permission!);
    }
}
