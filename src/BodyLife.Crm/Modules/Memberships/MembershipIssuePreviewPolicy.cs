using BodyLife.Crm.Modules.MembershipTypes;

namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipIssuePreviewPolicy
{
    public static MembershipIssuePreview Create(
        Guid clientId,
        MembershipTypeCatalogItem? membershipType,
        DateOnly proposedStartDate,
        MembershipIssueNegativeContext? existingNegativeState = null,
        MembershipNegativeHandlingDecision? negativeHandlingDecision = null)
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

        var issueTerms = MembershipIssueTerms.FromActiveMembershipType(
            membershipType,
            proposedStartDate);
        var expectedInitialState = MembershipStateCalculator.CalculateInitial(issueTerms);

        if (existingNegativeState is null)
        {
            return new MembershipIssuePreview(
                clientId,
                issueTerms,
                expectedInitialState,
                existingNegativeState: null,
                selectedNegativeHandlingDecision: null,
                negativeHandlingOptions: [],
                warnings: []);
        }

        // Coverage and explicit closure remain deferred until their workflows are accepted.
        MembershipNegativeHandlingOption[] options =
        [
            new(MembershipNegativeHandlingDecision.LeaveVisible, isAvailable: true),
            new(MembershipNegativeHandlingDecision.CoverWithNewMembership, isAvailable: false),
            new(MembershipNegativeHandlingDecision.RecordExplicitClosure, isAvailable: false),
        ];
        MembershipWarning[] warnings =
        [
            new(
                MembershipWarningCodes.NegativeBalance,
                MembershipWarningSeverity.Danger,
                "Client has negative visits. Check the start date of the new membership."),
        ];

        return new MembershipIssuePreview(
            clientId,
            issueTerms,
            expectedInitialState,
            existingNegativeState,
            negativeHandlingDecision,
            options,
            warnings);
    }
}
