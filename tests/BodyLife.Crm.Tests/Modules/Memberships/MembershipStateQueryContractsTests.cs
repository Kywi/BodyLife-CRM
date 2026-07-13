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
    public void QueryPermissionIntentUsesStableAdminOrOwnerContract()
    {
        Assert.Equal(
            "memberships.create_opening_state",
            MembershipActionKeys.CreateOpeningState);
        Assert.Equal("BodyLife.AdminOrOwner", MembershipActionKeys.AdminOrOwnerPolicy);
    }

    private static MembershipStateReadModel CreateState(DateOnly? asOfDate = null)
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
            asOfDate: asOfDate ?? AsOfDate);
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
