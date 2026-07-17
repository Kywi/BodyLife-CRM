using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Clients.Search;

public static class ClientProfileMembershipProjection
{
    private static readonly IReadOnlyList<ClientWarning> AmbiguousCurrentMembershipWarnings
        = Array.AsReadOnly(
        [
            new ClientWarning(
                ClientProfileMembershipWarningCodes.AmbiguousCurrentMembership,
                "Multiple active memberships require explicit selection."),
        ]);

    public static ClientProfileMembershipArea Project(
        ClientMembershipStatesReadModel stateCollection,
        IEnumerable<MembershipExtensionExplanation>? extensionExplanations = null)
    {
        ArgumentNullException.ThrowIfNull(stateCollection);

        var explanationItems = extensionExplanations?.ToArray() ?? [];
        if (explanationItems.Any(item => item is null))
        {
            throw new ArgumentException(
                "Extension explanations cannot contain a missing item.",
                nameof(extensionExplanations));
        }

        var membershipIds = stateCollection.Timeline
            .Select(item => item.State.MembershipId)
            .ToHashSet();
        if (explanationItems.Any(item => !membershipIds.Contains(item.MembershipId)))
        {
            throw new ArgumentException(
                "Extension explanations must belong to a projected Membership.",
                nameof(extensionExplanations));
        }

        var explanationsByMembershipId = explanationItems.ToLookup(
            explanation => explanation.MembershipId);
        var timeline = stateCollection.Timeline
            .Select(item => ProjectSummary(
                item,
                explanationsByMembershipId[item.State.MembershipId]))
            .ToArray();
        var readOnlyTimeline = Array.AsReadOnly(timeline);
        var selection = stateCollection.ActiveCandidateSelection;

        return selection.Status switch
        {
            ActiveMembershipCandidateStatus.None => new ClientProfileMembershipArea(
                CurrentMembership: null,
                readOnlyTimeline,
                Warnings: []),
            ActiveMembershipCandidateStatus.Single => ProjectSingleCandidate(
                selection,
                timeline,
                readOnlyTimeline),
            ActiveMembershipCandidateStatus.Ambiguous => new ClientProfileMembershipArea(
                CurrentMembership: null,
                readOnlyTimeline,
                AmbiguousCurrentMembershipWarnings),
            _ => throw new InvalidOperationException(
                $"Unsupported active membership candidate status '{selection.Status}'."),
        };
    }

    private static ClientProfileMembershipArea ProjectSingleCandidate(
        ActiveMembershipCandidateSelection selection,
        IReadOnlyList<ClientMembershipSummary> timeline,
        IReadOnlyList<ClientMembershipSummary> readOnlyTimeline)
    {
        var candidate = selection.SingleCandidate
            ?? throw new InvalidOperationException(
                "A single active membership selection must contain its candidate.");
        var currentMembership = timeline.Single(
            item => item.MembershipId == candidate.State.MembershipId);
        var warnings = Array.AsReadOnly(
            candidate.State.Warnings
                .Select(warning => new ClientWarning(warning.Code, warning.Message))
                .ToArray());

        return new ClientProfileMembershipArea(
            currentMembership,
            readOnlyTimeline,
            warnings);
    }

    private static ClientMembershipSummary ProjectSummary(
        ClientMembershipStateTimelineItem item,
        IEnumerable<MembershipExtensionExplanation> extensionExplanations)
    {
        var readOnlyExplanations = Array.AsReadOnly(extensionExplanations.ToArray());

        return new ClientMembershipSummary(
            item.State.MembershipId,
            item.State.Snapshot.TypeName,
            MapStatus(item),
            item.State.RemainingVisits,
            item.State.EffectiveEndDate,
            readOnlyExplanations);
    }

    private static string MapStatus(ClientMembershipStateTimelineItem item)
    {
        return item.LifecycleStatus switch
        {
            IssuedMembershipLifecycleStatus.Active when item.State.IsActiveByDate
                => ClientMembershipSummaryStatusCodes.Active,
            IssuedMembershipLifecycleStatus.Active
                => ClientMembershipSummaryStatusCodes.Expired,
            IssuedMembershipLifecycleStatus.Canceled
                => ClientMembershipSummaryStatusCodes.Canceled,
            IssuedMembershipLifecycleStatus.Corrected
                => ClientMembershipSummaryStatusCodes.Corrected,
            _ => throw new InvalidOperationException(
                $"Unsupported issued membership lifecycle status '{item.LifecycleStatus}'."),
        };
    }
}
