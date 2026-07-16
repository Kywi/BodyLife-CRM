using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipFreezeEligibilityPolicy
{
    public static MembershipFreezeEligibility Evaluate(
        MembershipStateReadModel state,
        IssuedMembershipLifecycleStatus lifecycleStatus,
        DateRange range,
        IEnumerable<MembershipVisitSourceFact>? visitSources)
    {
        ArgumentNullException.ThrowIfNull(state);

        var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
            state.MembershipTypeId,
            state.Snapshot,
            state.StartDate,
            state.BaseEndDate);
        var calculatedState = MembershipCalculatedState.FromStoredCache(
            issueTerms,
            state.CountedVisits,
            state.RemainingVisits,
            state.NegativeBalance,
            state.FirstNegativeVisitId,
            state.FirstNegativeVisitDate,
            state.ExtensionDays,
            state.EffectiveEndDate,
            state.LastCountedVisitAt);

        return Evaluate(
            state.MembershipId,
            issueTerms,
            calculatedState,
            lifecycleStatus,
            range,
            visitSources);
    }

    public static MembershipFreezeEligibility Evaluate(
        Guid membershipId,
        MembershipIssueTerms? issueTerms,
        MembershipCalculatedState? calculatedState,
        IssuedMembershipLifecycleStatus lifecycleStatus,
        DateRange range,
        IEnumerable<MembershipVisitSourceFact>? visitSources)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        ArgumentNullException.ThrowIfNull(issueTerms);
        ArgumentNullException.ThrowIfNull(calculatedState);
        ArgumentNullException.ThrowIfNull(visitSources);

        if (!Enum.IsDefined(lifecycleStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifecycleStatus),
                lifecycleStatus,
                "Issued membership lifecycle status is not supported.");
        }

        var sources = ValidateVisitSources(membershipId, visitSources);

        if (lifecycleStatus != IssuedMembershipLifecycleStatus.Active)
        {
            return Ineligible(
                membershipId,
                range,
                MembershipFreezeEligibilityStatus.MembershipInactive);
        }

        if (range.StartDate < issueTerms.StartDate)
        {
            return Ineligible(
                membershipId,
                range,
                MembershipFreezeEligibilityStatus.BeforeMembershipStart);
        }

        if (range.StartDate > calculatedState.EffectiveEndDate)
        {
            return Ineligible(
                membershipId,
                range,
                MembershipFreezeEligibilityStatus.AfterMembershipEffectiveEnd);
        }

        if (sources.Any(source =>
                source.IsActiveCounted && range.Contains(source.BusinessDate)))
        {
            return Ineligible(
                membershipId,
                range,
                MembershipFreezeEligibilityStatus.ConflictsWithActiveVisit);
        }

        return new MembershipFreezeEligibility(
            membershipId,
            range,
            MembershipFreezeEligibilityStatus.Eligible);
    }

    private static MembershipVisitSourceFact[] ValidateVisitSources(
        Guid membershipId,
        IEnumerable<MembershipVisitSourceFact> visitSources)
    {
        var sources = visitSources.ToArray();
        var visitIds = new HashSet<Guid>();

        foreach (var source in sources)
        {
            if (source is null)
            {
                throw new ArgumentException(
                    "Membership Visit sources cannot contain a missing item.",
                    nameof(visitSources));
            }

            if (source.MembershipId != membershipId)
            {
                throw new ArgumentException(
                    "Membership Visit sources must belong to the selected membership.",
                    nameof(visitSources));
            }

            if (!visitIds.Add(source.VisitId))
            {
                throw new ArgumentException(
                    "Each Membership Visit source id must be unique.",
                    nameof(visitSources));
            }
        }

        return sources;
    }

    private static MembershipFreezeEligibility Ineligible(
        Guid membershipId,
        DateRange range,
        MembershipFreezeEligibilityStatus status)
    {
        return new MembershipFreezeEligibility(
            membershipId,
            range,
            status);
    }
}
