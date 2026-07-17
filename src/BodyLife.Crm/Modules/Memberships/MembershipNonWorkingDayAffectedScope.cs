using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipNonWorkingDayAffectedScope
{
    public MembershipNonWorkingDayAffectedScope(
        DateRange period,
        IEnumerable<MembershipNonWorkingDayAffectedScopeItem> affectedMemberships)
    {
        ArgumentNullException.ThrowIfNull(affectedMemberships);

        var items = affectedMemberships.ToArray();
        if (items.Any(item => item is null))
        {
            throw new ArgumentException(
                "Affected Membership scope cannot contain a missing item.",
                nameof(affectedMemberships));
        }

        if (items.Any(item => item.AppliedRange != period))
        {
            throw new ArgumentException(
                "Every affected Membership must preserve the full NonWorkingDay period.",
                nameof(affectedMemberships));
        }

        if (items.Select(item => item.MembershipId).Distinct().Count() != items.Length)
        {
            throw new ArgumentException(
                "Affected Membership scope cannot contain duplicate Memberships.",
                nameof(affectedMemberships));
        }

        if (!items.SequenceEqual(items.OrderBy(item => item.MembershipId)))
        {
            throw new ArgumentException(
                "Affected Membership scope must use deterministic Membership id order.",
                nameof(affectedMemberships));
        }

        Period = period;
        AffectedMemberships = Array.AsReadOnly(items);
    }

    public DateRange Period { get; }

    public IReadOnlyList<MembershipNonWorkingDayAffectedScopeItem> AffectedMemberships { get; }

    public int AffectedCount => AffectedMemberships.Count;
}

public sealed class MembershipNonWorkingDayAffectedScopeItem
{
    public MembershipNonWorkingDayAffectedScopeItem(
        Guid membershipId,
        Guid clientId,
        DateRange appliedRange)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        MembershipId = membershipId;
        ClientId = clientId;
        AppliedRange = appliedRange;
    }

    public Guid MembershipId { get; }

    public Guid ClientId { get; }

    public DateRange AppliedRange { get; }
}
