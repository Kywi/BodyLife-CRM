namespace BodyLife.Crm.Modules.Memberships;

public interface IMembershipVisitFreezeSourceSnapshotProvider
{
    Task<IReadOnlyList<MembershipVisitFreezeSource>> GetSnapshotForVisitAsync(
        Guid membershipId,
        DateOnly visitDate,
        CancellationToken cancellationToken = default);
}
