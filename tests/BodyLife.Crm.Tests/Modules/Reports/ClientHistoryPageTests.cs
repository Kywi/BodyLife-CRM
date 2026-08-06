using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Reports;

public sealed class ClientHistoryPageTests
{
    [Fact]
    public void NegativeCoverageRowMustBeTheOnlyCanonicalSource()
    {
        var clientId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);
        var actor = new ActorContext(
            AccountId.New(), ActorRole.Admin, AccountKind.NamedAdmin, SessionId.New(), "Tablet");
        var audit = new ClientAuditEntry(
            AuditEntryId.New(), "membership_negative_closure.created",
            ClientAuditEntityFilter.MembershipNegativeClosure, Guid.NewGuid(), actor.AccountId,
            actor.AccountKind, actor.Role, actor.SessionId, actor.DeviceLabel, at, at,
            EntryOrigin.Normal, null, null, "{}", "{}", "{}",
            new RequestCorrelationId("negative-history-page"), "negative-history-page", false);
        var closure = new NegativeVisitCoverageClosureHistorySnapshot(
            audit.EntityId, clientId, NegativeVisitCoverageClosureMethod.OneOff, Guid.NewGuid(),
            1, null, at, at, actor.AccountId, actor.SessionId, EntryOrigin.Normal, null,
            NegativeVisitCoverageClosureHistoryStatus.Active, [], [], null, null);
        var coverage = new ClientNegativeVisitCoverageHistorySourceRow(
            ClientNegativeVisitCoverageHistorySourceKind.Created, clientId, at, at,
            EntryOrigin.Normal, closure, null, null, null, audit);
        var valid = new ClientHistorySourceRow(
            ClientHistorySourceKind.NegativeCoverageCreated, clientId, at, at,
            EntryOrigin.Normal, null, null, null, null, null, audit, coverage);

        Assert.Single(ClientHistoryPage.Create(
            clientId, null, null, [ClientHistoryEntityFilter.NegativeCoverage], 0,
            [valid], false).Items);
        Assert.Throws<ArgumentException>(() => ClientHistoryPage.Create(
            clientId, null, null, [ClientHistoryEntityFilter.NegativeCoverage], 0,
            [valid with { NegativeCoverageSourceRow = null }], false));
    }
}
