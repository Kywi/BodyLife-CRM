using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Reports;

public sealed class ListLowRemainingMembershipsContractsTests
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

        var query = new ListLowRemainingMembershipsQuery(actor, AsOfDate);

        Assert.IsAssignableFrom<IBodyLifeQuery<ListLowRemainingMembershipsResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(AsOfDate, query.AsOfDate);
        Assert.Equal(
            MembershipWarningRules.LowRemainingVisitsThreshold,
            query.RemainingVisitsThreshold);
        Assert.Equal(GetLowRemainingMembershipStateRowsQuery.DefaultLimit, query.Limit);
        Assert.Equal(0, query.Offset);
    }

    [Fact]
    public void PageRetainsCanonicalMembershipStateWithoutRecountingVisits()
    {
        var actor = CreateActor();
        var state = CreateState(remainingVisits: 2, extensionDays: 1);
        var sourceRow = new LowRemainingMembershipStateSourceRow(
            "  Report Client  ",
            "+380 67 123 4567",
            IssuedMembershipLifecycleStatus.Active,
            state);
        var mutableRows = new List<LowRemainingMembershipStateSourceRow> { sourceRow };
        var source = new LowRemainingMembershipStateRowsPage(
            AsOfDate,
            remainingVisitsThreshold: 2,
            offset: 20,
            mutableRows,
            hasMore: true);
        mutableRows.Clear();
        var query = new ListLowRemainingMembershipsQuery(
            actor,
            AsOfDate,
            Limit: 1,
            Offset: 20);

        var created = LowRemainingMembershipsPage.TryCreate(query, source, out var page);

        Assert.True(created);
        var report = Assert.IsType<LowRemainingMembershipsPage>(page);
        Assert.Equal(AsOfDate, report.AsOfDate);
        Assert.Equal(2, report.RemainingVisitsThreshold);
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
        Assert.Equal(state.Snapshot.VisitsLimit, row.VisitsLimitSnapshot);
        Assert.Equal(state.CountedVisits, row.CountedVisits);
        Assert.Equal(state.RemainingVisits, row.RemainingVisits);
        Assert.Equal(state.EffectiveEndDate, row.EffectiveEndDate);
        Assert.Equal(state.LastCountedVisitAt, row.LastCountedVisitAt);
        Assert.Same(state.Warnings, row.Warnings);
        Assert.True(row.HasExtensionExplanation);
        Assert.Equal(
            [MembershipWarningCodes.LowRemaining],
            row.Warnings.Select(warning => warning.Code));
        AssertReadOnly(source.Items, sourceRow);
        AssertReadOnly(report.Items, row);
    }

    [Fact]
    public void MembershipSourceUsesTheLiteralAtOrBelowThresholdContract()
    {
        var negative = CreateSourceRow(CreateState(remainingVisits: -1));
        var zero = CreateSourceRow(CreateState(remainingVisits: 0));
        var two = CreateSourceRow(CreateState(remainingVisits: 2));
        var aboveThreshold = CreateSourceRow(CreateState(remainingVisits: 3));
        var mismatchedDate = CreateSourceRow(CreateState(
            remainingVisits: 1,
            stateAsOfDate: AsOfDate.AddDays(1)));

        var accepted = new LowRemainingMembershipStateRowsPage(
            AsOfDate,
            remainingVisitsThreshold: 2,
            offset: 0,
            [negative, zero, two],
            hasMore: false);

        Assert.Equal([-1, 0, 2], accepted.Items.Select(item => item.State.RemainingVisits));
        Assert.Throws<ArgumentException>(() => new LowRemainingMembershipStateRowsPage(
            AsOfDate,
            remainingVisitsThreshold: 2,
            offset: 0,
            [aboveThreshold],
            hasMore: false));
        Assert.Throws<ArgumentException>(() => new LowRemainingMembershipStateRowsPage(
            AsOfDate,
            remainingVisitsThreshold: 2,
            offset: 0,
            [new LowRemainingMembershipStateSourceRow(
                "Canceled Client",
                clientPhone: null,
                IssuedMembershipLifecycleStatus.Canceled,
                two.State)],
            hasMore: false));
        Assert.Throws<ArgumentException>(() => new LowRemainingMembershipStateRowsPage(
            AsOfDate,
            remainingVisitsThreshold: 2,
            offset: 0,
            [mismatchedDate],
            hasMore: false));
        Assert.Throws<ArgumentException>(() => new LowRemainingMembershipStateRowsPage(
            AsOfDate,
            remainingVisitsThreshold: 2,
            offset: 0,
            [two, two],
            hasMore: false));
    }

    [Fact]
    public void ReportPageRejectsMismatchedOrImpossiblePaginationSources()
    {
        var actor = CreateActor();
        var firstRow = CreateSourceRow(CreateState(remainingVisits: 1));
        var secondRow = CreateSourceRow(CreateState(remainingVisits: 2));
        var oneRowPage = new LowRemainingMembershipStateRowsPage(
            AsOfDate,
            remainingVisitsThreshold: 2,
            offset: 0,
            [firstRow],
            hasMore: false);
        var twoRowPage = new LowRemainingMembershipStateRowsPage(
            AsOfDate,
            remainingVisitsThreshold: 2,
            offset: 0,
            [firstRow, secondRow],
            hasMore: false);
        var incompletePageWithMore = new LowRemainingMembershipStateRowsPage(
            AsOfDate,
            remainingVisitsThreshold: 2,
            offset: 0,
            [firstRow],
            hasMore: true);

        Assert.False(LowRemainingMembershipsPage.TryCreate(
            new ListLowRemainingMembershipsQuery(actor, AsOfDate, Offset: 1),
            oneRowPage,
            out var mismatchedPage));
        Assert.Null(mismatchedPage);
        Assert.False(LowRemainingMembershipsPage.TryCreate(
            new ListLowRemainingMembershipsQuery(actor, AsOfDate, Limit: 1),
            twoRowPage,
            out var overLimitPage));
        Assert.Null(overLimitPage);
        Assert.False(LowRemainingMembershipsPage.TryCreate(
            new ListLowRemainingMembershipsQuery(actor, AsOfDate, Limit: 2),
            incompletePageWithMore,
            out var incompletePage));
        Assert.Null(incompletePage);
    }

    [Fact]
    public void FailuresNeverCarryPartialReportData()
    {
        var failures = new[]
        {
            ListLowRemainingMembershipsResult.Denied(),
            ListLowRemainingMembershipsResult.Invalid(
                "Threshold is invalid.",
                "remainingVisitsThreshold"),
            ListLowRemainingMembershipsResult.RecalculationFailed(),
            ListLowRemainingMembershipsResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                ListLowRemainingMembershipsStatus.PermissionDenied,
                ListLowRemainingMembershipsStatus.ValidationFailed,
                ListLowRemainingMembershipsStatus.RecalculationFailed,
                ListLowRemainingMembershipsStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    private static LowRemainingMembershipStateSourceRow CreateSourceRow(
        MembershipStateReadModel state)
    {
        return new LowRemainingMembershipStateSourceRow(
            "Report Client",
            clientPhone: null,
            IssuedMembershipLifecycleStatus.Active,
            state);
    }

    private static MembershipStateReadModel CreateState(
        int remainingVisits,
        int extensionDays = 0,
        DateOnly? stateAsOfDate = null)
    {
        const int durationDays = 30;
        const int visitsLimit = 8;
        var effectiveEndDate = AsOfDate.AddDays(20);
        var baseEndDate = effectiveEndDate.AddDays(-extensionDays);
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
        var isNegative = remainingVisits < 0;
        var calculatedState = MembershipCalculatedState.FromStoredCache(
            terms,
            countedVisits: visitsLimit - remainingVisits,
            remainingVisits,
            negativeBalance: isNegative ? -remainingVisits : 0,
            firstNegativeVisitId: isNegative ? Guid.NewGuid() : null,
            firstNegativeVisitDate: isNegative ? AsOfDate.AddDays(-1) : null,
            extensionDays,
            effectiveEndDate,
            LastCountedVisitAt);
        var explanation = extensionDays == 0
            ? []
            : new[]
            {
                MembershipExtensionDay.FromStoredExplanation(
                    baseEndDate,
                    "freeze",
                    Guid.NewGuid(),
                    "Summer freeze",
                    isActive: true),
            };

        return new MembershipStateReadModel(
            Guid.NewGuid(),
            Guid.NewGuid(),
            terms,
            calculatedState,
            stateAsOfDate ?? AsOfDate,
            explanation);
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
