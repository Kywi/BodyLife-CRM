using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class ClientMembershipStatesQueryContractsTests
{
    private static readonly Guid ClientId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid MembershipTypeId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid OlderMembershipId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");
    private static readonly Guid NewerMembershipId = Guid.Parse(
        "44444444-4444-4444-4444-444444444444");
    private static readonly Guid TieBreakMembershipId = Guid.Parse(
        "55555555-5555-5555-5555-555555555555");
    private static readonly DateOnly AsOfDate = new(2026, 7, 14);

    [Fact]
    public void QueryCarriesActorClientSelectorAndRequiredAsOfDate()
    {
        var actor = CreateActor();

        var query = new GetClientMembershipStatesQuery(actor, ClientId, AsOfDate);

        Assert.IsAssignableFrom<IBodyLifeQuery<GetClientMembershipStatesResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(ClientId, query.ClientId);
        Assert.Equal(AsOfDate, query.AsOfDate);
    }

    [Fact]
    public void EmptyTimelineHasNoActiveCandidate()
    {
        var collection = ClientMembershipStatesPolicy.Create(ClientId, AsOfDate, []);

        Assert.Equal(ClientId, collection.ClientId);
        Assert.Equal(AsOfDate, collection.AsOfDate);
        Assert.Empty(collection.Timeline);
        Assert.Equal(
            ActiveMembershipCandidateStatus.None,
            collection.ActiveCandidateSelection.Status);
        Assert.Null(collection.ActiveCandidateSelection.SingleCandidate);
        Assert.Empty(collection.ActiveCandidateSelection.Candidates);
    }

    [Fact]
    public void OneLifecycleActiveAndDateActiveMembershipIsSelected()
    {
        var item = CreateItem(
            NewerMembershipId,
            startDate: new DateOnly(2026, 7, 1),
            durationDays: 30,
            IssuedMembershipLifecycleStatus.Active);

        var collection = ClientMembershipStatesPolicy.Create(
            ClientId,
            AsOfDate,
            [item]);

        Assert.Equal(
            ActiveMembershipCandidateStatus.Single,
            collection.ActiveCandidateSelection.Status);
        Assert.Same(item, collection.ActiveCandidateSelection.SingleCandidate);
        Assert.Same(item, Assert.Single(collection.ActiveCandidateSelection.Candidates));
    }

    [Fact]
    public void DateExpiredMembershipIsNotAnActiveCandidate()
    {
        var expired = CreateItem(
            OlderMembershipId,
            startDate: new DateOnly(2026, 6, 1),
            durationDays: 30,
            IssuedMembershipLifecycleStatus.Active);

        var collection = ClientMembershipStatesPolicy.Create(
            ClientId,
            AsOfDate,
            [expired]);

        Assert.False(expired.State.IsActiveByDate);
        Assert.False(expired.IsActiveCandidate);
        Assert.Equal(
            ActiveMembershipCandidateStatus.None,
            collection.ActiveCandidateSelection.Status);
    }

    [Theory]
    [InlineData(IssuedMembershipLifecycleStatus.Canceled)]
    [InlineData(IssuedMembershipLifecycleStatus.Corrected)]
    public void HistoricalLifecycleMembershipIsNeverAnActiveCandidate(
        IssuedMembershipLifecycleStatus lifecycleStatus)
    {
        var historical = CreateItem(
            OlderMembershipId,
            startDate: new DateOnly(2026, 7, 1),
            durationDays: 30,
            lifecycleStatus);

        var collection = ClientMembershipStatesPolicy.Create(
            ClientId,
            AsOfDate,
            [historical]);

        Assert.True(historical.State.IsActiveByDate);
        Assert.False(historical.IsActiveCandidate);
        Assert.Equal(
            ActiveMembershipCandidateStatus.None,
            collection.ActiveCandidateSelection.Status);
    }

    [Fact]
    public void MultipleActiveCandidatesRemainExplicitlyAmbiguous()
    {
        var older = CreateItem(
            OlderMembershipId,
            startDate: new DateOnly(2026, 7, 1),
            durationDays: 30,
            IssuedMembershipLifecycleStatus.Active);
        var newer = CreateItem(
            NewerMembershipId,
            startDate: new DateOnly(2026, 7, 10),
            durationDays: 30,
            IssuedMembershipLifecycleStatus.Active);

        var collection = ClientMembershipStatesPolicy.Create(
            ClientId,
            AsOfDate,
            [older, newer]);

        Assert.Equal(
            ActiveMembershipCandidateStatus.Ambiguous,
            collection.ActiveCandidateSelection.Status);
        Assert.Null(collection.ActiveCandidateSelection.SingleCandidate);
        Assert.Collection(
            collection.ActiveCandidateSelection.Candidates,
            item => Assert.Equal(NewerMembershipId, item.State.MembershipId),
            item => Assert.Equal(OlderMembershipId, item.State.MembershipId));
    }

    [Fact]
    public void TimelineUsesStableNewestFirstOrderingAndDefensiveStorage()
    {
        var mutableTimeline = new List<ClientMembershipStateTimelineItem>
        {
            CreateItem(
                TieBreakMembershipId,
                startDate: new DateOnly(2026, 7, 10),
                durationDays: 30,
                IssuedMembershipLifecycleStatus.Canceled,
                issuedAt: new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero)),
            CreateItem(
                OlderMembershipId,
                startDate: new DateOnly(2026, 6, 1),
                durationDays: 30,
                IssuedMembershipLifecycleStatus.Active),
            CreateItem(
                NewerMembershipId,
                startDate: new DateOnly(2026, 7, 10),
                durationDays: 30,
                IssuedMembershipLifecycleStatus.Active,
                issuedAt: new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero)),
        };

        var collection = ClientMembershipStatesPolicy.Create(
            ClientId,
            AsOfDate,
            mutableTimeline);
        mutableTimeline.Clear();

        Assert.Collection(
            collection.Timeline,
            item => Assert.Equal(NewerMembershipId, item.State.MembershipId),
            item => Assert.Equal(TieBreakMembershipId, item.State.MembershipId),
            item => Assert.Equal(OlderMembershipId, item.State.MembershipId));
        var timeline = Assert.IsAssignableFrom<IList<ClientMembershipStateTimelineItem>>(
            collection.Timeline);
        Assert.True(timeline.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => timeline.Add(collection.Timeline[0]));
    }

    [Fact]
    public void TimelineUsesMembershipIdAsFinalDeterministicTieBreak()
    {
        var issuedAt = new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);
        var higherId = CreateItem(
            TieBreakMembershipId,
            startDate: new DateOnly(2026, 7, 10),
            durationDays: 30,
            IssuedMembershipLifecycleStatus.Canceled,
            issuedAt);
        var lowerId = CreateItem(
            NewerMembershipId,
            startDate: new DateOnly(2026, 7, 10),
            durationDays: 30,
            IssuedMembershipLifecycleStatus.Canceled,
            issuedAt);

        var collection = ClientMembershipStatesPolicy.Create(
            ClientId,
            AsOfDate,
            [higherId, lowerId]);

        Assert.Collection(
            collection.Timeline,
            item => Assert.Equal(NewerMembershipId, item.State.MembershipId),
            item => Assert.Equal(TieBreakMembershipId, item.State.MembershipId));
    }

    [Fact]
    public void PolicyRejectsMismatchedOrDuplicateCanonicalStates()
    {
        var item = CreateItem(
            NewerMembershipId,
            startDate: new DateOnly(2026, 7, 1),
            durationDays: 30,
            IssuedMembershipLifecycleStatus.Active);
        var anotherClient = CreateItem(
            OlderMembershipId,
            startDate: new DateOnly(2026, 7, 1),
            durationDays: 30,
            IssuedMembershipLifecycleStatus.Active,
            clientId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var anotherAsOfDate = CreateItem(
            OlderMembershipId,
            startDate: new DateOnly(2026, 7, 1),
            durationDays: 30,
            IssuedMembershipLifecycleStatus.Active,
            asOfDate: AsOfDate.AddDays(1));

        Assert.Equal(
            "timeline",
            Assert.Throws<ArgumentException>(() =>
                ClientMembershipStatesPolicy.Create(
                    ClientId,
                    AsOfDate,
                    [anotherClient])).ParamName);
        Assert.Equal(
            "timeline",
            Assert.Throws<ArgumentException>(() =>
                ClientMembershipStatesPolicy.Create(
                    ClientId,
                    AsOfDate,
                    [anotherAsOfDate])).ParamName);
        Assert.Equal(
            "timeline",
            Assert.Throws<ArgumentException>(() =>
                ClientMembershipStatesPolicy.Create(
                    ClientId,
                    AsOfDate,
                    [item, item])).ParamName);
        Assert.Equal(
            "timeline",
            Assert.Throws<ArgumentException>(() =>
                ClientMembershipStatesPolicy.Create(
                    ClientId,
                    AsOfDate,
                    [null!])).ParamName);
    }

    [Fact]
    public void PolicyRejectsMissingSelectorsAndUnknownLifecycleStatus()
    {
        Assert.Equal(
            "clientId",
            Assert.Throws<ArgumentException>(() =>
                ClientMembershipStatesPolicy.Create(Guid.Empty, AsOfDate, [])).ParamName);
        Assert.Equal(
            "asOfDate",
            Assert.Throws<ArgumentException>(() =>
                ClientMembershipStatesPolicy.Create(ClientId, default, [])).ParamName);
        Assert.Equal(
            "lifecycleStatus",
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ClientMembershipStateTimelineItem(
                    CreateState(
                        NewerMembershipId,
                        ClientId,
                        new DateOnly(2026, 7, 1),
                        durationDays: 30,
                        AsOfDate),
                    (IssuedMembershipLifecycleStatus)999,
                    DateTimeOffset.UtcNow)).ParamName);
    }

    [Fact]
    public void SuccessfulResultCarriesCollectionAndClientScopedActions()
    {
        var collection = ClientMembershipStatesPolicy.Create(ClientId, AsOfDate, []);
        var permissions = new QueryPermissionSet(
        [
            QueryPermissionResult.Allowed(
                MembershipActionKeys.Issue,
                MembershipActionKeys.AdminOrOwnerPolicy),
        ]);

        var result = GetClientMembershipStatesResult.Succeeded(collection, permissions);

        Assert.Equal(GetClientMembershipStatesStatus.Success, result.Status);
        Assert.Same(collection, result.StateCollection);
        Assert.True(result.AllowedActions.IsAllowed(MembershipActionKeys.Issue));
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
    }

    [Fact]
    public void FailureResultsUseStableErrorsAndContainNoStateOrActions()
    {
        var denied = GetClientMembershipStatesResult.Denied();
        var missing = GetClientMembershipStatesResult.MissingClient();
        var invalid = GetClientMembershipStatesResult.Invalid(
            "  Client id is required.  ",
            "clientId");
        var recalculationFailed = GetClientMembershipStatesResult.RecalculationFailed();

        Assert.Equal(GetClientMembershipStatesStatus.PermissionDenied, denied.Status);
        Assert.Equal("permission_denied", denied.ErrorCode);
        Assert.Equal(GetClientMembershipStatesStatus.NotFound, missing.Status);
        Assert.Equal("not_found", missing.ErrorCode);
        Assert.Equal("clientId", missing.ErrorField);
        Assert.Equal(GetClientMembershipStatesStatus.ValidationFailed, invalid.Status);
        Assert.Equal("validation_failed", invalid.ErrorCode);
        Assert.Equal("Client id is required.", invalid.ErrorMessage);
        Assert.Equal(GetClientMembershipStatesStatus.RecalculationFailed, recalculationFailed.Status);
        Assert.Equal("recalculation_failed", recalculationFailed.ErrorCode);

        Assert.All(
            new[] { denied, missing, invalid, recalculationFailed },
            failure =>
            {
                Assert.Null(failure.StateCollection);
                Assert.Empty(failure.AllowedActions.Items);
            });
    }

    [Fact]
    public void CollectionAndSelectionContractsExposeReadOnlyState()
    {
        var collection = ClientMembershipStatesPolicy.Create(
            ClientId,
            AsOfDate,
            [
                CreateItem(
                    NewerMembershipId,
                    startDate: new DateOnly(2026, 7, 1),
                    durationDays: 30,
                    IssuedMembershipLifecycleStatus.Active),
            ]);

        Assert.All(
            typeof(ClientMembershipStatesReadModel).GetProperties(),
            property => Assert.Null(property.SetMethod));
        Assert.All(
            typeof(ClientMembershipStateTimelineItem).GetProperties(),
            property => Assert.Null(property.SetMethod));
        Assert.All(
            typeof(ActiveMembershipCandidateSelection).GetProperties(),
            property => Assert.Null(property.SetMethod));
        var candidates = Assert.IsAssignableFrom<IList<ClientMembershipStateTimelineItem>>(
            collection.ActiveCandidateSelection.Candidates);
        Assert.True(candidates.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => candidates.Add(collection.Timeline[0]));
    }

    private static ClientMembershipStateTimelineItem CreateItem(
        Guid membershipId,
        DateOnly startDate,
        int durationDays,
        IssuedMembershipLifecycleStatus lifecycleStatus,
        DateTimeOffset? issuedAt = null,
        Guid? clientId = null,
        DateOnly? asOfDate = null)
    {
        return new ClientMembershipStateTimelineItem(
            CreateState(
                membershipId,
                clientId ?? ClientId,
                startDate,
                durationDays,
                asOfDate ?? AsOfDate),
            lifecycleStatus,
            issuedAt ?? new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero));
    }

    private static MembershipStateReadModel CreateState(
        Guid membershipId,
        Guid clientId,
        DateOnly startDate,
        int durationDays,
        DateOnly asOfDate)
    {
        var snapshot = new IssuedMembershipSnapshot(
            "Eight visits",
            durationDays,
            visitsLimit: 8,
            new Money(1000m, "UAH"));
        var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
            MembershipTypeId,
            snapshot,
            startDate,
            MembershipDateRules.CalculateBaseEndDate(startDate, durationDays));

        return new MembershipStateReadModel(
            membershipId,
            clientId,
            issueTerms,
            MembershipStateCalculator.CalculateInitial(issueTerms),
            asOfDate);
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
