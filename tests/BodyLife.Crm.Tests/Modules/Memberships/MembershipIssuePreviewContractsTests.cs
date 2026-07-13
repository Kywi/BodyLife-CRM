using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipIssuePreviewContractsTests
{
    private static readonly Guid ClientId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid MembershipTypeId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly ProposedStartDate = new(2026, 7, 1);
    private static readonly DateTimeOffset CatalogTimestamp = new(
        2026,
        7,
        13,
        20,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void QueryCarriesActorSelectorsStartDateAndOptionalNegativeDecision()
    {
        var actor = CreateActor();

        var query = new PreviewIssueMembershipQuery(
            actor,
            ClientId,
            MembershipTypeId,
            ProposedStartDate,
            MembershipNegativeHandlingDecision.LeaveVisible);
        var queryWithoutDecision = new PreviewIssueMembershipQuery(
            actor,
            ClientId,
            MembershipTypeId,
            ProposedStartDate);

        Assert.IsAssignableFrom<IBodyLifeQuery<PreviewIssueMembershipResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(ClientId, query.ClientId);
        Assert.Equal(MembershipTypeId, query.MembershipTypeId);
        Assert.Equal(ProposedStartDate, query.ProposedStartDate);
        Assert.Equal(
            MembershipNegativeHandlingDecision.LeaveVisible,
            query.NegativeHandlingDecision);
        Assert.Null(queryWithoutDecision.NegativeHandlingDecision);
    }

    [Fact]
    public void PolicyCopiesSnapshotAndUsesInclusiveInitialStateRules()
    {
        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId,
            CreateMembershipType(),
            ProposedStartDate);

        Assert.Equal(ClientId, preview.ClientId);
        Assert.Equal(MembershipTypeId, preview.MembershipTypeId);
        Assert.Equal("Eight visits", preview.Snapshot.TypeName);
        Assert.Equal(30, preview.Snapshot.DurationDays);
        Assert.Equal(8, preview.Snapshot.VisitsLimit);
        Assert.Equal(new Money(1000m, "UAH"), preview.Snapshot.Price);
        Assert.Equal(ProposedStartDate, preview.ProposedStartDate);
        Assert.Equal(new DateOnly(2026, 7, 30), preview.BaseEndDate);
        Assert.Equal(0, preview.ExpectedInitialState.CountedVisits);
        Assert.Equal(8, preview.ExpectedInitialState.RemainingVisits);
        Assert.Equal(0, preview.ExpectedInitialState.NegativeBalance);
        Assert.Null(preview.ExpectedInitialState.FirstNegativeVisitDate);
        Assert.Equal(0, preview.ExpectedInitialState.ExtensionDays);
        Assert.Equal(preview.BaseEndDate, preview.ExpectedInitialState.EffectiveEndDate);
        Assert.Null(preview.ExistingNegativeState);
        Assert.Empty(preview.NegativeHandlingOptions);
        Assert.Empty(preview.Warnings);
        Assert.False(preview.RequiresNegativeHandlingDecision);
        Assert.True(preview.CanProceedToIssue);
        Assert.All(
            typeof(MembershipIssuePreview).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void PreviewSnapshotDoesNotFollowLaterCatalogValues()
    {
        var original = CreateMembershipType();
        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId,
            original,
            ProposedStartDate);

        var editedCatalogValue = original with
        {
            Name = "Twelve visits",
            DurationDays = 45,
            VisitsLimit = 12,
            Price = new Money(1500m, "UAH"),
            UpdatedAt = CatalogTimestamp.AddMinutes(1),
        };

        Assert.Equal("Twelve visits", editedCatalogValue.Name);
        Assert.Equal("Eight visits", preview.Snapshot.TypeName);
        Assert.Equal(30, preview.Snapshot.DurationDays);
        Assert.Equal(8, preview.Snapshot.VisitsLimit);
        Assert.Equal(new Money(1000m, "UAH"), preview.Snapshot.Price);
    }

    [Fact]
    public void ExistingNegativeWithoutDecisionReturnsBlockingWarningAndHonestOptions()
    {
        var negativeState = new MembershipIssueNegativeContext(
            negativeBalance: 2,
            firstNegativeVisitDate: new DateOnly(2026, 6, 28));

        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId,
            CreateMembershipType(),
            ProposedStartDate,
            negativeState);

        var existingNegativeState = Assert.IsType<MembershipIssueNegativeContext>(
            preview.ExistingNegativeState);
        Assert.Same(negativeState, existingNegativeState);
        Assert.Equal(2, existingNegativeState.NegativeBalance);
        Assert.Equal(
            new DateOnly(2026, 6, 28),
            existingNegativeState.FirstNegativeVisitDate);
        Assert.Null(preview.SelectedNegativeHandlingDecision);
        Assert.True(preview.RequiresNegativeHandlingDecision);
        Assert.False(preview.CanProceedToIssue);
        var warning = Assert.Single(preview.Warnings);
        Assert.Equal(MembershipWarningCodes.NegativeBalance, warning.Code);
        Assert.Equal(MembershipWarningSeverity.Danger, warning.Severity);
        Assert.Equal(
            "Client has negative visits. Check the start date of the new membership.",
            warning.Message);
        Assert.Collection(
            preview.NegativeHandlingOptions,
            leaveVisible =>
            {
                Assert.Equal(
                    MembershipNegativeHandlingDecision.LeaveVisible,
                    leaveVisible.Decision);
                Assert.True(leaveVisible.IsAvailable);
            },
            cover =>
            {
                Assert.Equal(
                    MembershipNegativeHandlingDecision.CoverWithNewMembership,
                    cover.Decision);
                Assert.False(cover.IsAvailable);
            },
            closure =>
            {
                Assert.Equal(
                    MembershipNegativeHandlingDecision.RecordExplicitClosure,
                    closure.Decision);
                Assert.False(closure.IsAvailable);
            });

        var optionList = Assert.IsAssignableFrom<IList<MembershipNegativeHandlingOption>>(
            preview.NegativeHandlingOptions);
        Assert.True(optionList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => optionList.Add(optionList[0]));
        var warningList = Assert.IsAssignableFrom<IList<MembershipWarning>>(preview.Warnings);
        Assert.True(warningList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => warningList.Add(warning));
        Assert.All(
            typeof(MembershipNegativeHandlingOption).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void LeaveVisibleIsExplicitAndDoesNotHideExistingNegativeState()
    {
        var negativeState = new MembershipIssueNegativeContext(
            negativeBalance: 2,
            firstNegativeVisitDate: new DateOnly(2026, 6, 28));

        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId,
            CreateMembershipType(),
            ProposedStartDate,
            negativeState,
            MembershipNegativeHandlingDecision.LeaveVisible);

        Assert.Equal(
            MembershipNegativeHandlingDecision.LeaveVisible,
            preview.SelectedNegativeHandlingDecision);
        Assert.False(preview.RequiresNegativeHandlingDecision);
        Assert.True(preview.CanProceedToIssue);
        Assert.Equal(2, preview.ExistingNegativeState!.NegativeBalance);
        Assert.Equal(0, preview.ExpectedInitialState.NegativeBalance);
        Assert.Equal(8, preview.ExpectedInitialState.RemainingVisits);
        Assert.Single(preview.Warnings);
    }

    [Theory]
    [InlineData(MembershipNegativeHandlingDecision.CoverWithNewMembership)]
    [InlineData(MembershipNegativeHandlingDecision.RecordExplicitClosure)]
    public void DeferredNegativeDecisionsCannotProceed(
        MembershipNegativeHandlingDecision decision)
    {
        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId,
            CreateMembershipType(),
            ProposedStartDate,
            new MembershipIssueNegativeContext(
                negativeBalance: 1,
                firstNegativeVisitDate: new DateOnly(2026, 6, 30)),
            decision);

        Assert.Equal(decision, preview.SelectedNegativeHandlingDecision);
        Assert.False(preview.RequiresNegativeHandlingDecision);
        Assert.False(preview.CanProceedToIssue);
        var selectedOption = Assert.Single(
            preview.NegativeHandlingOptions,
            option => option.Decision == decision);
        Assert.False(selectedOption.IsAvailable);
        Assert.Equal(1, preview.ExistingNegativeState!.NegativeBalance);
        Assert.Single(preview.Warnings);
    }

    [Fact]
    public void OpeningStateNegativeCanRemainHonestWhenFirstNegativeDateIsUnknown()
    {
        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId,
            CreateMembershipType(),
            ProposedStartDate,
            new MembershipIssueNegativeContext(
                negativeBalance: 3,
                firstNegativeVisitDate: null));

        Assert.Equal(3, preview.ExistingNegativeState!.NegativeBalance);
        Assert.Null(preview.ExistingNegativeState.FirstNegativeVisitDate);
        Assert.True(preview.RequiresNegativeHandlingDecision);
        Assert.False(preview.CanProceedToIssue);
        Assert.Single(preview.Warnings);
    }

    [Fact]
    public void NegativeContextRejectsNonPositiveBalances()
    {
        var zero = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MembershipIssueNegativeContext(0, firstNegativeVisitDate: null));
        var positive = new MembershipIssueNegativeContext(
            1,
            new DateOnly(2026, 6, 30));

        Assert.Equal("negativeBalance", zero.ParamName);
        Assert.Equal(1, positive.NegativeBalance);
        Assert.Equal(new DateOnly(2026, 6, 30), positive.FirstNegativeVisitDate);
        Assert.All(
            typeof(MembershipIssueNegativeContext).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void PolicyRejectsInvalidIdentityCatalogAndDecisionCombinations()
    {
        Assert.Equal(
            "clientId",
            Assert.Throws<ArgumentException>(() =>
                MembershipIssuePreviewPolicy.Create(
                    Guid.Empty,
                    CreateMembershipType(),
                    ProposedStartDate)).ParamName);
        Assert.Equal(
            "membershipType",
            Assert.Throws<ArgumentNullException>(() =>
                MembershipIssuePreviewPolicy.Create(
                    ClientId,
                    membershipType: null,
                    ProposedStartDate)).ParamName);
        Assert.Throws<InvalidOperationException>(() =>
            MembershipIssuePreviewPolicy.Create(
                ClientId,
                CreateMembershipType(isActive: false),
                ProposedStartDate));
        Assert.Equal(
            "negativeHandlingDecision",
            Assert.Throws<ArgumentException>(() =>
                MembershipIssuePreviewPolicy.Create(
                    ClientId,
                    CreateMembershipType(),
                    ProposedStartDate,
                    existingNegativeState: null,
                    MembershipNegativeHandlingDecision.LeaveVisible)).ParamName);
        Assert.Equal(
            "negativeHandlingDecision",
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MembershipIssuePreviewPolicy.Create(
                    ClientId,
                    CreateMembershipType(),
                    ProposedStartDate,
                    new MembershipIssueNegativeContext(1, ProposedStartDate),
                    (MembershipNegativeHandlingDecision)999)).ParamName);
    }

    [Fact]
    public void PolicyReusesSupportedCalendarOverflowGuard()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipIssuePreviewPolicy.Create(
                ClientId,
                CreateMembershipType(durationDays: 2),
                DateOnly.MaxValue));

        Assert.Equal("durationDays", exception.ParamName);
    }

    [Fact]
    public void SuccessfulResultCarriesPreviewAndIssuePermission()
    {
        var preview = MembershipIssuePreviewPolicy.Create(
            ClientId,
            CreateMembershipType(),
            ProposedStartDate);
        var permissions = new QueryPermissionSet(
        [
            QueryPermissionResult.Allowed(
                MembershipActionKeys.Issue,
                MembershipActionKeys.AdminOrOwnerPolicy),
        ]);

        var result = PreviewIssueMembershipResult.Succeeded(preview, permissions);

        Assert.Equal(PreviewIssueMembershipStatus.Success, result.Status);
        Assert.Same(preview, result.Preview);
        Assert.True(result.AllowedActions.IsAllowed(MembershipActionKeys.Issue));
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
    }

    [Fact]
    public void FailureResultsUseStableErrorsWithoutLeakingPreviewOrActions()
    {
        var denied = PreviewIssueMembershipResult.Denied();
        var missingClient = PreviewIssueMembershipResult.MissingClient();
        var missingType = PreviewIssueMembershipResult.MissingMembershipType();
        var inactive = PreviewIssueMembershipResult.InactiveMembershipType();
        var invalid = PreviewIssueMembershipResult.Invalid(
            "  Start date is invalid.  ",
            "proposedStartDate");
        var recalculationFailed = PreviewIssueMembershipResult.RecalculationFailed();
        var failures = new[]
        {
            denied,
            missingClient,
            missingType,
            inactive,
            invalid,
            recalculationFailed,
        };

        Assert.Equal(PreviewIssueMembershipStatus.PermissionDenied, denied.Status);
        Assert.Equal("permission_denied", denied.ErrorCode);
        Assert.Null(denied.ErrorField);
        Assert.Equal(PreviewIssueMembershipStatus.NotFound, missingClient.Status);
        Assert.Equal("clientId", missingClient.ErrorField);
        Assert.Equal(PreviewIssueMembershipStatus.NotFound, missingType.Status);
        Assert.Equal("membershipTypeId", missingType.ErrorField);
        Assert.Equal(
            PreviewIssueMembershipStatus.MembershipTypeInactive,
            inactive.Status);
        Assert.Equal("membership_type_inactive", inactive.ErrorCode);
        Assert.Equal("membershipTypeId", inactive.ErrorField);
        Assert.Equal(PreviewIssueMembershipStatus.ValidationFailed, invalid.Status);
        Assert.Equal("validation_failed", invalid.ErrorCode);
        Assert.Equal("Start date is invalid.", invalid.ErrorMessage);
        Assert.Equal("proposedStartDate", invalid.ErrorField);
        Assert.Equal(
            PreviewIssueMembershipStatus.RecalculationFailed,
            recalculationFailed.Status);
        Assert.Equal("recalculation_failed", recalculationFailed.ErrorCode);
        Assert.All(
            failures,
            failure =>
            {
                Assert.Null(failure.Preview);
                Assert.Empty(failure.AllowedActions.Items);
            });
    }

    [Fact]
    public void IssuePermissionIntentUsesStableAdminOrOwnerContract()
    {
        Assert.Equal("memberships.issue", MembershipActionKeys.Issue);
        Assert.Equal("BodyLife.AdminOrOwner", MembershipActionKeys.AdminOrOwnerPolicy);
    }

    private static MembershipTypeCatalogItem CreateMembershipType(
        bool isActive = true,
        int durationDays = 30)
    {
        return new MembershipTypeCatalogItem(
            MembershipTypeId,
            "Eight visits",
            durationDays,
            VisitsLimit: 8,
            new Money(1000m, "UAH"),
            isActive,
            Comment: null,
            CreatedAt: CatalogTimestamp.AddDays(-1),
            UpdatedAt: CatalogTimestamp,
            DeactivatedAt: isActive ? null : CatalogTimestamp);
    }

    private static ActorContext CreateActor()
    {
        return new ActorContext(
            AccountId.New(),
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            SessionId.New(),
            "reception tablet");
    }
}
