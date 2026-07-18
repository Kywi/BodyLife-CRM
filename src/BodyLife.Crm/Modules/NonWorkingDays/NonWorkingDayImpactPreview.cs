using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class NonWorkingDayImpactPreview
{
    public NonWorkingDayImpactPreview(
        NonWorkingDayPreviewInput input,
        IEnumerable<NonWorkingDayImpactMembershipPreview> affectedMemberships,
        NonWorkingDayPreviewConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(affectedMemberships);
        ArgumentNullException.ThrowIfNull(confirmation);

        var membershipItems = affectedMemberships.ToArray();
        if (membershipItems.Any(item => item is null))
        {
            throw new ArgumentException(
                "NonWorkingDay preview cannot contain a missing Membership item.",
                nameof(affectedMemberships));
        }

        if (membershipItems.Any(item => item.AppliedRange != input.Period)
            || membershipItems.Select(item => item.MembershipId).Distinct().Count()
                != membershipItems.Length
            || !membershipItems.SequenceEqual(
                membershipItems.OrderBy(item => item.MembershipId)))
        {
            throw new ArgumentException(
                "NonWorkingDay preview must preserve the exact ordered full-period scope.",
                nameof(affectedMemberships));
        }

        Period = input.Period;
        ReasonCode = input.ReasonCode;
        ReasonComment = input.ReasonComment;
        AffectedMemberships = Array.AsReadOnly(membershipItems);
        Confirmation = confirmation;
    }

    public DateRange Period { get; }

    public string ReasonCode { get; }

    public string? ReasonComment { get; }

    public IReadOnlyList<NonWorkingDayImpactMembershipPreview> AffectedMemberships { get; }

    public int AffectedCount => AffectedMemberships.Count;

    public int OverlapWarningCount => AffectedMemberships.Sum(
        membership => membership.OverlapWarnings.Count);

    public bool HasOverlapWarnings => OverlapWarningCount > 0;

    public NonWorkingDayPreviewConfirmation Confirmation { get; }
}

public sealed class NonWorkingDayImpactMembershipPreview
{
    public NonWorkingDayImpactMembershipPreview(
        Guid membershipId,
        Guid clientId,
        string clientDisplayName,
        DateRange appliedRange,
        int beforeExtensionDays,
        DateOnly beforeEffectiveEndDate,
        int estimatedAfterExtensionDays,
        DateOnly estimatedAfterEffectiveEndDate,
        int addedUniqueExtensionDays,
        int existingOverlapDays,
        IEnumerable<NonWorkingDayImpactOverlapWarning> overlapWarnings)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException("Membership id is required.", nameof(membershipId));
        }

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(clientDisplayName);
        if (!string.Equals(
                clientDisplayName,
                clientDisplayName.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Client display name must be normalized.",
                nameof(clientDisplayName));
        }

        ArgumentNullException.ThrowIfNull(overlapWarnings);
        var warnings = overlapWarnings.ToArray();
        if (warnings.Any(warning => warning is null))
        {
            throw new ArgumentException(
                "NonWorkingDay impact cannot contain a missing overlap warning.",
                nameof(overlapWarnings));
        }

        if (beforeExtensionDays < 0
            || estimatedAfterExtensionDays < beforeExtensionDays
            || addedUniqueExtensionDays < 0
            || existingOverlapDays < 0
            || estimatedAfterExtensionDays - beforeExtensionDays
                != addedUniqueExtensionDays
            || appliedRange.InclusiveDays
                != addedUniqueExtensionDays + existingOverlapDays
            || estimatedAfterEffectiveEndDate.DayNumber
                - beforeEffectiveEndDate.DayNumber != addedUniqueExtensionDays)
        {
            throw new ArgumentException(
                "NonWorkingDay impact estimate is inconsistent.",
                nameof(estimatedAfterExtensionDays));
        }

        MembershipId = membershipId;
        ClientId = clientId;
        ClientDisplayName = clientDisplayName;
        AppliedRange = appliedRange;
        BeforeExtensionDays = beforeExtensionDays;
        BeforeEffectiveEndDate = beforeEffectiveEndDate;
        EstimatedAfterExtensionDays = estimatedAfterExtensionDays;
        EstimatedAfterEffectiveEndDate = estimatedAfterEffectiveEndDate;
        AddedUniqueExtensionDays = addedUniqueExtensionDays;
        ExistingOverlapDays = existingOverlapDays;
        OverlapWarnings = Array.AsReadOnly(warnings);
    }

    public Guid MembershipId { get; }

    public Guid ClientId { get; }

    public string ClientDisplayName { get; }

    public DateRange AppliedRange { get; }

    public int BeforeExtensionDays { get; }

    public DateOnly BeforeEffectiveEndDate { get; }

    public int EstimatedAfterExtensionDays { get; }

    public DateOnly EstimatedAfterEffectiveEndDate { get; }

    public int AddedUniqueExtensionDays { get; }

    public int ExistingOverlapDays { get; }

    public IReadOnlyList<NonWorkingDayImpactOverlapWarning> OverlapWarnings { get; }
}

public sealed record NonWorkingDayImpactOverlapWarning(
    string SourceType,
    Guid SourceId,
    string SourceLabel,
    DateRange OverlapRange,
    int OverlapDays);
