using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipIssueTermsTests
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
    public void ActiveCatalogTypeCreatesCopiedSnapshotAndInclusiveBaseEndDate()
    {
        var membershipType = CreateMembershipType();

        var terms = MembershipIssueTerms.FromActiveMembershipType(
            membershipType,
            new DateOnly(2026, 7, 1));

        Assert.Equal(membershipType.MembershipTypeId, terms.MembershipTypeId);
        Assert.Equal("Slice 2 visits / 30 days", terms.Snapshot.TypeName);
        Assert.Equal(30, terms.Snapshot.DurationDays);
        Assert.Equal(2, terms.Snapshot.VisitsLimit);
        Assert.Equal(new Money(1000m, "UAH"), terms.Snapshot.Price);
        Assert.Equal(new DateOnly(2026, 7, 1), terms.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 30), terms.BaseEndDate);
    }

    [Fact]
    public void CopiedSnapshotDoesNotChangeWithLaterCatalogValues()
    {
        var membershipType = CreateMembershipType();
        var terms = MembershipIssueTerms.FromActiveMembershipType(
            membershipType,
            new DateOnly(2026, 7, 1));

        var editedCatalogType = membershipType with
        {
            Name = "Future 12 visits / 60 days",
            DurationDays = 60,
            VisitsLimit = 12,
            Price = new Money(1800m, "UAH"),
            UpdatedAt = CatalogTimestamp.AddHours(1),
        };

        Assert.Equal("Slice 2 visits / 30 days", terms.Snapshot.TypeName);
        Assert.Equal(30, terms.Snapshot.DurationDays);
        Assert.Equal(2, terms.Snapshot.VisitsLimit);
        Assert.Equal(new Money(1000m, "UAH"), terms.Snapshot.Price);
        Assert.Equal(new DateOnly(2026, 7, 30), terms.BaseEndDate);
        Assert.NotEqual(editedCatalogType.Name, terms.Snapshot.TypeName);
        Assert.NotEqual(editedCatalogType.Price, terms.Snapshot.Price);
    }

    [Fact]
    public void InactiveCatalogTypeCannotCreateOrdinaryIssueTerms()
    {
        var membershipType = CreateMembershipType() with
        {
            IsActive = false,
            DeactivatedAt = CatalogTimestamp,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MembershipIssueTerms.FromActiveMembershipType(
                membershipType,
                new DateOnly(2026, 7, 1)));

        Assert.Equal(
            "Inactive membership types cannot be used for ordinary issue.",
            exception.Message);
    }

    [Fact]
    public void MembershipTypeIdentityIsRequired()
    {
        var membershipType = CreateMembershipType() with
        {
            MembershipTypeId = Guid.Empty,
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipIssueTerms.FromActiveMembershipType(
                membershipType,
                new DateOnly(2026, 7, 1)));

        Assert.Equal("membershipType", exception.ParamName);
    }

    [Fact]
    public void SnapshotValuesUseCatalogValidation()
    {
        var snapshot = new IssuedMembershipSnapshot(
            "  Slice   type  ",
            durationDays: 30,
            visitsLimit: 2,
            new Money(1000m, "uah"));

        Assert.Equal("Slice type", snapshot.TypeName);
        Assert.Equal(new Money(1000m, "UAH"), snapshot.Price);

        Assert.Throws<ArgumentException>(() =>
            new IssuedMembershipSnapshot(
                "   ",
                durationDays: 30,
                visitsLimit: 2,
                new Money(1000m, "UAH")));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IssuedMembershipSnapshot(
                "Slice type",
                durationDays: 0,
                visitsLimit: 2,
                new Money(1000m, "UAH")));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IssuedMembershipSnapshot(
                "Slice type",
                durationDays: 30,
                visitsLimit: -1,
                new Money(1000m, "UAH")));
    }

    [Fact]
    public void IssueTermsExposeNoMutableCatalogReferenceOrEditableEndDate()
    {
        var termsProperties = typeof(MembershipIssueTerms).GetProperties();
        var snapshotProperties = typeof(IssuedMembershipSnapshot).GetProperties();

        Assert.All(
            termsProperties,
            property => Assert.False(property.SetMethod?.IsPublic == true));
        Assert.All(
            snapshotProperties,
            property => Assert.False(property.SetMethod?.IsPublic == true));
        Assert.DoesNotContain(
            termsProperties,
            property => property.PropertyType == typeof(MembershipTypeCatalogItem));
        Assert.DoesNotContain(
            termsProperties,
            property => property.Name == "EffectiveEndDate");
    }

    private static MembershipTypeCatalogItem CreateMembershipType()
    {
        return new MembershipTypeCatalogItem(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Slice 2 visits / 30 days",
            30,
            2,
            new Money(1000m, "UAH"),
            IsActive: true,
            Comment: "Vertical slice type",
            CreatedAt: CatalogTimestamp.AddDays(-1),
            UpdatedAt: CatalogTimestamp,
            DeactivatedAt: null);
    }
}
