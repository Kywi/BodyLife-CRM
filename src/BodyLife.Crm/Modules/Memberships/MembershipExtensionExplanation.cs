using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipExtensionExplanation
{
    public MembershipExtensionExplanation(
        Guid membershipId,
        MembershipExtensionSourceKind sourceKind,
        Guid sourceId,
        Guid? nonWorkingPeriodId,
        DateRange range,
        MembershipExtensionSourceStatus status,
        string? reasonLabel)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException("Membership id is required.", nameof(membershipId));
        }

        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, null);
        }

        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Extension source id is required.", nameof(sourceId));
        }

        if (sourceKind == MembershipExtensionSourceKind.Freeze
            && nonWorkingPeriodId is not null)
        {
            throw new ArgumentException(
                "Freeze explanations cannot reference a non-working period.",
                nameof(nonWorkingPeriodId));
        }

        if (sourceKind == MembershipExtensionSourceKind.NonWorkingDay
            && nonWorkingPeriodId is null)
        {
            throw new ArgumentException(
                "Non-working day explanations require their period id.",
                nameof(nonWorkingPeriodId));
        }

        if (nonWorkingPeriodId == Guid.Empty)
        {
            throw new ArgumentException(
                "Non-working period id cannot be empty.",
                nameof(nonWorkingPeriodId));
        }

        if (range.StartDate == default || range.EndDate == default)
        {
            throw new ArgumentException("Extension source range is required.", nameof(range));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        var normalizedReasonLabel = reasonLabel?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReasonLabel))
        {
            throw new ArgumentException(
                "Extension source reason label is required.",
                nameof(reasonLabel));
        }

        if (normalizedReasonLabel.Length > MembershipExtensionSourceRange.MaxSourceLabelLength)
        {
            throw new ArgumentException(
                "Extension source reason label is too long.",
                nameof(reasonLabel));
        }

        MembershipId = membershipId;
        SourceKind = sourceKind;
        SourceId = sourceId;
        NonWorkingPeriodId = nonWorkingPeriodId;
        Range = range;
        Status = status;
        ReasonLabel = normalizedReasonLabel;
    }

    public Guid MembershipId { get; }

    public MembershipExtensionSourceKind SourceKind { get; }

    public Guid SourceId { get; }

    public Guid? NonWorkingPeriodId { get; }

    public DateRange Range { get; }

    public MembershipExtensionSourceStatus Status { get; }

    public string ReasonLabel { get; }

    public bool IsActive => Status == MembershipExtensionSourceStatus.Active;
}
