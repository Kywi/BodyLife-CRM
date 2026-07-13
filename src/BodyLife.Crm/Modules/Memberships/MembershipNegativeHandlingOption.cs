namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipNegativeHandlingOption
{
    internal MembershipNegativeHandlingOption(
        MembershipNegativeHandlingDecision decision,
        bool isAvailable)
    {
        Decision = decision;
        IsAvailable = isAvailable;
    }

    public MembershipNegativeHandlingDecision Decision { get; }

    public bool IsAvailable { get; }
}
