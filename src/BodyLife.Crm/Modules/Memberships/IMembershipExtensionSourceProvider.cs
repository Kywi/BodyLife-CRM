namespace BodyLife.Crm.Modules.Memberships;

public interface IMembershipExtensionSourceProvider
{
    Task<IReadOnlyList<MembershipExtensionSourceRange>> GetForMembershipAsync(
        Guid membershipId,
        CancellationToken cancellationToken = default);
}
