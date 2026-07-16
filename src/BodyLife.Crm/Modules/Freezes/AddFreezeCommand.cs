using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Freezes;

public sealed record AddFreezeCommand(
    CommandEnvelope Envelope,
    Guid ClientId,
    Guid MembershipId,
    DateRange Range,
    Guid? EntryBatchId = null)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "freeze";
    public const string MembershipEntityType = "membership";
    public const string CanonicalRereadEntityType = "client";

    public EntityId CanonicalRereadTargetId =>
        new(CanonicalRereadEntityType, ClientId);
}
