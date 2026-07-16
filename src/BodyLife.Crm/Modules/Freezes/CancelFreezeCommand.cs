using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.Freezes;

public sealed record CancelFreezeCommand(
    CommandEnvelope Envelope,
    Guid FreezeId,
    Guid? EntryBatchId = null)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "freeze_cancellation";
    public const string SourceFreezeEntityType = "freeze";
    public const string CanonicalRereadEntityType = "client";
}
