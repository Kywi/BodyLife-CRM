using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class GetClientMembershipHistorySourceRowsContractsTests
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
    public void QueryCarriesTheFirstClientHistorySliceSelectors()
    {
        var actor = CreateActor();
        var clientId = Guid.NewGuid();

        var query = new GetClientMembershipHistorySourceRowsQuery(
            actor,
            clientId,
            From,
            From.AddMonths(1),
            Limit: 25,
            Offset: 50);

        Assert.IsAssignableFrom<
            IBodyLifeQuery<GetClientMembershipHistorySourceRowsResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(clientId, query.ClientId);
        Assert.Equal(From, query.OccurredFromInclusive);
        Assert.Equal(From.AddMonths(1), query.OccurredBeforeExclusive);
        Assert.Equal(25, query.Limit);
        Assert.Equal(50, query.Offset);
        Assert.Equal(50, GetClientMembershipHistorySourceRowsQuery.DefaultLimit);
        Assert.Equal(100, GetClientMembershipHistorySourceRowsQuery.MaxLimit);
        Assert.Equal(10_000, GetClientMembershipHistorySourceRowsQuery.MaxOffset);
    }

    [Fact]
    public void PageSnapshotsCanonicalRowsAndComputesNextOffset()
    {
        var clientId = Guid.NewGuid();
        var row = CreateIssuedRow(clientId);
        var rows = new List<ClientMembershipHistorySourceRow> { row };

        var page = ClientMembershipHistorySourceRowsPage.Create(
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
        Assert.Same(row, Assert.Single(page.Items));
        Assert.Equal(ClientMembershipHistorySourceKind.IssuedMembership, row.Kind);
        Assert.NotNull(row.IssuedMembership);
        Assert.Null(row.OpeningState);
        Assert.Equal("membership.issued", row.AuditEntry.ActionType);
        Assert.Equal(10, page.Offset);
        Assert.True(page.HasMore);
        Assert.Equal(11, page.NextOffset);
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<ClientMembershipHistorySourceRow>)page.Items).Add(row));
        Assert.Throws<ArgumentException>(() =>
            ClientMembershipHistorySourceRowsPage.Create(
                Guid.NewGuid(),
                null,
                null,
                0,
                [row],
                hasMore: false));
    }

    [Fact]
    public void FailureResultsNeverCarryPartialCanonicalHistory()
    {
        var failures = new[]
        {
            GetClientMembershipHistorySourceRowsResult.Denied(),
            GetClientMembershipHistorySourceRowsResult.MissingClient(),
            GetClientMembershipHistorySourceRowsResult.Invalid(
                "Invalid range.",
                "occurredBeforeExclusive"),
            GetClientMembershipHistorySourceRowsResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                GetClientMembershipHistorySourceRowsStatus.PermissionDenied,
                GetClientMembershipHistorySourceRowsStatus.NotFound,
                GetClientMembershipHistorySourceRowsStatus.ValidationFailed,
                GetClientMembershipHistorySourceRowsStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    [Fact]
    public void OpeningStateHistorySourceExposesItsDeclarationAsScalarSnapshotValues()
    {
        var openingAsOfDate = new DateOnly(2026, 6, 30);
        var knownEffectiveEndDate = new DateOnly(2026, 8, 15);
        var declaration = MembershipOpeningState.FromDeclaration(
            openingAsOfDate,
            declaredRemainingVisits: -3,
            knownEffectiveEndDate,
            knownExtensionDays: 4);
        var source = new MembershipOpeningStateHistorySource(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            declaration,
            "legacy-register-42",
            "Initial migration",
            From,
            AccountId.New(),
            SessionId.New(),
            EntryBatchId: null,
            MembershipOpeningStateSourceStatus.Active);

        Assert.Equal(openingAsOfDate, source.OpeningAsOfDate);
        Assert.Equal(-3, source.DeclaredRemainingVisits);
        Assert.Equal(3, source.DeclaredNegativeBalance);
        Assert.Equal(knownEffectiveEndDate, source.KnownEffectiveEndDate);
        Assert.Equal(4, source.KnownExtensionDays);
    }

    private static ClientMembershipHistorySourceRow CreateIssuedRow(Guid clientId)
    {
        var membershipId = Guid.NewGuid();
        var recordedAt = From.AddDays(1).AddMinutes(1);
        var auditEntry = new ClientAuditEntry(
            AuditEntryId.New(),
            "membership.issued",
            ClientAuditEntityFilter.Membership,
            membershipId,
            AccountId.New(),
            AccountKind.NamedAdmin,
            ActorRole.Admin,
            SessionId.New(),
            "Reception tablet",
            From.AddDays(1),
            recordedAt,
            EntryOrigin.Normal,
            Reason: null,
            Comment: null,
            "{}",
            "{}",
            "{}",
            new RequestCorrelationId("membership-history-contract"),
            "membership-history-idempotency",
            ChangedAfterClose: false);
        var source = new IssuedMembershipHistorySource(
            membershipId,
            clientId,
            Guid.NewGuid(),
            new IssuedMembershipSnapshot(
                "Monthly",
                30,
                12,
                new Money(1200m, "UAH")),
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30),
            recordedAt,
            auditEntry.ActorAccountId,
            IssuedMembershipLifecycleStatus.Active,
            EntryBatchId: null,
            Comment: null);

        return new ClientMembershipHistorySourceRow(
            ClientMembershipHistorySourceKind.IssuedMembership,
            clientId,
            membershipId,
            auditEntry.OccurredAt,
            recordedAt,
            EntryOrigin.Normal,
            source,
            OpeningState: null,
            auditEntry);
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
