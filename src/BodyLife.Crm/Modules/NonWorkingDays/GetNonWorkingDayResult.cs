using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record GetNonWorkingDayResult(
    GetNonWorkingDayStatus Status,
    NonWorkingDayCanonicalPeriod? Period,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetNonWorkingDayResult Succeeded(
        NonWorkingDayCanonicalPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);
        return new GetNonWorkingDayResult(
            GetNonWorkingDayStatus.Success,
            period,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetNonWorkingDayResult Denied()
    {
        return Failure(
            GetNonWorkingDayStatus.PermissionDenied,
            "permission_denied",
            "An active Owner session is required to view a non-working period.",
            field: null);
    }

    public static GetNonWorkingDayResult Missing()
    {
        return Failure(
            GetNonWorkingDayStatus.NotFound,
            "not_found",
            "Non-working period was not found.",
            "periodId");
    }

    public static GetNonWorkingDayResult Invalid(string message, string? field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return Failure(
            GetNonWorkingDayStatus.ValidationFailed,
            "validation_failed",
            message.Trim(),
            field);
    }

    public static GetNonWorkingDayResult InconsistentSource()
    {
        return Failure(
            GetNonWorkingDayStatus.SourceInconsistent,
            "source_inconsistent",
            "The non-working period result is unavailable because canonical source or recalculated Membership state is inconsistent.",
            field: null);
    }

    private static GetNonWorkingDayResult Failure(
        GetNonWorkingDayStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetNonWorkingDayResult(
            status,
            Period: null,
            errorCode,
            errorMessage,
            field);
    }
}

public sealed class NonWorkingDayCanonicalPeriod
{
    public NonWorkingDayCanonicalPeriod(
        Guid periodId,
        DateRange period,
        string reasonCode,
        string? reasonComment,
        DateTimeOffset createdAt,
        Guid createdByAccountId,
        Guid sessionId,
        NonWorkingDayCorrectionSourceStatus status,
        Guid auditEntryId,
        IEnumerable<NonWorkingDayCanonicalApplication> applications)
    {
        RequireId(periodId, nameof(periodId));
        RequireId(createdByAccountId, nameof(createdByAccountId));
        RequireId(sessionId, nameof(sessionId));
        RequireId(auditEntryId, nameof(auditEntryId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentNullException.ThrowIfNull(applications);

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

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var applicationItems = applications.ToArray();
        if (applicationItems.Any(application => application is null)
            || applicationItems.Select(application => application.ApplicationId)
                .Distinct()
                .Count() != applicationItems.Length
            || applicationItems.Select(application => application.MembershipId)
                .Distinct()
                .Count() != applicationItems.Length
            || applicationItems.Any(application =>
                application.AppliedRange != period
                || application.Status != status)
            || !applicationItems.SequenceEqual(applicationItems
                .OrderBy(application => application.MembershipId)
                .ThenBy(application => application.ApplicationId)))
        {
            throw new ArgumentException(
                "Canonical NonWorkingDay applications are inconsistent.",
                nameof(applications));
        }

        PeriodId = periodId;
        Period = period;
        ReasonCode = reasonCode;
        ReasonComment = reasonComment;
        CreatedAt = createdAt;
        CreatedByAccountId = createdByAccountId;
        SessionId = sessionId;
        Status = status;
        AuditEntryId = auditEntryId;
        Applications = Array.AsReadOnly(applicationItems);
    }

    public Guid PeriodId { get; }

    public DateRange Period { get; }

    public string ReasonCode { get; }

    public string? ReasonComment { get; }

    public DateTimeOffset CreatedAt { get; }

    public Guid CreatedByAccountId { get; }

    public Guid SessionId { get; }

    public NonWorkingDayCorrectionSourceStatus Status { get; }

    public Guid AuditEntryId { get; }

    public IReadOnlyList<NonWorkingDayCanonicalApplication> Applications { get; }

    public int AffectedCount => Applications.Count;

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }
}

public sealed class NonWorkingDayCanonicalApplication
{
    public NonWorkingDayCanonicalApplication(
        Guid applicationId,
        Guid membershipId,
        Guid clientId,
        string clientDisplayName,
        DateRange appliedRange,
        DateTimeOffset previewedAt,
        DateTimeOffset confirmedAt,
        NonWorkingDayCorrectionSourceStatus status,
        DateOnly currentEffectiveEndDate,
        int currentExtensionDays,
        DateTimeOffset recalculatedAt)
    {
        RequireId(applicationId, nameof(applicationId));
        RequireId(membershipId, nameof(membershipId));
        RequireId(clientId, nameof(clientId));
        ArgumentException.ThrowIfNullOrWhiteSpace(clientDisplayName);

        if (clientDisplayName != clientDisplayName.Trim())
        {
            throw new ArgumentException(
                "Client display name must be normalized.",
                nameof(clientDisplayName));
        }

        if (previewedAt > confirmedAt)
        {
            throw new ArgumentException(
                "NonWorkingDay preview cannot follow confirmation.",
                nameof(previewedAt));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (currentExtensionDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentExtensionDays));
        }

        ApplicationId = applicationId;
        MembershipId = membershipId;
        ClientId = clientId;
        ClientDisplayName = clientDisplayName;
        AppliedRange = appliedRange;
        PreviewedAt = previewedAt;
        ConfirmedAt = confirmedAt;
        Status = status;
        CurrentEffectiveEndDate = currentEffectiveEndDate;
        CurrentExtensionDays = currentExtensionDays;
        RecalculatedAt = recalculatedAt;
    }

    public Guid ApplicationId { get; }

    public Guid MembershipId { get; }

    public Guid ClientId { get; }

    public string ClientDisplayName { get; }

    public DateRange AppliedRange { get; }

    public DateTimeOffset PreviewedAt { get; }

    public DateTimeOffset ConfirmedAt { get; }

    public NonWorkingDayCorrectionSourceStatus Status { get; }

    public DateOnly CurrentEffectiveEndDate { get; }

    public int CurrentExtensionDays { get; }

    public DateTimeOffset RecalculatedAt { get; }

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }
}
