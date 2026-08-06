using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlCloseNegativeVisitsOneOffCommandTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        20,
        12,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task PartialClosureUsesOldestVisitsExactPaymentSnapshotsAndRebuild()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId);
        var command = CreateCommand(
            fixture,
            "partial-two-types",
            fixture.VisitIds[2],
            [
                new NegativeVisitClosureLineSelection(
                    fixture.OneOffTypeAId,
                    TestNow,
                    1),
                new NegativeVisitClosureLineSelection(
                    fixture.OneOffTypeBId,
                    TestNow,
                    1),
            ]);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);

        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Equal(
            CloseNegativeVisitsOneOffCommand.PrimaryEntityType,
            result.PrimaryEntityId!.Value.Type);
        Assert.Equal(new EntityId("client", fixture.ClientId), result.RereadTargetId);
        Assert.Contains(MembershipWarningCodes.NegativeBalance, result.Warnings);
        Assert.Empty(result.Errors);
        var closureId = result.PrimaryEntityId.Value.Value;

        Assert.Equal(
            1,
            await database.ExecuteScalarAsync<int>(
                $"select negative_balance from bodylife.membership_state_cache where membership_id = '{fixture.SourceMembershipId}'"));
        Assert.Equal(
            125m,
            await database.ExecuteScalarAsync<decimal>(
                $"select amount from bodylife.payments where negative_closure_id = '{closureId}' and status = 'active'"));
        Assert.Equal(
            2L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.membership_negative_closure_lines where negative_closure_id = '{closureId}'"));
        Assert.Equal(
            fixture.VisitIds[2],
            await database.ExecuteScalarAsync<Guid>(
                $"select visit_id from bodylife.membership_negative_closure_items where negative_closure_id = '{closureId}' and sequence = 1"));
        Assert.Equal(
            fixture.VisitIds[3],
            await database.ExecuteScalarAsync<Guid>(
                $"select visit_id from bodylife.membership_negative_closure_items where negative_closure_id = '{closureId}' and sequence = 2"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.membership_negative_closure_items where negative_closure_id = '{closureId}' and visit_id = '{fixture.VisitIds[4]}'"));
        Assert.Equal(
            50m,
            await database.ExecuteScalarAsync<decimal>(
                $"select unit_price_amount_snapshot from bodylife.membership_negative_closure_lines where negative_closure_id = '{closureId}' and sequence = 1"));
        Assert.Equal(
            75m,
            await database.ExecuteScalarAsync<decimal>(
                $"select unit_price_amount_snapshot from bodylife.membership_negative_closure_lines where negative_closure_id = '{closureId}' and sequence = 2"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.command_idempotency_keys where command_name = 'CloseNegativeVisitsOneOff' and idempotency_key = 'partial-two-types'"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.business_audit_entries where id = '{result.AuditEntryId!.Value.Value}' and action_type = 'membership_negative_closure.created'"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.business_audit_entries audit join bodylife.payments payment on payment.id = audit.entity_id where payment.negative_closure_id = '{closureId}' and audit.action_type = 'payment.created'"));

        await database.ExecuteScalarAsync<int>(
            $"update bodylife.membership_types set name = 'Changed', price_amount = 99, updated_at = now() where id = '{fixture.OneOffTypeAId}'; select 1;");
        Assert.Equal(
            "Single A",
            await database.ExecuteScalarAsync<string>(
                $"select type_name_snapshot from bodylife.membership_negative_closure_lines where negative_closure_id = '{closureId}' and sequence = 1"));
        Assert.Equal(
            50m,
            await database.ExecuteScalarAsync<decimal>(
                $"select unit_price_amount_snapshot from bodylife.membership_negative_closure_lines where negative_closure_id = '{closureId}' and sequence = 1"));
    }

    [PostgreSqlFact]
    public async Task PaperFallbackClosureDerivesBatchAndLinksEveryCreatedFact()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId);
        var occurredAt = TestNow.AddMinutes(30);
        var paper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            database,
            fixture.Actor,
            occurredAt,
            "negative_coverage",
            occurredAt,
            explanation: "Recovered one-off negative closure");
        var command = new CloseNegativeVisitsOneOffCommand(
            new CommandEnvelope(
                fixture.Actor,
                new RequestCorrelationId("correlation-paper-negative-closure"),
                EntryOrigin.PaperFallback,
                occurredAt,
                "paper-negative-closure",
                Reason: "Recovered from paper sheet",
                Comment: null,
                EntryBatchRowId: paper.EntryBatchRowId),
            fixture.ClientId,
            fixture.VisitIds[2],
            [new NegativeVisitClosureLineSelection(
                fixture.OneOffTypeAId,
                TestNow,
                1)]);

        var handler = CreateHandler(dbContext);
        var result = await handler.ExecuteAsync(command, CancellationToken.None);

        Assert.Equal(CommandStatus.Success, result.Status);
        var closureId = result.PrimaryEntityId!.Value.Value;
        var paymentId = await database.ExecuteScalarAsync<Guid>(
            $"select id from bodylife.payments where negative_closure_id = '{closureId}'");
        Assert.Equal(
            paper.EntryBatchId,
            await database.ExecuteScalarAsync<Guid>(
                $"select entry_batch_id from bodylife.membership_negative_closures where id = '{closureId}'"));
        var links = await PostgreSqlPaperFallbackTestData.ReadLinksAsync(
            database,
            paper.EntryBatchRowId);
        Assert.Contains(links, link => link.EntityType == "membership_negative_closure"
            && link.EntityId == closureId);
        Assert.Contains(links, link => link.EntityType == "payment" && link.EntityId == paymentId);
        Assert.Equal(1, links.Count(link => link.EntityType == "membership_negative_closure_line"));
        Assert.Equal(1, links.Count(link => link.EntityType == "membership_negative_closure_item"));

        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        Assert.Equal(CommandStatus.Success, replay.Status);
        Assert.Equal(result.PrimaryEntityId, replay.PrimaryEntityId);
        Assert.Equal(result.AuditEntryId, replay.AuditEntryId);
        Assert.Equal(
            links,
            await PostgreSqlPaperFallbackTestData.ReadLinksAsync(
                database,
                paper.EntryBatchRowId));

        var reusedRow = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with
                {
                    RequestCorrelationId = new RequestCorrelationId(
                        "correlation-paper-negative-closure-reused-row"),
                    IdempotencyKey = "paper-negative-closure-reused-row",
                },
            },
            CancellationToken.None);
        AssertError(
            reusedRow,
            CommandErrorCode.DuplicateSubmission,
            "entryBatchRowId");
    }

    [PostgreSqlFact]
    public async Task PaperFallbackRequiresMatchingRowAndRejectsCallerBatchMetadata()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId);
        var occurredAt = TestNow.AddMinutes(30);
        var wrongEvent = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            database,
            fixture.Actor,
            occurredAt,
            "payment",
            occurredAt);
        var validShape = CreatePaperCommand(
            fixture,
            wrongEvent,
            "paper-negative-validation",
            fixture.VisitIds[2]);
        var handler = CreateHandler(dbContext);

        var wrongEventResult = await handler.ExecuteAsync(
            validShape,
            CancellationToken.None);
        var missingRowResult = await handler.ExecuteAsync(
            validShape with
            {
                Envelope = validShape.Envelope with
                {
                    RequestCorrelationId = new RequestCorrelationId(
                        "correlation-paper-negative-missing-row"),
                    IdempotencyKey = "paper-negative-missing-row",
                    EntryBatchRowId = Guid.NewGuid(),
                },
            },
            CancellationToken.None);
        var normalWithRow = CreateCommand(
            fixture,
            "normal-negative-with-row",
            fixture.VisitIds[2],
            [new NegativeVisitClosureLineSelection(
                fixture.OneOffTypeAId,
                TestNow,
                1)]);
        var normalWithRowResult = await handler.ExecuteAsync(
            normalWithRow with
            {
                Envelope = normalWithRow.Envelope with
                {
                    EntryBatchRowId = wrongEvent.EntryBatchRowId,
                },
            },
            CancellationToken.None);
        var legacyBatchResult = await handler.ExecuteAsync(
            validShape with
            {
                EntryBatchId = wrongEvent.EntryBatchId,
                Envelope = validShape.Envelope with
                {
                    RequestCorrelationId = new RequestCorrelationId(
                        "correlation-paper-negative-legacy-batch"),
                    IdempotencyKey = "paper-negative-legacy-batch",
                },
            },
            CancellationToken.None);
        var missingOccurredAtResult = await handler.ExecuteAsync(
            validShape with
            {
                Envelope = validShape.Envelope with
                {
                    RequestCorrelationId = new RequestCorrelationId(
                        "correlation-paper-negative-missing-occurred"),
                    IdempotencyKey = "paper-negative-missing-occurred",
                    OccurredAt = null,
                },
            },
            CancellationToken.None);
        var missingExplanationResult = await handler.ExecuteAsync(
            validShape with
            {
                Envelope = validShape.Envelope with
                {
                    RequestCorrelationId = new RequestCorrelationId(
                        "correlation-paper-negative-missing-explanation"),
                    IdempotencyKey = "paper-negative-missing-explanation",
                    Reason = null,
                    Comment = null,
                },
            },
            CancellationToken.None);

        AssertError(
            wrongEventResult,
            CommandErrorCode.ValidationFailed,
            "entryBatchRowId");
        AssertError(
            missingRowResult,
            CommandErrorCode.NotFound,
            "entryBatchRowId");
        AssertError(
            normalWithRowResult,
            CommandErrorCode.ValidationFailed,
            "entryBatchRowId");
        AssertError(
            legacyBatchResult,
            CommandErrorCode.ValidationFailed,
            "entryBatchId");
        AssertError(
            missingOccurredAtResult,
            CommandErrorCode.ValidationFailed,
            "occurredAt");
        AssertError(
            missingExplanationResult,
            CommandErrorCode.ValidationFailed,
            "reason");
        Assert.Empty(await PostgreSqlPaperFallbackTestData.ReadLinksAsync(
            database,
            wrongEvent.EntryBatchRowId));
        await AssertNoClosureAsync(database);
    }

    [PostgreSqlFact]
    public async Task PaperFallbackCanonicalReadsVerifyEveryAggregateLink()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId);
        var occurredAt = TestNow.AddMinutes(30);
        var paper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            database,
            fixture.Actor,
            occurredAt,
            "negative_coverage",
            occurredAt,
            explanation: "Canonical paper negative closure");
        var commandResult = await CreateHandler(dbContext).ExecuteAsync(
            CreatePaperCommand(
                fixture,
                paper,
                "paper-negative-canonical-reads",
                fixture.VisitIds[2]),
            CancellationToken.None);
        Assert.Equal(CommandStatus.Success, commandResult.Status);
        var closureId = commandResult.PrimaryEntityId!.Value.Value;
        var paymentId = await database.ExecuteScalarAsync<Guid>(
            $"select id from bodylife.payments where negative_closure_id = '{closureId}'");

        var coverageHandler = new GetClientNegativeVisitCoverageQueryHandler(
            dbContext,
            new MembershipNegativeVisitSelector(dbContext),
            new FixedTimeProvider(TestNow.AddMinutes(30)));
        var coverage = await coverageHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);
        Assert.Equal(GetClientNegativeVisitCoverageStatus.Success, coverage.Status);
        var closure = Assert.Single(coverage.Coverage!.ActiveClosures);
        var closurePaper = Assert.IsType<PaperFallbackEntryRowReference>(
            closure.PaperReference);
        Assert.Equal(paper.EntryBatchId, closure.EntryBatchId);
        Assert.Equal(paper.EntryBatchRowId, closurePaper.EntryBatchRowId);
        Assert.Equal(PaperFallbackEventType.NegativeCoverage, closurePaper.EventType);

        var auditHandler = new GetClientAuditEntriesQueryHandler(
            dbContext,
            new FixedTimeProvider(TestNow.AddMinutes(30)));
        var paymentHistory = await new GetClientPaymentHistorySourceRowsQueryHandler(
                dbContext,
                auditHandler)
            .ExecuteAsync(
                new GetClientPaymentHistorySourceRowsQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None);
        Assert.Equal(
            GetClientPaymentHistorySourceRowsStatus.Success,
            paymentHistory.Status);
        var paymentHistoryRow = Assert.Single(paymentHistory.Page!.Items);
        var paymentPaper = Assert.IsType<PaperFallbackEntryRowReference>(
            paymentHistoryRow.CreatedPayment!.PaperReference);
        Assert.Equal(paper.EntryBatchRowId, paymentPaper.EntryBatchRowId);
        Assert.Equal(PaperFallbackEventType.NegativeCoverage, paymentPaper.EventType);

        var dailyPayments = await new GetDailyPaymentSourceRowsQueryHandler(
                dbContext,
                new OpenPaymentDayStatusProvider(),
                new FixedTimeProvider(TestNow.AddMinutes(30)))
            .ExecuteAsync(
                new GetDailyPaymentSourceRowsQuery(
                    fixture.Actor,
                    BusinessTimeZone.GetBusinessDate(occurredAt)),
                CancellationToken.None);
        Assert.Equal(GetDailyPaymentSourceRowsStatus.Success, dailyPayments.Status);
        var dailyPayment = Assert.Single(
            dailyPayments.Snapshot!.Rows,
            row => row.Payment.PaymentId == paymentId);
        Assert.Equal(EntryOrigin.PaperFallback, dailyPayment.Payment.EntryOrigin);
        Assert.Equal(paper.EntryBatchId, dailyPayment.Payment.EntryBatchId);

        var links = await PostgreSqlPaperFallbackTestData.ReadLinksAsync(
            database,
            paper.EntryBatchRowId);
        var lineLink = Assert.Single(
            links,
            link => link.EntityType == "membership_negative_closure_line");
        var unexpectedLink = new PaperFallbackEntityLink(
            MembershipAuditActions.MembershipEntityType,
            fixture.SourceMembershipId);
        await PostgreSqlPaperFallbackTestData.CorruptLinksAsync(
            database,
            paper.EntryBatchRowId,
            unexpectedLink);
        var unexpectedAggregateLink = await coverageHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.CanonicalStateInvalid,
            unexpectedAggregateLink.Status);
        await database.ExecutePrivilegedPaperLinkCorruptionAsync<int>(
            $"""
            delete from bodylife.entry_batch_row_entities
            where entry_batch_row_id = '{paper.EntryBatchRowId}'
              and entity_type = '{unexpectedLink.EntityType}'
              and entity_id = '{unexpectedLink.EntityId}';
            select 1;
            """);
        var restoredAggregate = await coverageHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.Success,
            restoredAggregate.Status);

        await database.ExecutePrivilegedPaperLinkCorruptionAsync<int>(
            $"""
            delete from bodylife.entry_batch_row_entities
            where entry_batch_row_id = '{paper.EntryBatchRowId}'
              and entity_type = '{lineLink.EntityType}'
              and entity_id = '{lineLink.EntityId}';
            select 1;
            """);
        var missingLine = await coverageHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.CanonicalStateInvalid,
            missingLine.Status);

        await PostgreSqlPaperFallbackTestData.LinkRowAsync(
            database,
            paper.EntryBatchRowId,
            lineLink);
        await database.ExecutePrivilegedPaperLinkCorruptionAsync<int>(
            $"""
            delete from bodylife.entry_batch_row_entities
            where entry_batch_row_id = '{paper.EntryBatchRowId}'
              and entity_type = 'payment'
              and entity_id = '{paymentId}';
            select 1;
            """);
        var missingPaymentCoverage = await coverageHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);
        var missingPaymentHistory = await new GetClientPaymentHistorySourceRowsQueryHandler(
                dbContext,
                auditHandler)
            .ExecuteAsync(
                new GetClientPaymentHistorySourceRowsQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None);
        var missingDailyPayment = await new GetDailyPaymentSourceRowsQueryHandler(
                dbContext,
                new OpenPaymentDayStatusProvider(),
                new FixedTimeProvider(TestNow.AddMinutes(30)))
            .ExecuteAsync(
                new GetDailyPaymentSourceRowsQuery(
                    fixture.Actor,
                    BusinessTimeZone.GetBusinessDate(occurredAt)),
                CancellationToken.None);
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.CanonicalStateInvalid,
            missingPaymentCoverage.Status);
        Assert.Equal(
            GetClientPaymentHistorySourceRowsStatus.SourceInconsistent,
            missingPaymentHistory.Status);
        Assert.Equal(
            GetDailyPaymentSourceRowsStatus.SourceInconsistent,
            missingDailyPayment.Status);
    }

    [PostgreSqlFact]
    public async Task MultiplePaperClosuresRequireExactLinksOnEachRow()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId);
        var occurredAt = TestNow.AddMinutes(30);
        var firstPaper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            database,
            fixture.Actor,
            occurredAt,
            "negative_coverage",
            occurredAt,
            lineNumber: 21,
            explanation: "First paper negative closure");
        var secondPaper = await PostgreSqlPaperFallbackTestData.SeedRowInBatchAsync(
            database,
            firstPaper,
            fixture.Actor,
            occurredAt,
            "negative_coverage",
            occurredAt,
            lineNumber: 22,
            explanation: "Second paper negative closure");
        var handler = CreateHandler(dbContext);

        var first = await handler.ExecuteAsync(
            CreatePaperCommand(
                fixture,
                firstPaper,
                "paper-negative-first-row",
                fixture.VisitIds[2]),
            CancellationToken.None);
        var second = await handler.ExecuteAsync(
            CreatePaperCommand(
                fixture,
                secondPaper,
                "paper-negative-second-row",
                fixture.VisitIds[3]),
            CancellationToken.None);
        Assert.Equal(CommandStatus.Success, first.Status);
        Assert.Equal(CommandStatus.Success, second.Status);

        var coverageHandler = new GetClientNegativeVisitCoverageQueryHandler(
            dbContext,
            new MembershipNegativeVisitSelector(dbContext),
            new FixedTimeProvider(occurredAt));
        var valid = await coverageHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);
        Assert.Equal(GetClientNegativeVisitCoverageStatus.Success, valid.Status);
        Assert.Equal(2, valid.Coverage!.ActiveClosures.Count);

        var firstLinks = await PostgreSqlPaperFallbackTestData.ReadLinksAsync(
            database,
            firstPaper.EntryBatchRowId);
        var movedLink = Assert.Single(
            firstLinks,
            link => link.EntityType == "membership_negative_closure_line");
        await database.ExecutePrivilegedPaperLinkCorruptionAsync<int>(
            $"""
            update bodylife.entry_batch_row_entities
            set entry_batch_row_id = '{secondPaper.EntryBatchRowId}'
            where entry_batch_row_id = '{firstPaper.EntryBatchRowId}'
              and entity_type = '{movedLink.EntityType}'
              and entity_id = '{movedLink.EntityId}';
            select 1;
            """);
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.CanonicalStateInvalid,
            (await coverageHandler.ExecuteAsync(
                new GetClientNegativeVisitCoverageQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None)).Status);

        await database.ExecutePrivilegedPaperLinkCorruptionAsync<int>(
            $"""
            update bodylife.entry_batch_row_entities
            set entry_batch_row_id = '{firstPaper.EntryBatchRowId}'
            where entry_batch_row_id = '{secondPaper.EntryBatchRowId}'
              and entity_type = '{movedLink.EntityType}'
              and entity_id = '{movedLink.EntityId}';
            select 1;
            """);
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.Success,
            (await coverageHandler.ExecuteAsync(
                new GetClientNegativeVisitCoverageQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None)).Status);

        await database.ExecutePrivilegedPaperLinkCorruptionAsync<int>(
            $"""
            update bodylife.entry_batch_row_entities
            set entry_batch_row_id = case
                when entry_batch_row_id = '{firstPaper.EntryBatchRowId}'
                    then '{secondPaper.EntryBatchRowId}'::uuid
                else '{firstPaper.EntryBatchRowId}'::uuid
            end
            where entry_batch_row_id in (
                '{firstPaper.EntryBatchRowId}',
                '{secondPaper.EntryBatchRowId}');
            select 1;
            """);
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.CanonicalStateInvalid,
            (await coverageHandler.ExecuteAsync(
                new GetClientNegativeVisitCoverageQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None)).Status);

        await database.ExecutePrivilegedPaperLinkCorruptionAsync<int>(
            $"""
            update bodylife.entry_batch_row_entities
            set entry_batch_row_id = case
                when entry_batch_row_id = '{firstPaper.EntryBatchRowId}'
                    then '{secondPaper.EntryBatchRowId}'::uuid
                else '{firstPaper.EntryBatchRowId}'::uuid
            end
            where entry_batch_row_id in (
                '{firstPaper.EntryBatchRowId}',
                '{secondPaper.EntryBatchRowId}');
            select 1;
            """);
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.Success,
            (await coverageHandler.ExecuteAsync(
                new GetClientNegativeVisitCoverageQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None)).Status);

        await PostgreSqlPaperFallbackTestData.CorruptLinksAsync(
            database,
            secondPaper.EntryBatchRowId,
            new PaperFallbackEntityLink(
                MembershipAuditActions.MembershipEntityType,
                fixture.SourceMembershipId));
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.CanonicalStateInvalid,
            (await coverageHandler.ExecuteAsync(
                new GetClientNegativeVisitCoverageQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None)).Status);
    }

    [PostgreSqlFact]
    public async Task ActiveOneOffClosureBlocksVisitCancellationAndKeepsCanonicalReads()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId);
        var coveredVisitId = fixture.VisitIds[2];
        var closureResult = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                fixture,
                "closure-before-cancel",
                coveredVisitId,
                [
                    new NegativeVisitClosureLineSelection(
                        fixture.OneOffTypeAId,
                        TestNow,
                        1),
                ]),
            CancellationToken.None);
        Assert.Equal(CommandStatus.Success, closureResult.Status);
        var closureId = closureResult.PrimaryEntityId!.Value.Value;
        var auditCountBeforeCancel = await database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.business_audit_entries");
        var idempotencyCountBeforeCancel = await database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.command_idempotency_keys");
        var dayStatusProvider = new OpenVisitDayStatusProvider();

        var clientRowsResult = await new GetClientVisitRowsQueryHandler(
                dbContext,
                dayStatusProvider,
                new FixedTimeProvider(TestNow.AddMinutes(31)))
            .ExecuteAsync(
                new GetClientVisitRowsQuery(fixture.Actor, fixture.ClientId),
                CancellationToken.None);
        Assert.Equal(GetClientVisitRowsStatus.Success, clientRowsResult.Status);
        var coveredClientRow = Assert.Single(
            clientRowsResult.Page!.Items,
            row => row.VisitId == coveredVisitId);
        Assert.True(coveredClientRow.AllowedActions.TryGet(
            VisitActionKeys.Cancel,
            out var clientCancellationPermission));
        Assert.False(clientCancellationPermission!.IsAllowed);
        Assert.Equal(
            "negative_coverage_dependency",
            clientCancellationPermission.DeniedReasonCode);

        var dailyRowsResult = await new GetDailyVisitSourceRowsQueryHandler(
                dbContext,
                dayStatusProvider,
                new FixedTimeProvider(TestNow.AddMinutes(31)))
            .ExecuteAsync(
                new GetDailyVisitSourceRowsQuery(
                    fixture.Actor,
                    new DateOnly(2026, 7, 3)),
                CancellationToken.None);
        Assert.Equal(GetDailyVisitSourceRowsStatus.Success, dailyRowsResult.Status);
        var coveredDailyRow = Assert.Single(dailyRowsResult.Snapshot!.Rows).Visit;
        Assert.Equal(coveredVisitId, coveredDailyRow.VisitId);
        Assert.True(coveredDailyRow.AllowedActions.TryGet(
            VisitActionKeys.Cancel,
            out var dailyCancellationPermission));
        Assert.False(dailyCancellationPermission!.IsAllowed);
        Assert.Equal(
            "negative_coverage_dependency",
            dailyCancellationPermission.DeniedReasonCode);

        var cancelResult = await CreateCancelVisitHandler(dbContext).ExecuteAsync(
            CreateCancelVisitCommand(fixture, coveredVisitId),
            CancellationToken.None);

        AssertError(
            cancelResult,
            CommandErrorCode.VisitHasActiveNegativeCoverage,
            "visitId");
        Assert.Equal(
            "active",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.visits where id = '{coveredVisitId}'"));
        Assert.Equal(
            "active",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.visit_consumptions where id = '{fixture.ConsumptionIds[2]}'"));
        Assert.Equal(
            "active",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.membership_negative_closures where id = '{closureId}'"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.visit_cancellations where visit_id = '{coveredVisitId}'"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.payments where negative_closure_id = '{closureId}' and status = 'active'"));
        Assert.Equal(
            auditCountBeforeCancel,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
        Assert.Equal(
            idempotencyCountBeforeCancel,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.command_idempotency_keys"));

        await using var lockContext = database.CreateDbContext();
        await using var lockTransaction = await lockContext.Database
            .BeginTransactionAsync();
        var preparation = await new CancelVisitSourcePreparer(lockContext)
            .PrepareAsync(coveredVisitId);
        Assert.Equal(
            CancelVisitSourcePreparationStatus.VisitHasActiveNegativeCoverage,
            preparation.Status);
        var closureItemId = await database.ExecuteScalarAsync<Guid>(
            $"select id from bodylife.membership_negative_closure_items where negative_closure_id = '{closureId}'");
        var lockFailure = await AssertCoverageItemUpdateBlockedAsync(
            database.ConnectionString,
            closureItemId);
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, lockFailure.SqlState);
        await lockTransaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task StaleOverLimitAndDuplicateRequestsDoNotCreatePartialFacts()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId);
        var overLimit = CreateCommand(
            fixture,
            "over-limit",
            fixture.VisitIds[2],
            [new NegativeVisitClosureLineSelection(
                fixture.OneOffTypeAId,
                TestNow,
                4)]);

        var overLimitResult = await CreateHandler(dbContext).ExecuteAsync(
            overLimit,
            CancellationToken.None);

        AssertError(overLimitResult, CommandErrorCode.ValidationFailed, "lines");
        await AssertNoClosureAsync(database);

        var staleResult = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                fixture,
                "stale-oldest",
                fixture.VisitIds[3],
                [new NegativeVisitClosureLineSelection(
                    fixture.OneOffTypeAId,
                    TestNow,
                    1)]),
            CancellationToken.None);
        AssertError(
            staleResult,
            CommandErrorCode.StaleState,
            "expectedOldestOpenNegativeVisitId");
        await AssertNoClosureAsync(database);

        var command = CreateCommand(
            fixture,
            "replay-success",
            fixture.VisitIds[2],
            [new NegativeVisitClosureLineSelection(
                fixture.OneOffTypeAId,
                TestNow,
                1)]);
        var first = await CreateHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);
        var replay = await CreateHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);
        Assert.Equal(CommandStatus.Success, first.Status);
        Assert.Equal(first.PrimaryEntityId, replay.PrimaryEntityId);
        Assert.Equal(first.AuditEntryId, replay.AuditEntryId);

        var changedPayload = command with
        {
            Lines = [new NegativeVisitClosureLineSelection(
                fixture.OneOffTypeBId,
                TestNow,
                1)],
        };
        var duplicate = await CreateHandler(dbContext).ExecuteAsync(
            changedPayload,
            CancellationToken.None);
        AssertError(
            duplicate,
            CommandErrorCode.DuplicateSubmission,
            "idempotencyKey");
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_negative_closures"));
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackClosurePaymentItemsCacheAndIdempotency()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId);
        var occurredAt = TestNow.AddMinutes(30);
        var paper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            database,
            fixture.Actor,
            occurredAt,
            "negative_coverage",
            occurredAt,
            explanation: "Recovered closure after audit retry");
        var command = CreatePaperCommand(
            fixture,
            paper,
            "paper-negative-audit-failure",
            fixture.VisitIds[2]);
        await database.ExecuteScalarAsync<int>(
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_negative_closure_audit
            check (action_type <> 'membership_negative_closure.created');
            select 1;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext).ExecuteAsync(
                command,
                CancellationToken.None));

        await AssertNoClosureAsync(database);
        Assert.Empty(await PostgreSqlPaperFallbackTestData.ReadLinksAsync(
            database,
            paper.EntryBatchRowId));
        Assert.Empty(dbContext.ChangeTracker.Entries());
        Assert.Equal(
            3,
            await database.ExecuteScalarAsync<int>(
                $"select negative_balance from bodylife.membership_state_cache where membership_id = '{fixture.SourceMembershipId}'"));

        await database.ExecuteScalarAsync<int>(
            """
            alter table bodylife.business_audit_entries
            drop constraint ck_test_reject_negative_closure_audit;
            select 1;
            """);
        var retry = await CreateHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);
        Assert.Equal(CommandStatus.Success, retry.Status);
        Assert.Equal(
            4,
            (await PostgreSqlPaperFallbackTestData.ReadLinksAsync(
                database,
                paper.EntryBatchRowId)).Count);
    }

    [PostgreSqlFact]
    public async Task ConcurrentPaperCommandsBindExactlyOneClosureAggregate()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        ClosureFixture fixture;
        await using (var setupContext = database.CreateDbContext())
        {
            fixture = await SeedFixtureAsync(
                database,
                ActorRole.Admin,
                AccountKind.NamedAdmin);
            await RebuildSourceAsync(setupContext, fixture.SourceMembershipId);
        }

        var occurredAt = TestNow.AddMinutes(30);
        var paper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            database,
            fixture.Actor,
            occurredAt,
            "negative_coverage",
            occurredAt,
            explanation: "Concurrent recovered one-off closure");
        var firstCommand = CreatePaperCommand(
            fixture,
            paper,
            "paper-negative-race-first",
            fixture.VisitIds[2]);
        var secondCommand = CreatePaperCommand(
            fixture,
            paper,
            "paper-negative-race-second",
            fixture.VisitIds[2]);
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(
                firstCommand,
                CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(
                secondCommand,
                CancellationToken.None));

        var success = Assert.Single(
            results,
            result => result.Status == CommandStatus.Success);
        var rejected = Assert.Single(
            results,
            result => result.Status == CommandStatus.Error);
        AssertError(
            rejected,
            CommandErrorCode.DuplicateSubmission,
            "entryBatchRowId");
        var links = await PostgreSqlPaperFallbackTestData.ReadLinksAsync(
            database,
            paper.EntryBatchRowId);
        Assert.Equal(4, links.Count);
        Assert.Contains(
            links,
            link => link.EntityType == CloseNegativeVisitsOneOffCommand.PrimaryEntityType
                && link.EntityId == success.PrimaryEntityId!.Value.Value);
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_negative_closures"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InvalidActorShapeIsDeniedWithoutMutation()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId);
        var invalidActor = fixture.Actor with
        {
            Role = ActorRole.Owner,
            AccountKind = AccountKind.NamedAdmin,
        };
        var command = CreateCommand(
            fixture with { Actor = invalidActor },
            "denied-shape",
            fixture.VisitIds[2],
            [new NegativeVisitClosureLineSelection(
                fixture.OneOffTypeAId,
                TestNow,
                1)]);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);

        AssertError(result, CommandErrorCode.PermissionDenied);
        await AssertNoClosureAsync(database);
    }

    [Fact]
    public void PersistenceRegistrationExposesClosureHandlerSelectorAndPaymentWriter()
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

        var handler = Assert.Single(
            services,
            candidate => candidate.ServiceType
                == typeof(IBodyLifeCommandHandler<CloseNegativeVisitsOneOffCommand>));
        Assert.Equal(ServiceLifetime.Scoped, handler.Lifetime);
        Assert.Equal(
            typeof(CloseNegativeVisitsOneOffCommandHandler),
            handler.ImplementationType);
        var writer = Assert.Single(
            services,
            candidate => candidate.ServiceType
                == typeof(INegativeClosurePaymentWriter));
        Assert.Equal(typeof(NegativeClosurePaymentWriter), writer.ImplementationType);
        Assert.Contains(
            services,
            candidate => candidate.ServiceType
                == typeof(MembershipNegativeVisitSelector));
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        return database;
    }

    private static async Task<ClosureFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        ActorRole role,
        AccountKind accountKind)
    {
        var fixture = new ClosureFixture(
            new ActorContext(
                new AccountId(Guid.NewGuid()),
                role,
                accountKind,
                new SessionId(Guid.NewGuid()),
                "negative closure test"),
            ClientId: Guid.NewGuid(),
            SourceTypeId: Guid.NewGuid(),
            OneOffTypeAId: Guid.NewGuid(),
            OneOffTypeBId: Guid.NewGuid(),
            SourceMembershipId: Guid.NewGuid(),
            VisitIds: Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray(),
            ConsumptionIds: Enumerable.Range(0, 5)
                .Select(_ => Guid.NewGuid())
                .ToArray());
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into bodylife.accounts (
                id, display_name, account_type, role, is_active, created_at, deactivated_at)
            values (
                @account_id, 'Closure actor', @account_type, @role, true, @now, null);

            insert into bodylife.sessions (
                id, account_id, device_label, started_at, expires_at, ended_at, last_seen_at)
            values (
                @session_id, @account_id, 'negative closure test', @now,
                @expires_at, null, @now);

            insert into bodylife.clients (
                id, surname, name, patronymic, normalized_full_name,
                phone_raw, phone_normalized, phone_last4, comment,
                operational_status, created_at, created_by_account_id, updated_at)
            values (
                @client_id, 'Negative', 'Client', null, 'NEGATIVE CLIENT',
                null, null, null, null, 'active', @now, @account_id, @now);

            insert into bodylife.membership_types (
                id, name, kind, duration_days, visits_limit, price_amount,
                price_currency, is_active, comment, created_at, updated_at, deactivated_at)
            values
                (@source_type_id, 'Two visits', 'ordinary', 30, 2, 1000, 'UAH', true, null, @now, @now, null),
                (@one_off_a_id, 'Single A', 'one_off', 1, 1, 50, 'UAH', true, null, @now, @now, null),
                (@one_off_b_id, 'Single B', 'one_off', 1, 1, 75, 'UAH', true, null, @now, @now, null);

            insert into bodylife.issued_memberships (
                id, client_id, membership_type_id, issuance_mode, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot,
                price_currency_snapshot, start_date, base_end_date, issued_at,
                issued_by_account_id, status, entry_origin, entry_batch_id, comment)
            values (
                @membership_id, @client_id, @source_type_id, 'sale', 'Two visits',
                30, 2, 1000, 'UAH', date '2026-07-01', date '2026-07-30', @now,
                @account_id, 'active', 'normal', null, null);

            insert into bodylife.payments (
                id, client_id, membership_id, negative_closure_id, amount, currency,
                method, payment_context, occurred_at, recorded_at,
                recorded_by_account_id, session_id, entry_origin, entry_batch_id,
                comment, status)
            values (
                gen_random_uuid(), @client_id, @membership_id, null, 1000, 'UAH',
                'cash', 'membership_sale', @now, @now,
                @account_id, @session_id, 'normal', null, null, 'active');
            """,
            ("account_id", fixture.Actor.AccountId.Value),
            ("account_type", MapAccountKind(accountKind)),
            ("role", MapRole(role)),
            ("session_id", fixture.Actor.SessionId.Value),
            ("client_id", fixture.ClientId),
            ("source_type_id", fixture.SourceTypeId),
            ("one_off_a_id", fixture.OneOffTypeAId),
            ("one_off_b_id", fixture.OneOffTypeBId),
            ("membership_id", fixture.SourceMembershipId),
            ("now", TestNow),
            ("expires_at", TestNow.AddHours(8)));

        for (var index = 0; index < fixture.VisitIds.Length; index++)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                insert into bodylife.visits (
                    id, client_id, occurred_at, recorded_at, recorded_by_account_id,
                    session_id, visit_kind, entry_origin, entry_batch_id, comment, status)
                values (
                    @visit_id, @client_id, @occurred_at, @recorded_at, @account_id,
                    @session_id, 'membership', 'normal', null, null, 'active');

                insert into bodylife.visit_consumptions (
                    id, visit_id, client_id, visit_kind, membership_id,
                    consumption_type, source_fact_type, source_fact_id, recorded_at,
                    recorded_by_account_id, recorded_session_id, status)
                values (
                    @consumption_id, @visit_id, @client_id, 'membership', @membership_id,
                    'counted', 'visit', @visit_id, @recorded_at,
                    @account_id, @session_id, 'active');
                """,
                ("visit_id", fixture.VisitIds[index]),
                ("consumption_id", fixture.ConsumptionIds[index]),
                ("client_id", fixture.ClientId),
                ("membership_id", fixture.SourceMembershipId),
                ("occurred_at", new DateTimeOffset(
                    2026,
                    7,
                    index + 1,
                    10,
                    0,
                    0,
                    TimeSpan.Zero)),
                ("recorded_at", TestNow.AddMinutes(index + 1)),
                ("account_id", fixture.Actor.AccountId.Value),
                ("session_id", fixture.Actor.SessionId.Value));
        }

        await transaction.CommitAsync();
        return fixture;
    }

    private static CloseNegativeVisitsOneOffCommand CreateCommand(
        ClosureFixture fixture,
        string idempotencyKey,
        Guid expectedOldestVisitId,
        IReadOnlyList<NegativeVisitClosureLineSelection> lines)
    {
        return new CloseNegativeVisitsOneOffCommand(
            new CommandEnvelope(
                fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                TestNow.AddMinutes(30),
                idempotencyKey,
                Reason: null,
                Comment: "  Negative visit closure  "),
            fixture.ClientId,
            expectedOldestVisitId,
            lines);
    }

    private static CloseNegativeVisitsOneOffCommand CreatePaperCommand(
        ClosureFixture fixture,
        PaperFallbackRowFixture paper,
        string idempotencyKey,
        Guid expectedOldestVisitId)
    {
        return new CloseNegativeVisitsOneOffCommand(
            new CommandEnvelope(
                fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.PaperFallback,
                TestNow.AddMinutes(30),
                idempotencyKey,
                Reason: paper.Explanation,
                Comment: null,
                EntryBatchRowId: paper.EntryBatchRowId),
            fixture.ClientId,
            expectedOldestVisitId,
            [new NegativeVisitClosureLineSelection(
                fixture.OneOffTypeAId,
                TestNow,
                1)]);
    }

    private static CloseNegativeVisitsOneOffCommandHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow.AddMinutes(30));
        var auditAppender = new BusinessAuditAppender(dbContext);
        return new CloseNegativeVisitsOneOffCommandHandler(
            dbContext,
            auditAppender,
            new PaperFallbackEntryRowBinder(dbContext),
            new NegativeClosurePaymentWriter(dbContext, auditAppender),
            new MembershipNegativeVisitSelector(dbContext),
            new MembershipStateCacheRebuilder(dbContext, timeProvider),
            timeProvider);
    }

    private static CancelVisitCommandHandler CreateCancelVisitHandler(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow.AddMinutes(31));
        return new CancelVisitCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new PaperFallbackEntryRowBinder(dbContext),
            new CancelVisitSourcePreparer(dbContext),
            new MembershipStateRecalculator(
                new MembershipStateCacheRebuilder(dbContext, timeProvider)),
            new GetMembershipStateQueryHandler(dbContext, timeProvider),
            new OpenVisitDayStatusProvider(),
            timeProvider);
    }

    private static CancelVisitCommand CreateCancelVisitCommand(
        ClosureFixture fixture,
        Guid visitId)
    {
        return new CancelVisitCommand(
            new CommandEnvelope(
                fixture.Actor,
                new RequestCorrelationId("correlation-cancel-covered-visit"),
                EntryOrigin.Normal,
                TestNow.AddMinutes(31),
                "cancel-covered-visit",
                Reason: "Mistaken covered Visit",
                Comment: "Direct cancellation must be rejected."),
            visitId);
    }

    private static async Task<PostgresException> AssertCoverageItemUpdateBlockedAsync(
        string connectionString,
        Guid closureItemId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            set local lock_timeout = '100ms';
            update bodylife.membership_negative_closure_items
            set status = status
            where id = @closure_item_id;
            """;
        command.Parameters.AddWithValue("closure_item_id", closureItemId);
        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync());
        await transaction.RollbackAsync();
        return exception;
    }

    private static async Task RebuildSourceAsync(
        BodyLifeDbContext dbContext,
        Guid membershipId)
    {
        var result = await new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(TestNow))
            .RebuildAsync(membershipId);
        Assert.True(result.Succeeded);
        Assert.Equal(3, result.State!.NegativeBalance);
    }

    private static async Task AssertNoClosureAsync(PostgreSqlTestDatabase database)
    {
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_negative_closures"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_negative_closure_lines"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_negative_closure_items"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.payments"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.command_idempotency_keys"));
    }

    private static void AssertError(
        CommandResult result,
        CommandErrorCode code,
        string? field = null)
    {
        Assert.Equal(CommandStatus.Error, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        if (field is not null)
        {
            Assert.Equal(field, error.Field);
        }
    }

    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    private static string MapAccountKind(AccountKind kind) => kind switch
    {
        AccountKind.Owner => "owner",
        AccountKind.NamedAdmin => "named_admin",
        AccountKind.SharedReceptionAdmin => "shared_reception_admin",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string MapRole(ActorRole role) => role switch
    {
        ActorRole.Owner => "owner",
        ActorRole.Admin => "admin",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    private sealed record ClosureFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid SourceTypeId,
        Guid OneOffTypeAId,
        Guid OneOffTypeBId,
        Guid SourceMembershipId,
        Guid[] VisitIds,
        Guid[] ConsumptionIds);

    private sealed class OpenVisitDayStatusProvider
        : IVisitDayReconciliationStatusProvider
    {
        public Task<VisitDayReconciliationStatus> GetStatusAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(VisitDayReconciliationStatus.Open);
        }
    }

    private sealed class OpenPaymentDayStatusProvider
        : IPaymentDayReconciliationStatusProvider
    {
        public Task<PaymentDayReconciliationStatus> GetStatusAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PaymentDayReconciliationStatus.Open);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
