using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Reports;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class GetClientHistoryQueryHandlerTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        20,
        18,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task QueryComposesExactModuleRowsInGlobalAuditOrder()
    {
        var actor = CreateActor();
        var clientId = Guid.NewGuid();
        ClientAuditEntry[] auditEntries =
        [
            CreateAuditEntry(
                actor,
                clientId,
                NonWorkingDayAuditActions.Added,
                ClientAuditEntityFilter.NonWorkingPeriod,
                TestNow),
            CreateAuditEntry(
                actor,
                clientId,
                VisitAuditActions.Canceled,
                ClientAuditEntityFilter.Visit,
                TestNow.AddMinutes(-1)),
            CreateAuditEntry(
                actor,
                clientId,
                MembershipAuditActions.Issued,
                ClientAuditEntityFilter.Membership,
                TestNow.AddMinutes(-2)),
            CreateAuditEntry(
                actor,
                clientId,
                PaymentAuditActions.Corrected,
                ClientAuditEntityFilter.Payment,
                TestNow.AddMinutes(-3)),
            CreateAuditEntry(
                actor,
                clientId,
                FreezeAuditActions.Added,
                ClientAuditEntityFilter.Freeze,
                TestNow.AddMinutes(-4)),
        ];
        var membershipRows = new[] { CreateMembershipRow(auditEntries[2], clientId) };
        var visitRows = new[] { CreateVisitRow(auditEntries[1], clientId) };
        var paymentRows = new[] { CreatePaymentRow(auditEntries[3], clientId) };
        var freezeRows = new[] { CreateFreezeRow(auditEntries[4], clientId) };
        var nonWorkingDayRows = new[]
        {
            CreateNonWorkingDayRow(auditEntries[0], clientId),
        };

        var auditHandler = new StubQueryHandler<
            GetClientAuditEntriesQuery,
            GetClientAuditEntriesResult>(query =>
                GetClientAuditEntriesResult.Succeeded(
                    ClientAuditEntriesPage.Create(
                        query.ClientId,
                        query.OccurredFromInclusive,
                        query.OccurredBeforeExclusive,
                        query.EntityFilters ?? [],
                        query.ActionTypes ?? [],
                        query.Offset,
                        auditEntries,
                        hasMore: true)));
        var membershipHandler = CreateMembershipHandler(membershipRows);
        var visitHandler = CreateVisitHandler(visitRows);
        var paymentHandler = CreatePaymentHandler(paymentRows);
        var freezeHandler = CreateFreezeHandler(freezeRows);
        var nonWorkingDayHandler = CreateNonWorkingDayHandler(nonWorkingDayRows);
        var handler = new GetClientHistoryQueryHandler(
            auditHandler,
            membershipHandler,
            visitHandler,
            paymentHandler,
            freezeHandler,
            nonWorkingDayHandler);

        var result = await handler.ExecuteAsync(
            new GetClientHistoryQuery(
                actor,
                clientId,
                TestNow.AddDays(-1),
                TestNow.AddDays(1),
                Limit: 5,
                Offset: 20),
            CancellationToken.None);

        Assert.Equal(GetClientHistoryStatus.Success, result.Status);
        var page = Assert.IsType<ClientHistoryPage>(result.Page);
        Assert.Equal(
            [
                ClientHistorySourceKind.NonWorkingDayAdded,
                ClientHistorySourceKind.VisitCanceled,
                ClientHistorySourceKind.MembershipIssued,
                ClientHistorySourceKind.PaymentCorrected,
                ClientHistorySourceKind.FreezeAdded,
            ],
            page.Items.Select(item => item.Kind).ToArray());
        Assert.Equal(
            auditEntries.Select(entry => entry.AuditEntryId),
            page.Items.Select(item => item.AuditEntry.AuditEntryId));
        Assert.Equal(20, page.Offset);
        Assert.True(page.HasMore);
        Assert.Equal(25, page.NextOffset);
        Assert.Equal(
            Enum.GetValues<ClientHistoryEntityFilter>(),
            page.EntityFilters);
        Assert.Null(result.ErrorCode);

        var auditQuery = Assert.Single(auditHandler.Queries);
        Assert.Null(auditQuery.AuditEntryIds);
        Assert.Equal(5, auditQuery.Limit);
        Assert.Equal(20, auditQuery.Offset);
        AssertExactSelection(
            Assert.Single(membershipHandler.Queries),
            auditEntries[2].AuditEntryId);
        AssertExactSelection(
            Assert.Single(visitHandler.Queries),
            auditEntries[1].AuditEntryId);
        AssertExactSelection(
            Assert.Single(paymentHandler.Queries),
            auditEntries[3].AuditEntryId);
        AssertExactSelection(
            Assert.Single(freezeHandler.Queries),
            auditEntries[4].AuditEntryId);
        AssertExactSelection(
            Assert.Single(nonWorkingDayHandler.Queries),
            auditEntries[0].AuditEntryId);
    }

    [Fact]
    public async Task InvalidFiltersAndSourceFailuresReturnNoPartialHistory()
    {
        var actor = CreateActor();
        var clientId = Guid.NewGuid();
        var auditEntry = CreateAuditEntry(
            actor,
            clientId,
            VisitAuditActions.Marked,
            ClientAuditEntityFilter.Visit,
            TestNow);
        var auditHandler = new StubQueryHandler<
            GetClientAuditEntriesQuery,
            GetClientAuditEntriesResult>(query =>
                GetClientAuditEntriesResult.Succeeded(
                    ClientAuditEntriesPage.Create(
                        query.ClientId,
                        query.OccurredFromInclusive,
                        query.OccurredBeforeExclusive,
                        query.EntityFilters ?? [],
                        query.ActionTypes ?? [],
                        query.Offset,
                        [auditEntry],
                        hasMore: false)));
        var visitHandler = new StubQueryHandler<
            GetClientVisitHistorySourceRowsQuery,
            GetClientVisitHistorySourceRowsResult>(_ =>
                GetClientVisitHistorySourceRowsResult.InconsistentSource());
        var handler = new GetClientHistoryQueryHandler(
            auditHandler,
            CreateMembershipHandler([]),
            visitHandler,
            CreatePaymentHandler([]),
            CreateFreezeHandler([]),
            CreateNonWorkingDayHandler([]));

        var invalid = await handler.ExecuteAsync(
            new GetClientHistoryQuery(
                actor,
                clientId,
                EntityFilters: [(ClientHistoryEntityFilter)999]),
            CancellationToken.None);
        var sourceFailure = await handler.ExecuteAsync(
            new GetClientHistoryQuery(
                actor,
                clientId,
                EntityFilters: [ClientHistoryEntityFilter.Visit]),
            CancellationToken.None);

        AssertFailure(
            invalid,
            GetClientHistoryStatus.ValidationFailed,
            "entityFilters");
        AssertFailure(sourceFailure, GetClientHistoryStatus.SourceInconsistent);
        Assert.Single(auditHandler.Queries);
        Assert.Single(visitHandler.Queries);
    }

    [Fact]
    public async Task SourceHandlersForwardExactAuditSelection()
    {
        var actor = CreateActor();
        var clientId = Guid.NewGuid();
        var auditEntryIds = new[] { AuditEntryId.New(), AuditEntryId.New() };
        var recordingAuditHandler = new StubQueryHandler<
            GetClientAuditEntriesQuery,
            GetClientAuditEntriesResult>(_ => GetClientAuditEntriesResult.Denied());
        var options = new DbContextOptionsBuilder<BodyLifeDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=bodylife;Username=bodylife;Password=not-used")
            .Options;
        await using var dbContext = new BodyLifeDbContext(options);

        await new GetClientMembershipHistorySourceRowsQueryHandler(
                dbContext,
                recordingAuditHandler)
            .ExecuteAsync(
                new GetClientMembershipHistorySourceRowsQuery(
                    actor,
                    clientId,
                    Limit: 2,
                    AuditEntryIds: auditEntryIds),
                CancellationToken.None);
        await new GetClientVisitHistorySourceRowsQueryHandler(
                dbContext,
                recordingAuditHandler)
            .ExecuteAsync(
                new GetClientVisitHistorySourceRowsQuery(
                    actor,
                    clientId,
                    Limit: 2,
                    AuditEntryIds: auditEntryIds),
                CancellationToken.None);
        await new GetClientPaymentHistorySourceRowsQueryHandler(
                dbContext,
                recordingAuditHandler)
            .ExecuteAsync(
                new GetClientPaymentHistorySourceRowsQuery(
                    actor,
                    clientId,
                    Limit: 2,
                    AuditEntryIds: auditEntryIds),
                CancellationToken.None);
        await new GetClientFreezeHistorySourceRowsQueryHandler(
                dbContext,
                recordingAuditHandler)
            .ExecuteAsync(
                new GetClientFreezeHistorySourceRowsQuery(
                    actor,
                    clientId,
                    Limit: 2,
                    AuditEntryIds: auditEntryIds),
                CancellationToken.None);
        await new GetClientNonWorkingDayHistorySourceRowsQueryHandler(
                dbContext,
                recordingAuditHandler)
            .ExecuteAsync(
                new GetClientNonWorkingDayHistorySourceRowsQuery(
                    actor,
                    clientId,
                    Limit: 2,
                    AuditEntryIds: auditEntryIds),
                CancellationToken.None);

        Assert.Equal(5, recordingAuditHandler.Queries.Count);
        Assert.All(recordingAuditHandler.Queries, query =>
        {
            Assert.Same(auditEntryIds, query.AuditEntryIds);
            Assert.Equal(2, query.Limit);
            Assert.Equal(0, query.Offset);
        });
    }

    [Fact]
    public void PersistenceRegistrationResolvesGetClientHistoryQuery()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] =
                    "Host=localhost;Database=bodylife;Username=bodylife;Password=not-used",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBodyLifePersistence(configuration);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(
                    IBodyLifeQueryHandler<
                        GetClientHistoryQuery,
                        GetClientHistoryResult>)
                && descriptor.ImplementationType
                    == typeof(GetClientHistoryQueryHandler)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<GetClientHistoryQuery, GetClientHistoryResult>>());
    }

    private static StubQueryHandler<
        GetClientMembershipHistorySourceRowsQuery,
        GetClientMembershipHistorySourceRowsResult> CreateMembershipHandler(
        IReadOnlyList<ClientMembershipHistorySourceRow> rows)
    {
        return new(query => GetClientMembershipHistorySourceRowsResult.Succeeded(
            ClientMembershipHistorySourceRowsPage.Create(
                query.ClientId,
                query.OccurredFromInclusive,
                query.OccurredBeforeExclusive,
                query.Offset,
                SelectRows(rows, query.AuditEntryIds),
                hasMore: false)));
    }

    private static StubQueryHandler<
        GetClientVisitHistorySourceRowsQuery,
        GetClientVisitHistorySourceRowsResult> CreateVisitHandler(
        IReadOnlyList<ClientVisitHistorySourceRow> rows)
    {
        return new(query => GetClientVisitHistorySourceRowsResult.Succeeded(
            ClientVisitHistorySourceRowsPage.Create(
                query.ClientId,
                query.OccurredFromInclusive,
                query.OccurredBeforeExclusive,
                query.Offset,
                SelectRows(rows, query.AuditEntryIds),
                hasMore: false)));
    }

    private static StubQueryHandler<
        GetClientPaymentHistorySourceRowsQuery,
        GetClientPaymentHistorySourceRowsResult> CreatePaymentHandler(
        IReadOnlyList<ClientPaymentHistorySourceRow> rows)
    {
        return new(query => GetClientPaymentHistorySourceRowsResult.Succeeded(
            ClientPaymentHistorySourceRowsPage.Create(
                query.ClientId,
                query.OccurredFromInclusive,
                query.OccurredBeforeExclusive,
                query.Offset,
                SelectRows(rows, query.AuditEntryIds),
                hasMore: false)));
    }

    private static StubQueryHandler<
        GetClientFreezeHistorySourceRowsQuery,
        GetClientFreezeHistorySourceRowsResult> CreateFreezeHandler(
        IReadOnlyList<ClientFreezeHistorySourceRow> rows)
    {
        return new(query => GetClientFreezeHistorySourceRowsResult.Succeeded(
            ClientFreezeHistorySourceRowsPage.Create(
                query.ClientId,
                query.OccurredFromInclusive,
                query.OccurredBeforeExclusive,
                query.Offset,
                SelectRows(rows, query.AuditEntryIds),
                hasMore: false)));
    }

    private static StubQueryHandler<
        GetClientNonWorkingDayHistorySourceRowsQuery,
        GetClientNonWorkingDayHistorySourceRowsResult> CreateNonWorkingDayHandler(
        IReadOnlyList<ClientNonWorkingDayHistorySourceRow> rows)
    {
        return new(query => GetClientNonWorkingDayHistorySourceRowsResult.Succeeded(
            ClientNonWorkingDayHistorySourceRowsPage.Create(
                query.ClientId,
                query.OccurredFromInclusive,
                query.OccurredBeforeExclusive,
                query.Offset,
                SelectRows(rows, query.AuditEntryIds),
                hasMore: false)));
    }

    private static IReadOnlyList<TRow> SelectRows<TRow>(
        IReadOnlyList<TRow> rows,
        IReadOnlyCollection<AuditEntryId>? auditEntryIds)
        where TRow : class
    {
        if (auditEntryIds is null)
        {
            return rows;
        }

        var selectedIds = auditEntryIds.ToHashSet();
        return rows.Where(row => selectedIds.Contains(GetAuditEntry(row).AuditEntryId))
            .ToArray();
    }

    private static ClientAuditEntry GetAuditEntry<TRow>(TRow row)
        where TRow : class
    {
        return row switch
        {
            ClientMembershipHistorySourceRow membership => membership.AuditEntry,
            ClientVisitHistorySourceRow visit => visit.AuditEntry,
            ClientPaymentHistorySourceRow payment => payment.AuditEntry,
            ClientFreezeHistorySourceRow freeze => freeze.AuditEntry,
            ClientNonWorkingDayHistorySourceRow nonWorkingDay
                => nonWorkingDay.AuditEntry,
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        };
    }

    private static ClientMembershipHistorySourceRow CreateMembershipRow(
        ClientAuditEntry auditEntry,
        Guid clientId)
    {
        return new ClientMembershipHistorySourceRow(
            ClientMembershipHistorySourceKind.IssuedMembership,
            clientId,
            auditEntry.EntityId,
            auditEntry.OccurredAt,
            auditEntry.RecordedAt,
            auditEntry.EntryOrigin,
            IssuedMembership: null,
            OpeningState: null,
            auditEntry);
    }

    private static ClientVisitHistorySourceRow CreateVisitRow(
        ClientAuditEntry auditEntry,
        Guid clientId)
    {
        return new ClientVisitHistorySourceRow(
            ClientVisitHistorySourceKind.CanceledVisit,
            clientId,
            auditEntry.EntityId,
            auditEntry.OccurredAt,
            auditEntry.RecordedAt,
            auditEntry.EntryOrigin,
            MarkedVisit: null,
            Cancellation: null,
            auditEntry);
    }

    private static ClientPaymentHistorySourceRow CreatePaymentRow(
        ClientAuditEntry auditEntry,
        Guid clientId)
    {
        return new ClientPaymentHistorySourceRow(
            ClientPaymentHistorySourceKind.CorrectedPayment,
            clientId,
            auditEntry.EntityId,
            auditEntry.OccurredAt,
            auditEntry.RecordedAt,
            auditEntry.EntryOrigin,
            CreatedPayment: null,
            Correction: null,
            Cancellation: null,
            auditEntry);
    }

    private static ClientFreezeHistorySourceRow CreateFreezeRow(
        ClientAuditEntry auditEntry,
        Guid clientId)
    {
        return new ClientFreezeHistorySourceRow(
            ClientFreezeHistorySourceKind.AddedFreeze,
            clientId,
            auditEntry.EntityId,
            auditEntry.OccurredAt,
            auditEntry.RecordedAt,
            auditEntry.EntryOrigin,
            AddedFreeze: null,
            Cancellation: null,
            auditEntry);
    }

    private static ClientNonWorkingDayHistorySourceRow CreateNonWorkingDayRow(
        ClientAuditEntry auditEntry,
        Guid clientId)
    {
        return new ClientNonWorkingDayHistorySourceRow(
            ClientNonWorkingDayHistorySourceKind.Added,
            clientId,
            auditEntry.EntityId,
            auditEntry.OccurredAt,
            auditEntry.RecordedAt,
            auditEntry.EntryOrigin,
            AddedPeriod: null,
            Correction: null,
            auditEntry);
    }

    private static ClientAuditEntry CreateAuditEntry(
        ActorContext actor,
        Guid clientId,
        string actionType,
        ClientAuditEntityFilter entityType,
        DateTimeOffset occurredAt)
    {
        return new ClientAuditEntry(
            AuditEntryId.New(),
            actionType,
            entityType,
            Guid.NewGuid(),
            actor.AccountId,
            actor.AccountKind,
            actor.Role,
            actor.SessionId,
            actor.DeviceLabel,
            occurredAt,
            occurredAt.AddSeconds(10),
            EntryOrigin.Normal,
            Reason: null,
            Comment: null,
            $"{{\"clientId\":\"{clientId}\"}}",
            "{}",
            "{}",
            new RequestCorrelationId($"history-{Guid.NewGuid():N}"),
            $"history-{Guid.NewGuid():N}",
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

    private static void AssertExactSelection(
        GetClientMembershipHistorySourceRowsQuery query,
        AuditEntryId expectedId)
    {
        Assert.Equal([expectedId], query.AuditEntryIds);
        Assert.Equal(1, query.Limit);
        Assert.Equal(0, query.Offset);
    }

    private static void AssertExactSelection(
        GetClientVisitHistorySourceRowsQuery query,
        AuditEntryId expectedId)
    {
        Assert.Equal([expectedId], query.AuditEntryIds);
        Assert.Equal(1, query.Limit);
        Assert.Equal(0, query.Offset);
    }

    private static void AssertExactSelection(
        GetClientPaymentHistorySourceRowsQuery query,
        AuditEntryId expectedId)
    {
        Assert.Equal([expectedId], query.AuditEntryIds);
        Assert.Equal(1, query.Limit);
        Assert.Equal(0, query.Offset);
    }

    private static void AssertExactSelection(
        GetClientFreezeHistorySourceRowsQuery query,
        AuditEntryId expectedId)
    {
        Assert.Equal([expectedId], query.AuditEntryIds);
        Assert.Equal(1, query.Limit);
        Assert.Equal(0, query.Offset);
    }

    private static void AssertExactSelection(
        GetClientNonWorkingDayHistorySourceRowsQuery query,
        AuditEntryId expectedId)
    {
        Assert.Equal([expectedId], query.AuditEntryIds);
        Assert.Equal(1, query.Limit);
        Assert.Equal(0, query.Offset);
    }

    private static void AssertFailure(
        GetClientHistoryResult result,
        GetClientHistoryStatus status,
        string? field = null)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Page);
        Assert.NotNull(result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        if (field is not null)
        {
            Assert.Equal(field, result.ErrorField);
        }
    }

    private sealed class StubQueryHandler<TQuery, TResult>(
        Func<TQuery, TResult> execute)
        : IBodyLifeQueryHandler<TQuery, TResult>
        where TQuery : IBodyLifeQuery<TResult>
    {
        public List<TQuery> Queries { get; } = [];

        public Task<TResult> ExecuteAsync(
            TQuery query,
            CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(execute(query));
        }
    }
}
