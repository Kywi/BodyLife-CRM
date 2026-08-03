using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Pages.Reception;

namespace BodyLife.Crm.Web.Tests.Pages.Reception;

public sealed class IssuedMembershipSaleCorrectionFormViewModelTests
{
    [Fact]
    public void InitialHasNoModeOrReplacementPreselectionAndCannotSubmit()
    {
        var model = IssuedMembershipSaleCorrectionFormViewModel.Initial(Guid.NewGuid(), Guid.NewGuid(), [], SuccessPreview([]), new(null, default, false, null), DateTime.UtcNow);

        Assert.Null(model.Input.Mode);
        Assert.Null(model.Input.ReplacementMembershipTypeId);
        Assert.Null(model.Input.ReplacementStartDate);
        Assert.False(model.CanSubmit);
    }

    [Fact]
    public void MatchingPreviewWithoutDependenciesCanSubmitOnlyAfterConfirmation()
    {
        var input = new IssuedMembershipSaleCorrectionFormInput
        {
            ClientId = Guid.NewGuid(),
            OriginalMembershipId = Guid.NewGuid(),
            Mode = IssuedMembershipSaleCorrectionMode.Cancel,
            Reason = "mistake",
            OccurredAtLocal = DateTime.UtcNow,
            Confirmed = true,
            IdempotencyKey = "key",
        };
        var model = IssuedMembershipSaleCorrectionFormViewModel.FromSubmission(input, [], SuccessPreview([]), []);

        Assert.NotNull(model.Input.ExpectedDependencyToken);
        Assert.True(model.HasConfirmedPreview);
        Assert.True(model.CanSubmit);
    }

    [Fact]
    public void DependenciesBlockSubmission()
    {
        var input = new IssuedMembershipSaleCorrectionFormInput
        {
            ClientId = Guid.NewGuid(),
            OriginalMembershipId = Guid.NewGuid(),
            Mode = IssuedMembershipSaleCorrectionMode.Cancel,
            Reason = "mistake",
            OccurredAtLocal = DateTime.UtcNow,
            Confirmed = true,
            IdempotencyKey = "key",
        };
        var model = IssuedMembershipSaleCorrectionFormViewModel.FromSubmission(
            input,
            [],
            SuccessPreview(
            [
                new IssuedMembershipSaleDependency(
                    "visit",
                    Guid.NewGuid(),
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    "counted:visit"),
            ]),
            []);

        Assert.False(model.HasConfirmedPreview);
        Assert.False(model.CanSubmit);
    }

    [Fact]
    public void ReplacementPreviewNormalizesServerTypeVersionStartAndToken()
    {
        var selectedTypeId = Guid.NewGuid();
        var expectedVersion = new DateTimeOffset(
            2026,
            8,
            3,
            10,
            0,
            0,
            TimeSpan.Zero);
        var startDate = new DateOnly(2026, 8, 4);
        var input = new IssuedMembershipSaleCorrectionFormInput
        {
            ClientId = Guid.NewGuid(),
            OriginalMembershipId = Guid.NewGuid(),
            Mode = IssuedMembershipSaleCorrectionMode.Replace,
            ReplacementMembershipTypeId = selectedTypeId,
            ReplacementStartDate = startDate,
            Reason = "Wrong type",
            OccurredAtLocal = new DateTime(2026, 8, 3, 13, 0, 0),
            Confirmed = true,
            IdempotencyKey = "replace-key",
        };

        var model = IssuedMembershipSaleCorrectionFormViewModel.FromSubmission(
            input,
            [],
            SuccessPreview(
                [],
                new IssuedMembershipSaleReplacementTerms(
                    selectedTypeId,
                    expectedVersion,
                    "Replacement",
                    45,
                    12,
                    new Money(1200, "UAH"),
                    startDate,
                    startDate.AddDays(44))),
            []);

        Assert.Equal(selectedTypeId, model.Input.ReplacementMembershipTypeId);
        Assert.Equal(expectedVersion, model.Input.ExpectedMembershipTypeUpdatedAt);
        Assert.Equal(startDate, model.Input.ReplacementStartDate);
        Assert.Equal("token", model.Input.ExpectedDependencyToken);
        Assert.True(model.HasConfirmedPreview);
        Assert.True(model.CanSubmit);
    }

    [Fact]
    public void BaselinePreviewCannotConfirmIncompleteReplacement()
    {
        var input = new IssuedMembershipSaleCorrectionFormInput
        {
            ClientId = Guid.NewGuid(),
            OriginalMembershipId = Guid.NewGuid(),
            Mode = IssuedMembershipSaleCorrectionMode.Replace,
            ReplacementMembershipTypeId = Guid.NewGuid(),
            Reason = "Wrong type",
            OccurredAtLocal = DateTime.UtcNow,
            Confirmed = true,
            IdempotencyKey = "replace-key",
        };

        var model = IssuedMembershipSaleCorrectionFormViewModel.FromSubmission(
            input,
            [],
            SuccessPreview([]),
            []);

        Assert.False(model.HasConfirmedPreview);
        Assert.False(model.CanSubmit);
    }

    [Fact]
    public void DuplicateSubmissionRotatesIdempotencyAndRequiresConfirmationAgain()
    {
        var input = new IssuedMembershipSaleCorrectionFormInput
        {
            ClientId = Guid.NewGuid(),
            OriginalMembershipId = Guid.NewGuid(),
            Mode = IssuedMembershipSaleCorrectionMode.Cancel,
            Reason = "Wrong sale",
            OccurredAtLocal = DateTime.UtcNow,
            Confirmed = true,
            IdempotencyKey = "used-key",
        };

        var model = IssuedMembershipSaleCorrectionFormViewModel.FromSubmission(
            input,
            [],
            SuccessPreview([]),
            [new CommandError(CommandErrorCode.DuplicateSubmission, "Duplicate", "idempotencyKey")]);

        Assert.NotEqual("used-key", model.Input.IdempotencyKey);
        Assert.False(model.Input.Confirmed);
        Assert.True(model.HasConfirmedPreview);
        Assert.False(model.CanSubmit);
    }

    private static PreviewIssuedMembershipSaleCorrectionResult SuccessPreview(
        IReadOnlyList<IssuedMembershipSaleDependency> dependencies,
        IssuedMembershipSaleReplacementTerms? replacement = null) =>
        PreviewIssuedMembershipSaleCorrectionResult.Succeeded(new(
            new IssuedMembershipSaleDetails(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Original",
                30,
                8,
                new Money(100, "UAH"),
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 30),
                DateTimeOffset.UtcNow),
            dependencies,
            "token",
            replacement));
}
