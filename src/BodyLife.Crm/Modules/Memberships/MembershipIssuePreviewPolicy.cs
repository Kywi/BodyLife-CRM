using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipIssuePreviewPolicy
{
    public static MembershipIssuePreview Create(
        Guid clientId,
        MembershipTypeCatalogItem? membershipType,
        DateOnly proposedStartDate,
        MembershipIssueNegativeContext? existingNegativeState = null,
        MembershipNegativeHandlingDecision? negativeHandlingDecision = null,
        int? negativeCoverageCount = null,
        DateOnly? previewBusinessDate = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        ArgumentNullException.ThrowIfNull(membershipType);

        if (negativeHandlingDecision is { } selectedDecision
            && !Enum.IsDefined(selectedDecision))
        {
            throw new ArgumentOutOfRangeException(
                nameof(negativeHandlingDecision),
                selectedDecision,
                "Negative handling decision is not supported.");
        }

        if (existingNegativeState is null && negativeHandlingDecision is not null)
        {
            throw new ArgumentException(
                "A negative handling decision requires existing negative membership state.",
                nameof(negativeHandlingDecision));
        }

        if (negativeCoverageCount is not null
            && negativeHandlingDecision
                != MembershipNegativeHandlingDecision.CoverWithNewMembership)
        {
            throw new ArgumentException(
                "Negative coverage count requires new-Membership coverage.",
                nameof(negativeCoverageCount));
        }

        if (previewBusinessDate is { } asOfDate
            && !BusinessTimeZone.IsSupportedBusinessDate(asOfDate))
        {
            throw new ArgumentOutOfRangeException(
                nameof(previewBusinessDate),
                previewBusinessDate,
                "Preview business date is outside the supported range.");
        }

        var isCoverage = negativeHandlingDecision
            == MembershipNegativeHandlingDecision.CoverWithNewMembership;
        var forcedCoverageStartDate = isCoverage
            ? existingNegativeState?.OldestOpenConcreteVisitDate
            : null;

        var issueTerms = MembershipIssueTerms.FromActiveMembershipType(
            membershipType,
            forcedCoverageStartDate ?? proposedStartDate);
        var coverageSelectionIsValid = isCoverage
            && negativeCoverageCount is { } selectedCoverageCount
            && selectedCoverageCount >= 1
            && selectedCoverageCount <= issueTerms.Snapshot.VisitsLimit
            && selectedCoverageCount
                <= (existingNegativeState?.OpenConcreteVisitCount ?? 0);
        var expectedInitialState = coverageSelectionIsValid
            ? MembershipStateCalculator.CalculateInitialWithCoveredVisits(
                issueTerms,
                existingNegativeState!.OpenConcreteVisits.Take(
                    negativeCoverageCount!.Value))
            : MembershipStateCalculator.CalculateInitial(issueTerms);

        if (existingNegativeState is null)
        {
            return new MembershipIssuePreview(
                clientId,
                membershipType.UpdatedAt,
                issueTerms,
                expectedInitialState,
                existingNegativeState: null,
                selectedNegativeHandlingDecision: null,
                selectedNegativeCoverageCount: null,
                negativeCoverageSelectionIsValid: true,
                previewBusinessDate: previewBusinessDate,
                negativeHandlingOptions: [],
                warnings: []);
        }

        var canCoverWithNewMembership =
            existingNegativeState.OpenConcreteVisitCount > 0
            && issueTerms.Snapshot.VisitsLimit > 0;
        MembershipNegativeHandlingOption[] options =
        [
            new(MembershipNegativeHandlingDecision.LeaveVisible, isAvailable: true),
            new(
                MembershipNegativeHandlingDecision.CoverWithNewMembership,
                canCoverWithNewMembership),
            new(MembershipNegativeHandlingDecision.RecordExplicitClosure, isAvailable: false),
        ];
        MembershipWarning[] warnings =
        [
            new(
                MembershipWarningCodes.NegativeBalance,
                MembershipWarningSeverity.Danger,
                "Client has negative visits. Check the start date of the new membership."),
        ];
        if (coverageSelectionIsValid
            && previewBusinessDate is { } currentBusinessDate
            && expectedInitialState.EffectiveEndDate < currentBusinessDate)
        {
            warnings =
            [
                .. warnings,
                new MembershipWarning(
                    MembershipWarningCodes.ExpiredByDate,
                    MembershipWarningSeverity.Danger,
                    "The backdated covering membership will already be expired."),
            ];
        }

        return new MembershipIssuePreview(
            clientId,
            membershipType.UpdatedAt,
            issueTerms,
            expectedInitialState,
            existingNegativeState,
            negativeHandlingDecision,
            negativeCoverageCount,
            coverageSelectionIsValid,
            previewBusinessDate,
            options,
            warnings);
    }
}
