using BodyLife.Crm.Modules.MembershipTypes;

namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipIssuePreparationPolicy
{
    public static MembershipIssuePreparation Prepare(
        Guid clientId,
        MembershipTypeCatalogItem? membershipType,
        DateOnly startDate,
        MembershipIssueNegativeContext? existingNegativeState = null,
        DateOnly? previewBusinessDate = null)
    {
        var preview = MembershipIssuePreviewPolicy.Create(
            clientId,
            membershipType,
            startDate,
            existingNegativeState,
            previewBusinessDate);

        if (!preview.CanProceedToIssue)
        {
            throw new ArgumentException(
                "The selected membership type has no capacity for the current concrete negative Visits.",
                nameof(membershipType));
        }

        return new MembershipIssuePreparation(preview);
    }
}
