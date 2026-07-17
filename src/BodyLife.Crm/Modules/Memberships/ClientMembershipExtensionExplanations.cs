namespace BodyLife.Crm.Modules.Memberships;

public sealed class ClientMembershipExtensionExplanations
{
    public ClientMembershipExtensionExplanations(
        Guid clientId,
        IEnumerable<MembershipExtensionExplanation> items)
    {
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        ArgumentNullException.ThrowIfNull(items);

        var materializedItems = items.ToArray();
        if (materializedItems.Any(item => item is null))
        {
            throw new ArgumentException(
                "Extension explanations cannot contain a missing item.",
                nameof(items));
        }

        ClientId = clientId;
        Items = Array.AsReadOnly(materializedItems);
    }

    public Guid ClientId { get; }

    public IReadOnlyList<MembershipExtensionExplanation> Items { get; }
}
