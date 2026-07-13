using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipCalculatedStateStoredCacheTests
{
    private static readonly Guid MembershipTypeId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid FirstNegativeVisitId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly BaseEndDate = new(2026, 7, 30);
    private static readonly DateOnly EffectiveEndDate = new(2026, 8, 3);

    [Fact]
    public void StoredCacheFactoryRehydratesCanonicalCalculatedState()
    {
        var lastCountedVisitAt = new DateTimeOffset(
            2026,
            7,
            13,
            8,
            30,
            0,
            TimeSpan.Zero);

        var state = MembershipCalculatedState.FromStoredCache(
            CreateIssueTerms(),
            countedVisits: 10,
            remainingVisits: -2,
            negativeBalance: 2,
            firstNegativeVisitId: FirstNegativeVisitId,
            firstNegativeVisitDate: new DateOnly(2026, 7, 12),
            extensionDays: 4,
            effectiveEndDate: EffectiveEndDate,
            lastCountedVisitAt: lastCountedVisitAt);

        Assert.Equal(10, state.CountedVisits);
        Assert.Equal(-2, state.RemainingVisits);
        Assert.Equal(2, state.NegativeBalance);
        Assert.Equal(FirstNegativeVisitId, state.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 12), state.FirstNegativeVisitDate);
        Assert.Equal(4, state.ExtensionDays);
        Assert.Equal(EffectiveEndDate, state.EffectiveEndDate);
        Assert.Equal(lastCountedVisitAt, state.LastCountedVisitAt);
    }

    [Theory]
    [InlineData(-1, -2, 2, 4, "countedVisits")]
    [InlineData(10, -2, 1, 4, "negativeBalance")]
    [InlineData(10, int.MinValue, int.MaxValue, 4, "remainingVisits")]
    [InlineData(10, -2, 2, -1, "extensionDays")]
    public void StoredCacheFactoryRejectsInvalidNumericInvariants(
        int countedVisits,
        int remainingVisits,
        int negativeBalance,
        int extensionDays,
        string expectedParameter)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            MembershipCalculatedState.FromStoredCache(
                CreateIssueTerms(),
                countedVisits,
                remainingVisits,
                negativeBalance,
                firstNegativeVisitId: null,
                firstNegativeVisitDate: null,
                extensionDays,
                effectiveEndDate: EffectiveEndDate,
                lastCountedVisitAt: null));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    [Fact]
    public void StoredCacheFactoryRejectsEffectiveEndDateDrift()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipCalculatedState.FromStoredCache(
                CreateIssueTerms(),
                countedVisits: 10,
                remainingVisits: -2,
                negativeBalance: 2,
                firstNegativeVisitId: null,
                firstNegativeVisitDate: null,
                extensionDays: 4,
                effectiveEndDate: EffectiveEndDate.AddDays(-1),
                lastCountedVisitAt: null));

        Assert.Equal("effectiveEndDate", exception.ParamName);
    }

    [Fact]
    public void StoredCacheFactoryRejectsUnsupportedCalendarExtension()
    {
        var snapshot = new IssuedMembershipSnapshot(
            "Final calendar day",
            durationDays: 1,
            visitsLimit: 1,
            new Money(1m, "UAH"));
        var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
            MembershipTypeId,
            snapshot,
            DateOnly.MaxValue,
            DateOnly.MaxValue);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipCalculatedState.FromStoredCache(
                issueTerms,
                countedVisits: 0,
                remainingVisits: 1,
                negativeBalance: 0,
                firstNegativeVisitId: null,
                firstNegativeVisitDate: null,
                extensionDays: 1,
                effectiveEndDate: DateOnly.MaxValue,
                lastCountedVisitAt: null));

        Assert.Equal("extensionDays", exception.ParamName);
    }

    [Fact]
    public void StoredCacheFactoryRequiresIssueTerms()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            MembershipCalculatedState.FromStoredCache(
                issueTerms: null,
                countedVisits: 0,
                remainingVisits: 1,
                negativeBalance: 0,
                firstNegativeVisitId: null,
                firstNegativeVisitDate: null,
                extensionDays: 0,
                effectiveEndDate: BaseEndDate,
                lastCountedVisitAt: null));

        Assert.Equal("issueTerms", exception.ParamName);
    }

    private static MembershipIssueTerms CreateIssueTerms()
    {
        var snapshot = new IssuedMembershipSnapshot(
            "Eight visits",
            durationDays: 30,
            visitsLimit: 8,
            new Money(1000m, "UAH"));

        return MembershipIssueTerms.FromIssuedSnapshot(
            MembershipTypeId,
            snapshot,
            startDate: new DateOnly(2026, 7, 1),
            baseEndDate: BaseEndDate);
    }
}
