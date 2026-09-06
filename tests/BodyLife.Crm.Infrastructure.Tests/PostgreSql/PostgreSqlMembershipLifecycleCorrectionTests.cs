using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed partial class PostgreSqlCorrectNegativeVisitCoverageCommandTests
{
    [PostgreSqlFact]
    public async Task ClosedCoveringCorrectionChecksFinalReplacementBeforeAnyWrites()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(database, ActorRole.Owner, AccountKind.Owner);
        await using var context = database.CreateDbContext();
        await RebuildSourceAsync(context, fixture.SourceMembershipId, 3);
        var first = await IssueCoveringMembershipAsync(database, context, fixture, "first-rollover", 2);
        await IssueCoveringMembershipAsync(database, context, fixture, "second-rollover", 1);
        Assert.Equal("closed", await database.ExecuteScalarAsync<string>(
            $"select status from bodylife.issued_memberships where id = '{first.MembershipId}'"));
        var auditsBefore = await database.ExecuteScalarAsync<long>("select count(*) from bodylife.business_audit_entries");
        var cacheBefore = await database.ExecuteScalarAsync<string>(
            $"select row_to_json(c)::text from bodylife.membership_state_cache c where membership_id = '{first.MembershipId}'");
        var preview = await new PreviewCorrectNegativeVisitCoverageQueryHandler(context,
            new MembershipNegativeVisitSelector(context), new FixedTimeProvider(CorrectionNow))
            .ExecuteAsync(new PreviewCorrectNegativeVisitCoverageQuery(fixture.Actor, first.ClosureId,
                NegativeVisitCoverageCorrectionMode.Cancel, "Review closed covering correction"), CancellationToken.None);
        Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.LifecycleDependency, preview.Status);
        Assert.Equal("lifecycle_dependency", preview.ErrorCode);
        var cancel = await CreateCorrectionHandler(context).ExecuteAsync(
            CreateCancelCommand(fixture, first.ClosureId, "blocked-closed-cancel"), CancellationToken.None);
        Assert.Equal(CommandErrorCode.LifecycleDependency, Assert.Single(cancel.Errors).Code);
        var replace = await CreateCorrectionHandler(context).ExecuteAsync(
            CreateReplaceMembershipCoverageCommand(fixture, first.ClosureId, "blocked-closed-replace",
                fixture.VisitIds[2], 1), CancellationToken.None);
        Assert.Equal(CommandErrorCode.LifecycleDependency, Assert.Single(replace.Errors).Code);
        Assert.Equal(auditsBefore, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.business_audit_entries"));
        Assert.Equal(cacheBefore, await database.ExecuteScalarAsync<string>(
            $"select row_to_json(c)::text from bodylife.membership_state_cache c where membership_id = '{first.MembershipId}'"));
        Assert.Equal(0L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.membership_negative_closure_corrections"));

        // Cancel alone restores +2; replacing the same two visits ends at zero and is allowed.
        var safe = await CreateCorrectionHandler(context).ExecuteAsync(
            CreateReplaceMembershipCoverageCommand(fixture, first.ClosureId, "safe-closed-replace",
                fixture.VisitIds[2], 2), CancellationToken.None);
        AssertSuccessful(safe, fixture.ClientId);
        Assert.Equal(0, await ReadRemainingAsync(database, first.MembershipId));
        Assert.Equal("closed", await database.ExecuteScalarAsync<string>(
            $"select status from bodylife.issued_memberships where id = '{first.MembershipId}'"));
        Assert.Equal(2L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.membership_lifecycle_closures"));
        var coverage = await new GetClientNegativeVisitCoverageQueryHandler(context,
            new MembershipNegativeVisitSelector(context), new FixedTimeProvider(CorrectionNow))
            .ExecuteAsync(new GetClientNegativeVisitCoverageQuery(fixture.Actor, fixture.ClientId), CancellationToken.None);
        Assert.Equal(GetClientNegativeVisitCoverageStatus.Success, coverage.Status);
    }

    [PostgreSqlFact]
    public async Task ClosedDebtRemainsInAttentionAndVisitCancellationCannotCreatePositiveCredit()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        await using var context = database.CreateDbContext();
        await RebuildSourceAsync(context, fixture.SourceMembershipId, 3);
        await IssueCoveringMembershipAsync(database, context, fixture, "negative-rollover", 2);
        var attention = await new GetReceptionAttentionCountsQueryHandler(context, new FixedTimeProvider(CorrectionNow))
            .ExecuteAsync(new GetReceptionAttentionCountsQuery(fixture.Actor, new DateOnly(2026, 7, 20), 7), CancellationToken.None);
        Assert.Equal(GetReceptionAttentionCountsStatus.Success, attention.Status);
        Assert.Equal(1, attention.NegativeClientCount);
        Assert.Equal(-1, await ReadRemainingAsync(database, fixture.SourceMembershipId));

        var time = new FixedTimeProvider(CorrectionNow);
        var cancelHandler = new CancelVisitCommandHandler(context, new BusinessAuditAppender(context),
            new PaperFallbackEntryRowBinder(context), new CancelVisitSourcePreparer(context),
            new MembershipStateRecalculator(new MembershipStateCacheRebuilder(context, time)),
            new GetMembershipStateQueryHandler(context, time), new OpenLifecycleVisitDays(), time);
        var allowed = await cancelHandler.ExecuteAsync(new CancelVisitCommand(
            CreateEnvelope(fixture.Actor, "closed-negative-visit-cancel", "Remove mistaken Visit"), fixture.VisitIds[0]),
            CancellationToken.None);
        Assert.Equal(CommandStatus.Success, allowed.Status);
        Assert.Equal(0, await ReadRemainingAsync(database, fixture.SourceMembershipId));
        var auditsBefore = await database.ExecuteScalarAsync<long>("select count(*) from bodylife.business_audit_entries");
        var cacheBefore = await database.ExecuteScalarAsync<string>(
            $"select row_to_json(c)::text from bodylife.membership_state_cache c where membership_id = '{fixture.SourceMembershipId}'");
        var blocked = await cancelHandler.ExecuteAsync(new CancelVisitCommand(
            CreateEnvelope(fixture.Actor, "closed-zero-visit-cancel", "Remove mistaken Visit"), fixture.VisitIds[1]),
            CancellationToken.None);
        Assert.Equal(CommandErrorCode.LifecycleDependency, Assert.Single(blocked.Errors).Code);
        Assert.Equal(auditsBefore, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.business_audit_entries"));
        Assert.Equal(cacheBefore, await database.ExecuteScalarAsync<string>(
            $"select row_to_json(c)::text from bodylife.membership_state_cache c where membership_id = '{fixture.SourceMembershipId}'"));
        Assert.Equal("active", await database.ExecuteScalarAsync<string>(
            $"select status from bodylife.visits where id = '{fixture.VisitIds[1]}'"));
        Assert.Equal(0L, await database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.command_idempotency_keys where idempotency_key = 'closed-zero-visit-cancel'"));
        Assert.Equal("closed", await database.ExecuteScalarAsync<string>(
            $"select status from bodylife.issued_memberships where id = '{fixture.SourceMembershipId}'"));
    }

    [PostgreSqlFact]
    public async Task FullPaperOneOffClosesCurrentAndPreservesExactHistoryAfterCancellation()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin);
        await using var context = database.CreateDbContext();
        await RebuildSourceAsync(context, fixture.SourceMembershipId, 3);
        var paper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(database, fixture.Actor,
            CorrectionNow, "negative_coverage", CorrectionNow, explanation: "Full paper debt coverage");
        var envelope = CreateEnvelope(fixture.Actor, "full-paper-oneoff", paper.Explanation) with
        {
            EntryOrigin = EntryOrigin.PaperFallback,
            EntryBatchRowId = paper.EntryBatchRowId,
        };
        var result = await CreateCloseHandler(context).ExecuteAsync(new CloseNegativeVisitsOneOffCommand(
            envelope, fixture.ClientId, fixture.VisitIds[2],
            [new NegativeVisitClosureLineSelection(fixture.OneOffTypeAId, CatalogUpdatedAt, 3)]), CancellationToken.None);
        Assert.Equal(CommandStatus.Success, result.Status);
        var closureId = result.PrimaryEntityId!.Value.Value;
        var lifecycleId = await database.ExecuteScalarAsync<Guid>(
            $"select id from bodylife.membership_lifecycle_closures where negative_closure_id = '{closureId}' and reason_code = 'one_off_zero_balance'");
        Assert.Contains(await PostgreSqlPaperFallbackTestData.ReadLinksAsync(database, paper.EntryBatchRowId),
            link => link.EntityType == "membership_lifecycle_closure" && link.EntityId == lifecycleId);
        Assert.Equal(0L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.issued_memberships where status = 'active'"));
        var coverage = await new GetClientNegativeVisitCoverageQueryHandler(context,
            new MembershipNegativeVisitSelector(context), new FixedTimeProvider(CorrectionNow))
            .ExecuteAsync(new GetClientNegativeVisitCoverageQuery(fixture.Actor, fixture.ClientId), CancellationToken.None);
        Assert.Equal(GetClientNegativeVisitCoverageStatus.Success, coverage.Status);
        var canceled = await CreateCorrectionHandler(context).ExecuteAsync(
            CreateCancelCommand(fixture, closureId, "cancel-full-paper-oneoff"), CancellationToken.None);
        AssertSuccessful(canceled, fixture.ClientId);
        Assert.Equal(3, await ReadNegativeAsync(database, fixture.SourceMembershipId));
        Assert.Equal("closed", await database.ExecuteScalarAsync<string>(
            $"select status from bodylife.issued_memberships where id = '{fixture.SourceMembershipId}'"));
        var history = await new GetClientNegativeVisitCoverageHistorySourceRowsQueryHandler(context, new GetClientAuditEntriesQueryHandler(context, new FixedTimeProvider(CorrectionNow)))
            .ExecuteAsync(new GetClientNegativeVisitCoverageHistorySourceRowsQuery(fixture.Actor, fixture.ClientId, Limit: 10), CancellationToken.None);
        Assert.Equal(GetClientNegativeVisitCoverageHistorySourceRowsStatus.Success, history.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IssueAndFullOneOffSerializeWithoutPartialLifecycleWrites(bool oneOffFirst)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(database, ActorRole.Owner, AccountKind.Owner);
        await using var issueContext = database.CreateDbContext();
        await using var closureContext = database.CreateDbContext();
        await RebuildSourceAsync(issueContext, fixture.SourceMembershipId, 3);
        var issue = new IssueMembershipCommand(CreateEnvelope(fixture.Actor, "racing-issue", "Rollover"),
            fixture.ClientId, fixture.CoveringTypeId, CatalogUpdatedAt, new DateOnly(2026, 7, 20),
            await CreateIssuePreviewTokenAsync(issueContext, fixture));
        var oneOff = new CloseNegativeVisitsOneOffCommand(CreateEnvelope(fixture.Actor, "racing-oneoff", "Full closure"),
            fixture.ClientId, fixture.VisitIds[2],
            [new NegativeVisitClosureLineSelection(fixture.OneOffTypeAId, CatalogUpdatedAt, 3)]);
        await issueContext.Database.OpenConnectionAsync();
        await closureContext.Database.OpenConnectionAsync();
        var issuePid = await issueContext.Database.SqlQuery<int>($"select pg_backend_pid() as \"Value\"").SingleAsync();
        var closurePid = await closureContext.Database.SqlQuery<int>($"select pg_backend_pid() as \"Value\"").SingleAsync();
        await using var locker = database.CreateDbContext();
        await using var transaction = await locker.Database.BeginTransactionAsync();
        await locker.Database.ExecuteSqlInterpolatedAsync(
            $"select id from bodylife.clients where id = {fixture.ClientId} for no key update");
        Task<CommandResult> RunIssue() => CreateIssueHandler(issueContext).ExecuteAsync(issue, CancellationToken.None);
        Task<CommandResult> RunOneOff() => CreateCloseHandler(closureContext).ExecuteAsync(oneOff, CancellationToken.None);
        var first = oneOffFirst ? RunOneOff() : RunIssue();
        var second = oneOffFirst ? RunIssue() : RunOneOff();
        var bothWaiting = false;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            bothWaiting = await database.ExecuteScalarAsync<bool>(
                $"select count(*) = 2 from pg_stat_activity where pid in ({issuePid}, {closurePid}) and wait_event_type = 'Lock'");
            if (bothWaiting) break;
            await Task.Delay(25);
        }
        await transaction.RollbackAsync();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(bothWaiting);
        Assert.Single(results, result => result.Status == CommandStatus.Success);
        Assert.Equal(CommandErrorCode.StaleState,
            Assert.Single(Assert.Single(results, result => result.Status == CommandStatus.Error).Errors).Code);
        Assert.Equal(1L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.membership_lifecycle_closures"));
        Assert.Equal(1L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.membership_negative_closures"));
        Assert.Equal(2L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.payments"));
        Assert.Equal(1L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.command_idempotency_keys"));
        Assert.Equal("closed", await database.ExecuteScalarAsync<string>(
            $"select status from bodylife.issued_memberships where id = '{fixture.SourceMembershipId}'"));
        Assert.InRange(await database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.issued_memberships where status = 'active'"), 0, 1);
    }

    private sealed class OpenLifecycleVisitDays : IVisitDayReconciliationStatusProvider
    {
        public Task<VisitDayReconciliationStatus> GetStatusAsync(DateOnly businessDate, CancellationToken cancellationToken = default)
            => Task.FromResult(VisitDayReconciliationStatus.Open);
    }
}
