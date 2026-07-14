using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Visits;

public sealed class CancelVisitPreparation
{
    internal CancelVisitPreparation(
        CommandEnvelope envelope,
        VisitCancellationSource source,
        Guid? entryBatchId,
        bool changedAfterClose)
    {
        Envelope = envelope;
        Source = source;
        EntryBatchId = entryBatchId;
        ChangedAfterClose = changedAfterClose;
    }

    public CommandEnvelope Envelope { get; }

    public VisitCancellationSource Source { get; }

    public Guid? EntryBatchId { get; }

    public bool ChangedAfterClose { get; }

    public bool RequiresMembershipRecalculation => Source.MembershipId is not null;

    public EntityId SourceVisitEntityId =>
        new(CancelVisitCommand.SourceVisitEntityType, Source.VisitId);

    public EntityId CanonicalRereadTargetId =>
        new(CancelVisitCommand.CanonicalRereadEntityType, Source.ClientId);
}
