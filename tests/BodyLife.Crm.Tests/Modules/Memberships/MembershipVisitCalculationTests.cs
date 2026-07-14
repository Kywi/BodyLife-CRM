using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipVisitCalculationTests
{
    private static readonly Guid MembershipId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid ForeignMembershipId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid VisitId1 = Guid.Parse(
        "00000000-0000-0000-0000-000000000001");
    private static readonly Guid VisitId2 = Guid.Parse(
        "00000000-0000-0000-0000-000000000002");
    private static readonly Guid VisitId3 = Guid.Parse(
        "00000000-0000-0000-0000-000000000003");
    private static readonly Guid VisitId4 = Guid.Parse(
        "00000000-0000-0000-0000-000000000004");

    [Fact]
    public void NativeHistoryUsesOccurredRecordedAndStableIdOrdering()
    {
        var firstRecorded = At(day: 3, hour: 10, minute: 30);
        var tiedRecorded = At(day: 3, hour: 11);
        var facts = new[]
        {
            CreateFact(VisitId4, At(day: 4, hour: 9), At(day: 4, hour: 9, minute: 1)),
            CreateFact(VisitId3, At(day: 3, hour: 10), tiedRecorded),
            CreateFact(VisitId2, At(day: 3, hour: 10), tiedRecorded),
            CreateFact(VisitId1, At(day: 3, hour: 10), firstRecorded),
        };

        var state = MembershipStateCalculator.CalculateFromVisitFacts(
            MembershipId,
            CreateIssueTerms(visitsLimit: 2),
            facts);

        Assert.Equal(4, state.CountedVisits);
        Assert.Equal(-2, state.RemainingVisits);
        Assert.Equal(2, state.NegativeBalance);
        Assert.Equal(VisitId3, state.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 3), state.FirstNegativeVisitDate);
        Assert.Equal(facts[0].OccurredAt, state.LastCountedVisitAt);
    }

    [Fact]
    public void StableVisitIdIsFinalTieBreakForFirstNegativeVisit()
    {
        var occurredAt = At(day: 3, hour: 10);
        var recordedAt = At(day: 3, hour: 11);

        var state = MembershipStateCalculator.CalculateFromVisitFacts(
            MembershipId,
            CreateIssueTerms(visitsLimit: 1),
            [
                CreateFact(VisitId3, occurredAt, recordedAt),
                CreateFact(VisitId1, occurredAt, recordedAt),
                CreateFact(VisitId2, occurredAt, recordedAt),
            ]);

        Assert.Equal(VisitId2, state.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 3), state.FirstNegativeVisitDate);
    }

    [Fact]
    public void CancelingFirstNegativeVisitMovesTransitionToNextActiveFact()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 2);
        var activeFacts = new[]
        {
            CreateFact(VisitId1, At(day: 1, hour: 10), At(day: 1, hour: 11)),
            CreateFact(VisitId2, At(day: 2, hour: 10), At(day: 2, hour: 11)),
            CreateFact(VisitId3, At(day: 3, hour: 10), At(day: 3, hour: 11)),
            CreateFact(VisitId4, At(day: 4, hour: 10), At(day: 4, hour: 11)),
        };
        var canceledFacts = new[]
        {
            activeFacts[0],
            activeFacts[1],
            CreateFact(
                VisitId3,
                activeFacts[2].OccurredAt,
                activeFacts[2].RecordedAt,
                MembershipVisitSourceStatus.Canceled),
            activeFacts[3],
        };

        var beforeCancellation = MembershipStateCalculator.CalculateFromVisitFacts(
            MembershipId,
            issueTerms,
            activeFacts);
        var afterCancellation = MembershipStateCalculator.CalculateFromVisitFacts(
            MembershipId,
            issueTerms,
            canceledFacts);

        Assert.Equal(VisitId3, beforeCancellation.FirstNegativeVisitId);
        Assert.Equal(4, beforeCancellation.CountedVisits);
        Assert.Equal(-2, beforeCancellation.RemainingVisits);
        Assert.Equal(VisitId4, afterCancellation.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 4), afterCancellation.FirstNegativeVisitDate);
        Assert.Equal(3, afterCancellation.CountedVisits);
        Assert.Equal(-1, afterCancellation.RemainingVisits);
        Assert.Equal(1, afterCancellation.NegativeBalance);
        Assert.Equal(activeFacts[3].OccurredAt, afterCancellation.LastCountedVisitAt);
    }

    [Fact]
    public void PositiveOpeningBaselineAppliesOnlyExplicitlyUncoveredVisitFacts()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var openingState = MembershipOpeningState.FromDeclaration(
            new DateOnly(2026, 7, 13),
            declaredRemainingVisits: 1,
            knownExtensionDays: 4);
        var uncoveredFacts = new[]
        {
            CreateFact(VisitId1, At(day: 14, hour: 10), At(day: 14, hour: 11)),
            CreateFact(VisitId2, At(day: 15, hour: 10), At(day: 15, hour: 11)),
        };

        var state = MembershipStateCalculator.CalculateFromOpeningStateAndVisitFacts(
            MembershipId,
            issueTerms,
            openingState,
            uncoveredFacts);

        Assert.Equal(2, state.CountedVisits);
        Assert.Equal(-1, state.RemainingVisits);
        Assert.Equal(1, state.NegativeBalance);
        Assert.Equal(VisitId2, state.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 15), state.FirstNegativeVisitDate);
        Assert.Equal(4, state.ExtensionDays);
        Assert.Equal(new DateOnly(2026, 8, 3), state.EffectiveEndDate);
        Assert.Equal(uncoveredFacts[1].OccurredAt, state.LastCountedVisitAt);
    }

    [Fact]
    public void NegativeOpeningBaselineKeepsUnknownHistoricalFirstNegativeMetadata()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var openingState = MembershipOpeningState.FromDeclaration(
            new DateOnly(2026, 7, 13),
            declaredRemainingVisits: -2);

        var state = MembershipStateCalculator.CalculateFromOpeningStateAndVisitFacts(
            MembershipId,
            issueTerms,
            openingState,
            [
                CreateFact(VisitId1, At(day: 14, hour: 10), At(day: 14, hour: 11)),
                CreateFact(VisitId2, At(day: 15, hour: 10), At(day: 15, hour: 11)),
            ]);

        Assert.Equal(2, state.CountedVisits);
        Assert.Equal(-4, state.RemainingVisits);
        Assert.Equal(4, state.NegativeBalance);
        Assert.Null(state.FirstNegativeVisitId);
        Assert.Null(state.FirstNegativeVisitDate);
    }

    [Fact]
    public void CalculationRejectsDuplicateAndForeignVisitFacts()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var fact = CreateFact(VisitId1, At(day: 3, hour: 10), At(day: 3, hour: 11));
        var foreignFact = CreateFact(
            VisitId2,
            At(day: 4, hour: 10),
            At(day: 4, hour: 11),
            membershipId: ForeignMembershipId);

        var duplicate = Assert.Throws<ArgumentException>(() =>
            MembershipStateCalculator.CalculateFromVisitFacts(
                MembershipId,
                issueTerms,
                [fact, fact]));
        var foreign = Assert.Throws<ArgumentException>(() =>
            MembershipStateCalculator.CalculateFromVisitFacts(
                MembershipId,
                issueTerms,
                [foreignFact]));

        Assert.Equal("visitFacts", duplicate.ParamName);
        Assert.Equal("visitFacts", foreign.ParamName);
    }

    [Fact]
    public void SourceContractsRejectMissingIdentifiersAndUnsupportedStatus()
    {
        var occurredAt = At(day: 3, hour: 10);
        var recordedAt = At(day: 3, hour: 11);

        var missingMembership = Assert.Throws<ArgumentException>(() =>
            new MembershipVisitSourceFact(
                Guid.Empty,
                VisitId1,
                new DateOnly(2026, 7, 3),
                occurredAt,
                recordedAt,
                MembershipVisitSourceStatus.Active));
        var missingVisit = Assert.Throws<ArgumentException>(() =>
            new MembershipVisitSourceFact(
                MembershipId,
                Guid.Empty,
                new DateOnly(2026, 7, 3),
                occurredAt,
                recordedAt,
                MembershipVisitSourceStatus.Active));
        var unsupportedStatus = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MembershipVisitSourceFact(
                MembershipId,
                VisitId1,
                new DateOnly(2026, 7, 3),
                occurredAt,
                recordedAt,
                (MembershipVisitSourceStatus)999));

        Assert.Equal("membershipId", missingMembership.ParamName);
        Assert.Equal("visitId", missingVisit.ParamName);
        Assert.Equal("status", unsupportedStatus.ParamName);
    }

    [Fact]
    public void VisitCalculationRejectsUnrepresentableNegativeBalance()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var openingState = MembershipOpeningState.FromDeclaration(
            new DateOnly(2026, 7, 13),
            declaredRemainingVisits: -int.MaxValue);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipStateCalculator.CalculateFromOpeningStateAndVisitFacts(
                MembershipId,
                issueTerms,
                openingState,
                [CreateFact(VisitId1, At(day: 14, hour: 10), At(day: 14, hour: 11))]));

        Assert.Equal("visitFactsNotIncludedInOpeningState", exception.ParamName);
    }

    private static MembershipIssueTerms CreateIssueTerms(int visitsLimit)
    {
        var snapshot = new IssuedMembershipSnapshot(
            "Visit calculation membership",
            durationDays: 30,
            visitsLimit,
            new Money(1000m, "UAH"));

        return MembershipIssueTerms.FromIssuedSnapshot(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            snapshot,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30));
    }

    private static MembershipVisitSourceFact CreateFact(
        Guid visitId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        MembershipVisitSourceStatus status = MembershipVisitSourceStatus.Active,
        Guid? membershipId = null)
    {
        return new MembershipVisitSourceFact(
            membershipId ?? MembershipId,
            visitId,
            DateOnly.FromDateTime(occurredAt.DateTime),
            occurredAt,
            recordedAt,
            status);
    }

    private static DateTimeOffset At(int day, int hour, int minute = 0)
    {
        return new DateTimeOffset(
            2026,
            7,
            day,
            hour,
            minute,
            0,
            TimeSpan.Zero);
    }
}
