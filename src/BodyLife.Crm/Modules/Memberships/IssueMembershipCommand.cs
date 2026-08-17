using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record IssueMembershipCommand(
    CommandEnvelope Envelope,
    Guid ClientId,
    Guid MembershipTypeId,
    DateTimeOffset ExpectedMembershipTypeUpdatedAt,
    DateOnly StartDate,
    string PreviewToken,
    Guid? EntryBatchId = null)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "membership";
    public const string CanonicalRereadEntityType = "client";

    public EntityId CanonicalRereadTargetId =>
        new(CanonicalRereadEntityType, ClientId);
}
