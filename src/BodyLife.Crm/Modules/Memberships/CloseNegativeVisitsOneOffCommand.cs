using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record CloseNegativeVisitsOneOffCommand(
    CommandEnvelope Envelope,
    Guid ClientId,
    Guid ExpectedOldestOpenNegativeVisitId,
    IReadOnlyList<NegativeVisitClosureLineSelection> Lines,
    Guid? EntryBatchId = null)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "membership_negative_closure";
    public const string CanonicalRereadEntityType = "client";

    public EntityId CanonicalRereadTargetId =>
        new(CanonicalRereadEntityType, ClientId);
}
