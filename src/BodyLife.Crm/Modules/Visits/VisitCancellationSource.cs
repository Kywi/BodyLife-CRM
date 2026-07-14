using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.Visits;

public sealed class VisitCancellationSource
{
    public VisitCancellationSource(
        Guid visitId,
        Guid clientId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId,
        Guid sessionId,
        VisitKind visitKind,
        EntryOrigin entryOrigin,
        Guid? entryBatchId,
        string? comment,
        VisitCancellationSourceStatus status,
        Guid? activeConsumptionId,
        Guid? membershipId,
        Guid? existingCancellationId)
    {
        RequireId(visitId, nameof(visitId));
        RequireId(clientId, nameof(clientId));
        RequireId(recordedByAccountId, nameof(recordedByAccountId));
        RequireId(sessionId, nameof(sessionId));
        RequireOptionalId(entryBatchId, nameof(entryBatchId));
        RequireOptionalId(activeConsumptionId, nameof(activeConsumptionId));
        RequireOptionalId(membershipId, nameof(membershipId));
        RequireOptionalId(existingCancellationId, nameof(existingCancellationId));

        if (!Enum.IsDefined(visitKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(visitKind),
                visitKind,
                "Visit kind is not supported.");
        }

        if (!Enum.IsDefined(entryOrigin))
        {
            throw new ArgumentOutOfRangeException(
                nameof(entryOrigin),
                entryOrigin,
                "Entry origin is not supported.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Visit cancellation source status is not supported.");
        }

        VisitId = visitId;
        ClientId = clientId;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
        RecordedByAccountId = recordedByAccountId;
        SessionId = sessionId;
        VisitKind = visitKind;
        EntryOrigin = entryOrigin;
        EntryBatchId = entryBatchId;
        Comment = comment;
        Status = status;
        ActiveConsumptionId = activeConsumptionId;
        MembershipId = membershipId;
        ExistingCancellationId = existingCancellationId;
    }

    public Guid VisitId { get; }

    public Guid ClientId { get; }

    public DateTimeOffset OccurredAt { get; }

    public DateTimeOffset RecordedAt { get; }

    public Guid RecordedByAccountId { get; }

    public Guid SessionId { get; }

    public VisitKind VisitKind { get; }

    public EntryOrigin EntryOrigin { get; }

    public Guid? EntryBatchId { get; }

    public string? Comment { get; }

    public VisitCancellationSourceStatus Status { get; }

    public Guid? ActiveConsumptionId { get; }

    public Guid? MembershipId { get; }

    public Guid? ExistingCancellationId { get; }

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }

    private static void RequireOptionalId(Guid? id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "A supplied id must be non-empty.",
                parameterName);
        }
    }
}
