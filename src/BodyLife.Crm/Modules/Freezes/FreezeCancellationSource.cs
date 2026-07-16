using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Freezes;

public sealed class FreezeCancellationSource
{
    public FreezeCancellationSource(
        Guid freezeId,
        Guid clientId,
        Guid membershipId,
        DateRange range,
        string reason,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId,
        Guid sessionId,
        EntryOrigin entryOrigin,
        Guid? entryBatchId,
        FreezeCancellationSourceStatus status,
        Guid? existingCancellationId)
    {
        RequireId(freezeId, nameof(freezeId));
        RequireId(clientId, nameof(clientId));
        RequireId(membershipId, nameof(membershipId));
        RequireId(recordedByAccountId, nameof(recordedByAccountId));
        RequireId(sessionId, nameof(sessionId));
        RequireOptionalId(entryBatchId, nameof(entryBatchId));
        RequireOptionalId(existingCancellationId, nameof(existingCancellationId));

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Freeze source reason is required.",
                nameof(reason));
        }

        if (!Enum.IsDefined(entryOrigin))
        {
            throw new ArgumentOutOfRangeException(
                nameof(entryOrigin),
                entryOrigin,
                "Freeze entry origin is not supported.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Freeze cancellation source status is not supported.");
        }

        FreezeId = freezeId;
        ClientId = clientId;
        MembershipId = membershipId;
        Range = range;
        Reason = reason;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
        RecordedByAccountId = recordedByAccountId;
        SessionId = sessionId;
        EntryOrigin = entryOrigin;
        EntryBatchId = entryBatchId;
        Status = status;
        ExistingCancellationId = existingCancellationId;
    }

    public Guid FreezeId { get; }

    public Guid ClientId { get; }

    public Guid MembershipId { get; }

    public DateRange Range { get; }

    public string Reason { get; }

    public DateTimeOffset OccurredAt { get; }

    public DateTimeOffset RecordedAt { get; }

    public Guid RecordedByAccountId { get; }

    public Guid SessionId { get; }

    public EntryOrigin EntryOrigin { get; }

    public Guid? EntryBatchId { get; }

    public FreezeCancellationSourceStatus Status { get; }

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
