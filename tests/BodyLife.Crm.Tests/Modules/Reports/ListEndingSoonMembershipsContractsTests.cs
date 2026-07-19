using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Reports;

public sealed class ListEndingSoonMembershipsContractsTests
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 19);

    [Fact]
    public void QueryCarriesSelectorsAndUsesAcceptedDefaults()
    {
        var actor = CreateActor();

        var query = new ListEndingSoonMembershipsQuery(actor, AsOfDate);

        Assert.IsAssignableFrom<IBodyLifeQuery<ListEndingSoonMembershipsResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(AsOfDate, query.AsOfDate);
        Assert.Equal(MembershipWarningRules.EndingSoonDaysThreshold, query.DaysThreshold);
        Assert.Equal(GetEndingSoonMembershipStateRowsQuery.DefaultLimit, query.Limit);
        Assert.Equal(0, query.Offset);
    }

    [Fact]
    public void PageCalculatesOnlyDaysLeftAndRetainsCanonicalMembershipState()
    {
        var actor = CreateActor();
        var state = CreateState(
            AsOfDate.AddDays(3),
            remainingVisits: 1,
            extensionDays: 1);
        var sourceRow = new EndingSoonMembershipStateSourceRow(
            "  Report Client  ",
            "+380 67 123 4567",
            IssuedMembershipLifecycleStatus.Active,
            state);
        var mutableRows = new List<EndingSoonMembershipStateSourceRow> { sourceRow };
        var source = new EndingSoonMembershipStateRowsPage(
            AsOfDate,
            MembershipWarningRules.EndingSoonDaysThreshold,
            offset: 20,
            mutableRows,
            hasMore: true);
        mutableRows.Clear();
        var query = new ListEndingSoonMembershipsQuery(
            actor,
            AsOfDate,
            Limit: 1,
            Offset: 20);

        var created = EndingSoonMembershipsPage.TryCreate(query, source, out var page);

        Assert.True(created);
        var report = Assert.IsType<EndingSoonMembershipsPage>(page);
        Assert.Equal(AsOfDate, report.AsOfDate);
        Assert.Equal(MembershipWarningRules.EndingSoonDaysThreshold, report.DaysThreshold);
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
        Assert.Equal(state.EffectiveEndDate, row.EffectiveEndDate);
        Assert.Equal(state.RemainingVisits, row.RemainingVisits);
        Assert.Same(state.Warnings, row.Warnings);
        Assert.Equal(3, row.DaysLeft);
        Assert.True(row.HasExtensionExplanation);
        Assert.Equal(
            [MembershipWarningCodes.LowRemaining, MembershipWarningCodes.EndingSoon],
            row.Warnings.Select(warning => warning.Code));
        AssertReadOnly(source.Items, sourceRow);
        AssertReadOnly(report.Items, row);
    }

    [Fact]
    public void MembershipSourcePageRejectsRowsOutsideItsCanonicalSelector()
    {
        var validState = CreateState(AsOfDate.AddDays(3));
        var validRow = CreateSourceRow(validState);
        var outsideRange = CreateSourceRow(CreateState(AsOfDate.AddDays(8)));
        var mismatchedDate = CreateSourceRow(CreateState(
            AsOfDate.AddDays(3),
            stateAsOfDate: AsOfDate.AddDays(1)));

        Assert.Throws<ArgumentException>(() => new EndingSoonMembershipStateRowsPage(
            AsOfDate,
            daysThreshold: 7,
            offset: 0,
            [validRow, validRow],
            hasMore: false));
        Assert.Throws<ArgumentException>(() => new EndingSoonMembershipStateRowsPage(
            AsOfDate,
            daysThreshold: 7,
            offset: 0,
            [new EndingSoonMembershipStateSourceRow(
                "Canceled Client",
                clientPhone: null,
                IssuedMembershipLifecycleStatus.Canceled,
                validState)],
            hasMore: false));
        Assert.Throws<ArgumentException>(() => new EndingSoonMembershipStateRowsPage(
            AsOfDate,
            daysThreshold: 7,
            offset: 0,
            [outsideRange],
            hasMore: false));
        Assert.Throws<ArgumentException>(() => new EndingSoonMembershipStateRowsPage(
            AsOfDate,
            daysThreshold: 7,
            offset: 0,
            [mismatchedDate],
            hasMore: false));
    }

    [Fact]
    public void ReportPageRejectsMismatchedOrImpossiblePaginationSources()
    {
        var actor = CreateActor();
        var firstRow = CreateSourceRow(CreateState(AsOfDate.AddDays(1)));
        var secondRow = CreateSourceRow(CreateState(AsOfDate.AddDays(2)));
        var oneRowPage = new EndingSoonMembershipStateRowsPage(
            AsOfDate,
            daysThreshold: 7,
            offset: 0,
            [firstRow],
            hasMore: false);
        var twoRowPage = new EndingSoonMembershipStateRowsPage(
            AsOfDate,
            daysThreshold: 7,
            offset: 0,
            [firstRow, secondRow],
            hasMore: false);
        var incompletePageWithMore = new EndingSoonMembershipStateRowsPage(
            AsOfDate,
            daysThreshold: 7,
            offset: 0,
            [firstRow],
            hasMore: true);

        Assert.False(EndingSoonMembershipsPage.TryCreate(
            new ListEndingSoonMembershipsQuery(actor, AsOfDate, Offset: 1),
            oneRowPage,
            out var mismatchedPage));
        Assert.Null(mismatchedPage);
        Assert.False(EndingSoonMembershipsPage.TryCreate(
            new ListEndingSoonMembershipsQuery(actor, AsOfDate, Limit: 1),
            twoRowPage,
            out var overLimitPage));
        Assert.Null(overLimitPage);
        Assert.False(EndingSoonMembershipsPage.TryCreate(
            new ListEndingSoonMembershipsQuery(actor, AsOfDate, Limit: 2),
            incompletePageWithMore,
            out var incompletePage));
        Assert.Null(incompletePage);
    }

    [Fact]
    public void FailuresNeverCarryPartialReportData()
    {
        var failures = new[]
        {
            ListEndingSoonMembershipsResult.Denied(),
            ListEndingSoonMembershipsResult.Invalid(
                "Days threshold is invalid.",
                "daysThreshold"),
            ListEndingSoonMembershipsResult.RecalculationFailed(),
            ListEndingSoonMembershipsResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                ListEndingSoonMembershipsStatus.PermissionDenied,
                ListEndingSoonMembershipsStatus.ValidationFailed,
                ListEndingSoonMembershipsStatus.RecalculationFailed,
                ListEndingSoonMembershipsStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    private static EndingSoonMembershipStateSourceRow CreateSourceRow(
        MembershipStateReadModel state)
    {
        return new EndingSoonMembershipStateSourceRow(
            "Report Client",
            clientPhone: null,
            IssuedMembershipLifecycleStatus.Active,
            state);
    }

    private static MembershipStateReadModel CreateState(
        DateOnly effectiveEndDate,
        int remainingVisits = 2,
        int extensionDays = 0,
        DateOnly? stateAsOfDate = null)
    {
        const int durationDays = 30;
        const int visitsLimit = 8;
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
        var calculatedState = MembershipCalculatedState.FromStoredCache(
            terms,
            countedVisits: visitsLimit - remainingVisits,
            remainingVisits,
            negativeBalance: 0,
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays,
            effectiveEndDate,
            lastCountedVisitAt: null);
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
