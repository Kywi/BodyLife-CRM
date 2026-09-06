using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed partial class PostgreSqlIssueMembershipCommandTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(-2, false)]
    [InlineData(-2, true)]
    public async Task IssueClosesZeroOrUnknownPredecessorWithExactProvenance(int remaining, bool paper)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var predecessor = await PostgreSqlMembershipLifecycleTestData.CreateOpeningAsync(
            database, actor, fixture.ClientId, fixture.MembershipTypeId, remaining, ExistingStartDate, TestNow);
        var command = CreateCommand(actor, fixture, "lifecycle-rollover");
        PaperFallbackRowFixture? paperRow = null;
        if (paper)
        {
            paperRow = await PostgreSqlPaperFallbackTestData.SeedRowAsync(database, actor,
                TestNow.AddMinutes(-1), "membership_sale", TestNow, explanation: "Recovered rollover");
            command = CreatePaperCommand(actor, fixture, paperRow, TestNow.AddMinutes(-1), "lifecycle-rollover");
        }
        var preview = await PreviewLifecycleIssueAsync(context, actor, fixture);
        Assert.True(preview.Preview!.CanProceedToIssue);
        Assert.Equal(predecessor, preview.Preview.Predecessor!.MembershipId);
        Assert.Equal(remaining, preview.Preview.Predecessor.RemainingVisits);
        Assert.False(string.IsNullOrWhiteSpace(preview.Preview.Predecessor.StateVersion));
        command = command with { PreviewToken = preview.PreviewToken!.Value };
        var result = await CreateHandler(context).ExecuteAsync(command, CancellationToken.None);
        var replay = await CreateHandler(context).ExecuteAsync(command, CancellationToken.None);
        Assert.True(result.Status == CommandStatus.Success, string.Join("; ", result.Errors));
        AssertSuccessfulResult(result, fixture.ClientId);
        Assert.Equal(result.PrimaryEntityId, replay.PrimaryEntityId);
        var successor = result.PrimaryEntityId!.Value.Value;
        Assert.Equal("closed", await database.ExecuteScalarAsync<string>(
            $"select status from bodylife.issued_memberships where id = '{predecessor}'"));
        Assert.Equal(1L, await database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.issued_memberships where client_id = '{fixture.ClientId}' and status = 'active'"));
        var closureJson = await database.ExecuteScalarAsync<string>("select row_to_json(c)::text from bodylife.membership_lifecycle_closures c");
        Assert.NotNull(closureJson);
        var closure = JsonSerializer.Deserialize<LifecycleClosureTestRow>(closureJson,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        Assert.Equal(predecessor, closure.SourceMembershipId);
        Assert.Equal(successor, closure.SuccessorMembershipId);
        Assert.Null(closure.NegativeClosureId);
        Assert.Equal(remaining == 0 ? "zero_balance_rollover" : "negative_balance_rollover", closure.ReasonCode);
        Assert.Equal(actor.AccountId.Value, closure.RecordedByAccountId);
        Assert.Equal(actor.SessionId.Value, closure.SessionId);
        Assert.Equal(command.Envelope.RequestCorrelationId.Value, closure.CorrelationId);
        Assert.Equal(command.Envelope.IdempotencyKey, closure.IdempotencyKey);
        Assert.Equal(command.Envelope.OccurredAt ?? TestNow, closure.OccurredAt);
        Assert.Equal(TestNow, closure.RecordedAt);
        Assert.Equal(remaining, (await ReadCacheAsync(database, predecessor)).RemainingVisits);
        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        using var auditSummary = JsonDocument.Parse(audit.AfterSummary);
        var auditClosure = auditSummary.RootElement.GetProperty("predecessorClosure");
        Assert.Equal(closure.Id, auditClosure.GetProperty("lifecycleClosureId").GetGuid());
        Assert.Equal(predecessor, auditClosure.GetProperty("sourceMembershipId").GetGuid());
        Assert.Equal(successor, auditClosure.GetProperty("successorMembershipId").GetGuid());
        Assert.Equal(closure.ReasonCode, auditClosure.GetProperty("reasonCode").GetString());
        Assert.Equal(remaining, auditClosure.GetProperty("remainingVisitsBefore").GetInt32());
        Assert.Equal(remaining, auditClosure.GetProperty("remainingVisitsAfter").GetInt32());
        Assert.Equal(-remaining, auditClosure.GetProperty("remainingClientUnknownNegativeBalance").GetInt32());
        Assert.Equal(0, auditClosure.GetProperty("remainingClientConcreteVisitCount").GetInt32());
        if (paperRow is not null)
        {
            Assert.Equal(paperRow.EntryBatchId, closure.EntryBatchId);
            Assert.Contains(await PostgreSqlPaperFallbackTestData.ReadLinksAsync(database, paperRow.EntryBatchRowId),
                link => link.EntityType == "membership_lifecycle_closure" && link.EntityId == closure.Id);
        }
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(0)]
    [InlineData(60)]
    public async Task PositivePredecessorBlocksIssueRegardlessOfDates(int startOffset)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var predecessor = await PostgreSqlMembershipLifecycleTestData.CreateOpeningAsync(
            database, actor, fixture.ClientId, fixture.MembershipTypeId, 1,
            DateOnly.FromDateTime(TestNow.UtcDateTime).AddDays(startOffset), TestNow);
        var before = await ReadCacheAsync(database, predecessor);
        var preview = await PreviewLifecycleIssueAsync(context, actor, fixture);
        Assert.False(preview.Preview!.CanProceedToIssue);
        Assert.True(preview.Preview.Predecessor!.BlocksIssue);
        Assert.Null(preview.Preview.Predecessor.ClosureReasonCode);
        var rejected = await CreateHandler(context).ExecuteAsync(
            CreateCommand(actor, fixture, "positive-predecessor") with { PreviewToken = preview.PreviewToken!.Value },
            CancellationToken.None);
        AssertError(rejected, CommandErrorCode.MembershipNotEligible, "predecessorMembershipId");
        Assert.Equal(before, await ReadCacheAsync(database, predecessor));
        Assert.Equal(1L, await CountRowsAsync(database, "issued_memberships"));
        foreach (var table in new[] { "membership_lifecycle_closures", "payments", "business_audit_entries", "command_idempotency_keys" })
        {
            Assert.Equal(0L, await CountRowsAsync(database, table));
        }
    }

    [PostgreSqlFact]
    public async Task SameBalanceStateChangeInvalidatesSignedPredecessorBeforeWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var predecessor = await PostgreSqlMembershipLifecycleTestData.CreateOpeningAsync(
            database, actor, fixture.ClientId, fixture.MembershipTypeId, 0, ExistingStartDate, TestNow);
        var preview = await PreviewLifecycleIssueAsync(context, actor, fixture);
        await new MembershipStateCacheRebuilder(context, TimeProvider.System).RebuildAsync(predecessor);
        var current = await PreviewLifecycleIssueAsync(context, actor, fixture);
        Assert.Equal(preview.Preview!.Predecessor!.RemainingVisits, current.Preview!.Predecessor!.RemainingVisits);
        Assert.NotEqual(preview.Preview.Predecessor.StateVersion, current.Preview.Predecessor.StateVersion);
        var stale = await CreateHandler(context).ExecuteAsync(
            CreateCommand(actor, fixture, "stale-predecessor") with { PreviewToken = preview.PreviewToken!.Value },
            CancellationToken.None);
        AssertError(stale, CommandErrorCode.StaleState, "previewToken");
        Assert.Equal(1L, await CountRowsAsync(database, "issued_memberships"));
        Assert.Equal(0L, await CountRowsAsync(database, "membership_lifecycle_closures"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
    }

    [PostgreSqlFact]
    public async Task ClientSerializationAllowsForeignKeyLockWhileIssueWaitsForMembership()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var predecessor = await PostgreSqlMembershipLifecycleTestData.CreateOpeningAsync(
            database, actor, fixture.ClientId, fixture.MembershipTypeId, 0, ExistingStartDate, TestNow);
        var preview = await PreviewLifecycleIssueAsync(context, actor, fixture);
        await context.Database.OpenConnectionAsync();
        var pid = await context.Database.SqlQuery<int>($"select pg_backend_pid() as \"Value\"").SingleAsync();
        await using var locker = new NpgsqlConnection(database.ConnectionString);
        await locker.OpenAsync();
        await using var transaction = await locker.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            $"select id from bodylife.issued_memberships where id = '{predecessor}' for update", locker, transaction);
        Assert.Equal(predecessor, await command.ExecuteScalarAsync());
        var issueTask = CreateHandler(context).ExecuteAsync(
            CreateCommand(actor, fixture, "foreign-key-compatible-lock") with { PreviewToken = preview.PreviewToken!.Value },
            CancellationToken.None);
        var observedWait = false;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            observedWait = await database.ExecuteScalarAsync<bool>(
                $"select exists(select 1 from pg_stat_activity where pid = {pid} and wait_event_type = 'Lock')");
            if (observedWait) break;
            await Task.Delay(25);
        }
        // Global NonWorkingDay holds a Membership, then its Client FK requests KEY SHARE.
        command.CommandText = $"set local lock_timeout = '1s'; select id from bodylife.clients where id = '{fixture.ClientId}' for key share";
        var foreignKeyLockFailure = await Record.ExceptionAsync(async () => { await command.ExecuteScalarAsync(); });
        await transaction.RollbackAsync();
        var result = await issueTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(observedWait);
        Assert.Null(foreignKeyLockFailure);
        Assert.True(result.Status == CommandStatus.Success, string.Join("; ", result.Errors));
        AssertSuccessfulResult(result, fixture.ClientId);
        Assert.Equal(1L, await database.ExecuteScalarAsync<long>("select count(*) from bodylife.issued_memberships where status = 'active'"));
    }

    [PostgreSqlFact]
    public async Task MixedOpeningAndConcreteDebtCoversOnlyRealVisitsAndPreservesUnknownRemainder()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var predecessor = await PostgreSqlMembershipLifecycleTestData.CreateOpeningAsync(
            database, actor, fixture.ClientId, fixture.MembershipTypeId, -2, ExistingStartDate, TestNow);
        var visits = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var visit in visits)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                insert into bodylife.visits (id, client_id, occurred_at, recorded_at, recorded_by_account_id,
                    session_id, visit_kind, entry_origin, status)
                values ({visit}, {fixture.ClientId}, {TestNow.AddHours(-1)}, {TestNow.AddMinutes(-1)},
                    {actor.AccountId.Value}, {actor.SessionId.Value}, 'membership', 'normal', 'active');
                insert into bodylife.visit_consumptions (id, visit_id, client_id, visit_kind, membership_id,
                    consumption_type, source_fact_type, source_fact_id, recorded_at, recorded_by_account_id, recorded_session_id, status)
                values ({Guid.NewGuid()}, {visit}, {fixture.ClientId}, 'membership', {predecessor}, 'counted',
                    'visit', {visit}, {TestNow.AddMinutes(-1)}, {actor.AccountId.Value}, {actor.SessionId.Value}, 'active');
                """);
        }
        await new MembershipStateCacheRebuilder(context, TimeProvider.System).RebuildAsync(predecessor);
        var preview = await PreviewLifecycleIssueAsync(context, actor, fixture);
        Assert.Equal(-4, preview.Preview!.Predecessor!.RemainingVisits);
        Assert.Equal(2, preview.Preview.AutomaticCoveredNegativeVisitCount);
        Assert.Equal(2, preview.Preview.UnknownNegativeBalance);
        var issued = await CreateHandler(context).ExecuteAsync(
            CreateCommand(actor, fixture, "mixed-negative-rollover") with { PreviewToken = preview.PreviewToken!.Value },
            CancellationToken.None);
        AssertSuccessfulResult(issued, fixture.ClientId);
        Assert.Contains(MembershipWarningCodes.NegativeBalance, issued.Warnings);
        Assert.Equal(-2, (await ReadCacheAsync(database, predecessor)).RemainingVisits);
        Assert.Equal(6, (await ReadCacheAsync(database, issued.PrimaryEntityId!.Value.Value)).RemainingVisits);
        Assert.Equal(2L, await CountRowsAsync(database, "membership_negative_closure_items"));
        Assert.Equal(2L, await CountRowsAsync(database, "visits"));
        Assert.Equal("closed", await database.ExecuteScalarAsync<string>(
            $"select status from bodylife.issued_memberships where id = '{predecessor}'"));
    }

    [PostgreSqlFact]
    public async Task FinalAuditFailureRestoresZeroPredecessorAndAllowsRetry()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var predecessor = await PostgreSqlMembershipLifecycleTestData.CreateOpeningAsync(
            database, actor, fixture.ClientId, fixture.MembershipTypeId, 0, ExistingStartDate, TestNow);
        var before = await ReadCacheAsync(database, predecessor);
        var preview = await PreviewLifecycleIssueAsync(context, actor, fixture);
        var command = CreateCommand(actor, fixture, "rollover-audit-failure") with { PreviewToken = preview.PreviewToken!.Value };
        await ExecuteNonQueryAsync(database, """
            alter table bodylife.business_audit_entries add constraint ck_test_reject_rollover_audit
            check (action_type <> 'membership.issued')
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(context).ExecuteAsync(command, CancellationToken.None));
        Assert.Equal("active", await database.ExecuteScalarAsync<string>(
            $"select status from bodylife.issued_memberships where id = '{predecessor}'"));
        Assert.Equal(before, await ReadCacheAsync(database, predecessor));
        Assert.Equal(1L, await CountRowsAsync(database, "issued_memberships"));
        foreach (var table in new[] { "membership_lifecycle_closures", "payments", "business_audit_entries", "command_idempotency_keys" })
        {
            Assert.Equal(0L, await CountRowsAsync(database, table));
        }
        Assert.Empty(context.ChangeTracker.Entries());
        await ExecuteNonQueryAsync(database,
            "alter table bodylife.business_audit_entries drop constraint ck_test_reject_rollover_audit");
        var retry = await CreateHandler(context).ExecuteAsync(command, CancellationToken.None);
        AssertSuccessfulResult(retry, fixture.ClientId);
        Assert.Equal(1L, await CountRowsAsync(database, "membership_lifecycle_closures"));
    }

    private sealed record LifecycleClosureTestRow(Guid Id, Guid SourceMembershipId, Guid? SuccessorMembershipId,
        Guid? NegativeClosureId, string ReasonCode, Guid RecordedByAccountId, Guid SessionId,
        string CorrelationId, string IdempotencyKey, Guid? EntryBatchId,
        DateTimeOffset OccurredAt, DateTimeOffset RecordedAt);

    private static async Task<PreviewIssueMembershipResult> PreviewLifecycleIssueAsync(
        BodyLifeDbContext context, ActorContext actor, IssueFixture fixture)
    {
        var time = new FixedTimeProvider(TestNow);
        var result = await new PreviewIssueMembershipQueryHandler(context,
            new MembershipNegativeVisitSelector(context),
            new HmacMembershipIssuePreviewTokenService(TokenOptions(), time), time)
            .ExecuteAsync(new PreviewIssueMembershipQuery(actor, fixture.ClientId, fixture.MembershipTypeId, NewStartDate),
                CancellationToken.None);
        Assert.Equal(PreviewIssueMembershipStatus.Success, result.Status);
        return result;
    }
}
