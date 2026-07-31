using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipVisitCoverageResolverTests
{
    private static readonly Guid SourceMembershipId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid CoveringMembershipId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid[] VisitIds =
    [
        Guid.Parse("00000000-0000-0000-0000-000000000001"),
        Guid.Parse("00000000-0000-0000-0000-000000000002"),
        Guid.Parse("00000000-0000-0000-0000-000000000003"),
        Guid.Parse("00000000-0000-0000-0000-000000000004"),
    ];

    [Fact]
    public void PartialOutboundCoverageMovesFirstNegativeVisitToOldestRemainder()
    {
        var originals = CreateOriginalVisits();
        var effective = MembershipVisitCoverageResolver.ResolveEffectiveVisits(
            SourceMembershipId,
            originals,
            [CreateCoverage(index: 2, coveringMembershipId: null)]);

        var state = MembershipStateCalculator.CalculateFromVisitFacts(
            SourceMembershipId,
            CreateIssueTerms(visitsLimit: 2),
            effective);

        Assert.Equal(3, state.CountedVisits);
        Assert.Equal(-1, state.RemainingVisits);
        Assert.Equal(1, state.NegativeBalance);
        Assert.Equal(VisitIds[3], state.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 4), state.FirstNegativeVisitDate);
    }

    [Fact]
    public void FullOutboundCoverageClearsNegativeStateWithoutDeletingOriginalFacts()
    {
        var originals = CreateOriginalVisits();
        var effective = MembershipVisitCoverageResolver.ResolveEffectiveVisits(
            SourceMembershipId,
            originals,
            [
                CreateCoverage(index: 2, coveringMembershipId: null),
                CreateCoverage(index: 3, coveringMembershipId: null),
            ]);

        var state = MembershipStateCalculator.CalculateFromVisitFacts(
            SourceMembershipId,
            CreateIssueTerms(visitsLimit: 2),
            effective);

        Assert.Equal(4, originals.Length);
        Assert.Equal(2, state.CountedVisits);
        Assert.Equal(0, state.RemainingVisits);
        Assert.Equal(0, state.NegativeBalance);
        Assert.Null(state.FirstNegativeVisitId);
        Assert.Null(state.FirstNegativeVisitDate);
    }

    [Fact]
    public void NewMembershipCoverageConsumesInboundLimitAndKeepsUnusedRemainder()
    {
        var coverageFacts = new[]
        {
            CreateCoverage(index: 2, CoveringMembershipId),
            CreateCoverage(index: 3, CoveringMembershipId),
        };
        var effective = MembershipVisitCoverageResolver.ResolveEffectiveVisits(
            CoveringMembershipId,
            originalVisitFacts: [],
            coverageFacts);

        var state = MembershipStateCalculator.CalculateFromVisitFacts(
            CoveringMembershipId,
            CreateIssueTerms(visitsLimit: 3),
            effective);

        Assert.Equal(2, state.CountedVisits);
        Assert.Equal(1, state.RemainingVisits);
        Assert.Equal(0, state.NegativeBalance);
        Assert.Null(state.FirstNegativeVisitId);
        Assert.Equal(At(day: 4, hour: 10), state.LastCountedVisitAt);
    }

    [Fact]
    public void CanceledCoverageRestoresOutboundAndRemovesInboundEffects()
    {
        var canceledCoverage = CreateCoverage(
            index: 2,
            CoveringMembershipId,
            MembershipNegativeCoverageSourceStatus.Canceled);
        var sourceEffective = MembershipVisitCoverageResolver.ResolveEffectiveVisits(
            SourceMembershipId,
            CreateOriginalVisits(),
            [canceledCoverage]);
        var coveringEffective = MembershipVisitCoverageResolver.ResolveEffectiveVisits(
            CoveringMembershipId,
            originalVisitFacts: [],
            [canceledCoverage]);

        var sourceState = MembershipStateCalculator.CalculateFromVisitFacts(
            SourceMembershipId,
            CreateIssueTerms(visitsLimit: 2),
            sourceEffective);
        var coveringState = MembershipStateCalculator.CalculateFromVisitFacts(
            CoveringMembershipId,
            CreateIssueTerms(visitsLimit: 3),
            coveringEffective);

        Assert.Equal(4, sourceState.CountedVisits);
        Assert.Equal(-2, sourceState.RemainingVisits);
        Assert.Equal(VisitIds[2], sourceState.FirstNegativeVisitId);
        Assert.Equal(0, coveringState.CountedVisits);
        Assert.Equal(3, coveringState.RemainingVisits);
    }

    [Fact]
    public void ResolverRejectsConcurrentActiveCoverageForTheSameVisit()
    {
        var first = CreateCoverage(index: 2, coveringMembershipId: null);
        var duplicate = new MembershipNegativeCoverageSourceFact(
            Guid.NewGuid(),
            first.VisitId,
            first.SourceMembershipId,
            coveringMembershipId: null,
            first.BusinessDate,
            first.OccurredAt,
            first.RecordedAt.AddMinutes(1),
            MembershipNegativeCoverageSourceStatus.Active);

        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipVisitCoverageResolver.ResolveEffectiveVisits(
                SourceMembershipId,
                CreateOriginalVisits(),
                [first, duplicate]));

        Assert.Equal("coverageFacts", exception.ParamName);
    }

    private static MembershipVisitSourceFact[] CreateOriginalVisits()
    {
        return VisitIds
            .Select((visitId, index) => new MembershipVisitSourceFact(
                SourceMembershipId,
                visitId,
                new DateOnly(2026, 7, index + 1),
                At(index + 1, hour: 10),
                At(index + 1, hour: 11),
                MembershipVisitSourceStatus.Active))
            .ToArray();
    }

    private static MembershipNegativeCoverageSourceFact CreateCoverage(
        int index,
        Guid? coveringMembershipId,
        MembershipNegativeCoverageSourceStatus status =
            MembershipNegativeCoverageSourceStatus.Active)
    {
        return new MembershipNegativeCoverageSourceFact(
            Guid.Parse($"aaaaaaaa-aaaa-aaaa-aaaa-{index + 1:000000000000}"),
            VisitIds[index],
            SourceMembershipId,
            coveringMembershipId,
            new DateOnly(2026, 7, index + 1),
            At(index + 1, hour: 10),
            At(index + 1, hour: 12),
            status);
    }

    private static MembershipIssueTerms CreateIssueTerms(int visitsLimit)
    {
        return MembershipIssueTerms.FromIssuedSnapshot(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            new IssuedMembershipSnapshot(
                "Coverage membership",
                durationDays: 30,
                visitsLimit,
                new Money(1000m, "UAH")),
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30));
    }

    private static DateTimeOffset At(int day, int hour)
    {
        return new DateTimeOffset(2026, 7, day, hour, 0, 0, TimeSpan.Zero);
    }
}
