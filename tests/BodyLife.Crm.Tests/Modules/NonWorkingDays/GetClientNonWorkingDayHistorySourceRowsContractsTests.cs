using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.NonWorkingDays;

public sealed class GetClientNonWorkingDayHistorySourceRowsContractsTests
{
    private const string AddedAction = "non_working_day.added";
    private const string CorrectedAction = "non_working_day.corrected";
    private const string CanceledAction = "non_working_day.canceled";

    private static readonly DateTimeOffset From = new(
        2026,
        7,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void QueryCarriesTheNonWorkingDayHistorySliceSelectors()
    {
        var actor = CreateActor();
        var clientId = Guid.NewGuid();

        var query = new GetClientNonWorkingDayHistorySourceRowsQuery(
            actor,
            clientId,
            From,
            From.AddMonths(1),
            Limit: 25,
            Offset: 50);

        Assert.IsAssignableFrom<
            IBodyLifeQuery<GetClientNonWorkingDayHistorySourceRowsResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(clientId, query.ClientId);
        Assert.Equal(From, query.OccurredFromInclusive);
        Assert.Equal(From.AddMonths(1), query.OccurredBeforeExclusive);
        Assert.Equal(25, query.Limit);
        Assert.Equal(50, query.Offset);
        Assert.Equal(
            50,
            GetClientNonWorkingDayHistorySourceRowsQuery.DefaultLimit);
        Assert.Equal(
            100,
            GetClientNonWorkingDayHistorySourceRowsQuery.MaxLimit);
        Assert.Equal(
            10_000,
            GetClientNonWorkingDayHistorySourceRowsQuery.MaxOffset);
    }

    [Fact]
    public void PageKeepsAddedCorrectedAndCanceledSourcesImmutable()
    {
        var clientId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var actorAccountId = AccountId.New();
        var sessionId = SessionId.New();
        var added = CreateAddedRow(
            clientId,
            membershipId,
            actorAccountId,
            sessionId);
        var corrected = CreateCorrectedRow(
            clientId,
            membershipId,
            actorAccountId,
            sessionId);
        var canceled = CreateCanceledRow(
            clientId,
            membershipId,
            actorAccountId,
            sessionId);
        var rows = new List<ClientNonWorkingDayHistorySourceRow>
        {
            canceled,
            corrected,
            added,
        };

        var page = ClientNonWorkingDayHistorySourceRowsPage.Create(
            clientId,
            From,
            From.AddMonths(1),
            offset: 10,
            rows,
            hasMore: true);
        rows.Clear();

        Assert.Equal(clientId, page.ClientId);
        Assert.Equal(
            [
                ClientNonWorkingDayHistorySourceKind.Canceled,
                ClientNonWorkingDayHistorySourceKind.Corrected,
                ClientNonWorkingDayHistorySourceKind.Added,
            ],
            page.Items.Select(row => row.Kind));
        Assert.Null(page.Items[0].AddedPeriod);
        Assert.NotNull(page.Items[0].Correction);
        Assert.Null(page.Items[1].AddedPeriod);
        var correction = Assert.IsType<NonWorkingDayCorrectionHistorySource>(
            page.Items[1].Correction);
        var addedPeriod = Assert.IsType<NonWorkingDayHistoryPeriodSource>(
            page.Items[2].AddedPeriod);
        Assert.Null(page.Items[2].Correction);
        Assert.Equal(4, addedPeriod.Period.InclusiveDays);
        Assert.Single(addedPeriod.ClientApplications);
        Assert.Equal(
            addedPeriod.Period,
            addedPeriod.ClientApplications[0].AppliedRange);
        Assert.Equal(NonWorkingDayCorrectionMode.ReplaceRange, correction.Mode);
        Assert.Equal(
            NonWorkingDayCorrectionSourceStatus.Corrected,
            correction.OriginalPeriod.CurrentStatus);
        Assert.Equal(
            NonWorkingDayCorrectionSourceStatus.Active,
            correction.ReplacementPeriod!.CurrentStatus);
        Assert.Single(correction.AffectedMembershipIds);
        Assert.Equal(10, page.Offset);
        Assert.True(page.HasMore);
        Assert.Equal(13, page.NextOffset);
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<ClientNonWorkingDayHistorySourceRow>)page.Items)
                .Add(added));
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<NonWorkingDayHistoryApplicationSource>)
                addedPeriod.ClientApplications)
                .Add(addedPeriod.ClientApplications[0]));
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<Guid>)correction.AffectedMembershipIds)
                .Add(Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() =>
            ClientNonWorkingDayHistorySourceRowsPage.Create(
                Guid.NewGuid(),
                null,
                null,
                0,
                [added],
                hasMore: false));
    }

    [Fact]
    public void FailureResultsNeverCarryPartialNonWorkingDayHistory()
    {
        var failures = new[]
        {
            GetClientNonWorkingDayHistorySourceRowsResult.Denied(),
            GetClientNonWorkingDayHistorySourceRowsResult.MissingClient(),
            GetClientNonWorkingDayHistorySourceRowsResult.Invalid(
                "Invalid range.",
                "occurredBeforeExclusive"),
            GetClientNonWorkingDayHistorySourceRowsResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                GetClientNonWorkingDayHistorySourceRowsStatus.PermissionDenied,
                GetClientNonWorkingDayHistorySourceRowsStatus.NotFound,
                GetClientNonWorkingDayHistorySourceRowsStatus.ValidationFailed,
                GetClientNonWorkingDayHistorySourceRowsStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    private static ClientNonWorkingDayHistorySourceRow CreateAddedRow(
        Guid clientId,
        Guid membershipId,
        AccountId actorAccountId,
        SessionId sessionId)
    {
        var periodId = Guid.NewGuid();
        var recordedAt = From.AddMinutes(1);
        var period = CreatePeriod(
            periodId,
            clientId,
            membershipId,
            actorAccountId,
            sessionId,
            recordedAt,
            NonWorkingDayCorrectionSourceStatus.Active,
            cancellationId: null);
        var audit = CreateAudit(
            periodId,
            AddedAction,
            From,
            recordedAt,
            actorAccountId,
            sessionId,
            EntryOrigin.Normal,
            reason: null,
            comment: null);
        return new ClientNonWorkingDayHistorySourceRow(
            ClientNonWorkingDayHistorySourceKind.Added,
            clientId,
            periodId,
            From,
            recordedAt,
            EntryOrigin.Normal,
            period,
            Correction: null,
            audit);
    }

    private static ClientNonWorkingDayHistorySourceRow CreateCorrectedRow(
        Guid clientId,
        Guid membershipId,
        AccountId actorAccountId,
        SessionId sessionId)
    {
        var originalId = Guid.NewGuid();
        var replacementId = Guid.NewGuid();
        var occurredAt = From.AddDays(1);
        var recordedAt = occurredAt.AddMinutes(2);
        var original = CreatePeriod(
            originalId,
            clientId,
            membershipId,
            actorAccountId,
            sessionId,
            From.AddMinutes(1),
            NonWorkingDayCorrectionSourceStatus.Corrected,
            cancellationId: null);
        var replacement = CreatePeriod(
            replacementId,
            clientId,
            membershipId,
            actorAccountId,
            sessionId,
            recordedAt,
            NonWorkingDayCorrectionSourceStatus.Active,
            cancellationId: null);
        var audit = CreateAudit(
            originalId,
            CorrectedAction,
            occurredAt,
            recordedAt,
            actorAccountId,
            sessionId,
            EntryOrigin.PaperFallback,
            "Replace closure range",
            "Owner confirmed replacement scope");
        var correction = new NonWorkingDayCorrectionHistorySource(
            NonWorkingDayCorrectionMode.ReplaceRange,
            original,
            replacement,
            cancellation: null,
            "Replace closure range",
            "Owner confirmed replacement scope",
            occurredAt,
            recordedAt,
            actorAccountId,
            sessionId,
            EntryOrigin.PaperFallback,
            [membershipId]);
        return new ClientNonWorkingDayHistorySourceRow(
            ClientNonWorkingDayHistorySourceKind.Corrected,
            clientId,
            originalId,
            occurredAt,
            recordedAt,
            EntryOrigin.PaperFallback,
            AddedPeriod: null,
            correction,
            audit);
    }

    private static ClientNonWorkingDayHistorySourceRow CreateCanceledRow(
        Guid clientId,
        Guid membershipId,
        AccountId actorAccountId,
        SessionId sessionId)
    {
        var periodId = Guid.NewGuid();
        var cancellationId = Guid.NewGuid();
        var occurredAt = From.AddDays(2);
        var recordedAt = occurredAt.AddMinutes(3);
        var period = CreatePeriod(
            periodId,
            clientId,
            membershipId,
            actorAccountId,
            sessionId,
            From.AddMinutes(1),
            NonWorkingDayCorrectionSourceStatus.Canceled,
            cancellationId);
        var cancellation = new NonWorkingDayCancellationHistorySource(
            cancellationId,
            periodId,
            "Closure entered by mistake",
            recordedAt,
            actorAccountId,
            sessionId);
        var audit = CreateAudit(
            periodId,
            CanceledAction,
            occurredAt,
            recordedAt,
            actorAccountId,
            sessionId,
            EntryOrigin.ManualBackfill,
            cancellation.Reason,
            "Owner canceled the period");
        var correction = new NonWorkingDayCorrectionHistorySource(
            NonWorkingDayCorrectionMode.Cancel,
            period,
            replacementPeriod: null,
            cancellation,
            cancellation.Reason,
            "Owner canceled the period",
            occurredAt,
            recordedAt,
            actorAccountId,
            sessionId,
            EntryOrigin.ManualBackfill,
            [membershipId]);
        return new ClientNonWorkingDayHistorySourceRow(
            ClientNonWorkingDayHistorySourceKind.Canceled,
            clientId,
            periodId,
            occurredAt,
            recordedAt,
            EntryOrigin.ManualBackfill,
            AddedPeriod: null,
            correction,
            audit);
    }

    private static NonWorkingDayHistoryPeriodSource CreatePeriod(
        Guid periodId,
        Guid clientId,
        Guid membershipId,
        AccountId actorAccountId,
        SessionId sessionId,
        DateTimeOffset createdAt,
        NonWorkingDayCorrectionSourceStatus status,
        Guid? cancellationId)
    {
        var range = new DateRange(
            new DateOnly(2026, 7, 10),
            new DateOnly(2026, 7, 13));
        var application = new NonWorkingDayHistoryApplicationSource(
            Guid.NewGuid(),
            periodId,
            membershipId,
            clientId,
            "Eight visits / 30 days",
            range,
            createdAt.AddMinutes(-5),
            createdAt,
            status);
        return new NonWorkingDayHistoryPeriodSource(
            periodId,
            clientId,
            range,
            "maintenance",
            "Scheduled maintenance",
            createdAt,
            actorAccountId,
            sessionId,
            status,
            cancellationId,
            confirmedAffectedMembershipCount: 1,
            confirmedAffectedClientCount: 1,
            [application]);
    }

    private static ClientAuditEntry CreateAudit(
        Guid periodId,
        string actionType,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        AccountId actorAccountId,
        SessionId sessionId,
        EntryOrigin entryOrigin,
        string? reason,
        string? comment)
    {
        return new ClientAuditEntry(
            AuditEntryId.New(),
            actionType,
            ClientAuditEntityFilter.NonWorkingPeriod,
            periodId,
            actorAccountId,
            AccountKind.Owner,
            ActorRole.Owner,
            sessionId,
            "Owner laptop",
            occurredAt,
            recordedAt,
            entryOrigin,
            reason,
            comment,
            "{}",
            "{}",
            "{}",
            new RequestCorrelationId($"non-working-history-{actionType}"),
            $"non-working-history-idempotency-{actionType}",
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
