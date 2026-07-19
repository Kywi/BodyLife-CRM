namespace BodyLife.Crm.Modules.Memberships;

public sealed class ClientMembershipReportState
{
    public ClientMembershipReportState(
        Guid clientId,
        ClientMembershipStatesReadModel states)
    {
        ArgumentNullException.ThrowIfNull(states);

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (states.ClientId != clientId)
        {
            throw new ArgumentException(
                "Membership states must belong to the supplied Client.",
                nameof(states));
        }

        ClientId = clientId;
        States = states;
    }

    public Guid ClientId { get; }

    public ClientMembershipStatesReadModel States { get; }
}
