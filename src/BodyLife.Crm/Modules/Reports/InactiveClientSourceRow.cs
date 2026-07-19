using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Reports;

public sealed class InactiveClientSourceRow
{
    public InactiveClientSourceRow(
        Guid clientId,
        string clientDisplayName,
        string? clientPhone,
        string? currentCardNumber,
        ClientOperationalStatus operationalStatus,
        InactiveClientLastVisit? lastCountedVisit,
        ClientMembershipStatesReadModel membershipStates)
    {
        ArgumentNullException.ThrowIfNull(clientDisplayName);
        ArgumentNullException.ThrowIfNull(membershipStates);

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        var normalizedDisplayName = clientDisplayName.Trim();
        if (normalizedDisplayName.Length == 0)
        {
            throw new ArgumentException(
                "Client display name is required.",
                nameof(clientDisplayName));
        }

        if (clientPhone is not null && string.IsNullOrWhiteSpace(clientPhone))
        {
            throw new ArgumentException(
                "Client phone cannot be empty when supplied.",
                nameof(clientPhone));
        }

        if (currentCardNumber is not null && string.IsNullOrWhiteSpace(currentCardNumber))
        {
            throw new ArgumentException(
                "Current card number cannot be empty when supplied.",
                nameof(currentCardNumber));
        }

        if (!Enum.IsDefined(operationalStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationalStatus),
                operationalStatus,
                "Client operational status is not supported.");
        }

        if (membershipStates.ClientId != clientId)
        {
            throw new ArgumentException(
                "Membership states must belong to the supplied Client.",
                nameof(membershipStates));
        }

        ClientId = clientId;
        ClientDisplayName = normalizedDisplayName;
        ClientPhone = clientPhone;
        CurrentCardNumber = currentCardNumber;
        OperationalStatus = operationalStatus;
        LastCountedVisit = lastCountedVisit;
        MembershipStates = membershipStates;
    }

    public Guid ClientId { get; }

    public string ClientDisplayName { get; }

    public string? ClientPhone { get; }

    public string? CurrentCardNumber { get; }

    public ClientOperationalStatus OperationalStatus { get; }

    public InactiveClientLastVisit? LastCountedVisit { get; }

    public ClientMembershipStatesReadModel MembershipStates { get; }
}
