using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Reports;

public sealed class ListInactiveClientsContractsTests
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 20);

    [Fact]
    public void QueriesCarryExplicitSelectorsAndOnlyAcceptedThresholds()
    {
        var actor = CreateActor();
        var clientIds = new[] { Guid.NewGuid() };

        var query = new ListInactiveClientsQuery(
            actor,
            AsOfDate,
            ThresholdDays: 30,
            IncludeClientsWithNoVisits: true);
        var membershipQuery = new GetClientMembershipReportStatesQuery(
            actor,
            AsOfDate,
            clientIds);

        Assert.IsAssignableFrom<IBodyLifeQuery<ListInactiveClientsResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(AsOfDate, query.AsOfDate);
        Assert.Equal(30, query.ThresholdDays);
        Assert.True(query.IncludeClientsWithNoVisits);
        Assert.Equal(ListInactiveClientsQuery.DefaultLimit, query.Limit);
        Assert.Equal(0, query.Offset);
        Assert.IsAssignableFrom<
            IBodyLifeQuery<GetClientMembershipReportStatesResult>>(membershipQuery);
        Assert.Same(clientIds, membershipQuery.ClientIds);
        Assert.True(ListInactiveClientsQuery.IsSupportedThreshold(14));
        Assert.True(ListInactiveClientsQuery.IsSupportedThreshold(30));
        Assert.True(ListInactiveClientsQuery.IsSupportedThreshold(60));
        Assert.False(ListInactiveClientsQuery.IsSupportedThreshold(15));
    }

    [Fact]
    public void PageRetainsClientVisitAndCanonicalCurrentMembershipState()
    {
        var actor = CreateActor();
        var clientId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var membershipState = CreateMembershipState(
            clientId,
            startDate: AsOfDate.AddDays(-10),
            effectiveEndDate: AsOfDate.AddDays(19),
            remainingVisits: 2);
        var states = ClientMembershipStatesPolicy.Create(
            clientId,
            AsOfDate,
            [
                new ClientMembershipStateTimelineItem(
                    membershipState,
                    IssuedMembershipLifecycleStatus.Active,
                    new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero)),
            ]);
        var source = new InactiveClientSourceRow(
            clientId,
            "  Inactive Client  ",
            "+380 67 123 4567",
            "BL-1001",
            ClientOperationalStatus.Inactive,
            new InactiveClientLastVisit(
                visitId,
                new DateTimeOffset(2026, 7, 6, 20, 30, 0, TimeSpan.Zero),
                VisitKind.OneOff),
            states);
        var mutableRows = new List<InactiveClientSourceRow> { source };
        var query = new ListInactiveClientsQuery(
            actor,
            AsOfDate,
            ThresholdDays: 14,
            Limit: 1,
            Offset: 10);

        var created = InactiveClientsPage.TryCreate(
            query,
            mutableRows,
            hasMore: true,
            out var page);
        mutableRows.Clear();

        Assert.True(created);
        var report = Assert.IsType<InactiveClientsPage>(page);
        Assert.Equal(AsOfDate, report.AsOfDate);
        Assert.Equal(14, report.ThresholdDays);
        Assert.False(report.IncludesClientsWithNoVisits);
        Assert.Equal(10, report.Offset);
        Assert.True(report.HasMore);
        Assert.Equal(11, report.NextOffset);
        var row = Assert.Single(report.Items);
        Assert.Equal(clientId, row.ClientId);
        Assert.Equal("Inactive Client", row.ClientDisplayName);
        Assert.Equal("+380 67 123 4567", row.ClientPhone);
        Assert.Equal("BL-1001", row.CurrentCardNumber);
        Assert.Equal(ClientOperationalStatus.Inactive, row.OperationalStatus);
        Assert.Equal(visitId, row.LastCountedVisit!.VisitId);
        Assert.Equal(VisitKind.OneOff, row.LastCountedVisit.VisitKind);
        Assert.Equal(new DateOnly(2026, 7, 6), row.LastCountedVisitDate);
        Assert.Equal(14, row.DaysInactive);
        Assert.False(row.HasAmbiguousCurrentMembership);
        var membership = Assert.IsType<InactiveClientMembershipSummary>(
            row.MembershipSummary);
        Assert.Equal(InactiveClientMembershipSummaryKind.Current, membership.Kind);
        Assert.Same(membershipState, membership.MembershipState);
        Assert.Equal(membershipState.MembershipId, membership.MembershipId);
        Assert.Equal(membershipState.Snapshot.TypeName, membership.MembershipTypeName);
        Assert.Equal(2, membership.RemainingVisits);
        Assert.Equal(membershipState.EffectiveEndDate, membership.EffectiveEndDate);
        Assert.Same(membershipState.Warnings, membership.Warnings);
        Assert.Equal(
            [MembershipWarningCodes.LowRemaining],
            membership.Warnings.Select(warning => warning.Code));
        AssertReadOnly(report.Items, row);
    }

    [Fact]
    public void MembershipSummaryHonestlyDistinguishesCurrentLastAndAmbiguous()
    {
        var actor = CreateActor();
        var expiredClientId = Guid.NewGuid();
        var expiredState = CreateMembershipState(
            expiredClientId,
            startDate: AsOfDate.AddDays(-40),
            effectiveEndDate: AsOfDate.AddDays(-11));
        var ambiguousClientId = Guid.NewGuid();
        var olderCandidate = CreateMembershipState(
            ambiguousClientId,
            startDate: AsOfDate.AddDays(-20),
            effectiveEndDate: AsOfDate.AddDays(9));
        var newerCandidate = CreateMembershipState(
            ambiguousClientId,
            startDate: AsOfDate.AddDays(-10),
            effectiveEndDate: AsOfDate.AddDays(19));
        var noMembershipClientId = Guid.NewGuid();
        var sources = new[]
        {
            CreateNeverVisitedSource(
                expiredClientId,
                ClientMembershipStatesPolicy.Create(
                    expiredClientId,
                    AsOfDate,
                    [
                        new ClientMembershipStateTimelineItem(
                            expiredState,
                            IssuedMembershipLifecycleStatus.Active,
                            DateTimeOffset.UtcNow),
                    ])),
            CreateNeverVisitedSource(
                ambiguousClientId,
                ClientMembershipStatesPolicy.Create(
                    ambiguousClientId,
                    AsOfDate,
                    [
                        new ClientMembershipStateTimelineItem(
                            olderCandidate,
                            IssuedMembershipLifecycleStatus.Active,
                            DateTimeOffset.UtcNow.AddDays(-10)),
                        new ClientMembershipStateTimelineItem(
                            newerCandidate,
                            IssuedMembershipLifecycleStatus.Active,
                            DateTimeOffset.UtcNow),
                    ])),
            CreateNeverVisitedSource(
                noMembershipClientId,
                ClientMembershipStatesPolicy.Create(
                    noMembershipClientId,
                    AsOfDate,
                    [])),
        };

        var created = InactiveClientsPage.TryCreate(
            new ListInactiveClientsQuery(
                actor,
                AsOfDate,
                ThresholdDays: 14,
                IncludeClientsWithNoVisits: true),
            sources,
            hasMore: false,
            out var page);

        Assert.True(created);
        var rows = page!.Items.ToDictionary(row => row.ClientId);
        Assert.Equal(
            InactiveClientMembershipSummaryKind.Last,
            rows[expiredClientId].MembershipSummary!.Kind);
        Assert.Same(
            expiredState,
            rows[expiredClientId].MembershipSummary!.MembershipState);
        Assert.True(rows[ambiguousClientId].HasAmbiguousCurrentMembership);
        Assert.Equal(
            InactiveClientMembershipSummaryKind.Last,
            rows[ambiguousClientId].MembershipSummary!.Kind);
        Assert.Same(
            newerCandidate,
            rows[ambiguousClientId].MembershipSummary!.MembershipState);
        Assert.Null(rows[noMembershipClientId].MembershipSummary);
        Assert.False(rows[noMembershipClientId].HasAmbiguousCurrentMembership);
    }

    [Fact]
    public void PageEnforcesThresholdNoVisitAndCanonicalSelectorConsistency()
    {
        var actor = CreateActor();
        var boundaryClientId = Guid.NewGuid();
        var boundary = CreateVisitedSource(
            boundaryClientId,
            AsOfDate.AddDays(-14));
        var recent = CreateVisitedSource(
            Guid.NewGuid(),
            AsOfDate.AddDays(-13));
        var future = CreateVisitedSource(
            Guid.NewGuid(),
            AsOfDate.AddDays(1));
        var neverVisitedClientId = Guid.NewGuid();
        var neverVisited = CreateNeverVisitedSource(
            neverVisitedClientId,
            CreateEmptyStates(neverVisitedClientId, AsOfDate));
        var mismatchedDateClientId = Guid.NewGuid();
        var mismatchedDate = CreateNeverVisitedSource(
            mismatchedDateClientId,
            CreateEmptyStates(mismatchedDateClientId, AsOfDate.AddDays(1)));

        Assert.True(InactiveClientsPage.TryCreate(
            new ListInactiveClientsQuery(actor, AsOfDate, ThresholdDays: 14),
            [boundary],
            hasMore: false,
            out var boundaryPage));
        Assert.Equal(14, Assert.Single(boundaryPage!.Items).DaysInactive);
        Assert.False(InactiveClientsPage.TryCreate(
            new ListInactiveClientsQuery(actor, AsOfDate, ThresholdDays: 14),
            [recent],
            hasMore: false,
            out _));
        Assert.False(InactiveClientsPage.TryCreate(
            new ListInactiveClientsQuery(actor, AsOfDate, ThresholdDays: 14),
            [future],
            hasMore: false,
            out _));
        Assert.False(InactiveClientsPage.TryCreate(
            new ListInactiveClientsQuery(actor, AsOfDate, ThresholdDays: 14),
            [neverVisited],
            hasMore: false,
            out _));
        Assert.True(InactiveClientsPage.TryCreate(
            new ListInactiveClientsQuery(
                actor,
                AsOfDate,
                ThresholdDays: 14,
                IncludeClientsWithNoVisits: true),
            [neverVisited],
            hasMore: false,
            out var noVisitPage));
        Assert.Null(Assert.Single(noVisitPage!.Items).DaysInactive);
        Assert.Null(Assert.Single(noVisitPage.Items).LastCountedVisitDate);
        Assert.False(InactiveClientsPage.TryCreate(
            new ListInactiveClientsQuery(
                actor,
                AsOfDate,
                ThresholdDays: 14,
                IncludeClientsWithNoVisits: true),
            [mismatchedDate],
            hasMore: false,
            out _));
        Assert.False(InactiveClientsPage.TryCreate(
            new ListInactiveClientsQuery(actor, AsOfDate, ThresholdDays: 14),
            [boundary, boundary],
            hasMore: false,
            out _));
    }

    [Fact]
    public void PageRejectsInvalidSelectorsAndImpossiblePagination()
    {
        var actor = CreateActor();
        var first = CreateVisitedSource(Guid.NewGuid(), AsOfDate.AddDays(-30));
        var second = CreateVisitedSource(Guid.NewGuid(), AsOfDate.AddDays(-31));

        Assert.False(InactiveClientsPage.TryCreate(
            new ListInactiveClientsQuery(actor, AsOfDate, ThresholdDays: 15),
            [first],
            hasMore: false,
            out _));
        Assert.False(InactiveClientsPage.TryCreate(
            new ListInactiveClientsQuery(
                actor,
                AsOfDate,
                ThresholdDays: 14,
                Limit: 1),
            [first, second],
            hasMore: false,
            out _));
        Assert.False(InactiveClientsPage.TryCreate(
            new ListInactiveClientsQuery(
                actor,
                AsOfDate,
                ThresholdDays: 14,
                Limit: 2),
            [first],
            hasMore: true,
            out _));
    }

    [Fact]
    public void FailureResultsAndBatchStatesNeverCarryPartialData()
    {
        var failures = new[]
        {
            ListInactiveClientsResult.Denied(),
            ListInactiveClientsResult.Invalid("Threshold is invalid.", "thresholdDays"),
            ListInactiveClientsResult.RecalculationFailed(),
            ListInactiveClientsResult.InconsistentSource(),
        };
        var membershipFailures = new[]
        {
            GetClientMembershipReportStatesResult.Denied(),
            GetClientMembershipReportStatesResult.Invalid(
                "Client ids are invalid.",
                "clientIds"),
            GetClientMembershipReportStatesResult.RecalculationFailed(),
            GetClientMembershipReportStatesResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                ListInactiveClientsStatus.PermissionDenied,
                ListInactiveClientsStatus.ValidationFailed,
                ListInactiveClientsStatus.RecalculationFailed,
                ListInactiveClientsStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
        Assert.All(membershipFailures, result =>
        {
            Assert.Null(result.States);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });

        var clientId = Guid.NewGuid();
        var state = new ClientMembershipReportState(
            clientId,
            CreateEmptyStates(clientId, AsOfDate));
        var mutableStates = new List<ClientMembershipReportState> { state };
        var collection = new ClientMembershipReportStates(AsOfDate, mutableStates);
        mutableStates.Clear();
        Assert.Single(collection.Clients);
        AssertReadOnly(collection.Clients, state);
        Assert.Throws<ArgumentException>(() => new ClientMembershipReportStates(
            AsOfDate,
            [state, state]));
    }

    private static InactiveClientSourceRow CreateVisitedSource(
        Guid clientId,
        DateOnly visitDate)
    {
        return new InactiveClientSourceRow(
            clientId,
            "Report Client",
            clientPhone: null,
            currentCardNumber: null,
            ClientOperationalStatus.Active,
            new InactiveClientLastVisit(
                Guid.NewGuid(),
                new DateTimeOffset(
                    visitDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc)),
                VisitKind.Membership),
            CreateEmptyStates(clientId, AsOfDate));
    }

    private static InactiveClientSourceRow CreateNeverVisitedSource(
        Guid clientId,
        ClientMembershipStatesReadModel states)
    {
        return new InactiveClientSourceRow(
            clientId,
            "Never Visited",
            clientPhone: null,
            currentCardNumber: null,
            ClientOperationalStatus.Active,
            lastCountedVisit: null,
            states);
    }

    private static ClientMembershipStatesReadModel CreateEmptyStates(
        Guid clientId,
        DateOnly asOfDate)
    {
        return ClientMembershipStatesPolicy.Create(clientId, asOfDate, []);
    }

    private static MembershipStateReadModel CreateMembershipState(
        Guid clientId,
        DateOnly startDate,
        DateOnly effectiveEndDate,
        int remainingVisits = 4)
    {
        const int durationDays = 30;
        const int visitsLimit = 8;
        var snapshot = new IssuedMembershipSnapshot(
            "Eight visits / 30 days",
            durationDays,
            visitsLimit,
            new Money(1200m, "UAH"));
        var terms = MembershipIssueTerms.FromIssuedSnapshot(
            Guid.NewGuid(),
            snapshot,
            startDate,
            startDate.AddDays(durationDays - 1));
        var extensionDays = effectiveEndDate.DayNumber - terms.BaseEndDate.DayNumber;
        var calculatedState = MembershipCalculatedState.FromStoredCache(
            terms,
            countedVisits: visitsLimit - remainingVisits,
            remainingVisits,
            negativeBalance: Math.Max(0, -remainingVisits),
            firstNegativeVisitId: remainingVisits < 0 ? Guid.NewGuid() : null,
            firstNegativeVisitDate: remainingVisits < 0 ? AsOfDate.AddDays(-1) : null,
            extensionDays,
            effectiveEndDate,
            lastCountedVisitAt: null);

        return new MembershipStateReadModel(
            Guid.NewGuid(),
            clientId,
            terms,
            calculatedState,
            AsOfDate);
    }

    private static ActorContext CreateActor()
    {
        return new ActorContext(
            new AccountId(Guid.NewGuid()),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(Guid.NewGuid()),
            "Reports tablet");
    }

    private static void AssertReadOnly<T>(IReadOnlyList<T> items, T item)
    {
        var list = Assert.IsAssignableFrom<IList<T>>(items);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(item));
    }
}
