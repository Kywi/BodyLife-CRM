using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Audit;

public sealed record CreatePaperFallbackBatchCommand(
    CommandEnvelope Envelope,
    string PaperSheetNumber,
    DateOnly BusinessDateStart,
    DateOnly BusinessDateEnd,
    string? Note)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "entry_batch";
    public const string CanonicalRereadEntityType = "entry_batch";

    public EntityId CanonicalRereadTargetId(Guid entryBatchId) =>
        new(CanonicalRereadEntityType, entryBatchId);
}
