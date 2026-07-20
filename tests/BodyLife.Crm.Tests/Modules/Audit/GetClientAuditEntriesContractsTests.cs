using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Audit;

public sealed class GetClientAuditEntriesContractsTests
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
    public void QueryCarriesClientHistorySelectorsAndBoundedPagination()
    {
        var actor = CreateActor();
        var clientId = Guid.NewGuid();
        var filters = new[]
        {
            ClientAuditEntityFilter.Visit,
            ClientAuditEntityFilter.Payment,
        };
        var actionTypes = new[] { "visit.marked", "payment.created" };
        var auditEntryIds = new[] { AuditEntryId.New(), AuditEntryId.New() };

        var query = new GetClientAuditEntriesQuery(
            actor,
            clientId,
            From,
            From.AddMonths(1),
            filters,
            actionTypes,
            Limit: 25,
            Offset: 0,
            AuditEntryIds: auditEntryIds);

        Assert.IsAssignableFrom<IBodyLifeQuery<GetClientAuditEntriesResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(clientId, query.ClientId);
        Assert.Equal(From, query.OccurredFromInclusive);
        Assert.Equal(From.AddMonths(1), query.OccurredBeforeExclusive);
        Assert.Same(filters, query.EntityFilters);
        Assert.Same(actionTypes, query.ActionTypes);
        Assert.Same(auditEntryIds, query.AuditEntryIds);
        Assert.Equal(25, query.Limit);
        Assert.Equal(0, query.Offset);
        Assert.Equal(50, GetClientAuditEntriesQuery.DefaultLimit);
        Assert.Equal(100, GetClientAuditEntriesQuery.MaxLimit);
        Assert.Equal(50, GetClientAuditEntriesQuery.MaxActionTypeCount);
        Assert.Equal(120, GetClientAuditEntriesQuery.MaxActionTypeLength);
        Assert.Equal(100, GetClientAuditEntriesQuery.MaxAuditEntryIdCount);
    }

    [Fact]
    public void PageSnapshotsRowsAndFiltersAndComputesNextOffset()
    {
        var clientId = Guid.NewGuid();
        var filters = new List<ClientAuditEntityFilter>
        {
            ClientAuditEntityFilter.Visit,
            ClientAuditEntityFilter.Visit,
        };
        var actionTypes = new List<string>
        {
            " visit.marked ",
            "visit.marked",
        };
        var row = CreateEntry(ClientAuditEntityFilter.Visit);
        var rows = new List<ClientAuditEntry> { row };

        var page = ClientAuditEntriesPage.Create(
            clientId,
            From,
            From.AddMonths(1),
            filters,
            actionTypes,
            offset: 10,
            rows,
            hasMore: true);
        filters.Clear();
        actionTypes.Clear();
        rows.Clear();

        Assert.Equal(clientId, page.ClientId);
        Assert.Equal(From, page.OccurredFromInclusive);
        Assert.Equal(From.AddMonths(1), page.OccurredBeforeExclusive);
        Assert.Equal([ClientAuditEntityFilter.Visit], page.EntityFilters);
        Assert.Equal(["visit.marked"], page.ActionTypes);
        Assert.Same(row, Assert.Single(page.Items));
        Assert.Equal(10, page.Offset);
        Assert.True(page.HasMore);
        Assert.Equal(11, page.NextOffset);
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<ClientAuditEntry>)page.Items).Add(row));
    }

    [Fact]
    public void FailureResultsNeverCarryPartialHistory()
    {
        var failures = new[]
        {
            GetClientAuditEntriesResult.Denied(),
            GetClientAuditEntriesResult.MissingClient(),
            GetClientAuditEntriesResult.Invalid("Invalid range.", "occurredBeforeExclusive"),
            GetClientAuditEntriesResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                GetClientAuditEntriesStatus.PermissionDenied,
                GetClientAuditEntriesStatus.NotFound,
                GetClientAuditEntriesStatus.ValidationFailed,
                GetClientAuditEntriesStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    private static ClientAuditEntry CreateEntry(ClientAuditEntityFilter entityType)
    {
        return new ClientAuditEntry(
            AuditEntryId.New(),
            "visit.marked",
            entityType,
            Guid.NewGuid(),
            AccountId.New(),
            AccountKind.NamedAdmin,
            ActorRole.Admin,
            SessionId.New(),
            "Reception tablet",
            From.AddDays(1),
            From.AddDays(1).AddMinutes(1),
            EntryOrigin.Normal,
            Reason: null,
            Comment: null,
            "{}",
            "{}",
            "{}",
            new RequestCorrelationId("audit-contract"),
            "audit-idempotency",
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
