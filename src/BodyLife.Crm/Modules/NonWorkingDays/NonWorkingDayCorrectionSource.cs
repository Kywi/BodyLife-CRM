using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class NonWorkingDayCorrectionSource
{
    public NonWorkingDayCorrectionSource(
        Guid periodId,
        DateRange period,
        string reasonCode,
        string? reasonComment,
        DateTimeOffset createdAt,
        Guid createdByAccountId,
        Guid sessionId,
        NonWorkingDayCorrectionSourceStatus status,
        IEnumerable<NonWorkingDayCorrectionApplicationSource> applications,
        Guid? existingCancellationId)
    {
        RequireId(periodId, nameof(periodId));
        RequireId(createdByAccountId, nameof(createdByAccountId));
        RequireId(sessionId, nameof(sessionId));
        RequireOptionalId(existingCancellationId, nameof(existingCancellationId));
        ArgumentNullException.ThrowIfNull(reasonCode);
        ArgumentNullException.ThrowIfNull(applications);

        if (string.IsNullOrWhiteSpace(reasonCode)
            || reasonCode.Length > NonWorkingDayPreviewInput.ReasonCodeMaxLength)
        {
            throw new ArgumentException(
                "NonWorkingDay source reason code is invalid.",
                nameof(reasonCode));
        }

        if (reasonComment is not null
            && (string.IsNullOrWhiteSpace(reasonComment)
                || reasonComment.Length > NonWorkingDayPreviewInput.ReasonCommentMaxLength))
        {
            throw new ArgumentException(
                "NonWorkingDay source reason comment is invalid.",
                nameof(reasonComment));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "NonWorkingDay correction source status is not supported.");
        }

        var applicationItems = applications.ToArray();
        if (applicationItems.Any(application => application is null))
        {
            throw new ArgumentException(
                "NonWorkingDay source applications cannot contain a missing item.",
                nameof(applications));
        }

        if (applicationItems.Select(application => application.ApplicationId)
            .Distinct()
            .Count() != applicationItems.Length)
        {
            throw new ArgumentException(
                "NonWorkingDay source applications cannot contain duplicate ids.",
                nameof(applications));
        }

        if (applicationItems.Select(application => application.MembershipId)
            .Distinct()
            .Count() != applicationItems.Length)
        {
            throw new ArgumentException(
                "NonWorkingDay source applications cannot contain duplicate Memberships.",
                nameof(applications));
        }

        if (applicationItems.Any(application => application.AppliedRange != period))
        {
            throw new ArgumentException(
                "Every NonWorkingDay application must preserve the full period.",
                nameof(applications));
        }

        if (applicationItems.Any(application => application.Status != status))
        {
            throw new ArgumentException(
                "NonWorkingDay period and application statuses must agree.",
                nameof(applications));
        }

        if (!applicationItems.SequenceEqual(
                applicationItems
                    .OrderBy(application => application.MembershipId)
                    .ThenBy(application => application.ApplicationId)))
        {
            throw new ArgumentException(
                "NonWorkingDay source applications must use deterministic order.",
                nameof(applications));
        }

        if ((status == NonWorkingDayCorrectionSourceStatus.Canceled)
            != (existingCancellationId is not null))
        {
            throw new ArgumentException(
                "Only a canceled NonWorkingDay source must have a cancellation fact.",
                nameof(existingCancellationId));
        }

        PeriodId = periodId;
        Period = period;
        ReasonCode = reasonCode;
        ReasonComment = reasonComment;
        CreatedAt = createdAt;
        CreatedByAccountId = createdByAccountId;
        SessionId = sessionId;
        Status = status;
        Applications = Array.AsReadOnly(applicationItems);
        ExistingCancellationId = existingCancellationId;
    }

    public Guid PeriodId { get; }

    public DateRange Period { get; }

    public string ReasonCode { get; }

    public string? ReasonComment { get; }

    public DateTimeOffset CreatedAt { get; }

    public Guid CreatedByAccountId { get; }

    public Guid SessionId { get; }

    public NonWorkingDayCorrectionSourceStatus Status { get; }

    public IReadOnlyList<NonWorkingDayCorrectionApplicationSource> Applications { get; }

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
