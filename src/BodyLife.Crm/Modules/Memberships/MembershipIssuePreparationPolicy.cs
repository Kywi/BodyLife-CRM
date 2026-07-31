using BodyLife.Crm.Modules.MembershipTypes;

namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipIssuePreparationPolicy
{
    public static MembershipIssuePreparation Prepare(
        Guid clientId,
        MembershipTypeCatalogItem? membershipType,
        DateOnly startDate,
        MembershipIssueNegativeContext? existingNegativeState = null,
        MembershipNegativeHandlingDecision? negativeHandlingDecision = null,
        int? negativeCoverageCount = null,
        DateOnly? previewBusinessDate = null)
    {
        var preview = MembershipIssuePreviewPolicy.Create(
            clientId,
            membershipType,
            startDate,
            existingNegativeState,
            negativeHandlingDecision,
            negativeCoverageCount,
            previewBusinessDate);

        if (preview.RequiresNegativeHandlingDecision)
        {
            throw new ArgumentException(
                "An explicit negative handling decision is required.",
                nameof(negativeHandlingDecision));
        }

        if (!preview.CanProceedToIssue)
        {
            throw new ArgumentException(
                "The selected negative handling decision is not available.",
                nameof(negativeHandlingDecision));
        }

        return new MembershipIssuePreparation(preview);
    }
}
