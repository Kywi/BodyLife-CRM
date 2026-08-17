namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipIssuePreview
{
    internal MembershipIssuePreview(
        Guid clientId,
        DateTimeOffset membershipTypeUpdatedAt,
        MembershipIssueTerms issueTerms,
        MembershipCalculatedState expectedInitialState,
        MembershipIssueNegativeContext? existingNegativeState,
        int automaticCoveredNegativeVisitCount,
        DateOnly? previewBusinessDate,
        IEnumerable<MembershipWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);
        ArgumentNullException.ThrowIfNull(expectedInitialState);
        ArgumentNullException.ThrowIfNull(warnings);
        ClientId = clientId;
        MembershipTypeId = issueTerms.MembershipTypeId;
        MembershipTypeUpdatedAt = membershipTypeUpdatedAt;
        Snapshot = issueTerms.Snapshot;
        ProposedStartDate = issueTerms.StartDate;
        BaseEndDate = issueTerms.BaseEndDate;
        ExpectedInitialState = expectedInitialState;
        ExpectedInitialRemainingVisits = expectedInitialState.RemainingVisits;
        ExpectedInitialExtensionDays = expectedInitialState.ExtensionDays;
        ExpectedInitialEffectiveEndDate = expectedInitialState.EffectiveEndDate;
        ExistingNegativeState = existingNegativeState;
        AutomaticCoveredNegativeVisitCount = automaticCoveredNegativeVisitCount;
        CoveredNegativeVisits = Array.AsReadOnly(
            existingNegativeState?.OpenConcreteVisits
                .Take(automaticCoveredNegativeVisitCount)
                .ToArray() ?? []);
        RemainingExistingNegativeBalance = existingNegativeState is null
            ? 0
            : existingNegativeState.NegativeBalance - automaticCoveredNegativeVisitCount;
        RemainingConcreteNegativeVisitCount = existingNegativeState is null
            ? 0
            : existingNegativeState.OpenConcreteVisitCount - automaticCoveredNegativeVisitCount;
        UnknownNegativeBalance = existingNegativeState?.UnknownNegativeBalance ?? 0;
        UsesForcedCoverageStartDate = automaticCoveredNegativeVisitCount > 0;
        IsAlreadyExpiredAtPreview = UsesForcedCoverageStartDate
            && previewBusinessDate is { } asOfDate
            && expectedInitialState.EffectiveEndDate < asOfDate;
        CanProceedToIssue = existingNegativeState is null
            || existingNegativeState.OpenConcreteVisitCount == 0
            || automaticCoveredNegativeVisitCount > 0;
        Warnings = Array.AsReadOnly(warnings.ToArray());
    }
    public Guid ClientId { get; }
    public Guid MembershipTypeId { get; }
    public DateTimeOffset MembershipTypeUpdatedAt { get; }
    public IssuedMembershipSnapshot Snapshot { get; }
    public DateOnly ProposedStartDate { get; }
    public DateOnly BaseEndDate { get; }
    public MembershipCalculatedState ExpectedInitialState { get; }
    public int ExpectedInitialRemainingVisits { get; }
    public int ExpectedInitialExtensionDays { get; }
    public DateOnly ExpectedInitialEffectiveEndDate { get; }
    public MembershipIssueNegativeContext? ExistingNegativeState { get; }
    public int AutomaticCoveredNegativeVisitCount { get; }
    public IReadOnlyList<MembershipNegativeVisitCoverageCandidate> CoveredNegativeVisits { get; }
    public int RemainingExistingNegativeBalance { get; }
    public int RemainingConcreteNegativeVisitCount { get; }
    public int UnknownNegativeBalance { get; }
    public bool UsesForcedCoverageStartDate { get; }
    public bool IsAlreadyExpiredAtPreview { get; }
    public bool CanProceedToIssue { get; }
    public IReadOnlyList<MembershipWarning> Warnings { get; }
}
