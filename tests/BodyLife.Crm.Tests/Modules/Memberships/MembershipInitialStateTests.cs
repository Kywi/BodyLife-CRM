using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipInitialStateTests
{
    private static readonly DateTimeOffset CatalogTimestamp = new(
        2026,
        7,
        13,
        10,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void NewIssueInitializesCanonicalCalculatedState()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 2);

        var state = MembershipStateCalculator.CalculateInitial(issueTerms);

        Assert.Equal(0, state.CountedVisits);
        Assert.Equal(2, state.RemainingVisits);
        Assert.Equal(0, state.NegativeBalance);
        Assert.Null(state.FirstNegativeVisitId);
        Assert.Null(state.FirstNegativeVisitDate);
        Assert.Equal(0, state.ExtensionDays);
        Assert.Equal(issueTerms.BaseEndDate, state.EffectiveEndDate);
        Assert.Null(state.LastCountedVisitAt);
    }

    [Fact]
    public void ZeroVisitMembershipKeepsDateActivitySeparateFromVisitBalance()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 0);

        var state = MembershipStateCalculator.CalculateInitial(issueTerms);

        Assert.Equal(0, state.RemainingVisits);
        Assert.Equal(0, state.NegativeBalance);
        Assert.True(state.IsActiveByDate(issueTerms.BaseEndDate));
    }

    [Fact]
    public void ActiveByDateIncludesEffectiveEndButNotFollowingDay()
    {
        var state = MembershipStateCalculator.CalculateInitial(
            CreateIssueTerms(visitsLimit: 2));

        Assert.True(state.IsActiveByDate(new DateOnly(2026, 7, 30)));
        Assert.False(state.IsActiveByDate(new DateOnly(2026, 7, 31)));
    }

    [Fact]
    public void InitialCalculationRequiresIssueTerms()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MembershipStateCalculator.CalculateInitial(issueTerms: null));
    }

    [Fact]
    public void CalculatedStateCannotBePubliclyConstructedOrMutated()
    {
        var properties = typeof(MembershipCalculatedState).GetProperties();
        var remainingVisits = Assert.Single(
            properties,
            property => property.Name == nameof(MembershipCalculatedState.RemainingVisits));
        var effectiveEndDate = Assert.Single(
            properties,
            property => property.Name == nameof(MembershipCalculatedState.EffectiveEndDate));

        Assert.Empty(typeof(MembershipCalculatedState).GetConstructors());
        Assert.All(
            properties,
            property => Assert.False(property.SetMethod?.IsPublic == true));
        Assert.Equal(typeof(int), remainingVisits.PropertyType);
        Assert.Null(effectiveEndDate.SetMethod);
        Assert.DoesNotContain(properties, property => property.Name == "IsActive");
    }

    private static MembershipIssueTerms CreateIssueTerms(int visitsLimit)
    {
        var membershipType = new MembershipTypeCatalogItem(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Slice membership",
            DurationDays: 30,
            visitsLimit,
            new Money(1000m, "UAH"),
            IsActive: true,
            Comment: null,
            CreatedAt: CatalogTimestamp.AddDays(-1),
            UpdatedAt: CatalogTimestamp,
            DeactivatedAt: null);

        return MembershipIssueTerms.FromActiveMembershipType(
            membershipType,
            new DateOnly(2026, 7, 1));
    }
}
