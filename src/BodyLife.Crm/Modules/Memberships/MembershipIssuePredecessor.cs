namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipIssuePredecessor(
    Guid MembershipId,
    IssuedMembershipLifecycleStatus LifecycleStatus,
    string StateVersion,
    int RemainingVisits)
{
    public bool BlocksIssue => RemainingVisits > 0;

    public string? ClosureReasonCode => RemainingVisits switch
    {
        0 => "zero_balance_rollover",
        < 0 => "negative_balance_rollover",
        _ => null,
    };
}
