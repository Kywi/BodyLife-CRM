using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Freezes;

public sealed class GetClientFreezeHistorySourceRowsContractsTests
{
    private const string AddedAction = "freeze.added";
    private const string CanceledAction = "freeze.canceled";

    private static readonly DateTimeOffset From = new(
        2026,
        7,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void QueryCarriesTheFreezeHistorySliceSelectors()
    {
        var actor = CreateActor();
        var clientId = Guid.NewGuid();

        var query = new GetClientFreezeHistorySourceRowsQuery(
            actor,
            clientId,
            From,
            From.AddMonths(1),
            Limit: 25,
            Offset: 50);

        Assert.IsAssignableFrom<
            IBodyLifeQuery<GetClientFreezeHistorySourceRowsResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(clientId, query.ClientId);
        Assert.Equal(From, query.OccurredFromInclusive);
        Assert.Equal(From.AddMonths(1), query.OccurredBeforeExclusive);
        Assert.Equal(25, query.Limit);
        Assert.Equal(50, query.Offset);
        Assert.Equal(50, GetClientFreezeHistorySourceRowsQuery.DefaultLimit);
        Assert.Equal(100, GetClientFreezeHistorySourceRowsQuery.MaxLimit);
        Assert.Equal(10_000, GetClientFreezeHistorySourceRowsQuery.MaxOffset);
    }

    [Fact]
    public void PageKeepsAddedAndCanceledFactsAsSeparateImmutableRows()
    {
        var clientId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var freezeId = Guid.NewGuid();
        var cancellationId = Guid.NewGuid();
        var added = CreateAddedRow(
            clientId,
            membershipId,
            freezeId,
            cancellationId);
        var canceled = CreateCanceledRow(
            clientId,
            membershipId,
            freezeId,
            cancellationId);
        var rows = new List<ClientFreezeHistorySourceRow> { canceled, added };

        var page = ClientFreezeHistorySourceRowsPage.Create(
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
                ClientFreezeHistorySourceKind.CanceledFreeze,
                ClientFreezeHistorySourceKind.AddedFreeze,
            ],
            page.Items.Select(row => row.Kind));
        Assert.Null(page.Items[0].AddedFreeze);
        Assert.NotNull(page.Items[0].Cancellation);
        var addedFreeze = Assert.IsType<FreezeHistorySource>(
            page.Items[1].AddedFreeze);
        Assert.Null(page.Items[1].Cancellation);
        Assert.Equal(3, page.Items[0].Cancellation!.Freeze.Range.InclusiveDays);
        Assert.Equal(
            FreezeCancellationSourceStatus.Canceled,
            addedFreeze.CurrentStatus);
        Assert.Equal(cancellationId, addedFreeze.CurrentCancellationId);
        Assert.Equal(CanceledAction, page.Items[0].AuditEntry.ActionType);
        Assert.Equal(AddedAction, page.Items[1].AuditEntry.ActionType);
        Assert.Equal(10, page.Offset);
        Assert.True(page.HasMore);
        Assert.Equal(12, page.NextOffset);
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<ClientFreezeHistorySourceRow>)page.Items).Add(added));
        Assert.Throws<ArgumentException>(() =>
            ClientFreezeHistorySourceRowsPage.Create(
                Guid.NewGuid(),
                null,
                null,
                0,
                [added],
                hasMore: false));
    }

    [Fact]
    public void FailureResultsNeverCarryPartialFreezeHistory()
    {
        var failures = new[]
        {
            GetClientFreezeHistorySourceRowsResult.Denied(),
            GetClientFreezeHistorySourceRowsResult.MissingClient(),
            GetClientFreezeHistorySourceRowsResult.Invalid(
                "Invalid range.",
                "occurredBeforeExclusive"),
            GetClientFreezeHistorySourceRowsResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                GetClientFreezeHistorySourceRowsStatus.PermissionDenied,
                GetClientFreezeHistorySourceRowsStatus.NotFound,
                GetClientFreezeHistorySourceRowsStatus.ValidationFailed,
                GetClientFreezeHistorySourceRowsStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    private static ClientFreezeHistorySourceRow CreateAddedRow(
        Guid clientId,
        Guid membershipId,
        Guid freezeId,
        Guid cancellationId)
    {
        var occurredAt = From;
        var recordedAt = From.AddMinutes(1);
        var audit = CreateAudit(
            freezeId,
            AddedAction,
            occurredAt,
            recordedAt,
            EntryOrigin.Normal,
            "Medical pause");
        var source = CreateFreezeSource(
            audit,
            clientId,
            membershipId,
            freezeId,
            cancellationId);
        return new ClientFreezeHistorySourceRow(
            ClientFreezeHistorySourceKind.AddedFreeze,
            clientId,
            freezeId,
            occurredAt,
            recordedAt,
            EntryOrigin.Normal,
            source,
            Cancellation: null,
            audit);
    }

    private static ClientFreezeHistorySourceRow CreateCanceledRow(
        Guid clientId,
        Guid membershipId,
        Guid freezeId,
        Guid cancellationId)
    {
        var occurredAt = From.AddDays(1);
        var recordedAt = occurredAt.AddMinutes(2);
        var audit = CreateAudit(
            freezeId,
            CanceledAction,
            occurredAt,
            recordedAt,
            EntryOrigin.ManualBackfill,
            "Mistaken freeze range");
        var freeze = CreateFreezeSource(
            audit,
            clientId,
            membershipId,
            freezeId,
            cancellationId,
            occurredAt: From,
            recordedAt: From.AddMinutes(1),
            entryOrigin: EntryOrigin.Normal);
        var cancellation = new FreezeCancellationHistorySource(
            cancellationId,
            freezeId,
            clientId,
            membershipId,
            "Mistaken freeze range",
            occurredAt,
            recordedAt,
            audit.ActorAccountId,
            audit.SessionId,
            EntryOrigin.ManualBackfill,
            Guid.NewGuid(),
            freeze);
        return new ClientFreezeHistorySourceRow(
            ClientFreezeHistorySourceKind.CanceledFreeze,
            clientId,
            freezeId,
            occurredAt,
            recordedAt,
            EntryOrigin.ManualBackfill,
            AddedFreeze: null,
            cancellation,
            audit);
    }

    private static FreezeHistorySource CreateFreezeSource(
        ClientAuditEntry audit,
        Guid clientId,
        Guid membershipId,
        Guid freezeId,
        Guid cancellationId,
        DateTimeOffset? occurredAt = null,
        DateTimeOffset? recordedAt = null,
        EntryOrigin? entryOrigin = null)
    {
        return new FreezeHistorySource(
            freezeId,
            clientId,
            membershipId,
            "Eight visits / 30 days",
            new DateRange(new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 12)),
            "Medical pause",
            occurredAt ?? audit.OccurredAt,
            recordedAt ?? audit.RecordedAt,
            audit.ActorAccountId,
            audit.SessionId,
            entryOrigin ?? audit.EntryOrigin,
            EntryBatchId: null,
            FreezeCancellationSourceStatus.Canceled,
            cancellationId);
    }

    private static ClientAuditEntry CreateAudit(
        Guid freezeId,
        string actionType,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        EntryOrigin entryOrigin,
        string reason)
    {
        return new ClientAuditEntry(
            AuditEntryId.New(),
            actionType,
            ClientAuditEntityFilter.Freeze,
            freezeId,
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
            new RequestCorrelationId($"freeze-history-{actionType}"),
            $"freeze-history-idempotency-{actionType}",
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
