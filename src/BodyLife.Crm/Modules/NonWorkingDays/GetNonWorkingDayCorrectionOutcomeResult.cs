using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record GetNonWorkingDayCorrectionOutcomeResult(
    GetNonWorkingDayCorrectionOutcomeStatus Status,
    NonWorkingDayCanonicalCorrection? Correction,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetNonWorkingDayCorrectionOutcomeResult Succeeded(
        NonWorkingDayCanonicalCorrection correction)
    {
        ArgumentNullException.ThrowIfNull(correction);
        return new GetNonWorkingDayCorrectionOutcomeResult(
            GetNonWorkingDayCorrectionOutcomeStatus.Success,
            correction,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetNonWorkingDayCorrectionOutcomeResult Denied()
    {
        return Failure(
            GetNonWorkingDayCorrectionOutcomeStatus.PermissionDenied,
            "permission_denied",
            "An active Owner session is required to view a non-working period correction.",
            field: null);
    }

    public static GetNonWorkingDayCorrectionOutcomeResult Invalid(
        string message,
        string? field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return Failure(
            GetNonWorkingDayCorrectionOutcomeStatus.ValidationFailed,
            "validation_failed",
            message.Trim(),
            field);
    }

    public static GetNonWorkingDayCorrectionOutcomeResult Missing()
    {
        return Failure(
            GetNonWorkingDayCorrectionOutcomeStatus.NotFound,
            "not_found",
            "Non-working period correction outcome was not found.",
            field: null);
    }

    public static GetNonWorkingDayCorrectionOutcomeResult InconsistentSource()
    {
        return Failure(
            GetNonWorkingDayCorrectionOutcomeStatus.SourceInconsistent,
            "source_inconsistent",
            "The non-working period correction is unavailable because its canonical source, audit linkage or recalculated Membership state is inconsistent.",
            field: null);
    }

    private static GetNonWorkingDayCorrectionOutcomeResult Failure(
        GetNonWorkingDayCorrectionOutcomeStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetNonWorkingDayCorrectionOutcomeResult(
            status,
            Correction: null,
            errorCode,
            errorMessage,
            field);
    }
}

public sealed class NonWorkingDayCanonicalCorrection
{
    public NonWorkingDayCanonicalCorrection(
        NonWorkingDayCorrectionMode mode,
        NonWorkingDayCanonicalPeriod originalPeriod,
        NonWorkingDayCanonicalPeriod? replacementPeriod,
        NonWorkingDayCanonicalCancellation? cancellation,
        Guid auditEntryId,
        string correctionReason,
        string correctionComment,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        Guid actorAccountId,
        Guid sessionId,
        string? deviceLabel,
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

        RequireId(auditEntryId, nameof(auditEntryId));
        RequireId(actorAccountId, nameof(actorAccountId));
        RequireId(sessionId, nameof(sessionId));
        if (correctionReason != correctionReason.Trim()
            || correctionReason.Length > 1000)
        {
            throw new ArgumentException(
                "Correction reason is invalid.",
                nameof(correctionReason));
        }

        if (correctionComment != correctionComment.Trim()
            || correctionComment.Length > 1000)
        {
            throw new ArgumentException(
                "Correction comment is invalid.",
                nameof(correctionComment));
        }

        if (occurredAt.Offset != TimeSpan.Zero
            || recordedAt.Offset != TimeSpan.Zero
            || occurredAt > recordedAt)
        {
            throw new ArgumentException(
                "Correction timestamps must be ordered UTC values.",
                nameof(occurredAt));
        }

        if (!Enum.IsDefined(entryOrigin))
        {
            throw new ArgumentOutOfRangeException(nameof(entryOrigin));
        }

        var normalizedDeviceLabel = NormalizeOptional(deviceLabel);
        var affectedIds = affectedMembershipIds.ToArray();
        if (affectedIds.Any(id => id == Guid.Empty)
            || affectedIds.Distinct().Count() != affectedIds.Length
            || !affectedIds.SequenceEqual(affectedIds.Order()))
        {
            throw new ArgumentException(
                "Affected Membership ids must be non-empty, unique and ordered.",
                nameof(affectedMembershipIds));
        }

        var expectedAffectedIds = originalPeriod.Applications
            .Select(application => application.MembershipId)
            .Concat(replacementPeriod?.Applications.Select(
                application => application.MembershipId) ?? [])
            .Distinct()
            .Order()
            .ToArray();
        if (!affectedIds.SequenceEqual(expectedAffectedIds)
            || originalPeriod.AuditEntryId != auditEntryId
            || (replacementPeriod is not null
                && replacementPeriod.AuditEntryId != auditEntryId))
        {
            throw new ArgumentException(
                "Canonical correction scope or audit reference is inconsistent.",
                nameof(affectedMembershipIds));
        }

        if (mode == NonWorkingDayCorrectionMode.Cancel)
        {
            if (originalPeriod.Status != NonWorkingDayCorrectionSourceStatus.Canceled
                || replacementPeriod is not null
                || cancellation is null
                || cancellation.PeriodId != originalPeriod.PeriodId
                || cancellation.Reason != correctionReason
                || cancellation.RecordedAt != recordedAt
                || cancellation.RecordedByAccountId != actorAccountId
                || cancellation.SessionId != sessionId)
            {
                throw new ArgumentException(
                    "Canonical cancellation facts are inconsistent.",
                    nameof(cancellation));
            }
        }
        else if (originalPeriod.Status
                != NonWorkingDayCorrectionSourceStatus.Corrected
            || replacementPeriod is null
            || replacementPeriod.Status
                != NonWorkingDayCorrectionSourceStatus.Active
            || replacementPeriod.CreatedAt != recordedAt
            || replacementPeriod.CreatedByAccountId != actorAccountId
            || replacementPeriod.SessionId != sessionId
            || cancellation is not null)
        {
            throw new ArgumentException(
                "Canonical replacement facts are inconsistent.",
                nameof(replacementPeriod));
        }

        Mode = mode;
        OriginalPeriod = originalPeriod;
        ReplacementPeriod = replacementPeriod;
        Cancellation = cancellation;
        AuditEntryId = auditEntryId;
        CorrectionReason = correctionReason;
        CorrectionComment = correctionComment;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
        ActorAccountId = actorAccountId;
        SessionId = sessionId;
        DeviceLabel = normalizedDeviceLabel;
        EntryOrigin = entryOrigin;
        AffectedMembershipIds = Array.AsReadOnly(affectedIds);
    }

    public NonWorkingDayCorrectionMode Mode { get; }

    public NonWorkingDayCanonicalPeriod OriginalPeriod { get; }

    public NonWorkingDayCanonicalPeriod? ReplacementPeriod { get; }

    public NonWorkingDayCanonicalCancellation? Cancellation { get; }

    public Guid AuditEntryId { get; }

    public string CorrectionReason { get; }

    public string CorrectionComment { get; }

    public DateTimeOffset OccurredAt { get; }

    public DateTimeOffset RecordedAt { get; }

    public Guid ActorAccountId { get; }

    public Guid SessionId { get; }

    public string? DeviceLabel { get; }

    public EntryOrigin EntryOrigin { get; }

    public IReadOnlyList<Guid> AffectedMembershipIds { get; }

    public int OriginalAffectedCount => OriginalPeriod.AffectedCount;

    public int ReplacementAffectedCount => ReplacementPeriod?.AffectedCount ?? 0;

    public int AffectedUnionCount => AffectedMembershipIds.Count;

    public Guid PrimaryEntityId => Mode == NonWorkingDayCorrectionMode.Cancel
        ? Cancellation!.CancellationId
        : ReplacementPeriod!.PeriodId;

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }
}

public sealed class NonWorkingDayCanonicalCancellation
{
    public NonWorkingDayCanonicalCancellation(
        Guid cancellationId,
        Guid periodId,
        string reason,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId,
        Guid sessionId)
    {
        RequireId(cancellationId, nameof(cancellationId));
        RequireId(periodId, nameof(periodId));
        RequireId(recordedByAccountId, nameof(recordedByAccountId));
        RequireId(sessionId, nameof(sessionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason != reason.Trim() || recordedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Canonical cancellation values are not normalized.",
                nameof(reason));
        }

        CancellationId = cancellationId;
        PeriodId = periodId;
        Reason = reason;
        RecordedAt = recordedAt;
        RecordedByAccountId = recordedByAccountId;
        SessionId = sessionId;
    }

    public Guid CancellationId { get; }

    public Guid PeriodId { get; }

    public string Reason { get; }

    public DateTimeOffset RecordedAt { get; }

    public Guid RecordedByAccountId { get; }

    public Guid SessionId { get; }

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }
}
