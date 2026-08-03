using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Audit;

public sealed record CreatePaperFallbackBatchRowCommand(
    CommandEnvelope Envelope,
    Guid EntryBatchId,
    int? LineNumber,
    PaperFallbackEventType EventType,
    string Explanation)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "entry_batch_row";
    public const string CanonicalRereadEntityType = "entry_batch";

    public EntityId CanonicalRereadTargetId =>
        new(CanonicalRereadEntityType, EntryBatchId);
}
