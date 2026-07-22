namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed record MembershipStateCacheBulkRebuildResult(
    MembershipStateCacheBulkRebuildStatus Status,
    int Total,
    int Created,
    int Repaired,
    int Verified,
    Guid? MissingMembershipId)
{
    public bool Succeeded => Status == MembershipStateCacheBulkRebuildStatus.Succeeded;

    public int Processed => Created + Repaired + Verified;

    internal static MembershipStateCacheBulkRebuildResult Completed(
        int total,
        int created,
        int repaired,
        int verified) => new(
        MembershipStateCacheBulkRebuildStatus.Succeeded,
        total,
        created,
        repaired,
        verified,
        MissingMembershipId: null);

    internal static MembershipStateCacheBulkRebuildResult MissingSource(
        Guid membershipId,
        int total,
        int created,
        int repaired,
        int verified) => new(
        MembershipStateCacheBulkRebuildStatus.MissingSource,
        total,
        created,
        repaired,
        verified,
        membershipId);
}
