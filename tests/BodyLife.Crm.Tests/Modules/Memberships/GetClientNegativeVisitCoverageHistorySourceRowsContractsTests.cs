using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class GetClientNegativeVisitCoverageHistorySourceRowsContractsTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void QueryCarriesTheCanonicalHistorySelectors()
    {
        var ids = new[] { AuditEntryId.New() };
        var query = new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
            CreateActor(), Guid.NewGuid(), At, At.AddDays(1), 25, 4, ids);

        Assert.IsAssignableFrom<
            IBodyLifeQuery<GetClientNegativeVisitCoverageHistorySourceRowsResult>>(query);
        Assert.Same(ids, query.AuditEntryIds);
        Assert.Equal(25, query.Limit);
        Assert.Equal(4, query.Offset);
        Assert.Equal(50, GetClientNegativeVisitCoverageHistorySourceRowsQuery.DefaultLimit);
    }

    [Fact]
    public void PageSnapshotsRowsAndRejectsAnotherClient()
    {
        var clientId = Guid.NewGuid();
        var row = CreateRow(clientId);
        var source = new List<ClientNegativeVisitCoverageHistorySourceRow> { row };
        var page = ClientNegativeVisitCoverageHistorySourceRowsPage.Create(
            clientId, At, At.AddDays(1), 2, source, hasMore: true);
        source.Clear();

        Assert.Same(row, Assert.Single(page.Items));
        Assert.Equal(3, page.NextOffset);
        Assert.Throws<ArgumentException>(() =>
            ClientNegativeVisitCoverageHistorySourceRowsPage.Create(
                Guid.NewGuid(), null, null, 0, [row], false));
    }

    [Fact]
    public void FailureResultsNeverExposePartialCanonicalRows()
    {
        var results = new[]
        {
            GetClientNegativeVisitCoverageHistorySourceRowsResult.Denied(),
            GetClientNegativeVisitCoverageHistorySourceRowsResult.MissingClient(),
            GetClientNegativeVisitCoverageHistorySourceRowsResult.Invalid("Invalid range.", "range"),
            GetClientNegativeVisitCoverageHistorySourceRowsResult.InconsistentSource(),
        };

        Assert.All(results, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    private static ClientNegativeVisitCoverageHistorySourceRow CreateRow(Guid clientId)
    {
        var actor = CreateActor();
        var audit = new ClientAuditEntry(
            AuditEntryId.New(),
            "membership_negative_closure.created",
            ClientAuditEntityFilter.MembershipNegativeClosure,
            Guid.NewGuid(), actor.AccountId, actor.AccountKind, actor.Role, actor.SessionId,
            actor.DeviceLabel, At, At, EntryOrigin.Normal, null, null, "{}", "{}", "{}",
            new RequestCorrelationId("negative-history-contract"), "negative-history", false);
        var closure = new NegativeVisitCoverageClosureHistorySnapshot(
            audit.EntityId, clientId, NegativeVisitCoverageClosureMethod.OneOff,
            Guid.NewGuid(), 1, null, At, At, actor.AccountId, actor.SessionId,
            EntryOrigin.Normal, null, NegativeVisitCoverageClosureHistoryStatus.Active,
            [], [], null, null);
        return new ClientNegativeVisitCoverageHistorySourceRow(
            ClientNegativeVisitCoverageHistorySourceKind.Created, clientId, At, At,
            EntryOrigin.Normal, closure, null, null, null, audit);
    }

    private static ActorContext CreateActor() => new(
        AccountId.New(), ActorRole.Admin, AccountKind.NamedAdmin, SessionId.New(), "Tablet");
}
