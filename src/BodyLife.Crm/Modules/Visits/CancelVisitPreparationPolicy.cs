using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Visits;

public static class CancelVisitPreparationPolicy
{
    private const int IdempotencyKeyMaxLength = 200;
    private const int ReasonMaxLength = 1000;
    private const int CommentMaxLength = 1000;

    public static CancelVisitPreparation Prepare(
        CancelVisitCommand command,
        VisitCancellationSource source,
        bool changedAfterClose)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Envelope);
        ArgumentNullException.ThrowIfNull(source);

        if (command.VisitId == Guid.Empty)
        {
            throw new ArgumentException("Visit id is required.", "visitId");
        }

        if (command.VisitId != source.VisitId)
        {
            throw new ArgumentException(
                "Cancellation source must belong to the requested Visit.",
                nameof(source));
        }

        EnsureActiveSourceShape(source);

        var idempotencyKey = NormalizeOptional(command.Envelope.IdempotencyKey);
        if (idempotencyKey is null)
        {
            throw new ArgumentException(
                "Idempotency key is required for Visit cancellation.",
                "idempotencyKey");
        }

        if (idempotencyKey.Length > IdempotencyKeyMaxLength)
        {
            throw new ArgumentException(
                $"Idempotency key must be {IdempotencyKeyMaxLength} characters or fewer.",
                "idempotencyKey");
        }

        var reason = NormalizeOptional(command.Envelope.Reason);
        if (reason is null)
        {
            throw new ArgumentException(
                "Reason is required for Visit cancellation.",
                "reason");
        }

        if (reason.Length > ReasonMaxLength)
        {
            throw new ArgumentException(
                $"Reason must be {ReasonMaxLength} characters or fewer.",
                "reason");
        }

        var comment = NormalizeOptional(command.Envelope.Comment);
        if (comment?.Length > CommentMaxLength)
        {
            throw new ArgumentException(
                $"Comment must be {CommentMaxLength} characters or fewer.",
                "comment");
        }

        if (command.Envelope.OccurredAt is null)
        {
            throw new ArgumentException(
                "Occurred_at is required for Visit cancellation.",
                "occurredAt");
        }

        if (!Enum.IsDefined(command.Envelope.EntryOrigin))
        {
            throw new ArgumentException(
                "Entry origin is not supported.",
                "entryOrigin");
        }

        if (command.EntryBatchId == Guid.Empty)
        {
            throw new ArgumentException(
                "Entry batch id must be non-empty when supplied.",
                "entryBatchId");
        }

        if (command.Envelope.EntryOrigin == EntryOrigin.Normal
            && command.EntryBatchId is not null)
        {
            throw new ArgumentException(
                "Normal Visit cancellation cannot reference a backfill or fallback batch.",
                "entryBatchId");
        }

        var canonicalEnvelope = command.Envelope with
        {
            OccurredAt = command.Envelope.OccurredAt.Value.ToUniversalTime(),
            IdempotencyKey = idempotencyKey,
            Reason = reason,
            Comment = comment,
        };

        return new CancelVisitPreparation(
            canonicalEnvelope,
            source,
            command.EntryBatchId,
            changedAfterClose);
    }

    private static void EnsureActiveSourceShape(VisitCancellationSource source)
    {
        if (source.Status != VisitCancellationSourceStatus.Active
            || source.ExistingCancellationId is not null)
        {
            throw new InvalidOperationException(
                "Only an active Visit without an existing cancellation can be prepared.");
        }

        var hasConsumption = source.ActiveConsumptionId is not null;
        var hasMembership = source.MembershipId is not null;
        if (source.VisitKind == VisitKind.Membership)
        {
            if (!hasConsumption || !hasMembership)
            {
                throw new InvalidOperationException(
                    "An active membership Visit requires its active counted consumption.");
            }

            return;
        }

        if (hasConsumption || hasMembership)
        {
            throw new InvalidOperationException(
                "One-off and trial Visits cannot have a membership consumption.");
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
