using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record CreateMembershipOpeningStateCommand(
    CommandEnvelope Envelope,
    Guid ClientId,
    Guid MembershipTypeId,
    DateOnly StartDate,
    DateOnly OpeningAsOfDate,
    int DeclaredRemainingVisits,
    DateOnly? KnownEffectiveEndDate,
    int? KnownExtensionDays,
    string SourceReference,
    Guid? EntryBatchId)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "membership";
    public const string CanonicalRereadEntityType = "client";

    public EntityId CanonicalRereadTargetId =>
        new(CanonicalRereadEntityType, ClientId);
}
