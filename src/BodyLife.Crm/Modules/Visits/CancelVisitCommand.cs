using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.Visits;

public sealed record CancelVisitCommand(
    CommandEnvelope Envelope,
    Guid VisitId,
    Guid? EntryBatchId = null)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "visit_cancellation";
    public const string SourceVisitEntityType = "visit";
    public const string CanonicalRereadEntityType = "client";
}
