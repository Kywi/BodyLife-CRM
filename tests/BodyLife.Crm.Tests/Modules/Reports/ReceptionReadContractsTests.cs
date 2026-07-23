using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;

namespace BodyLife.Crm.Tests.Modules.Reports;

public sealed class ReceptionReadContractsTests
{
    [Fact]
    public void AmbiguousCompactStateRetainsEveryCandidateAndCannotMasqueradeAsNone()
    {
        var first = Candidate("11111111-1111-1111-1111-111111111111");
        var second = Candidate("22222222-2222-2222-2222-222222222222");

        var state = ReceptionActivityMembershipState.Create(
            ReceptionActivityMembershipSelectionStatus.Ambiguous,
            timelineState: null,
            [first, second]);

        Assert.Equal(ReceptionActivityMembershipSelectionStatus.Ambiguous, state.SelectionStatus);
        Assert.Null(state.TimelineState);
        Assert.Equal([first, second], state.Candidates);
        Assert.Throws<ArgumentException>(() => ReceptionActivityMembershipState.Create(
            ReceptionActivityMembershipSelectionStatus.None,
            timelineState: null,
            [first]));
        Assert.Throws<ArgumentException>(() => ReceptionActivityMembershipState.Create(
            ReceptionActivityMembershipSelectionStatus.Single,
            timelineState: second,
            [first]));
    }

    [Fact]
    public void AttentionAndActivityFactoriesRejectImpossiblePayloads()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GetReceptionAttentionCountsResult.Success(-1, 0));
        Assert.Throws<ArgumentException>(() => GetReceptionAttentionCountsResult.Failure(
            GetReceptionAttentionCountsStatus.Success, "error", "Error"));
        Assert.Throws<ArgumentException>(() => ReceptionActivityPage.Create(
            [new ReceptionActivityItem(
                ReceptionActivityEventType.VisitMarked,
                Guid.Empty,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Client",
                [],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                BodyLife.Crm.Application.Commands.EntryOrigin.Normal,
                false,
                false,
                ReceptionActivityMembershipState.Create(
                    ReceptionActivityMembershipSelectionStatus.None,
                    null,
                    []))],
            nextCursor: null,
            hasMore: false));
    }

    [Fact]
    public void AttentionUsesWebAgnosticDestinationKeys()
    {
        var summary = ReceptionAttentionSummary.Create(
            2,
            1,
            ReceptionAttentionDestination.EndingSoon,
            ReceptionAttentionDestination.NegativeClients);

        Assert.Equal(ReceptionAttentionDestination.EndingSoon, summary.EndingSoonReportDestination);
        Assert.Equal(ReceptionAttentionDestination.NegativeClients, summary.NegativeClientsReportDestination);
    }

    private static ReceptionActivityMembershipCandidate Candidate(string id) => new(
        Guid.Parse(id), 1, 0, new DateOnly(2026, 7, 24), []);
}
