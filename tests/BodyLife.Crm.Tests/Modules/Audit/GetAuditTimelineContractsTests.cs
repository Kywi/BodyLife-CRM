using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Audit;

public sealed class GetAuditTimelineContractsTests
{
    private static readonly DateTimeOffset From = new(
        2026,
        7,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void QueryCarriesGlobalAuditSelectorsAndBoundedPagination()
    {
        var actor = CreateActor();
        var clientId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var actionTypes = new[] { "visit.marked", "visit.canceled" };

        var query = new GetAuditTimelineQuery(
            actor,
            clientId,
            AuditTimelineEntityType.Visit,
            entityId,
            From,
            From.AddMonths(1),
            actionTypes,
            Limit: 25,
            Offset: 50);

        Assert.IsAssignableFrom<IBodyLifeQuery<GetAuditTimelineResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(clientId, query.ClientId);
        Assert.Equal(AuditTimelineEntityType.Visit, query.EntityType);
        Assert.Equal(entityId, query.EntityId);
        Assert.Equal(From, query.RecordedFromInclusive);
        Assert.Equal(From.AddMonths(1), query.RecordedBeforeExclusive);
        Assert.Same(actionTypes, query.ActionTypes);
        Assert.Equal(25, query.Limit);
        Assert.Equal(50, query.Offset);
        Assert.Equal(50, GetAuditTimelineQuery.DefaultLimit);
        Assert.Equal(100, GetAuditTimelineQuery.MaxLimit);
        Assert.Equal(10_000, GetAuditTimelineQuery.MaxOffset);
        Assert.Equal(50, GetAuditTimelineQuery.MaxActionTypeCount);
        Assert.Equal(120, GetAuditTimelineQuery.MaxActionTypeLength);
    }

    [Fact]
    public void PageSnapshotsFiltersAndRowsAndComputesNextOffset()
    {
        var clientId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var actionTypes = new List<string>
        {
            " visit.marked ",
            "visit.marked",
        };
        var row = CreateEntry(entityId);
        var rows = new List<AuditTimelineEntry> { row };

        var page = AuditTimelinePage.Create(
            clientId,
            AuditTimelineEntityType.Visit,
            entityId,
            From,
            From.AddMonths(1),
            actionTypes,
            offset: 10,
            rows,
            hasMore: true);
        actionTypes.Clear();
        rows.Clear();

        Assert.Equal(clientId, page.ClientId);
        Assert.Equal(AuditTimelineEntityType.Visit, page.EntityType);
        Assert.Equal(entityId, page.EntityId);
        Assert.Equal(From, page.RecordedFromInclusive);
        Assert.Equal(From.AddMonths(1), page.RecordedBeforeExclusive);
        Assert.Equal(["visit.marked"], page.ActionTypes);
        Assert.Same(row, Assert.Single(page.Items));
        Assert.Equal(10, page.Offset);
        Assert.True(page.HasMore);
        Assert.Equal(11, page.NextOffset);
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<AuditTimelineEntry>)page.Items).Add(row));
    }

    [Fact]
    public void PageRejectsRowsOutsideCanonicalSelectorsOrOrdering()
    {
        var entityId = Guid.NewGuid();
        var older = CreateEntry(
            entityId,
            auditEntryId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
            recordedAt: From.AddDays(1));
        var newer = CreateEntry(
            entityId,
            auditEntryId: Guid.Parse("00000000-0000-0000-0000-000000000002"),
            recordedAt: From.AddDays(2));

        Assert.Throws<ArgumentException>(() => AuditTimelinePage.Create(
            clientId: null,
            AuditTimelineEntityType.Visit,
            entityId,
            From,
            From.AddMonths(1),
            ["visit.marked"],
            offset: 0,
            [older, newer],
            hasMore: false));
        Assert.Throws<ArgumentException>(() => AuditTimelinePage.Create(
            clientId: null,
            AuditTimelineEntityType.Payment,
            entityId: null,
            From,
            From.AddMonths(1),
            actionTypes: [],
            offset: 0,
            [newer],
            hasMore: false));
    }

    [Fact]
    public void FailureResultsNeverCarryPartialTimeline()
    {
        var failures = new[]
        {
            GetAuditTimelineResult.Denied(),
            GetAuditTimelineResult.MissingClient(),
            GetAuditTimelineResult.Invalid("Invalid range.", "recordedBeforeExclusive"),
            GetAuditTimelineResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                GetAuditTimelineStatus.PermissionDenied,
                GetAuditTimelineStatus.NotFound,
                GetAuditTimelineStatus.ValidationFailed,
                GetAuditTimelineStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    private static AuditTimelineEntry CreateEntry(
        Guid entityId,
        Guid? auditEntryId = null,
        DateTimeOffset? recordedAt = null)
    {
        return new AuditTimelineEntry(
            new AuditEntryId(auditEntryId ?? Guid.NewGuid()),
            "visit.marked",
            AuditTimelineEntityType.Visit,
            entityId,
            AccountId.New(),
            AccountKind.NamedAdmin,
            ActorRole.Admin,
            SessionId.New(),
            "Reception tablet",
            From.AddHours(12),
            recordedAt ?? From.AddDays(1),
            EntryOrigin.Normal,
            Reason: null,
            Comment: null,
            "{}",
            "{}",
            "{}",
            new RequestCorrelationId("audit-timeline-contract"),
            "audit-timeline-idempotency",
            ChangedAfterClose: false);
    }

    private static ActorContext CreateActor()
    {
        return new ActorContext(
            AccountId.New(),
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            SessionId.New(),
            "Reception tablet");
    }
}
