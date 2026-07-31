namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipVisitCoverageResolver
{
    public static IReadOnlyList<MembershipVisitSourceFact> ResolveEffectiveVisits(
        Guid membershipId,
        IEnumerable<MembershipVisitSourceFact>? originalVisitFacts,
        IEnumerable<MembershipNegativeCoverageSourceFact>? coverageFacts)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException("Membership id is required.", nameof(membershipId));
        }

        ArgumentNullException.ThrowIfNull(originalVisitFacts);
        ArgumentNullException.ThrowIfNull(coverageFacts);

        var originals = originalVisitFacts.ToArray();
        if (originals.Any(fact => fact is null || fact.MembershipId != membershipId))
        {
            throw new ArgumentException(
                "Original Visit facts must belong to the selected Membership.",
                nameof(originalVisitFacts));
        }

        var facts = coverageFacts.ToArray();
        if (facts.Any(fact => fact is null))
        {
            throw new ArgumentException(
                "Negative coverage facts cannot contain a missing item.",
                nameof(coverageFacts));
        }

        if (facts.Select(fact => fact.ClosureItemId).Distinct().Count() != facts.Length)
        {
            throw new ArgumentException(
                "Each negative coverage item id must be unique.",
                nameof(coverageFacts));
        }

        var activeFacts = facts.Where(fact => fact.IsActive).ToArray();
        if (activeFacts.Select(fact => fact.VisitId).Distinct().Count()
            != activeFacts.Length)
        {
            throw new ArgumentException(
                "A Visit cannot have more than one active negative coverage item.",
                nameof(coverageFacts));
        }

        var outboundVisitIds = activeFacts
            .Where(fact => fact.SourceMembershipId == membershipId)
            .Select(fact => fact.VisitId)
            .ToHashSet();
        var effective = originals
            .Where(fact => !outboundVisitIds.Contains(fact.VisitId))
            .ToList();

        effective.AddRange(activeFacts
            .Where(fact => fact.CoveringMembershipId == membershipId)
            .Select(fact => new MembershipVisitSourceFact(
                membershipId,
                fact.VisitId,
                fact.BusinessDate,
                fact.OccurredAt,
                fact.RecordedAt,
                MembershipVisitSourceStatus.Active)));

        if (effective.Select(fact => fact.VisitId).Distinct().Count() != effective.Count)
        {
            throw new ArgumentException(
                "Effective Membership Visit facts must have unique Visit ids.",
                nameof(coverageFacts));
        }

        return Array.AsReadOnly(effective.ToArray());
    }
}
