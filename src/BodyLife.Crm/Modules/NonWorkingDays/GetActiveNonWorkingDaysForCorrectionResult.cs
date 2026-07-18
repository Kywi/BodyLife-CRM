using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record GetActiveNonWorkingDaysForCorrectionResult(
    GetActiveNonWorkingDaysForCorrectionStatus Status,
    IReadOnlyList<ActiveNonWorkingDayForCorrection> Items,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static GetActiveNonWorkingDaysForCorrectionResult Succeeded(
        IEnumerable<ActiveNonWorkingDayForCorrection> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var itemArray = items.ToArray();
        if (itemArray.Any(item => item is null)
            || itemArray.Select(item => item.PeriodId).Distinct().Count()
                != itemArray.Length)
        {
            throw new ArgumentException(
                "Active NonWorkingDay correction items are inconsistent.",
                nameof(items));
        }

        return new GetActiveNonWorkingDaysForCorrectionResult(
            GetActiveNonWorkingDaysForCorrectionStatus.Success,
            Array.AsReadOnly(itemArray),
            ErrorCode: null,
            ErrorMessage: null);
    }

    public static GetActiveNonWorkingDaysForCorrectionResult Denied()
    {
        return Failure(
            GetActiveNonWorkingDaysForCorrectionStatus.PermissionDenied,
            "permission_denied",
            "An active Owner session is required to list non-working periods for correction.");
    }

    public static GetActiveNonWorkingDaysForCorrectionResult InconsistentSource()
    {
        return Failure(
            GetActiveNonWorkingDaysForCorrectionStatus.SourceInconsistent,
            "source_inconsistent",
            "Active non-working periods are unavailable because canonical source records are inconsistent.");
    }

    private static GetActiveNonWorkingDaysForCorrectionResult Failure(
        GetActiveNonWorkingDaysForCorrectionStatus status,
        string errorCode,
        string errorMessage)
    {
        return new GetActiveNonWorkingDaysForCorrectionResult(
            status,
            Items: [],
            errorCode,
            errorMessage);
    }
}

public sealed class ActiveNonWorkingDayForCorrection
{
    public ActiveNonWorkingDayForCorrection(
        Guid periodId,
        DateRange period,
        string reasonCode,
        string? reasonComment,
        DateTimeOffset createdAt,
        int affectedCount)
    {
        if (periodId == Guid.Empty)
        {
            throw new ArgumentException(
                "NonWorkingDay period id is required.",
                nameof(periodId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
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

        if (affectedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(affectedCount));
        }

        PeriodId = periodId;
        Period = period;
        ReasonCode = reasonCode;
        ReasonComment = reasonComment;
        CreatedAt = createdAt;
        AffectedCount = affectedCount;
    }

    public Guid PeriodId { get; }

    public DateRange Period { get; }

    public string ReasonCode { get; }

    public string? ReasonComment { get; }

    public DateTimeOffset CreatedAt { get; }

    public int AffectedCount { get; }
}
