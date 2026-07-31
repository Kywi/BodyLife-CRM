using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Pages.Reception;

namespace BodyLife.Crm.Web.Tests.Pages.Reception;

public sealed class NegativeVisitCoveragePanelViewModelTests
{
    [Fact]
    public void CanonicalCorrectionInputUsesTheClosureOldestTokenAndHasNoPreselection()
    {
        var clientId = Guid.NewGuid();
        var closureOldestVisitId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var updatedAt = DateTimeOffset.UtcNow;
        var coverage = new ClientNegativeVisitCoverageReadModel(
            clientId, 2, 0, DateOnly.FromDateTime(DateTime.UtcNow), [],
            [new OneOffMembershipTypeReadModel(typeId, "One-off", 1, 1, new Money(100, "UAH"), updatedAt)],
            [new NegativeVisitCoverageClosureReadModel(
                Guid.NewGuid(), "one_off", null, null, closureOldestVisitId, 1, null,
                updatedAt, updatedAt, Guid.NewGuid(), Guid.NewGuid(), "normal", "active", [], [], null)]);

        var model = NegativeVisitCoveragePanelViewModel.FromCanonical(
            GetClientNegativeVisitCoverageResult.Succeeded(coverage, QueryPermissionSet.Empty),
            new ReceptionSearchContext("card", default, false, "cursor"));

        var input = Assert.Single(model.CorrectionInputs);
        Assert.Equal(closureOldestVisitId, input.ExpectedOldestOpenNegativeVisitId);
        Assert.Null(input.Mode);
        Assert.Null(input.ReplacementNewMembershipCoverageCount);
        Assert.Equal(0, Assert.Single(input.ReplacementOneOffLines!).Quantity);
        Assert.Equal("card", input.SearchQuery);
    }

    [Fact]
    public void UnsafeCanonicalReadFailsClosed()
    {
        var model = NegativeVisitCoveragePanelViewModel.FromCanonical(
            GetClientNegativeVisitCoverageResult.CanonicalStateInvalid(),
            new ReceptionSearchContext(null, default, false, null));

        Assert.False(model.IsSafe);
        Assert.Empty(model.CorrectionInputs);
    }
}
