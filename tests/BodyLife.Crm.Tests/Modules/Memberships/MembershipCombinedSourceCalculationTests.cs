using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipCombinedSourceCalculationTests
{
    private static readonly Guid MembershipId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset RecordedAt = new(
        2026,
        7,
        14,
        11,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void AdjustmentsEstablishBaselineBeforeDeterministicallyOrderedVisits()
    {
        var firstNegativeVisitId = Guid.Parse(
            "44444444-4444-4444-4444-444444444444");
        var finalOccurredAt = new DateTimeOffset(
            2026,
            7,
            16,
            9,
            0,
            0,
            TimeSpan.Zero);
        MembershipVisitSourceFact[] visits =
        [
            CreateVisit(firstNegativeVisitId, finalOccurredAt, RecordedAt.AddMinutes(4)),
            CreateVisit(
                Guid.Parse("11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.Zero),
                RecordedAt.AddMinutes(1)),
            CreateVisit(
                Guid.Parse("33333333-cccc-cccc-cccc-cccccccccccc"),
                finalOccurredAt,
                RecordedAt.AddMinutes(3)),
            CreateVisit(
                Guid.Parse("22222222-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero),
                RecordedAt.AddMinutes(2)),
        ];

        var state = MembershipStateCalculator.CalculateFromVisitAndAdjustmentFacts(
            MembershipId,
            CreateIssueTerms(),
            visits,
            [
                CreateAdjustment(
                    MembershipAdjustmentTypes.VisitBalance,
                    visitsDelta: 1),
                CreateAdjustment(
                    MembershipAdjustmentTypes.ExtensionDays,
                    daysDelta: 2),
            ]);

        Assert.Equal(4, state.CountedVisits);
        Assert.Equal(-1, state.RemainingVisits);
        Assert.Equal(1, state.NegativeBalance);
        Assert.Equal(firstNegativeVisitId, state.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 16), state.FirstNegativeVisitDate);
        Assert.Equal(2, state.ExtensionDays);
        Assert.Equal(new DateOnly(2026, 8, 1), state.EffectiveEndDate);
        Assert.Equal(finalOccurredAt, state.LastCountedVisitAt);
    }

    [Fact]
    public void AdjustmentCausedNegativeBaselineDoesNotInventFirstNegativeVisit()
    {
        var visit = CreateVisit(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero),
            RecordedAt.AddMinutes(1));

        var state = MembershipStateCalculator.CalculateFromVisitAndAdjustmentFacts(
            MembershipId,
            CreateIssueTerms(),
            [visit],
            [
                CreateAdjustment(
                    MembershipAdjustmentTypes.VisitBalance,
                    visitsDelta: -3),
            ]);

        Assert.Equal(1, state.CountedVisits);
        Assert.Equal(-2, state.RemainingVisits);
        Assert.Equal(2, state.NegativeBalance);
        Assert.Null(state.FirstNegativeVisitId);
        Assert.Null(state.FirstNegativeVisitDate);
        Assert.Equal(visit.OccurredAt, state.LastCountedVisitAt);
    }

    [Fact]
    public void OpeningBaselineComposesOnlyExplicitlyUncoveredAdjustmentsAndVisits()
    {
        var firstNegativeVisitId = Guid.Parse(
            "88888888-8888-8888-8888-888888888888");
        var openingState = MembershipOpeningState.FromDeclaration(
            new DateOnly(2026, 7, 13),
            declaredRemainingVisits: 1);

        var state = MembershipStateCalculator
            .CalculateFromOpeningStateVisitAndAdjustmentFacts(
                MembershipId,
                CreateIssueTerms(),
                openingState,
                [
                    CreateVisit(
                        Guid.Parse("66666666-6666-6666-6666-666666666666"),
                        new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.Zero),
                        RecordedAt.AddMinutes(1)),
                    CreateVisit(
                        Guid.Parse("77777777-7777-7777-7777-777777777777"),
                        new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero),
                        RecordedAt.AddMinutes(2)),
                    CreateVisit(
                        firstNegativeVisitId,
                        new DateTimeOffset(2026, 7, 16, 9, 0, 0, TimeSpan.Zero),
                        RecordedAt.AddMinutes(3)),
                ],
                [
                    CreateAdjustment(
                        MembershipAdjustmentTypes.VisitBalance,
                        visitsDelta: 1),
                ]);

        Assert.Equal(3, state.CountedVisits);
        Assert.Equal(-1, state.RemainingVisits);
        Assert.Equal(1, state.NegativeBalance);
        Assert.Equal(firstNegativeVisitId, state.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 16), state.FirstNegativeVisitDate);
    }

    private static MembershipIssueTerms CreateIssueTerms()
    {
        var snapshot = new IssuedMembershipSnapshot(
            "Combined source membership",
            durationDays: 30,
            visitsLimit: 2,
            new Money(1000m, "UAH"));

        return MembershipIssueTerms.FromIssuedSnapshot(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            snapshot,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30));
    }

    private static MembershipVisitSourceFact CreateVisit(
        Guid visitId,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt)
    {
        return new MembershipVisitSourceFact(
            MembershipId,
            visitId,
            DateOnly.FromDateTime(occurredAt.DateTime),
            occurredAt,
            recordedAt,
            MembershipVisitSourceStatus.Active);
    }

    private static MembershipAdjustmentSourceFact CreateAdjustment(
        string adjustmentType,
        int? daysDelta = null,
        int? visitsDelta = null)
    {
        return new MembershipAdjustmentSourceFact(
            MembershipId,
            Guid.NewGuid(),
            adjustmentType,
            daysDelta,
            visitsDelta,
            moneyDelta: null,
            new DateOnly(2026, 7, 14),
            MembershipAdjustmentSourceStatus.Active);
    }
}
