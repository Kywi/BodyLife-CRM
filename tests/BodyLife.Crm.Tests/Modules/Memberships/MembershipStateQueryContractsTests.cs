using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipStateQueryContractsTests
{
    private static readonly Guid MembershipId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid MembershipTypeId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");
    private static readonly Guid FirstNegativeVisitId = Guid.Parse(
        "44444444-4444-4444-4444-444444444444");
    private static readonly Guid FreezeId = Guid.Parse(
        "55555555-5555-5555-5555-555555555555");
    private static readonly Guid NonWorkingPeriodId = Guid.Parse(
        "66666666-6666-6666-6666-666666666666");
    private static readonly Guid AdjustmentId = Guid.Parse(
        "77777777-7777-7777-7777-777777777777");
    private static readonly DateOnly AsOfDate = new(2026, 7, 13);

    [Fact]
    public void QueryCarriesActorDirectMembershipSelectorAndAsOfDate()
    {
        var actor = CreateActor();

        var query = new GetMembershipStateQuery(actor, MembershipId, AsOfDate);

        Assert.IsAssignableFrom<IBodyLifeQuery<GetMembershipStateResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(MembershipId, query.MembershipId);
        Assert.Equal(AsOfDate, query.AsOfDate);
    }

    [Fact]
    public void ReadModelExposesCanonicalStableStateAndImmutableSnapshot()
    {
        var state = CreateState(asOfDate: new DateOnly(2026, 8, 4));

        Assert.Equal(MembershipId, state.MembershipId);
        Assert.Equal(ClientId, state.ClientId);
        Assert.Equal(MembershipTypeId, state.MembershipTypeId);
        Assert.Equal("Eight visits", state.Snapshot.TypeName);
        Assert.Equal(30, state.Snapshot.DurationDays);
        Assert.Equal(8, state.Snapshot.VisitsLimit);
        Assert.Equal(new Money(1000m, "UAH"), state.Snapshot.Price);
        Assert.Equal(new DateOnly(2026, 7, 1), state.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 30), state.BaseEndDate);
        Assert.Equal(new DateOnly(2026, 8, 3), state.EffectiveEndDate);
        Assert.Equal(10, state.CountedVisits);
        Assert.Equal(-2, state.RemainingVisits);
        Assert.Equal(2, state.NegativeBalance);
        Assert.Equal(FirstNegativeVisitId, state.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 12), state.FirstNegativeVisitDate);
        Assert.Equal(4, state.ExtensionDays);
        Assert.Empty(state.ExtensionExplanation);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 13, 8, 30, 0, TimeSpan.Zero),
            state.LastCountedVisitAt);
        Assert.Equal(new DateOnly(2026, 8, 4), state.AsOfDate);
        Assert.False(state.IsActiveByDate);
        Assert.Collection(
            state.Warnings,
            warning =>
            {
                Assert.Equal(MembershipWarningCodes.NegativeBalance, warning.Code);
                Assert.Equal(MembershipWarningSeverity.Danger, warning.Severity);
            },
            warning =>
            {
                Assert.Equal(MembershipWarningCodes.ExpiredByDate, warning.Code);
                Assert.Equal(MembershipWarningSeverity.Danger, warning.Severity);
            });
        var warningList = Assert.IsAssignableFrom<IList<MembershipWarning>>(state.Warnings);
        Assert.True(warningList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => warningList.Add(state.Warnings[0]));
        Assert.All(
            typeof(MembershipStateReadModel).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void ReadModelDefensivelyCarriesOverlappingAndInactiveExplanationRows()
    {
        var calculation = MembershipExtensionCalculator.Calculate(
        [
            ExtensionSource(
                "freeze",
                FreezeId,
                "Summer freeze",
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 12)),
            ExtensionSource(
                "non_working_period",
                NonWorkingPeriodId,
                "Gym closure",
                new DateOnly(2026, 7, 11),
                new DateOnly(2026, 7, 13)),
            ExtensionSource(
                "membership_adjustment",
                AdjustmentId,
                "Canceled adjustment",
                new DateOnly(2026, 7, 14),
                new DateOnly(2026, 7, 14),
                isActive: false),
        ]);
        var mutableExplanation = calculation.ExplanationDays.ToList();

        var state = CreateState(extensionExplanation: mutableExplanation);
        mutableExplanation.Clear();

        Assert.Equal(7, state.ExtensionExplanation.Count);
        Assert.Equal(
            2,
            state.ExtensionExplanation.Count(item =>
                item.ExtensionDate == new DateOnly(2026, 7, 11)));
        Assert.Contains(
            state.ExtensionExplanation,
            item => item.ExtensionDate == new DateOnly(2026, 7, 11)
                && item.SourceType == "freeze"
                && item.SourceId == FreezeId
                && item.SourceLabel == "Summer freeze"
                && item.IsActive);
        Assert.Contains(
            state.ExtensionExplanation,
            item => item.ExtensionDate == new DateOnly(2026, 7, 11)
                && item.SourceType == "non_working_period"
                && item.SourceId == NonWorkingPeriodId
                && item.SourceLabel == "Gym closure"
                && item.IsActive);
        var inactive = Assert.Single(
            state.ExtensionExplanation,
            item => !item.IsActive);
        Assert.Equal(new DateOnly(2026, 7, 14), inactive.ExtensionDate);
        Assert.Equal("membership_adjustment", inactive.SourceType);
        Assert.Equal(AdjustmentId, inactive.SourceId);
        Assert.Equal("Canceled adjustment", inactive.SourceLabel);

        var explanationList = Assert.IsAssignableFrom<IList<MembershipExtensionDay>>(
            state.ExtensionExplanation);
        Assert.True(explanationList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => explanationList.Add(inactive));
        var missingItem = Assert.Throws<ArgumentException>(() =>
            CreateState(extensionExplanation: [null!]));
        Assert.Equal("extensionExplanation", missingItem.ParamName);
    }

    [Fact]
    public void StoredExplanationFactoryNormalizesAndValidatesSourceMetadata()
    {
        var item = MembershipExtensionDay.FromStoredExplanation(
            new DateOnly(2026, 7, 10),
            "  freeze  ",
            FreezeId,
            "  Summer freeze  ",
            isActive: false);

        Assert.Equal(new DateOnly(2026, 7, 10), item.ExtensionDate);
        Assert.Equal("freeze", item.SourceType);
        Assert.Equal(FreezeId, item.SourceId);
        Assert.Equal("Summer freeze", item.SourceLabel);
        Assert.False(item.IsActive);
        Assert.Equal(
            "sourceId",
            Assert.Throws<ArgumentException>(() =>
                MembershipExtensionDay.FromStoredExplanation(
                    item.ExtensionDate,
                    item.SourceType,
                    Guid.Empty,
                    item.SourceLabel,
                    item.IsActive)).ParamName);
        Assert.Equal(
            "sourceType",
            Assert.Throws<ArgumentException>(() =>
                MembershipExtensionDay.FromStoredExplanation(
                    item.ExtensionDate,
                    "   ",
                    item.SourceId,
                    item.SourceLabel,
                    item.IsActive)).ParamName);
        Assert.Equal(
            "sourceLabel",
            Assert.Throws<ArgumentException>(() =>
                MembershipExtensionDay.FromStoredExplanation(
                    item.ExtensionDate,
                    item.SourceType,
                    item.SourceId,
                    null,
                    item.IsActive)).ParamName);
        Assert.All(
            typeof(MembershipExtensionDay).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void ActiveByDateUsesInclusiveMembershipRule()
    {
        Assert.True(CreateState(asOfDate: new DateOnly(2026, 8, 3)).IsActiveByDate);
        Assert.False(CreateState(asOfDate: new DateOnly(2026, 8, 4)).IsActiveByDate);
    }

    [Fact]
    public void SuccessfulResultCarriesCanonicalStateAndAllowedMembershipActions()
    {
        var state = CreateState();
        var permissions = new QueryPermissionSet(
        [
            QueryPermissionResult.Allowed(
                MembershipActionKeys.CreateOpeningState,
                MembershipActionKeys.AdminOrOwnerPolicy),
        ]);

        var result = GetMembershipStateResult.Succeeded(state, permissions);

        Assert.Equal(GetMembershipStateStatus.Success, result.Status);
        Assert.Same(state, result.State);
        Assert.True(result.AllowedActions.IsAllowed(MembershipActionKeys.CreateOpeningState));
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
    }

    [Fact]
    public void DeniedResultContainsNoStateOrActions()
    {
        var result = GetMembershipStateResult.Denied();

        Assert.Equal(GetMembershipStateStatus.PermissionDenied, result.Status);
        Assert.Null(result.State);
        Assert.Empty(result.AllowedActions.Items);
        Assert.Equal("permission_denied", result.ErrorCode);
        Assert.Null(result.ErrorField);
    }

    [Fact]
    public void MissingAndInvalidResultsUseStableErrorContracts()
    {
        var missing = GetMembershipStateResult.Missing();
        var invalid = GetMembershipStateResult.Invalid(
            "Membership id is required.",
            "membershipId");

        Assert.Equal(GetMembershipStateStatus.NotFound, missing.Status);
        Assert.Equal("not_found", missing.ErrorCode);
        Assert.Equal("membershipId", missing.ErrorField);
        Assert.Equal(GetMembershipStateStatus.ValidationFailed, invalid.Status);
        Assert.Equal("validation_failed", invalid.ErrorCode);
        Assert.Equal("Membership id is required.", invalid.ErrorMessage);
        Assert.Equal("membershipId", invalid.ErrorField);
        Assert.Null(missing.State);
        Assert.Null(invalid.State);
    }

    [Fact]
    public void RecalculationFailureContainsNoStateOrActions()
    {
        var result = GetMembershipStateResult.RecalculationFailed();

        Assert.Equal(GetMembershipStateStatus.RecalculationFailed, result.Status);
        Assert.Equal("recalculation_failed", result.ErrorCode);
        Assert.Null(result.ErrorField);
        Assert.Null(result.State);
        Assert.Empty(result.AllowedActions.Items);
    }

    [Fact]
    public void FailureResultsCannotLeakExtensionExplanationOrActions()
    {
        var state = CreateState(
            extensionExplanation:
            [
                MembershipExtensionDay.FromStoredExplanation(
                    new DateOnly(2026, 7, 10),
                    "freeze",
                    FreezeId,
                    "Summer freeze",
                    isActive: true),
            ]);
        var succeeded = GetMembershipStateResult.Succeeded(
            state,
            QueryPermissionSet.Empty);
        var failures = new[]
        {
            GetMembershipStateResult.Denied(),
            GetMembershipStateResult.Missing(),
            GetMembershipStateResult.Invalid("Invalid membership.", "membershipId"),
            GetMembershipStateResult.RecalculationFailed(),
        };

        Assert.Single(Assert.IsType<MembershipStateReadModel>(succeeded.State)
            .ExtensionExplanation);
        Assert.All(
            failures,
            failure =>
            {
                Assert.Null(failure.State);
                Assert.Empty(failure.AllowedActions.Items);
            });
    }

    [Fact]
    public void QueryPermissionIntentUsesStableAdminOrOwnerContract()
    {
        Assert.Equal(
            "memberships.create_opening_state",
            MembershipActionKeys.CreateOpeningState);
        Assert.Equal("BodyLife.AdminOrOwner", MembershipActionKeys.AdminOrOwnerPolicy);
    }

    private static MembershipStateReadModel CreateState(
        DateOnly? asOfDate = null,
        IEnumerable<MembershipExtensionDay>? extensionExplanation = null)
    {
        var snapshot = new IssuedMembershipSnapshot(
            "Eight visits",
            durationDays: 30,
            visitsLimit: 8,
            new Money(1000m, "UAH"));
        var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
            MembershipTypeId,
            snapshot,
            startDate: new DateOnly(2026, 7, 1),
            baseEndDate: new DateOnly(2026, 7, 30));
        var calculatedState = MembershipCalculatedState.FromStoredCache(
            issueTerms,
            countedVisits: 10,
            remainingVisits: -2,
            negativeBalance: 2,
            firstNegativeVisitId: FirstNegativeVisitId,
            firstNegativeVisitDate: new DateOnly(2026, 7, 12),
            extensionDays: 4,
            effectiveEndDate: new DateOnly(2026, 8, 3),
            lastCountedVisitAt: new DateTimeOffset(
                2026,
                7,
                13,
                8,
                30,
                0,
                TimeSpan.Zero));

        return new MembershipStateReadModel(
            MembershipId,
            ClientId,
            issueTerms,
            calculatedState,
            asOfDate: asOfDate ?? AsOfDate,
            extensionExplanation);
    }

    private static MembershipExtensionSourceRange ExtensionSource(
        string sourceType,
        Guid sourceId,
        string sourceLabel,
        DateOnly startDate,
        DateOnly endDate,
        bool isActive = true)
    {
        return new MembershipExtensionSourceRange(
            sourceType,
            sourceId,
            sourceLabel,
            new DateRange(startDate, endDate),
            isActive);
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
