namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipIssuePreview
{
    internal MembershipIssuePreview(
        Guid clientId,
        DateTimeOffset membershipTypeUpdatedAt,
        MembershipIssueTerms issueTerms,
        MembershipCalculatedState expectedInitialState,
        MembershipIssueNegativeContext? existingNegativeState,
        MembershipNegativeHandlingDecision? selectedNegativeHandlingDecision,
        int? selectedNegativeCoverageCount,
        bool negativeCoverageSelectionIsValid,
        DateOnly? previewBusinessDate,
        IEnumerable<MembershipNegativeHandlingOption> negativeHandlingOptions,
        IEnumerable<MembershipWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);
        ArgumentNullException.ThrowIfNull(expectedInitialState);
        ArgumentNullException.ThrowIfNull(negativeHandlingOptions);
        ArgumentNullException.ThrowIfNull(warnings);

        var optionItems = negativeHandlingOptions.ToArray();
        var warningItems = warnings.ToArray();

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
        SelectedNegativeHandlingDecision = selectedNegativeHandlingDecision;
        SelectedNegativeCoverageCount = selectedNegativeCoverageCount;
        ExpectedOldestOpenNegativeVisitId =
            existingNegativeState?.OldestOpenConcreteVisitId;
        CoveredNegativeVisitCount = selectedNegativeHandlingDecision
                == MembershipNegativeHandlingDecision.CoverWithNewMembership
            && negativeCoverageSelectionIsValid
            ? selectedNegativeCoverageCount!.Value
            : 0;
        RemainingExistingNegativeBalance = existingNegativeState is null
            ? 0
            : existingNegativeState.NegativeBalance - CoveredNegativeVisitCount;
        UsesForcedCoverageStartDate = selectedNegativeHandlingDecision
                == MembershipNegativeHandlingDecision.CoverWithNewMembership
            && existingNegativeState?.OldestOpenConcreteVisitDate is not null;
        IsAlreadyExpiredAtPreview = UsesForcedCoverageStartDate
            && previewBusinessDate is { } asOfDate
            && expectedInitialState.EffectiveEndDate < asOfDate;
        NegativeHandlingOptions = Array.AsReadOnly(optionItems);
        Warnings = Array.AsReadOnly(warningItems);
        RequiresNegativeHandlingDecision = existingNegativeState is not null
            && selectedNegativeHandlingDecision is null;
        CanProceedToIssue = existingNegativeState is null
            || selectedNegativeHandlingDecision is { } selectedDecision
            && optionItems.Any(option =>
                option.Decision == selectedDecision
                && option.IsAvailable)
            && (selectedDecision
                    != MembershipNegativeHandlingDecision.CoverWithNewMembership
                || negativeCoverageSelectionIsValid);
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

    public MembershipNegativeHandlingDecision? SelectedNegativeHandlingDecision { get; }

    public int? SelectedNegativeCoverageCount { get; }

    public Guid? ExpectedOldestOpenNegativeVisitId { get; }

    public int CoveredNegativeVisitCount { get; }

    public int RemainingExistingNegativeBalance { get; }

    public bool UsesForcedCoverageStartDate { get; }

    public bool IsAlreadyExpiredAtPreview { get; }

    public IReadOnlyList<MembershipNegativeHandlingOption> NegativeHandlingOptions { get; }

    public IReadOnlyList<MembershipWarning> Warnings { get; }

    public bool RequiresNegativeHandlingDecision { get; }

    public bool CanProceedToIssue { get; }
}
