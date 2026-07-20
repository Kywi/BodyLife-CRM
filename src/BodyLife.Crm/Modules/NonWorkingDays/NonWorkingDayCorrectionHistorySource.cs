using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class NonWorkingDayCorrectionHistorySource
{
    public NonWorkingDayCorrectionHistorySource(
        NonWorkingDayCorrectionMode mode,
        NonWorkingDayHistoryPeriodSource originalPeriod,
        NonWorkingDayHistoryPeriodSource? replacementPeriod,
        NonWorkingDayCancellationHistorySource? cancellation,
        string correctionReason,
        string correctionComment,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        AccountId actorAccountId,
        SessionId recordedSessionId,
        EntryOrigin entryOrigin,
        IEnumerable<Guid> affectedMembershipIds)
    {
        ArgumentNullException.ThrowIfNull(originalPeriod);
        ArgumentException.ThrowIfNullOrWhiteSpace(correctionReason);
        ArgumentException.ThrowIfNullOrWhiteSpace(correctionComment);
        ArgumentNullException.ThrowIfNull(affectedMembershipIds);

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (correctionReason != correctionReason.Trim()
            || correctionComment != correctionComment.Trim()
            || occurredAt.Offset != TimeSpan.Zero
            || recordedAt.Offset != TimeSpan.Zero
            || occurredAt > recordedAt
            || !Enum.IsDefined(entryOrigin))
        {
            throw new ArgumentException(
                "NonWorkingDay correction envelope is invalid.",
                nameof(correctionReason));
        }

        var affectedIds = affectedMembershipIds.ToArray();
        if (affectedIds.Any(id => id == Guid.Empty)
            || affectedIds.Distinct().Count() != affectedIds.Length
            || !affectedIds.SequenceEqual(affectedIds.Order()))
        {
            throw new ArgumentException(
                "Affected Membership ids must be non-empty, unique and ordered.",
                nameof(affectedMembershipIds));
        }

        if (mode == NonWorkingDayCorrectionMode.Cancel)
        {
            if (originalPeriod.CurrentStatus
                    != NonWorkingDayCorrectionSourceStatus.Canceled
                || replacementPeriod is not null
                || cancellation is null
                || cancellation.PeriodId != originalPeriod.PeriodId
                || cancellation.Reason != correctionReason
                || cancellation.RecordedAt != recordedAt
                || cancellation.RecordedByAccountId != actorAccountId
                || cancellation.RecordedSessionId != recordedSessionId)
            {
                throw new ArgumentException(
                    "NonWorkingDay cancellation source is inconsistent.",
                    nameof(cancellation));
            }
        }
        else if (originalPeriod.CurrentStatus
                    != NonWorkingDayCorrectionSourceStatus.Corrected
            || replacementPeriod is null
            || replacementPeriod.CreatedAt != recordedAt
            || replacementPeriod.CreatedByAccountId != actorAccountId
            || replacementPeriod.RecordedSessionId != recordedSessionId
            || cancellation is not null)
        {
            throw new ArgumentException(
                "NonWorkingDay replacement source is inconsistent.",
                nameof(replacementPeriod));
        }

        Mode = mode;
        OriginalPeriod = originalPeriod;
        ReplacementPeriod = replacementPeriod;
        Cancellation = cancellation;
        CorrectionReason = correctionReason;
        CorrectionComment = correctionComment;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
        ActorAccountId = actorAccountId;
        RecordedSessionId = recordedSessionId;
        EntryOrigin = entryOrigin;
        AffectedMembershipIds = Array.AsReadOnly(affectedIds);
    }

    public NonWorkingDayCorrectionMode Mode { get; }

    public NonWorkingDayHistoryPeriodSource OriginalPeriod { get; }

    public NonWorkingDayHistoryPeriodSource? ReplacementPeriod { get; }

    public NonWorkingDayCancellationHistorySource? Cancellation { get; }

    public string CorrectionReason { get; }

    public string CorrectionComment { get; }

    public DateTimeOffset OccurredAt { get; }

    public DateTimeOffset RecordedAt { get; }

    public AccountId ActorAccountId { get; }

    public SessionId RecordedSessionId { get; }

    public EntryOrigin EntryOrigin { get; }

    public IReadOnlyList<Guid> AffectedMembershipIds { get; }
}
