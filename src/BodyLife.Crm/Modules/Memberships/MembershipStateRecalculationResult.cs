namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipStateRecalculationResult(
    Guid MembershipId,
    MembershipStateRecalculationStatus Status)
{
    public bool Succeeded => Status == MembershipStateRecalculationStatus.Recalculated;
}
