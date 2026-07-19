using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Reports;

public sealed class NegativeClientRow
{
    internal NegativeClientRow(NegativeMembershipStateSourceRow source)
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

    public int RemainingVisits => MembershipState.RemainingVisits;

    public int NegativeBalance => MembershipState.NegativeBalance;

    public Guid? FirstNegativeVisitId => MembershipState.FirstNegativeVisitId;

    public DateOnly? FirstNegativeVisitDate => MembershipState.FirstNegativeVisitDate;

    public DateTimeOffset? LastCountedVisitAt => MembershipState.LastCountedVisitAt;

    public DateOnly EffectiveEndDate => MembershipState.EffectiveEndDate;

    public IReadOnlyList<MembershipWarning> Warnings => MembershipState.Warnings;
}
