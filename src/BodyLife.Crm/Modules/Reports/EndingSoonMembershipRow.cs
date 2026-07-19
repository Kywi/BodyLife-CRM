using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Reports;

public sealed class EndingSoonMembershipRow
{
    internal EndingSoonMembershipRow(
        EndingSoonMembershipStateSourceRow source,
        int daysLeft)
    {
        ClientDisplayName = source.ClientDisplayName;
        ClientPhone = source.ClientPhone;
        MembershipState = source.State;
        DaysLeft = daysLeft;
    }

    public string ClientDisplayName { get; }

    public string? ClientPhone { get; }

    public MembershipStateReadModel MembershipState { get; }

    public int DaysLeft { get; }

    public Guid ClientId => MembershipState.ClientId;

    public Guid MembershipId => MembershipState.MembershipId;

    public string MembershipTypeName => MembershipState.Snapshot.TypeName;

    public DateOnly EffectiveEndDate => MembershipState.EffectiveEndDate;

    public int RemainingVisits => MembershipState.RemainingVisits;

    public IReadOnlyList<MembershipWarning> Warnings => MembershipState.Warnings;

    public bool HasExtensionExplanation =>
        MembershipState.ExtensionExplanation.Count > 0;
}
