using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipNonWorkingDayImpactPreparation
{
    public MembershipNonWorkingDayImpactPreparation(
        MembershipNonWorkingDayAffectedScope affectedScope,
        IEnumerable<MembershipNonWorkingDayImpactItem> affectedMemberships)
    {
        ArgumentNullException.ThrowIfNull(affectedScope);
        ArgumentNullException.ThrowIfNull(affectedMemberships);

        var impactItems = affectedMemberships.ToArray();
        if (impactItems.Any(item => item is null))
        {
            throw new ArgumentException(
                "NonWorkingDay impact cannot contain a missing Membership item.",
                nameof(affectedMemberships));
        }

        if (impactItems.Length != affectedScope.AffectedCount)
        {
            throw new ArgumentException(
                "NonWorkingDay impact must match the exact affected Membership scope.",
                nameof(affectedMemberships));
        }

        for (var index = 0; index < impactItems.Length; index++)
        {
            var scopeItem = affectedScope.AffectedMemberships[index];
            var impactItem = impactItems[index];
            if (impactItem.MembershipId != scopeItem.MembershipId
                || impactItem.ClientId != scopeItem.ClientId
                || impactItem.AppliedRange != scopeItem.AppliedRange)
            {
                throw new ArgumentException(
                    "NonWorkingDay impact order and identities must match the exact scope.",
                    nameof(affectedMemberships));
            }
        }

        AffectedScope = affectedScope;
        AffectedMemberships = Array.AsReadOnly(impactItems);
    }

    public DateRange Period => AffectedScope.Period;

    public MembershipNonWorkingDayAffectedScope AffectedScope { get; }

    public IReadOnlyList<MembershipNonWorkingDayImpactItem> AffectedMemberships { get; }

    public int AffectedCount => AffectedMemberships.Count;
}

public sealed class MembershipNonWorkingDayImpactItem
{
    public MembershipNonWorkingDayImpactItem(
        Guid membershipId,
        Guid clientId,
        DateRange appliedRange,
        MembershipNonWorkingDayImpactEstimate estimate)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException("Membership id is required.", nameof(membershipId));
        }

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        ArgumentNullException.ThrowIfNull(estimate);

        MembershipId = membershipId;
        ClientId = clientId;
        AppliedRange = appliedRange;
        Estimate = estimate;
    }

    public Guid MembershipId { get; }

    public Guid ClientId { get; }

    public DateRange AppliedRange { get; }

    public MembershipNonWorkingDayImpactEstimate Estimate { get; }
}
