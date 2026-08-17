using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipIssuePreviewContractsTests
{
    private static readonly Guid ClientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MembershipTypeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly ProposedStartDate = new(2026, 7, 1);
    private static readonly DateTimeOffset CatalogTimestamp = new(2026, 7, 13, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void QueryCarriesOnlyActorAndCanonicalIssueSelectors()
    {
        var query = new PreviewIssueMembershipQuery(CreateActor(), ClientId, MembershipTypeId, ProposedStartDate);

        Assert.IsAssignableFrom<IBodyLifeQuery<PreviewIssueMembershipResult>>(query);
        Assert.DoesNotContain("NegativeHandlingDecision", typeof(PreviewIssueMembershipQuery).GetProperties().Select(x => x.Name));
        Assert.DoesNotContain("NegativeCoverageCount", typeof(PreviewIssueMembershipQuery).GetProperties().Select(x => x.Name));
    }

    [Fact]
    public void PreviewAutomaticallyAllocatesConcreteVisitsOldestFirst()
    {
        var first = Candidate("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", new DateOnly(2026, 6, 28));
        var second = Candidate("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", new DateOnly(2026, 6, 29));
        var third = Candidate("cccccccc-cccc-cccc-cccc-cccccccccccc", new DateOnly(2026, 6, 30));
        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId, CreateMembershipType(visitsLimit: 2), ProposedStartDate,
            new MembershipIssueNegativeContext(3, first.BusinessDate, [third, second, first]));

        Assert.Equal(first.BusinessDate, preview.ProposedStartDate);
        Assert.Equal(2, preview.AutomaticCoveredNegativeVisitCount);
        Assert.Equal([first.VisitId, second.VisitId], preview.CoveredNegativeVisits.Select(x => x.VisitId));
        Assert.Equal(1, preview.RemainingExistingNegativeBalance);
        Assert.True(preview.CanProceedToIssue);
        Assert.Contains(preview.Warnings, x => x.Code == MembershipWarningCodes.NegativeBalance);
        Assert.DoesNotContain("NegativeHandlingOptions", typeof(MembershipIssuePreview).GetProperties().Select(x => x.Name));
        Assert.DoesNotContain("RequiresNegativeHandlingDecision", typeof(MembershipIssuePreview).GetProperties().Select(x => x.Name));
    }

    [Fact]
    public void PreviewLeavesUnknownOpeningBalanceVisibleWithoutSynthesizingCoverage()
    {
        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId, CreateMembershipType(), ProposedStartDate,
            new MembershipIssueNegativeContext(2, null));

        Assert.Equal(0, preview.AutomaticCoveredNegativeVisitCount);
        Assert.Equal(2, preview.UnknownNegativeBalance);
        Assert.Equal(2, preview.RemainingExistingNegativeBalance);
        Assert.True(preview.CanProceedToIssue);
        Assert.Contains(preview.Warnings, x => x.Code == MembershipWarningCodes.NegativeBalance);
    }

    [Fact]
    public void NoNegativePreviewUsesCatalogSnapshotAndRequestedStartDate()
    {
        var preview = MembershipIssuePreviewPolicy.Create(ClientId, CreateMembershipType(), ProposedStartDate);

        Assert.Equal("Eight visits", preview.Snapshot.TypeName);
        Assert.Equal(8, preview.Snapshot.VisitsLimit);
        Assert.Equal(ProposedStartDate, preview.ProposedStartDate);
        Assert.Equal(new DateOnly(2026, 7, 30), preview.BaseEndDate);
        Assert.Equal(8, preview.ExpectedInitialRemainingVisits);
        Assert.Empty(preview.CoveredNegativeVisits);
        Assert.Empty(preview.Warnings);
    }

    [Fact]
    public void FullCoverageLeavesUnusedCapacityAndNoNegativeWarning()
    {
        var first = Candidate("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", new DateOnly(2026, 6, 28));
        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId, CreateMembershipType(visitsLimit: 3), ProposedStartDate,
            new MembershipIssueNegativeContext(1, first.BusinessDate, [first]));

        Assert.Equal(1, preview.AutomaticCoveredNegativeVisitCount);
        Assert.Equal(0, preview.RemainingExistingNegativeBalance);
        Assert.Equal(2, preview.ExpectedInitialRemainingVisits);
        Assert.DoesNotContain(preview.Warnings, x => x.Code == MembershipWarningCodes.NegativeBalance);
    }

    [Fact]
    public void ZeroLimitCannotIssueWhenConcreteNegativeVisitsExist()
    {
        var first = Candidate("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", new DateOnly(2026, 6, 28));
        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId, CreateMembershipType(visitsLimit: 0), ProposedStartDate,
            new MembershipIssueNegativeContext(1, first.BusinessDate, [first]));

        Assert.False(preview.CanProceedToIssue);
        Assert.Equal(0, preview.AutomaticCoveredNegativeVisitCount);
        Assert.Throws<ArgumentException>(() => MembershipIssuePreparationPolicy.Prepare(
            ClientId, CreateMembershipType(visitsLimit: 0), ProposedStartDate,
            new MembershipIssueNegativeContext(1, first.BusinessDate, [first])));
    }

    [Fact]
    public void AutomaticBackdateCanWarnThatMembershipIsAlreadyExpired()
    {
        var first = Candidate("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", new DateOnly(2026, 6, 1));
        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId, CreateMembershipType(visitsLimit: 1), ProposedStartDate,
            new MembershipIssueNegativeContext(1, first.BusinessDate, [first]),
            previewBusinessDate: new DateOnly(2026, 8, 1));

        Assert.True(preview.IsAlreadyExpiredAtPreview);
        Assert.Contains(preview.Warnings, x => x.Code == MembershipWarningCodes.ExpiredByDate);
    }

    [Fact]
    public void MixedConcreteAndUnknownNegativeCoversConcreteAndKeepsUnknownRemainder()
    {
        var first = Candidate("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", new DateOnly(2026, 6, 28));
        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId, CreateMembershipType(visitsLimit: 1), ProposedStartDate,
            new MembershipIssueNegativeContext(2, first.BusinessDate, [first]));

        Assert.Equal(1, preview.AutomaticCoveredNegativeVisitCount);
        Assert.Equal(1, preview.UnknownNegativeBalance);
        Assert.Equal(1, preview.RemainingExistingNegativeBalance);
        Assert.Contains(preview.Warnings, x => x.Code == MembershipWarningCodes.NegativeBalance);
    }

    private static MembershipNegativeVisitCoverageCandidate Candidate(string id, DateOnly date) => new(
        Guid.Parse(id), Guid.NewGuid(), Guid.NewGuid(),
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), date);

    private static MembershipTypeCatalogItem CreateMembershipType(int visitsLimit = 8) => new(
        MembershipTypeId, "Eight visits", 30, visitsLimit, new Money(1000m, "UAH"), true,
        null, CatalogTimestamp.AddDays(-1), CatalogTimestamp, null);

    private static ActorContext CreateActor() => new(
        AccountId.New(), ActorRole.Admin, AccountKind.NamedAdmin, SessionId.New(), "reception");
}
