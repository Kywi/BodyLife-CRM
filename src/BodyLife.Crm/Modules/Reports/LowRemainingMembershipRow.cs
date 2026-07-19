using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Reports;

public sealed class LowRemainingMembershipRow
{
    internal LowRemainingMembershipRow(
        LowRemainingMembershipStateSourceRow source)
    {
        ClientDisplayName = source.ClientDisplayName;
        ClientPhone = source.ClientPhone;
        MembershipState = source.State;
    }

    public string ClientDisplayName { get; }

    public string? ClientPhone { get; }

    public MembershipStateReadModel MembershipState { get; }

    public Guid ClientId => MembershipState.ClientId;

    public Guid MembershipId => MembershipState.MembershipId;

    public string MembershipTypeName => MembershipState.Snapshot.TypeName;

    public int VisitsLimitSnapshot => MembershipState.Snapshot.VisitsLimit;

    public int CountedVisits => MembershipState.CountedVisits;

    public int RemainingVisits => MembershipState.RemainingVisits;

    public DateOnly EffectiveEndDate => MembershipState.EffectiveEndDate;

    public DateTimeOffset? LastCountedVisitAt => MembershipState.LastCountedVisitAt;

    public IReadOnlyList<MembershipWarning> Warnings => MembershipState.Warnings;

    public bool HasExtensionExplanation =>
        MembershipState.ExtensionExplanation.Count > 0;
}
