using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record CreateMembershipOpeningStateCommand(
    CommandEnvelope Envelope,
    Guid MembershipId,
    DateOnly OpeningAsOfDate,
    int DeclaredRemainingVisits,
    DateOnly? KnownEffectiveEndDate,
    int? KnownExtensionDays,
    string SourceReference,
    Guid? EntryBatchId)
    : IBodyLifeCommand
{
    public const string CanonicalRereadEntityType = "membership";

    public EntityId CanonicalRereadTargetId =>
        new(CanonicalRereadEntityType, MembershipId);
}
