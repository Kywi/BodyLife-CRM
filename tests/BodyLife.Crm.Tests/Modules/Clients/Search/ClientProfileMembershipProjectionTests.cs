using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Clients.Search;

public sealed class ClientProfileMembershipProjectionTests
{
    private static readonly Guid ClientId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid MembershipTypeId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid CurrentMembershipId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");
    private static readonly Guid HistoricalMembershipId = Guid.Parse(
        "44444444-4444-4444-4444-444444444444");
    private static readonly DateOnly AsOfDate = new(2026, 7, 25);

    [Fact]
    public void SingleCandidateProjectsCanonicalSummaryWarningsAndTimelineOrder()
    {
        var current = CreateItem(
            CurrentMembershipId,
            new DateOnly(2026, 7, 1),
            IssuedMembershipLifecycleStatus.Active,
            remainingVisits: 1,
            issuedAt: new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero));
        var canceled = CreateItem(
            HistoricalMembershipId,
            new DateOnly(2026, 7, 20),
            IssuedMembershipLifecycleStatus.Canceled,
            remainingVisits: 6,
            issuedAt: new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero));
        var collection = ClientMembershipStatesPolicy.Create(
            ClientId,
            AsOfDate,
            [current, canceled]);

        var projection = ClientProfileMembershipProjection.Project(collection);

        Assert.NotNull(projection.CurrentMembership);
        Assert.Same(projection.Timeline[1], projection.CurrentMembership);
        Assert.Collection(
            projection.Timeline,
            summary =>
            {
                Assert.Equal(HistoricalMembershipId, summary.MembershipId);
                Assert.Equal("Eight visits / 30 days", summary.TypeNameSnapshot);
                Assert.Equal(ClientMembershipSummaryStatusCodes.Canceled, summary.Status);
                Assert.Equal(6, summary.RemainingVisits);
                Assert.Equal(new DateOnly(2026, 8, 18), summary.EffectiveEndDate);
            },
            summary =>
            {
                Assert.Equal(CurrentMembershipId, summary.MembershipId);
                Assert.Equal("Eight visits / 30 days", summary.TypeNameSnapshot);
                Assert.Equal(ClientMembershipSummaryStatusCodes.Active, summary.Status);
                Assert.Equal(1, summary.RemainingVisits);
                Assert.Equal(new DateOnly(2026, 7, 30), summary.EffectiveEndDate);
            });
        Assert.Equal(
            [MembershipWarningCodes.LowRemaining, MembershipWarningCodes.EndingSoon],
            projection.Warnings.Select(warning => warning.Code));
        Assert.Equal(
            current.State.Warnings.Select(warning => warning.Message),
            projection.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void DateExpiredLifecycleActiveMembershipIsNotProjectedAsCurrent()
    {
        var expired = CreateItem(
            HistoricalMembershipId,
            new DateOnly(2026, 6, 1),
            IssuedMembershipLifecycleStatus.Active,
            remainingVisits: 4);
        var collection = ClientMembershipStatesPolicy.Create(
            ClientId,
            AsOfDate,
            [expired]);

        var projection = ClientProfileMembershipProjection.Project(collection);

        Assert.Null(projection.CurrentMembership);
        var summary = Assert.Single(projection.Timeline);
        Assert.Equal(ClientMembershipSummaryStatusCodes.Expired, summary.Status);
        Assert.Equal(expired.State.RemainingVisits, summary.RemainingVisits);
        Assert.Equal(expired.State.EffectiveEndDate, summary.EffectiveEndDate);
        Assert.Empty(projection.Warnings);
    }

    [Fact]
    public void AmbiguousCandidatesExposeNoCurrentSummaryAndOneExplicitWarning()
    {
        var older = CreateItem(
            HistoricalMembershipId,
            new DateOnly(2026, 7, 1),
            IssuedMembershipLifecycleStatus.Active,
            remainingVisits: 5);
        var newer = CreateItem(
            CurrentMembershipId,
            new DateOnly(2026, 7, 10),
            IssuedMembershipLifecycleStatus.Active,
            remainingVisits: 3);
        var collection = ClientMembershipStatesPolicy.Create(
            ClientId,
            AsOfDate,
            [older, newer]);

        var projection = ClientProfileMembershipProjection.Project(collection);

        Assert.Null(projection.CurrentMembership);
        Assert.Equal(
            [CurrentMembershipId, HistoricalMembershipId],
            projection.Timeline.Select(summary => summary.MembershipId));
        var warning = Assert.Single(projection.Warnings);
        Assert.Equal(
            ClientProfileMembershipWarningCodes.AmbiguousCurrentMembership,
            warning.Code);
        Assert.Equal(
            "Multiple active memberships require explicit selection.",
            warning.Message);
    }

    [Fact]
    public void ProjectionCollectionsAreReadOnly()
    {
        var collection = ClientMembershipStatesPolicy.Create(
            ClientId,
            AsOfDate,
            [
                CreateItem(
                    CurrentMembershipId,
                    new DateOnly(2026, 7, 1),
                    IssuedMembershipLifecycleStatus.Active,
                    remainingVisits: 1),
            ]);

        var projection = ClientProfileMembershipProjection.Project(collection);

        var timeline = Assert.IsAssignableFrom<IList<ClientMembershipSummary>>(
            projection.Timeline);
        var warnings = Assert.IsAssignableFrom<IList<ClientWarning>>(projection.Warnings);
        Assert.True(timeline.IsReadOnly);
        Assert.True(warnings.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => timeline.Add(timeline[0]));
        Assert.Throws<NotSupportedException>(() => warnings.Add(warnings[0]));
    }

    [Fact]
    public void ExtensionExplanationsAreGroupedIntoTheirMembershipRowsAndRemainReadOnly()
    {
        var collection = ClientMembershipStatesPolicy.Create(
            ClientId,
            AsOfDate,
            [
                CreateItem(
                    CurrentMembershipId,
                    new DateOnly(2026, 7, 1),
                    IssuedMembershipLifecycleStatus.Active,
                    remainingVisits: 1),
                CreateItem(
                    HistoricalMembershipId,
                    new DateOnly(2026, 6, 1),
                    IssuedMembershipLifecycleStatus.Canceled,
                    remainingVisits: 4),
            ]);
        var currentFreeze = new MembershipExtensionExplanation(
            CurrentMembershipId,
            MembershipExtensionSourceKind.Freeze,
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            nonWorkingPeriodId: null,
            new DateRange(new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 12)),
            MembershipExtensionSourceStatus.Active,
            "Medical pause");
        var historicalApplication = new MembershipExtensionExplanation(
            HistoricalMembershipId,
            MembershipExtensionSourceKind.NonWorkingDay,
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            new DateRange(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 16)),
            MembershipExtensionSourceStatus.Corrected,
            "repair - Floor repair");

        var projection = ClientProfileMembershipProjection.Project(
            collection,
            [currentFreeze, historicalApplication]);

        var currentSummary = projection.Timeline.Single(
            summary => summary.MembershipId == CurrentMembershipId);
        Assert.Same(currentSummary, projection.CurrentMembership);
        Assert.Same(currentFreeze, Assert.Single(currentSummary.ExtensionExplanations));
        var historicalSummary = projection.Timeline.Single(
            summary => summary.MembershipId == HistoricalMembershipId);
        Assert.Same(
            historicalApplication,
            Assert.Single(historicalSummary.ExtensionExplanations));

        var explanations = Assert.IsAssignableFrom<IList<MembershipExtensionExplanation>>(
            currentSummary.ExtensionExplanations);
        Assert.True(explanations.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => explanations.Add(currentFreeze));
    }

    [Fact]
    public void RecalculationFailureContainsNoPartialProfile()
    {
        var result = GetClientProfileResult.RecalculationFailed();

        Assert.Equal(GetClientProfileStatus.RecalculationFailed, result.Status);
        Assert.Equal("recalculation_failed", result.ErrorCode);
        Assert.Null(result.Profile);
        Assert.Null(result.ErrorField);
    }

    private static ClientMembershipStateTimelineItem CreateItem(
        Guid membershipId,
        DateOnly startDate,
        IssuedMembershipLifecycleStatus lifecycleStatus,
        int remainingVisits,
        DateTimeOffset? issuedAt = null)
    {
        const int visitsLimit = 8;
        const int durationDays = 30;
        var snapshot = new IssuedMembershipSnapshot(
            "Eight visits / 30 days",
            durationDays,
            visitsLimit,
            new Money(1200m, "UAH"));
        var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
            MembershipTypeId,
            snapshot,
            startDate,
            MembershipDateRules.CalculateBaseEndDate(startDate, durationDays));
        var calculatedState = MembershipCalculatedState.FromStoredCache(
            issueTerms,
            countedVisits: visitsLimit - remainingVisits,
            remainingVisits,
            negativeBalance: 0,
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays: 0,
            issueTerms.BaseEndDate,
            lastCountedVisitAt: null);
        var state = new MembershipStateReadModel(
            membershipId,
            ClientId,
            issueTerms,
            calculatedState,
            AsOfDate);

        return new ClientMembershipStateTimelineItem(
            state,
            lifecycleStatus,
            issuedAt ?? new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero));
    }
}
