using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Visits;

public sealed class GetClientVisitHistorySourceRowsContractsTests
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
    public void QueryCarriesTheVisitHistorySliceSelectors()
    {
        var actor = CreateActor();
        var clientId = Guid.NewGuid();

        var query = new GetClientVisitHistorySourceRowsQuery(
            actor,
            clientId,
            From,
            From.AddMonths(1),
            Limit: 25,
            Offset: 50);

        Assert.IsAssignableFrom<
            IBodyLifeQuery<GetClientVisitHistorySourceRowsResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(clientId, query.ClientId);
        Assert.Equal(From, query.OccurredFromInclusive);
        Assert.Equal(From.AddMonths(1), query.OccurredBeforeExclusive);
        Assert.Equal(25, query.Limit);
        Assert.Equal(50, query.Offset);
        Assert.Equal(50, GetClientVisitHistorySourceRowsQuery.DefaultLimit);
        Assert.Equal(100, GetClientVisitHistorySourceRowsQuery.MaxLimit);
        Assert.Equal(10_000, GetClientVisitHistorySourceRowsQuery.MaxOffset);
    }

    [Fact]
    public void PageKeepsMarkedAndCanceledFactsAsSeparateImmutableRows()
    {
        var clientId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var marked = CreateMarkedRow(clientId, visitId);
        var canceled = CreateCanceledRow(clientId, visitId);
        var rows = new List<ClientVisitHistorySourceRow> { canceled, marked };

        var page = ClientVisitHistorySourceRowsPage.Create(
            clientId,
            From,
            From.AddMonths(1),
            offset: 10,
            rows,
            hasMore: true);
        rows.Clear();

        Assert.Equal(clientId, page.ClientId);
        Assert.Equal(From, page.OccurredFromInclusive);
        Assert.Equal(From.AddMonths(1), page.OccurredBeforeExclusive);
        Assert.Equal(
            [
                ClientVisitHistorySourceKind.CanceledVisit,
                ClientVisitHistorySourceKind.MarkedVisit,
            ],
            page.Items.Select(row => row.Kind));
        Assert.Null(page.Items[0].MarkedVisit);
        Assert.NotNull(page.Items[0].Cancellation);
        Assert.NotNull(page.Items[1].MarkedVisit);
        Assert.Null(page.Items[1].Cancellation);
        Assert.Equal("visit.canceled", page.Items[0].AuditEntry.ActionType);
        Assert.Equal("visit.marked", page.Items[1].AuditEntry.ActionType);
        Assert.Equal(10, page.Offset);
        Assert.True(page.HasMore);
        Assert.Equal(12, page.NextOffset);
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<ClientVisitHistorySourceRow>)page.Items).Add(marked));
        Assert.Throws<ArgumentException>(() =>
            ClientVisitHistorySourceRowsPage.Create(
                Guid.NewGuid(),
                null,
                null,
                0,
                [marked],
                hasMore: false));
    }

    [Fact]
    public void FailureResultsNeverCarryPartialVisitHistory()
    {
        var failures = new[]
        {
            GetClientVisitHistorySourceRowsResult.Denied(),
            GetClientVisitHistorySourceRowsResult.MissingClient(),
            GetClientVisitHistorySourceRowsResult.Invalid(
                "Invalid range.",
                "occurredBeforeExclusive"),
            GetClientVisitHistorySourceRowsResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                GetClientVisitHistorySourceRowsStatus.PermissionDenied,
                GetClientVisitHistorySourceRowsStatus.NotFound,
                GetClientVisitHistorySourceRowsStatus.ValidationFailed,
                GetClientVisitHistorySourceRowsStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    private static ClientVisitHistorySourceRow CreateMarkedRow(
        Guid clientId,
        Guid visitId)
    {
        var recordedAt = From.AddMinutes(1);
        var audit = CreateAudit(
            visitId,
            "visit.marked",
            From,
            recordedAt,
            EntryOrigin.Normal,
            reason: null);
        var source = new MarkedVisitHistorySource(
            visitId,
            clientId,
            From,
            recordedAt,
            audit.ActorAccountId,
            audit.SessionId,
            VisitKind.OneOff,
            EntryBatchId: null,
            Comment: null,
            ClientVisitRowStatus.Canceled,
            CurrentConsumption: null,
            CurrentCancellationId: Guid.NewGuid());
        return new ClientVisitHistorySourceRow(
            ClientVisitHistorySourceKind.MarkedVisit,
            clientId,
            visitId,
            From,
            recordedAt,
            EntryOrigin.Normal,
            source,
            Cancellation: null,
            audit);
    }

    private static ClientVisitHistorySourceRow CreateCanceledRow(
        Guid clientId,
        Guid visitId)
    {
        var occurredAt = From.AddDays(1);
        var recordedAt = occurredAt.AddMinutes(2);
        var audit = CreateAudit(
            visitId,
            "visit.canceled",
            occurredAt,
            recordedAt,
            EntryOrigin.ManualBackfill,
            "Incorrect visit");
        var source = new VisitCancellationHistorySource(
            Guid.NewGuid(),
            visitId,
            clientId,
            "Incorrect visit",
            occurredAt,
            recordedAt,
            audit.ActorAccountId,
            audit.SessionId,
            EntryBatchId: Guid.NewGuid());
        return new ClientVisitHistorySourceRow(
            ClientVisitHistorySourceKind.CanceledVisit,
            clientId,
            visitId,
            occurredAt,
            recordedAt,
            EntryOrigin.ManualBackfill,
            MarkedVisit: null,
            source,
            audit);
    }

    private static ClientAuditEntry CreateAudit(
        Guid visitId,
        string actionType,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        EntryOrigin entryOrigin,
        string? reason)
    {
        return new ClientAuditEntry(
            AuditEntryId.New(),
            actionType,
            ClientAuditEntityFilter.Visit,
            visitId,
            AccountId.New(),
            AccountKind.NamedAdmin,
            ActorRole.Admin,
            SessionId.New(),
            "Reception tablet",
            occurredAt,
            recordedAt,
            entryOrigin,
            reason,
            Comment: null,
            "{}",
            "{}",
            "{}",
            new RequestCorrelationId($"visit-history-{actionType}"),
            $"visit-history-idempotency-{actionType}",
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
