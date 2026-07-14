namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipVisitEligibilityPolicy
{
    public static MembershipVisitEligibility Evaluate(
        Guid membershipId,
        MembershipIssueTerms? issueTerms,
        MembershipCalculatedState? calculatedState,
        IssuedMembershipLifecycleStatus lifecycleStatus,
        DateOnly visitDate,
        IEnumerable<MembershipVisitFreezeSource>? freezeSources)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        ArgumentNullException.ThrowIfNull(issueTerms);
        ArgumentNullException.ThrowIfNull(calculatedState);
        ArgumentNullException.ThrowIfNull(freezeSources);

        if (!Enum.IsDefined(lifecycleStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifecycleStatus),
                lifecycleStatus,
                "Issued membership lifecycle status is not supported.");
        }

        var sources = ValidateFreezeSources(membershipId, freezeSources);

        if (lifecycleStatus != IssuedMembershipLifecycleStatus.Active)
        {
            return Ineligible(
                membershipId,
                MembershipVisitEligibilityStatus.MembershipInactive);
        }

        if (visitDate < issueTerms.StartDate)
        {
            return Ineligible(
                membershipId,
                MembershipVisitEligibilityStatus.BeforeMembershipStart);
        }

        if (sources.Any(source => source.IsActive && source.Range.Contains(visitDate)))
        {
            return Ineligible(
                membershipId,
                MembershipVisitEligibilityStatus.DuringActiveFreeze);
        }

        var requiredAcknowledgements = new List<MembershipVisitAcknowledgement>();
        if (visitDate > calculatedState.EffectiveEndDate)
        {
            requiredAcknowledgements.Add(MembershipVisitAcknowledgement.Expired);
        }

        if (calculatedState.RemainingVisits == 0)
        {
            requiredAcknowledgements.Add(MembershipVisitAcknowledgement.ZeroRemaining);
        }
        else if (calculatedState.RemainingVisits < 0)
        {
            requiredAcknowledgements.Add(MembershipVisitAcknowledgement.NegativeRemaining);
        }

        return new MembershipVisitEligibility(
            membershipId,
            MembershipVisitEligibilityStatus.Eligible,
            requiredAcknowledgements);
    }

    private static MembershipVisitFreezeSource[] ValidateFreezeSources(
        Guid membershipId,
        IEnumerable<MembershipVisitFreezeSource> freezeSources)
    {
        var sources = freezeSources.ToArray();
        var freezeIds = new HashSet<Guid>();

        foreach (var source in sources)
        {
            if (source is null)
            {
                throw new ArgumentException(
                    "Visit Freeze sources cannot contain a missing item.",
                    nameof(freezeSources));
            }

            if (source.MembershipId != membershipId)
            {
                throw new ArgumentException(
                    "Visit Freeze sources must belong to the selected membership.",
                    nameof(freezeSources));
            }

            if (!freezeIds.Add(source.FreezeId))
            {
                throw new ArgumentException(
                    "Each Visit Freeze source id must be unique.",
                    nameof(freezeSources));
            }
        }

        return sources;
    }

    private static MembershipVisitEligibility Ineligible(
        Guid membershipId,
        MembershipVisitEligibilityStatus status)
    {
        return new MembershipVisitEligibility(
            membershipId,
            status,
            requiredAcknowledgements: []);
    }
}
