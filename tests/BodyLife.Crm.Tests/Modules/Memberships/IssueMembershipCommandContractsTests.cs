using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class IssueMembershipCommandContractsTests
{
    private static readonly Guid ClientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MembershipTypeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly StartDate = new(2026, 7, 1);
    private static readonly DateTimeOffset CatalogTimestamp = new(2026, 7, 13, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CommandCarriesOnlyCanonicalSelectorsAndAuthenticatedPreviewToken()
    {
        var envelope = CreateEnvelope();
        var command = new IssueMembershipCommand(
            envelope, ClientId, MembershipTypeId, CatalogTimestamp, StartDate, "signed-preview", Guid.NewGuid());

        Assert.IsAssignableFrom<IBodyLifeCommand>(command);
        Assert.Equal("signed-preview", command.PreviewToken);
        Assert.DoesNotContain("NegativeHandlingDecision", typeof(IssueMembershipCommand).GetProperties().Select(x => x.Name));
        Assert.DoesNotContain("NegativeCoverageCount", typeof(IssueMembershipCommand).GetProperties().Select(x => x.Name));
        Assert.DoesNotContain("ExpectedOldestOpenNegativeVisitId", typeof(IssueMembershipCommand).GetProperties().Select(x => x.Name));
        Assert.Equal(new EntityId("client", ClientId), command.CanonicalRereadTargetId);
    }

    [Fact]
    public void PreparationAutomaticallyCoversOldestConcreteVisitsUpToMembershipLimit()
    {
        var first = Candidate("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", new DateOnly(2026, 6, 28));
        var second = Candidate("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", new DateOnly(2026, 6, 29));
        var third = Candidate("cccccccc-cccc-cccc-cccc-cccccccccccc", new DateOnly(2026, 6, 30));
        var negative = new MembershipIssueNegativeContext(3, first.BusinessDate, [first, second, third]);

        var preparation = MembershipIssuePreparationPolicy.Prepare(
            ClientId, CreateMembershipType(visitsLimit: 2), StartDate, negative);

        Assert.Equal(first.BusinessDate, preparation.StartDate);
        Assert.Equal([first.VisitId, second.VisitId], preparation.CoveredNegativeVisits.Select(x => x.VisitId));
        Assert.Equal(1, preparation.RemainingExistingNegativeBalance);
        Assert.Equal(0, preparation.ExpectedInitialState.RemainingVisits);
        Assert.Contains(preparation.Warnings, x => x.Code == MembershipWarningCodes.NegativeBalance);
    }

    [Fact]
    public void UnknownOpeningRemainderStaysVisibleButDoesNotBlockIssue()
    {
        var preparation = MembershipIssuePreparationPolicy.Prepare(
            ClientId, CreateMembershipType(), StartDate,
            new MembershipIssueNegativeContext(2, null));

        Assert.Empty(preparation.CoveredNegativeVisits);
        Assert.Equal(2, preparation.RemainingExistingNegativeBalance);
        Assert.Contains(preparation.Warnings, x => x.Code == MembershipWarningCodes.NegativeBalance);
    }

    [Fact]
    public void PreparationKeepsImmutableCatalogSnapshotAndInclusiveDates()
    {
        var catalog = CreateMembershipType();
        var preparation = MembershipIssuePreparationPolicy.Prepare(
            ClientId, catalog, StartDate);

        var changedCatalog = catalog with
        {
            Name = "Changed tariff",
            DurationDays = 45,
            VisitsLimit = 12,
            Price = new Money(1500m, "UAH"),
        };

        Assert.Equal("Eight visits", preparation.Snapshot.TypeName);
        Assert.Equal(30, preparation.Snapshot.DurationDays);
        Assert.Equal(8, preparation.Snapshot.VisitsLimit);
        Assert.Equal(new Money(1000m, "UAH"), preparation.Snapshot.Price);
        Assert.Equal(new DateOnly(2026, 7, 30), preparation.BaseEndDate);
        Assert.Equal("Changed tariff", changedCatalog.Name);
    }

    [Fact]
    public void PoliciesRejectInvalidIdentityAndCalendarOverflowWithoutLeakingSuccessState()
    {
        var invalidClient = Assert.Throws<ArgumentException>(() =>
            MembershipIssuePreviewPolicy.Create(Guid.Empty, CreateMembershipType(), StartDate));
        var calendarOverflow = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipIssuePreparationPolicy.Prepare(
                ClientId, CreateMembershipType(durationDays: 2), DateOnly.MaxValue));
        var failure = CommandResult.Error([
            new CommandError(CommandErrorCode.ValidationFailed, "Invalid issue request.", "startDate"),
        ]);

        Assert.Equal("clientId", invalidClient.ParamName);
        Assert.Equal("durationDays", calendarOverflow.ParamName);
        Assert.Null(failure.PrimaryEntityId);
        Assert.Null(failure.RereadTargetId);
        Assert.Empty(failure.Warnings);
    }

    private static MembershipNegativeVisitCoverageCandidate Candidate(string id, DateOnly date) => new(
        Guid.Parse(id), Guid.NewGuid(), Guid.NewGuid(),
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), date);

    private static MembershipTypeCatalogItem CreateMembershipType(int visitsLimit = 8, int durationDays = 30) => new(
        MembershipTypeId, "Eight visits", durationDays, visitsLimit, new Money(1000m, "UAH"), true,
        null, CatalogTimestamp.AddDays(-1), CatalogTimestamp, null);

    private static CommandEnvelope CreateEnvelope() => new(
        new ActorContext(AccountId.New(), ActorRole.Admin, AccountKind.NamedAdmin, SessionId.New(), "reception"),
        new RequestCorrelationId("issue-membership-contract"), EntryOrigin.Normal, null,
        "issue-membership-key", null, "Reception note");
}
