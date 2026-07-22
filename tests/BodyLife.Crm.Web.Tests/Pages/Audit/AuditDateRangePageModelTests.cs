using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Localization;
using BodyLife.Crm.Web.Operations;
using BodyLife.Crm.Web.Pages.Audit;
using BodyLife.Crm.Web.Tests.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BodyLife.Crm.Web.Tests.Pages.Audit;

[Collection(nameof(LocalizationCollection))]
public sealed class AuditDateRangePageModelTests
{
    [Theory]
    [InlineData(2026, 3, 29, "2026-03-28T22:00:00+00:00", "2026-03-29T21:00:00+00:00")]
    [InlineData(2026, 10, 25, "2026-10-24T21:00:00+00:00", "2026-10-25T22:00:00+00:00")]
    public async Task TimelineUsesExactKyivUtcBoundsForRecordedDate(
        int year,
        int month,
        int day,
        string expectedFrom,
        string expectedTo)
    {
        using var cultureScope = new CultureScope("en-US");
        var queryHandler = new CapturingQueryHandler<GetAuditTimelineQuery, GetAuditTimelineResult>(
            GetAuditTimelineResult.Denied());
        var model = new TimelineModel(
            RequestContextResolver(),
            queryHandler,
            Presentation(),
            ExplanationPresenter());
        model.RecordedFromDate = model.RecordedToDate = new DateOnly(year, month, day);

        await model.OnGetAsync(CancellationToken.None);

        var query = Assert.Single(queryHandler.Queries);
        Assert.Equal(DateTimeOffset.Parse(expectedFrom), query.RecordedFromInclusive);
        Assert.Equal(DateTimeOffset.Parse(expectedTo), query.RecordedBeforeExclusive);
    }

    [Theory]
    [InlineData(1, 1, 1, 2026, 3, 29)]
    [InlineData(2026, 3, 29, 9999, 12, 31)]
    [InlineData(2026, 10, 25, 2026, 3, 29)]
    public async Task TimelineRejectsExtremeOrReversedRangeWithoutQuery(
        int fromYear,
        int fromMonth,
        int fromDay,
        int toYear,
        int toMonth,
        int toDay)
    {
        using var cultureScope = new CultureScope("en-US");
        var queryHandler = new CapturingQueryHandler<GetAuditTimelineQuery, GetAuditTimelineResult>(
            GetAuditTimelineResult.Denied());
        var model = new TimelineModel(
            RequestContextResolver(),
            queryHandler,
            Presentation(),
            ExplanationPresenter())
        {
            RecordedFromDate = new DateOnly(fromYear, fromMonth, fromDay),
            RecordedToDate = new DateOnly(toYear, toMonth, toDay),
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Empty(queryHandler.Queries);
        Assert.Equal(Presentation().Text("Error.InvalidRange"), model.LoadError);
    }

    [Theory]
    [InlineData(2026, 3, 29, "2026-03-28T22:00:00+00:00", "2026-03-29T21:00:00+00:00")]
    [InlineData(2026, 10, 25, "2026-10-24T21:00:00+00:00", "2026-10-25T22:00:00+00:00")]
    public async Task ClientHistoryUsesExactKyivUtcBoundsForOccurredDate(
        int year,
        int month,
        int day,
        string expectedFrom,
        string expectedTo)
    {
        using var cultureScope = new CultureScope("en-US");
        var historyHandler = new CapturingQueryHandler<GetClientHistoryQuery, GetClientHistoryResult>(
            GetClientHistoryResult.Denied());
        var model = new ClientHistoryModel(
            RequestContextResolver(),
            historyHandler,
            new CapturingQueryHandler<GetClientProfileQuery, GetClientProfileResult>(
                GetClientProfileResult.Denied()),
            Presentation(),
            new ClientHistoryRowPresenter(Presentation()))
        {
            ClientId = Guid.NewGuid(),
            OccurredFromDate = new DateOnly(year, month, day),
            OccurredToDate = new DateOnly(year, month, day),
        };

        await model.OnGetAsync(CancellationToken.None);

        var query = Assert.Single(historyHandler.Queries);
        Assert.Equal(DateTimeOffset.Parse(expectedFrom), query.OccurredFromInclusive);
        Assert.Equal(DateTimeOffset.Parse(expectedTo), query.OccurredBeforeExclusive);
    }

    [Theory]
    [InlineData(1, 1, 1, 2026, 3, 29)]
    [InlineData(2026, 3, 29, 9999, 12, 31)]
    [InlineData(2026, 10, 25, 2026, 3, 29)]
    public async Task ClientHistoryRejectsExtremeOrReversedRangeWithoutQuery(
        int fromYear,
        int fromMonth,
        int fromDay,
        int toYear,
        int toMonth,
        int toDay)
    {
        using var cultureScope = new CultureScope("en-US");
        var historyHandler = new CapturingQueryHandler<GetClientHistoryQuery, GetClientHistoryResult>(
            GetClientHistoryResult.Denied());
        var model = new ClientHistoryModel(
            RequestContextResolver(),
            historyHandler,
            new CapturingQueryHandler<GetClientProfileQuery, GetClientProfileResult>(
                GetClientProfileResult.Denied()),
            Presentation(),
            new ClientHistoryRowPresenter(Presentation()))
        {
            ClientId = Guid.NewGuid(),
            OccurredFromDate = new DateOnly(fromYear, fromMonth, fromDay),
            OccurredToDate = new DateOnly(toYear, toMonth, toDay),
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Empty(historyHandler.Queries);
        Assert.Equal(Presentation().Text("Error.InvalidRange"), model.LoadError);
    }

    private static IBodyLifeRequestContextResolver RequestContextResolver() =>
        new StubRequestContextResolver(new BodyLifeRequestContext(
            new ActorContext(
                new AccountId(Guid.NewGuid()),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(Guid.NewGuid()),
                "test"),
            new RequestCorrelationId("test-correlation")));

    private static AuditPresentation Presentation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBodyLifeLocalization();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<AuditPresentation>();
    }

    private static AuditEntryExplanationPresenter ExplanationPresenter() => null!;

    private sealed class CapturingQueryHandler<TQuery, TResult>(TResult result)
        : IBodyLifeQueryHandler<TQuery, TResult>
        where TQuery : IBodyLifeQuery<TResult>
    {
        public List<TQuery> Queries { get; } = [];

        public Task<TResult> ExecuteAsync(TQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(result);
        }
    }

    private sealed class StubRequestContextResolver(BodyLifeRequestContext context)
        : IBodyLifeRequestContextResolver
    {
        public bool TryResolve([NotNullWhen(true)] out BodyLifeRequestContext? requestContext)
        {
            requestContext = context;
            return true;
        }

        public BodyLifeRequestContext Require() => context;

        public CommandEnvelope CreateCommandEnvelope(
            EntryOrigin entryOrigin = EntryOrigin.Normal,
            DateTimeOffset? occurredAt = null,
            string? idempotencyKey = null,
            string? reason = null,
            string? comment = null) => throw new NotSupportedException();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string culture)
        {
            var selected = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = selected;
            CultureInfo.CurrentUICulture = selected;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }
}
