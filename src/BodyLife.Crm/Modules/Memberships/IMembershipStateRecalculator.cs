namespace BodyLife.Crm.Modules.Memberships;

public interface IMembershipStateRecalculator
{
    Task<MembershipStateRecalculationResult> RecalculateAsync(
        Guid membershipId,
        CancellationToken cancellationToken = default);
}
