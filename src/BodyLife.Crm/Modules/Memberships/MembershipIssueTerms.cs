using BodyLife.Crm.Modules.MembershipTypes;

namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipIssueTerms
{
    private MembershipIssueTerms(
        Guid membershipTypeId,
        IssuedMembershipSnapshot snapshot,
        DateOnly startDate)
    {
        MembershipTypeId = membershipTypeId;
        Snapshot = snapshot;
        StartDate = startDate;
        BaseEndDate = MembershipDateRules.CalculateBaseEndDate(
            startDate,
            snapshot.DurationDays);
    }

    public Guid MembershipTypeId { get; }

    public IssuedMembershipSnapshot Snapshot { get; }

    public DateOnly StartDate { get; }

    public DateOnly BaseEndDate { get; }

    public static MembershipIssueTerms FromActiveMembershipType(
        MembershipTypeCatalogItem? membershipType,
        DateOnly startDate)
    {
        ArgumentNullException.ThrowIfNull(membershipType);

        if (membershipType.MembershipTypeId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership type id is required.",
                nameof(membershipType));
        }

        if (!membershipType.IsAvailableForOrdinaryIssue)
        {
            throw new InvalidOperationException(
                "Inactive membership types cannot be used for ordinary issue.");
        }

        var snapshot = new IssuedMembershipSnapshot(
            membershipType.Name,
            membershipType.DurationDays,
            membershipType.VisitsLimit,
            membershipType.Price);

        return new MembershipIssueTerms(
            membershipType.MembershipTypeId,
            snapshot,
            startDate);
    }

    public static MembershipIssueTerms FromIssuedSnapshot(
        Guid membershipTypeId,
        IssuedMembershipSnapshot? snapshot,
        DateOnly startDate,
        DateOnly baseEndDate)
    {
        if (membershipTypeId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership type id is required.",
                nameof(membershipTypeId));
        }

        ArgumentNullException.ThrowIfNull(snapshot);

        var terms = new MembershipIssueTerms(
            membershipTypeId,
            snapshot,
            startDate);

        if (terms.BaseEndDate != baseEndDate)
        {
            throw new ArgumentException(
                "Base end date must match the issued snapshot and inclusive duration.",
                nameof(baseEndDate));
        }

        return terms;
    }
}
