using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Visits;

public sealed record MarkVisitCommand(
    CommandEnvelope Envelope,
    Guid ClientId,
    VisitKind VisitKind,
    Guid? MembershipId,
    IReadOnlyList<MembershipVisitAcknowledgement> Acknowledgements,
    Guid? EntryBatchId = null)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "visit";
    public const string CanonicalRereadEntityType = "client";

    public EntityId CanonicalRereadTargetId =>
        new(CanonicalRereadEntityType, ClientId);
}
