using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Reports;

public sealed class GetClientHistoryContractsTests
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
    public void QueryCarriesTypedSelectorsAndBoundedPagination()
    {
        var actor = CreateActor();
        var clientId = Guid.NewGuid();
        var filters = new[]
        {
            ClientHistoryEntityFilter.Visit,
            ClientHistoryEntityFilter.Payment,
        };

        var query = new GetClientHistoryQuery(
            actor,
            clientId,
            From,
            From.AddMonths(1),
            filters,
            Limit: 25,
            Offset: 50);

        Assert.IsAssignableFrom<IBodyLifeQuery<GetClientHistoryResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(clientId, query.ClientId);
        Assert.Equal(From, query.OccurredFromInclusive);
        Assert.Equal(From.AddMonths(1), query.OccurredBeforeExclusive);
        Assert.Same(filters, query.EntityFilters);
        Assert.Equal(25, query.Limit);
        Assert.Equal(50, query.Offset);
        Assert.Equal(GetClientAuditEntriesQuery.DefaultLimit, GetClientHistoryQuery.DefaultLimit);
        Assert.Equal(GetClientAuditEntriesQuery.MaxLimit, GetClientHistoryQuery.MaxLimit);
        Assert.Equal(GetClientAuditEntriesQuery.MaxOffset, GetClientHistoryQuery.MaxOffset);
    }

    [Fact]
    public void PageSnapshotsCanonicalRowsAndComputesNextOffset()
    {
        var clientId = Guid.NewGuid();
        var auditEntry = CreateAuditEntry(clientId);
        var visitSource = new ClientVisitHistorySourceRow(
            ClientVisitHistorySourceKind.MarkedVisit,
            clientId,
            auditEntry.EntityId,
            auditEntry.OccurredAt,
            auditEntry.RecordedAt,
            auditEntry.EntryOrigin,
            MarkedVisit: null,
            Cancellation: null,
            auditEntry);
        var row = new ClientHistorySourceRow(
            ClientHistorySourceKind.VisitMarked,
            clientId,
            auditEntry.OccurredAt,
            auditEntry.RecordedAt,
            auditEntry.EntryOrigin,
            MembershipSourceRow: null,
            visitSource,
            PaymentSourceRow: null,
            FreezeSourceRow: null,
            NonWorkingDaySourceRow: null,
            auditEntry);
        var filters = new List<ClientHistoryEntityFilter>
        {
            ClientHistoryEntityFilter.Visit,
            ClientHistoryEntityFilter.Visit,
        };
        var rows = new List<ClientHistorySourceRow> { row };

        var page = ClientHistoryPage.Create(
            clientId,
            From,
            From.AddMonths(1),
            filters,
            offset: 10,
            rows,
            hasMore: true);
        filters.Clear();
        rows.Clear();

        Assert.Equal([ClientHistoryEntityFilter.Visit], page.EntityFilters);
        Assert.Same(row, Assert.Single(page.Items));
        Assert.Equal(10, page.Offset);
        Assert.True(page.HasMore);
        Assert.Equal(11, page.NextOffset);
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<ClientHistorySourceRow>)page.Items).Add(row));
        Assert.Throws<ArgumentException>(() => ClientHistoryPage.Create(
            clientId,
            From,
            From.AddMonths(1),
            [ClientHistoryEntityFilter.Visit],
            offset: 0,
            [row with { ClientId = Guid.NewGuid() }],
            hasMore: false));
    }

    [Fact]
    public void FailureResultsNeverCarryPartialHistory()
    {
        var failures = new[]
        {
            GetClientHistoryResult.Denied(),
            GetClientHistoryResult.MissingClient(),
            GetClientHistoryResult.Invalid("Invalid range.", "occurredBeforeExclusive"),
            GetClientHistoryResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                GetClientHistoryStatus.PermissionDenied,
                GetClientHistoryStatus.NotFound,
                GetClientHistoryStatus.ValidationFailed,
                GetClientHistoryStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    private static ClientAuditEntry CreateAuditEntry(Guid clientId)
    {
        return new ClientAuditEntry(
            AuditEntryId.New(),
            "visit.marked",
            ClientAuditEntityFilter.Visit,
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
            $"{{\"clientId\":\"{clientId}\"}}",
            "{}",
            "{}",
            new RequestCorrelationId("history-contract"),
            "history-idempotency",
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
