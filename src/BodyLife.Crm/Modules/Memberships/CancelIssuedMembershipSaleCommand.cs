using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record CancelIssuedMembershipSaleCommand(
    CommandEnvelope Envelope,
    Guid OriginalMembershipId,
    string ExpectedDependencyToken)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "issued_membership_sale_correction";
    public const string CanonicalRereadEntityType = "client";
}
