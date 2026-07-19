using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Reports;

public sealed class ListNegativeClientsContractsTests
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 20);
    private static readonly DateTimeOffset LastCountedVisitAt = new(
        2026,
        7,
        18,
        17,
        30,
        0,
        TimeSpan.Zero);

    [Fact]
    public void QueryCarriesSelectorsAndUsesAcceptedDefaults()
    {
        var actor = CreateActor();

        var query = new ListNegativeClientsQuery(actor, AsOfDate);

        Assert.IsAssignableFrom<IBodyLifeQuery<ListNegativeClientsResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(AsOfDate, query.AsOfDate);
        Assert.Equal(GetNegativeMembershipStateRowsQuery.DefaultLimit, query.Limit);
        Assert.Equal(0, query.Offset);
    }

    [Fact]
    public void PageRetainsCanonicalNegativeStateAndProfileWarnings()
    {
        var actor = CreateActor();
        var firstNegativeVisitId = Guid.NewGuid();
        var firstNegativeVisitDate = AsOfDate.AddDays(-5);
        var state = CreateState(
            remainingVisits: -2,
            effectiveEndDate: AsOfDate.AddDays(-1),
            firstNegativeVisitId,
            firstNegativeVisitDate);
        var sourceRow = new NegativeMembershipStateSourceRow(
            "  Report Client  ",
            "+380 67 123 4567",
            IssuedMembershipLifecycleStatus.Active,
            state);
        var mutableRows = new List<NegativeMembershipStateSourceRow> { sourceRow };
        var source = new NegativeMembershipStateRowsPage(
            AsOfDate,
            offset: 20,
            mutableRows,
            hasMore: true);
        mutableRows.Clear();
        var query = new ListNegativeClientsQuery(
            actor,
            AsOfDate,
            Limit: 1,
            Offset: 20);

        var created = NegativeClientsPage.TryCreate(query, source, out var page);

        Assert.True(created);
        var report = Assert.IsType<NegativeClientsPage>(page);
        Assert.Equal(AsOfDate, report.AsOfDate);
        Assert.Equal(20, report.Offset);
        Assert.True(report.HasMore);
        Assert.Equal(21, report.NextOffset);
        var row = Assert.Single(report.Items);
        Assert.Equal("Report Client", row.ClientDisplayName);
        Assert.Equal("+380 67 123 4567", row.ClientPhone);
        Assert.Same(state, row.MembershipState);
        Assert.Equal(state.ClientId, row.ClientId);
        Assert.Equal(state.MembershipId, row.MembershipId);
        Assert.Equal(state.Snapshot.TypeName, row.MembershipTypeName);
        Assert.Equal(-2, row.RemainingVisits);
        Assert.Equal(2, row.NegativeBalance);
        Assert.Equal(firstNegativeVisitId, row.FirstNegativeVisitId);
        Assert.Equal(firstNegativeVisitDate, row.FirstNegativeVisitDate);
        Assert.Equal(LastCountedVisitAt, row.LastCountedVisitAt);
        Assert.Equal(AsOfDate.AddDays(-1), row.EffectiveEndDate);
        Assert.Same(state.Warnings, row.Warnings);
        Assert.Equal(
            [MembershipWarningCodes.NegativeBalance, MembershipWarningCodes.ExpiredByDate],
            row.Warnings.Select(warning => warning.Code));
        AssertReadOnly(source.Items, sourceRow);
        AssertReadOnly(report.Items, row);
    }

    [Fact]
    public void MembershipSourceKeepsExpiredAndUnprovenNegativeStateVisible()
    {
        var expired = CreateSourceRow(CreateState(
            remainingVisits: -2,
            effectiveEndDate: AsOfDate.AddDays(-30),
            Guid.NewGuid(),
            AsOfDate.AddDays(-40)));
        var openingStateWithoutVisitProvenance = CreateSourceRow(CreateState(
            remainingVisits: -1,
            effectiveEndDate: AsOfDate.AddDays(20),
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null));
        var zero = CreateSourceRow(CreateState(
            remainingVisits: 0,
            effectiveEndDate: AsOfDate.AddDays(20),
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null));
        var mismatchedDate = CreateSourceRow(CreateState(
            remainingVisits: -1,
            effectiveEndDate: AsOfDate.AddDays(20),
            Guid.NewGuid(),
            AsOfDate.AddDays(-1),
            stateAsOfDate: AsOfDate.AddDays(1)));

        var accepted = new NegativeMembershipStateRowsPage(
            AsOfDate,
            offset: 0,
            [expired, openingStateWithoutVisitProvenance],
            hasMore: false);

        Assert.Equal([2, 1], accepted.Items.Select(item => item.State.NegativeBalance));
        Assert.False(expired.State.IsActiveByDate);
        Assert.Null(openingStateWithoutVisitProvenance.State.FirstNegativeVisitId);
        Assert.Null(openingStateWithoutVisitProvenance.State.FirstNegativeVisitDate);
        Assert.Throws<ArgumentException>(() => new NegativeMembershipStateRowsPage(
            AsOfDate,
            offset: 0,
            [zero],
            hasMore: false));
        Assert.Throws<ArgumentException>(() => new NegativeMembershipStateRowsPage(
            AsOfDate,
            offset: 0,
            [new NegativeMembershipStateSourceRow(
                "Canceled Client",
                clientPhone: null,
                IssuedMembershipLifecycleStatus.Canceled,
                expired.State)],
            hasMore: false));
        Assert.Throws<ArgumentException>(() => new NegativeMembershipStateRowsPage(
            AsOfDate,
            offset: 0,
            [mismatchedDate],
            hasMore: false));
        Assert.Throws<ArgumentException>(() => new NegativeMembershipStateRowsPage(
            AsOfDate,
            offset: 0,
            [expired, expired],
            hasMore: false));
    }

    [Fact]
    public void ReportPageRejectsMismatchedOrImpossiblePaginationSources()
    {
        var actor = CreateActor();
        var firstRow = CreateSourceRow(CreateState(
            -1,
            AsOfDate.AddDays(10),
            Guid.NewGuid(),
            AsOfDate.AddDays(-1)));
        var secondRow = CreateSourceRow(CreateState(
            -2,
            AsOfDate.AddDays(10),
            Guid.NewGuid(),
            AsOfDate.AddDays(-2)));
        var oneRowPage = new NegativeMembershipStateRowsPage(
            AsOfDate,
            offset: 0,
            [firstRow],
            hasMore: false);
        var twoRowPage = new NegativeMembershipStateRowsPage(
            AsOfDate,
            offset: 0,
            [firstRow, secondRow],
            hasMore: false);
        var incompletePageWithMore = new NegativeMembershipStateRowsPage(
            AsOfDate,
            offset: 0,
            [firstRow],
            hasMore: true);

        Assert.False(NegativeClientsPage.TryCreate(
            new ListNegativeClientsQuery(actor, AsOfDate, Offset: 1),
            oneRowPage,
            out var mismatchedPage));
        Assert.Null(mismatchedPage);
        Assert.False(NegativeClientsPage.TryCreate(
            new ListNegativeClientsQuery(actor, AsOfDate, Limit: 1),
            twoRowPage,
            out var overLimitPage));
        Assert.Null(overLimitPage);
        Assert.False(NegativeClientsPage.TryCreate(
            new ListNegativeClientsQuery(actor, AsOfDate, Limit: 2),
            incompletePageWithMore,
            out var incompletePage));
        Assert.Null(incompletePage);
    }

    [Fact]
    public void FailuresNeverCarryPartialReportData()
    {
        var failures = new[]
        {
            ListNegativeClientsResult.Denied(),
            ListNegativeClientsResult.Invalid("Limit is invalid.", "limit"),
            ListNegativeClientsResult.RecalculationFailed(),
            ListNegativeClientsResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                ListNegativeClientsStatus.PermissionDenied,
                ListNegativeClientsStatus.ValidationFailed,
                ListNegativeClientsStatus.RecalculationFailed,
                ListNegativeClientsStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    private static NegativeMembershipStateSourceRow CreateSourceRow(
        MembershipStateReadModel state)
    {
        return new NegativeMembershipStateSourceRow(
            "Report Client",
            clientPhone: null,
            IssuedMembershipLifecycleStatus.Active,
            state);
    }

    private static MembershipStateReadModel CreateState(
        int remainingVisits,
        DateOnly effectiveEndDate,
        Guid? firstNegativeVisitId,
        DateOnly? firstNegativeVisitDate,
        DateOnly? stateAsOfDate = null)
    {
        const int durationDays = 30;
        const int visitsLimit = 8;
        var baseEndDate = effectiveEndDate;
        var startDate = baseEndDate.AddDays(-(durationDays - 1));
        var snapshot = new IssuedMembershipSnapshot(
            "Eight visits / 30 days",
            durationDays,
            visitsLimit,
            new Money(1200m, "UAH"));
        var terms = MembershipIssueTerms.FromIssuedSnapshot(
            Guid.NewGuid(),
            snapshot,
            startDate,
            baseEndDate);
        var calculatedState = MembershipCalculatedState.FromStoredCache(
            terms,
            countedVisits: visitsLimit - remainingVisits,
            remainingVisits,
            negativeBalance: Math.Max(0, -remainingVisits),
            firstNegativeVisitId,
            firstNegativeVisitDate,
            extensionDays: 0,
            effectiveEndDate,
            LastCountedVisitAt);

        return new MembershipStateReadModel(
            Guid.NewGuid(),
            Guid.NewGuid(),
            terms,
            calculatedState,
            stateAsOfDate ?? AsOfDate);
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
