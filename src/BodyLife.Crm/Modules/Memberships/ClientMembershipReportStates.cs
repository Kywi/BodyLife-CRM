namespace BodyLife.Crm.Modules.Memberships;

public sealed class ClientMembershipReportStates
{
    public ClientMembershipReportStates(
        DateOnly asOfDate,
        IEnumerable<ClientMembershipReportState> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);

        if (asOfDate == default)
        {
            throw new ArgumentException("As-of date is required.", nameof(asOfDate));
        }

        var clientItems = clients.ToArray();
        if (clientItems.Any(client => client is null))
        {
            throw new ArgumentException(
                "Client Membership report states cannot contain a missing item.",
                nameof(clients));
        }

        if (clientItems.Select(client => client.ClientId).Distinct().Count()
            != clientItems.Length)
        {
            throw new ArgumentException(
                "Client Membership report states cannot contain duplicate Clients.",
                nameof(clients));
        }

        if (clientItems.Any(client => client.States.AsOfDate != asOfDate))
        {
            throw new ArgumentException(
                "Every Client Membership state must use the supplied as-of date.",
                nameof(clients));
        }

        AsOfDate = asOfDate;
        Clients = Array.AsReadOnly(clientItems);
    }

    public DateOnly AsOfDate { get; }

    public IReadOnlyList<ClientMembershipReportState> Clients { get; }
}
