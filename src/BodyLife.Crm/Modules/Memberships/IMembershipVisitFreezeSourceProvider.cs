namespace BodyLife.Crm.Modules.Memberships;

public interface IMembershipVisitFreezeSourceProvider
{
    Task<IReadOnlyList<MembershipVisitFreezeSource>> GetForVisitAsync(
        Guid membershipId,
        DateOnly visitDate,
        CancellationToken cancellationToken = default);
}
