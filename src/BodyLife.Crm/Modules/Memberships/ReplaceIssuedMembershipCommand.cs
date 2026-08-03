using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record ReplaceIssuedMembershipCommand(
    CommandEnvelope Envelope,
    Guid OriginalMembershipId,
    Guid ReplacementMembershipTypeId,
    DateTimeOffset ExpectedMembershipTypeUpdatedAt,
    DateOnly ReplacementStartDate,
    string ExpectedDependencyToken)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "issued_membership_sale_correction";
    public const string CanonicalRereadEntityType = "client";
}
