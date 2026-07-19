namespace BodyLife.Crm.Modules.Memberships;

public sealed class LowRemainingMembershipStateSourceRow
{
    public LowRemainingMembershipStateSourceRow(
        string clientDisplayName,
        string? clientPhone,
        IssuedMembershipLifecycleStatus lifecycleStatus,
        MembershipStateReadModel state)
    {
        ArgumentNullException.ThrowIfNull(clientDisplayName);
        ArgumentNullException.ThrowIfNull(state);

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

        if (!Enum.IsDefined(lifecycleStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifecycleStatus),
                lifecycleStatus,
                "Membership lifecycle status is not supported.");
        }

        ClientDisplayName = normalizedDisplayName;
        ClientPhone = clientPhone;
        LifecycleStatus = lifecycleStatus;
        State = state;
    }

    public string ClientDisplayName { get; }

    public string? ClientPhone { get; }

    public IssuedMembershipLifecycleStatus LifecycleStatus { get; }

    public MembershipStateReadModel State { get; }
}
