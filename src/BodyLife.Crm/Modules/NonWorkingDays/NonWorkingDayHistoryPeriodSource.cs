using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class NonWorkingDayHistoryPeriodSource
{
    public NonWorkingDayHistoryPeriodSource(
        Guid periodId,
        Guid clientId,
        DateRange period,
        string reasonCode,
        string? reasonComment,
        DateTimeOffset createdAt,
        AccountId createdByAccountId,
        SessionId recordedSessionId,
        NonWorkingDayCorrectionSourceStatus currentStatus,
        Guid? currentCancellationId,
        int confirmedAffectedMembershipCount,
        int confirmedAffectedClientCount,
        IEnumerable<NonWorkingDayHistoryApplicationSource> clientApplications)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentNullException.ThrowIfNull(clientApplications);
        RequireId(periodId, nameof(periodId));
        RequireId(clientId, nameof(clientId));
        RequireOptionalId(currentCancellationId, nameof(currentCancellationId));

        if (reasonCode != reasonCode.Trim()
            || reasonCode.Length > NonWorkingDayPreviewInput.ReasonCodeMaxLength)
        {
            throw new ArgumentException(
                "NonWorkingDay reason code is invalid.",
                nameof(reasonCode));
        }

        if (reasonComment is not null
            && (reasonComment != reasonComment.Trim()
                || reasonComment.Length
                    > NonWorkingDayPreviewInput.ReasonCommentMaxLength))
        {
            throw new ArgumentException(
                "NonWorkingDay reason comment is invalid.",
                nameof(reasonComment));
        }

        if (!Enum.IsDefined(currentStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(currentStatus));
        }

        if ((currentStatus == NonWorkingDayCorrectionSourceStatus.Canceled)
            != (currentCancellationId is not null))
        {
            throw new ArgumentException(
                "Only a canceled period may reference a cancellation fact.",
                nameof(currentCancellationId));
        }

        var applications = clientApplications.ToArray();
        if (confirmedAffectedMembershipCount < applications.Length
            || confirmedAffectedClientCount < (applications.Length == 0 ? 0 : 1)
            || applications.Any(application => application is null)
            || applications.Any(application =>
                application.ApplicationId == Guid.Empty
                || application.PeriodId != periodId
                || application.MembershipId == Guid.Empty
                || application.ClientId != clientId
                || string.IsNullOrWhiteSpace(
                    application.MembershipTypeNameSnapshot)
                || application.MembershipTypeNameSnapshot
                    != application.MembershipTypeNameSnapshot.Trim()
                || application.AppliedRange != period
                || application.PreviewedAt > application.ConfirmedAt
                || application.CurrentStatus != currentStatus)
            || applications.Select(application => application.ApplicationId)
                .Distinct()
                .Count() != applications.Length
            || applications.Select(application => application.MembershipId)
                .Distinct()
                .Count() != applications.Length
            || !applications.SequenceEqual(applications
                .OrderBy(application => application.MembershipId)
                .ThenBy(application => application.ApplicationId)))
        {
            throw new ArgumentException(
                "Client NonWorkingDay applications are inconsistent.",
                nameof(clientApplications));
        }

        PeriodId = periodId;
        ClientId = clientId;
        Period = period;
        ReasonCode = reasonCode;
        ReasonComment = reasonComment;
        CreatedAt = createdAt;
        CreatedByAccountId = createdByAccountId;
        RecordedSessionId = recordedSessionId;
        CurrentStatus = currentStatus;
        CurrentCancellationId = currentCancellationId;
        ConfirmedAffectedMembershipCount = confirmedAffectedMembershipCount;
        ConfirmedAffectedClientCount = confirmedAffectedClientCount;
        ClientApplications = Array.AsReadOnly(applications);
    }

    public Guid PeriodId { get; }

    public Guid ClientId { get; }

    public DateRange Period { get; }

    public string ReasonCode { get; }

    public string? ReasonComment { get; }

    public DateTimeOffset CreatedAt { get; }

    public AccountId CreatedByAccountId { get; }

    public SessionId RecordedSessionId { get; }

    public NonWorkingDayCorrectionSourceStatus CurrentStatus { get; }

    public Guid? CurrentCancellationId { get; }

    public int ConfirmedAffectedMembershipCount { get; }

    public int ConfirmedAffectedClientCount { get; }

    public IReadOnlyList<NonWorkingDayHistoryApplicationSource> ClientApplications
    {
        get;
    }

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
