using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlCorrectNegativeVisitCoverageCommandTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        20,
        12,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset CorrectionNow = TestNow.AddHours(1);
    private static readonly DateTimeOffset CatalogUpdatedAt = TestNow.AddDays(-1);

    [PostgreSqlFact]
    public async Task CancelOneOffRestoresNegativeAndCancelsClosureItemsAndPayment()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId, 3);
        var originalClosureId = await CloseOneOffAsync(
            dbContext,
            fixture,
            "one-off-to-cancel",
            quantity: 2,
            fixture.OneOffTypeAId);
        Assert.Equal(1, await ReadNegativeAsync(database, fixture.SourceMembershipId));
        var command = CreateCancelCommand(
            fixture,
            originalClosureId,
            "cancel-one-off");

        var result = await CreateCorrectionHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);
        var replay = await CreateCorrectionHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);

        AssertSuccessful(result, fixture.ClientId);
        Assert.Equal(result.PrimaryEntityId, replay.PrimaryEntityId);
        Assert.Equal(result.AuditEntryId, replay.AuditEntryId);
        Assert.Contains(MembershipWarningCodes.NegativeBalance, result.Warnings);
        Assert.Equal(3, await ReadNegativeAsync(database, fixture.SourceMembershipId));
        Assert.Equal(
            "canceled",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.membership_negative_closures where id = '{originalClosureId}'"));
        Assert.Equal(
            2L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.membership_negative_closure_items where negative_closure_id = '{originalClosureId}' and status = 'canceled'"));
        Assert.Equal(
            "canceled",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.payments where negative_closure_id = '{originalClosureId}'"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.membership_negative_closure_corrections where original_closure_id = '{originalClosureId}' and mode = 'cancel' and replacement_closure_id is null"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.business_audit_entries where entity_id = '{originalClosureId}' and action_type = 'membership_negative_closure.canceled'"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.business_audit_entries audit join bodylife.payments payment on payment.id = audit.entity_id where payment.negative_closure_id = '{originalClosureId}' and audit.action_type = 'payment.canceled'"));
        var correctionId = await database.ExecuteScalarAsync<Guid>(
            $"select id from bodylife.membership_negative_closure_corrections where original_closure_id = '{originalClosureId}'");
        Assert.Equal(
            CorrectionNow.UtcDateTime,
            await database.ExecuteScalarAsync<DateTime>(
                $"select occurred_at from bodylife.membership_negative_closure_corrections where id = '{correctionId}'"));
        Assert.Equal(
            "normal",
            await database.ExecuteScalarAsync<string>(
                $"select entry_origin from bodylife.membership_negative_closure_corrections where id = '{correctionId}'"));

        var paymentRows = await CreatePaymentRowsHandler(dbContext).ExecuteAsync(
            new GetClientPaymentRowsQuery(fixture.Actor, fixture.ClientId),
            CancellationToken.None);
        Assert.Equal(GetClientPaymentRowsStatus.Success, paymentRows.Status);
        var canceledPayment = Assert.Single(
            paymentRows.Page!.Items,
            payment => payment.PaymentContext == PaymentContext.NegativeClosure);
        Assert.Equal(ClientPaymentRowStatus.Canceled, canceledPayment.Status);
        var cancellation = Assert.IsType<ClientPaymentCancellation>(
            canceledPayment.Cancellation);
        Assert.Equal(correctionId, cancellation.CancellationId);
        Assert.Equal(CorrectionNow, cancellation.OccurredAt);
        Assert.Equal("Coverage was recorded incorrectly", cancellation.Reason);

        var secondCorrection = await CreateCorrectionHandler(dbContext).ExecuteAsync(
            CreateCancelCommand(fixture, originalClosureId, "cancel-again"),
            CancellationToken.None);
        AssertError(
            secondCorrection,
            CommandErrorCode.AlreadyCanceled,
            "originalNegativeClosureId");
    }

    [PostgreSqlFact]
    public async Task ReplaceOneOffUsesRestoredOldestVisitAndNewExactPayment()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId, 3);
        var originalClosureId = await CloseOneOffAsync(
            dbContext,
            fixture,
            "one-off-to-replace",
            quantity: 2,
            fixture.OneOffTypeAId);
        var command = CreateReplaceOneOffCommand(
            fixture,
            originalClosureId,
            "replace-one-off",
            fixture.VisitIds[2],
            fixture.OneOffTypeBId,
            quantity: 1);

        var result = await CreateCorrectionHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);

        AssertSuccessful(result, fixture.ClientId);
        Assert.Equal(2, await ReadNegativeAsync(database, fixture.SourceMembershipId));
        var replacementClosureId = await database.ExecuteScalarAsync<Guid>(
            $"select replacement_closure_id from bodylife.membership_negative_closure_corrections where original_closure_id = '{originalClosureId}'");
        Assert.Equal(
            "replaced",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.membership_negative_closures where id = '{originalClosureId}'"));
        Assert.Equal(
            "active",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.membership_negative_closures where id = '{replacementClosureId}'"));
        Assert.Equal(
            fixture.VisitIds[2],
            await database.ExecuteScalarAsync<Guid>(
                $"select oldest_open_negative_visit_id from bodylife.membership_negative_closures where id = '{replacementClosureId}'"));
        Assert.Equal(
            75m,
            await database.ExecuteScalarAsync<decimal>(
                $"select amount from bodylife.payments where negative_closure_id = '{replacementClosureId}' and status = 'active'"));
        Assert.Equal(
            "replaced",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.payments where negative_closure_id = '{originalClosureId}'"));
        Assert.Equal(
            "Single B",
            await database.ExecuteScalarAsync<string>(
                $"select type_name_snapshot from bodylife.membership_negative_closure_lines where negative_closure_id = '{replacementClosureId}'"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.business_audit_entries where entity_id = '{originalClosureId}' and action_type = 'membership_negative_closure.replaced'"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.business_audit_entries where entity_id = '{replacementClosureId}' and action_type = 'membership_negative_closure.created'"));
        var correctionId = await database.ExecuteScalarAsync<Guid>(
            $"select id from bodylife.membership_negative_closure_corrections where original_closure_id = '{originalClosureId}'");
        var originalPaymentId = await database.ExecuteScalarAsync<Guid>(
            $"select id from bodylife.payments where negative_closure_id = '{originalClosureId}'");
        var replacementPaymentId = await database.ExecuteScalarAsync<Guid>(
            $"select id from bodylife.payments where negative_closure_id = '{replacementClosureId}'");

        var paymentRows = await CreatePaymentRowsHandler(dbContext).ExecuteAsync(
            new GetClientPaymentRowsQuery(fixture.Actor, fixture.ClientId),
            CancellationToken.None);
        Assert.Equal(GetClientPaymentRowsStatus.Success, paymentRows.Status);
        var originalPayment = Assert.Single(
            paymentRows.Page!.Items,
            payment => payment.PaymentId == originalPaymentId);
        var replacementPayment = Assert.Single(
            paymentRows.Page.Items,
            payment => payment.PaymentId == replacementPaymentId);
        Assert.Equal(ClientPaymentRowStatus.Replaced, originalPayment.Status);
        Assert.Equal(ClientPaymentRowStatus.Active, replacementPayment.Status);
        var outgoing = Assert.IsType<ClientPaymentCorrection>(
            originalPayment.CorrectionToReplacement);
        var incoming = Assert.IsType<ClientPaymentCorrection>(
            replacementPayment.CorrectionFromOriginal);
        Assert.Equal(correctionId, outgoing.CorrectionId);
        Assert.Equal(correctionId, incoming.CorrectionId);
        Assert.Equal(originalPaymentId, outgoing.OriginalPaymentId);
        Assert.Equal(replacementPaymentId, outgoing.ReplacementPaymentId);
        Assert.Equal(["negative_coverage"], outgoing.ChangedFields);
        Assert.Equal(CorrectionNow, outgoing.OccurredAt);
        Assert.Equal("Wrong one-off selection", outgoing.Reason);

        var dailyRows = await CreateDailyPaymentRowsHandler(dbContext).ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(
                fixture.Actor,
                BusinessTimeZone.GetBusinessDate(CorrectionNow)),
            CancellationToken.None);
        Assert.Equal(GetDailyPaymentSourceRowsStatus.Success, dailyRows.Status);
        Assert.Equal(1, dailyRows.Snapshot!.ActivePaymentCount);
        Assert.Equal(75m, dailyRows.Snapshot.DailyCashSum.Amount);
        Assert.Contains(
            dailyRows.Snapshot.Rows,
            row => row.Payment.PaymentId == originalPaymentId
                && row.Payment.Status == ClientPaymentRowStatus.Replaced);

        var history = await new GetClientPaymentHistorySourceRowsQueryHandler(
                dbContext,
                new GetClientAuditEntriesQueryHandler(
                    dbContext,
                    new FixedTimeProvider(CorrectionNow)))
            .ExecuteAsync(
                new GetClientPaymentHistorySourceRowsQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None);
        Assert.Equal(GetClientPaymentHistorySourceRowsStatus.Success, history.Status);
        var correctedHistory = Assert.Single(
            history.Page!.Items,
            row => row.Kind == ClientPaymentHistorySourceKind.CorrectedPayment);
        Assert.Equal(correctionId, correctedHistory.Correction!.CorrectionId);
        Assert.Equal(originalPaymentId, correctedHistory.Correction.OriginalPaymentId);
        Assert.Equal(
            replacementPaymentId,
            correctedHistory.Correction.ReplacementPaymentId);
    }

    [Theory]
    [InlineData("missing_correction")]
    [InlineData("invalid_origin")]
    public async Task CorruptedOneOffCorrectionFailsProfileReportAndHistoryClosed(
        string corruption)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId, 3);
        var originalClosureId = await CloseOneOffAsync(
            dbContext,
            fixture,
            $"corrupt-{corruption}",
            quantity: 2,
            fixture.OneOffTypeAId);
        var result = await CreateCorrectionHandler(dbContext).ExecuteAsync(
            CreateReplaceOneOffCommand(
                fixture,
                originalClosureId,
                $"replace-{corruption}",
                fixture.VisitIds[2],
                fixture.OneOffTypeBId,
                quantity: 1),
            CancellationToken.None);
        AssertSuccessful(result, fixture.ClientId);

        if (corruption == "missing_correction")
        {
            await database.ExecuteScalarAsync<int>(
                $"alter table bodylife.membership_negative_closure_corrections disable trigger user; delete from bodylife.membership_negative_closure_corrections where original_closure_id = '{originalClosureId}'; alter table bodylife.membership_negative_closure_corrections enable trigger user; select 1;");
        }
        else
        {
            Assert.Equal("invalid_origin", corruption);
            await database.ExecuteScalarAsync<int>(
                $"alter table bodylife.membership_negative_closure_corrections disable trigger user; alter table bodylife.membership_negative_closure_corrections drop constraint ck_negative_closure_corrections_origin; update bodylife.membership_negative_closure_corrections set entry_origin = 'invalid' where original_closure_id = '{originalClosureId}'; alter table bodylife.membership_negative_closure_corrections enable trigger user; select 1;");
        }

        var paymentRows = await CreatePaymentRowsHandler(dbContext).ExecuteAsync(
            new GetClientPaymentRowsQuery(fixture.Actor, fixture.ClientId),
            CancellationToken.None);
        var dailyRows = await CreateDailyPaymentRowsHandler(dbContext).ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(
                fixture.Actor,
                BusinessTimeZone.GetBusinessDate(CorrectionNow)),
            CancellationToken.None);
        var history = await new GetClientPaymentHistorySourceRowsQueryHandler(
                dbContext,
                new GetClientAuditEntriesQueryHandler(
                    dbContext,
                    new FixedTimeProvider(CorrectionNow)))
            .ExecuteAsync(
                new GetClientPaymentHistorySourceRowsQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None);

        Assert.Equal(GetClientPaymentRowsStatus.SourceInconsistent, paymentRows.Status);
        Assert.Equal(
            GetDailyPaymentSourceRowsStatus.SourceInconsistent,
            dailyRows.Status);
        Assert.Equal(
            GetClientPaymentHistorySourceRowsStatus.SourceInconsistent,
            history.Status);
    }

    [PostgreSqlFact]
    public async Task CancelAndReplaceNewMembershipCoverageRestoreCoveringCapacity()
    {
        await using var cancelDatabase = await CreateMigratedDatabaseAsync();
        var cancelFixture = await SeedFixtureAsync(
            cancelDatabase,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin);
        await using (var cancelContext = cancelDatabase.CreateDbContext())
        {
            await RebuildSourceAsync(cancelContext, cancelFixture.SourceMembershipId, 3);
            var issued = await IssueCoveringMembershipAsync(
                cancelDatabase,
                cancelContext,
                cancelFixture,
                "coverage-to-cancel",
                coverageCount: 2);
            var cancel = await CreateCorrectionHandler(cancelContext).ExecuteAsync(
                CreateCancelCommand(
                    cancelFixture,
                    issued.ClosureId,
                    "cancel-membership-coverage"),
                CancellationToken.None);

            AssertSuccessful(cancel, cancelFixture.ClientId);
            Assert.Equal(
                3,
                await ReadNegativeAsync(
                    cancelDatabase,
                    cancelFixture.SourceMembershipId));
            Assert.Equal(
                2,
                await ReadRemainingAsync(cancelDatabase, issued.MembershipId));
            Assert.Equal(
                2L,
                await cancelDatabase.ExecuteScalarAsync<long>(
                    $"select count(*) from bodylife.visit_consumptions consumption join bodylife.membership_negative_closure_items item on item.new_consumption_id = consumption.id where item.negative_closure_id = '{issued.ClosureId}' and consumption.status = 'canceled'"));
            Assert.Equal(
                "active",
                await cancelDatabase.ExecuteScalarAsync<string>(
                    $"select status from bodylife.payments where membership_id = '{issued.MembershipId}'"));
        }

        await using var replaceDatabase = await CreateMigratedDatabaseAsync();
        var replaceFixture = await SeedFixtureAsync(
            replaceDatabase,
            ActorRole.Owner,
            AccountKind.Owner);
        await using var replaceContext = replaceDatabase.CreateDbContext();
        await RebuildSourceAsync(replaceContext, replaceFixture.SourceMembershipId, 3);
        var originalPaper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            replaceDatabase,
            replaceFixture.Actor,
            CorrectionNow,
            "membership_sale",
            CorrectionNow,
            explanation: "Original paper Membership coverage");
        var original = await IssueCoveringMembershipAsync(
            replaceDatabase,
            replaceContext,
            replaceFixture,
            "coverage-to-replace",
            coverageCount: 2,
            originalPaper);
        var replacementCommand = CreateReplaceMembershipCoverageCommand(
            replaceFixture,
            original.ClosureId,
            "replace-membership-coverage",
            replaceFixture.VisitIds[2],
            coverageCount: 1);
        var replace = await CreateCorrectionHandler(replaceContext).ExecuteAsync(
            replacementCommand,
            CancellationToken.None);
        var replay = await CreateCorrectionHandler(replaceContext).ExecuteAsync(
            replacementCommand,
            CancellationToken.None);

        AssertSuccessful(replace, replaceFixture.ClientId);
        Assert.Equal(replace.RelatedEntityIds, replay.RelatedEntityIds);
        Assert.Contains(
            new EntityId("membership", original.MembershipId),
            replay.RelatedEntityIds);
        Assert.Equal(
            2,
            await ReadNegativeAsync(
                replaceDatabase,
                replaceFixture.SourceMembershipId));
        Assert.Equal(1, await ReadRemainingAsync(replaceDatabase, original.MembershipId));
        var replacementClosureId = await replaceDatabase.ExecuteScalarAsync<Guid>(
            $"select replacement_closure_id from bodylife.membership_negative_closure_corrections where original_closure_id = '{original.ClosureId}'");
        Assert.Equal(
            original.MembershipId,
            await replaceDatabase.ExecuteScalarAsync<Guid>(
                $"select covering_membership_id from bodylife.membership_negative_closures where id = '{replacementClosureId}'"));
        Assert.Equal(
            0L,
            await replaceDatabase.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.payments where negative_closure_id in ('{original.ClosureId}', '{replacementClosureId}')"));
        Assert.Equal(
            1L,
            await replaceDatabase.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.payments where membership_id = '{original.MembershipId}' and payment_context = 'membership_sale' and status = 'active'"));
        var salePaymentId = await replaceDatabase.ExecuteScalarAsync<Guid>(
            $"select id from bodylife.payments where membership_id = '{original.MembershipId}' and payment_context = 'membership_sale' and status = 'active'");
        var originalLinks = await PostgreSqlPaperFallbackTestData.ReadLinksAsync(
            replaceDatabase,
            originalPaper.EntryBatchRowId);
        Assert.Contains(
            originalLinks,
            link => link.EntityType == MembershipAuditActions.MembershipEntityType
                && link.EntityId == original.MembershipId);
        Assert.Contains(
            originalLinks,
            link => link.EntityType == PaymentAuditActions.EntityType
                && link.EntityId == salePaymentId);
        Assert.DoesNotContain(
            originalLinks,
            link => link.EntityId == replacementClosureId);

        var coverageHandler = new GetClientNegativeVisitCoverageQueryHandler(
            replaceContext,
            new MembershipNegativeVisitSelector(replaceContext),
            new FixedTimeProvider(CorrectionNow));
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.Success,
            (await coverageHandler.ExecuteAsync(
                new GetClientNegativeVisitCoverageQuery(
                    replaceFixture.Actor,
                    replaceFixture.ClientId),
                CancellationToken.None)).Status);

        await replaceDatabase.ExecuteScalarAsync<int>(
            $"""
            delete from bodylife.entry_batch_row_entities
            where entry_batch_row_id = '{originalPaper.EntryBatchRowId}'
              and entity_type = '{MembershipAuditActions.MembershipEntityType}'
              and entity_id = '{original.MembershipId}';
            select 1;
            """);
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.CanonicalStateInvalid,
            (await coverageHandler.ExecuteAsync(
                new GetClientNegativeVisitCoverageQuery(
                    replaceFixture.Actor,
                    replaceFixture.ClientId),
                CancellationToken.None)).Status);
        await PostgreSqlPaperFallbackTestData.LinkRowAsync(
            replaceDatabase,
            originalPaper.EntryBatchRowId,
            new PaperFallbackEntityLink(
                MembershipAuditActions.MembershipEntityType,
                original.MembershipId));

        await replaceDatabase.ExecuteScalarAsync<int>(
            $"""
            delete from bodylife.entry_batch_row_entities
            where entry_batch_row_id = '{originalPaper.EntryBatchRowId}'
              and entity_type = '{PaymentAuditActions.EntityType}'
              and entity_id = '{salePaymentId}';
            select 1;
            """);
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.CanonicalStateInvalid,
            (await coverageHandler.ExecuteAsync(
                new GetClientNegativeVisitCoverageQuery(
                    replaceFixture.Actor,
                    replaceFixture.ClientId),
                CancellationToken.None)).Status);
    }

    [PostgreSqlFact]
    public async Task PreservedPaperSalesRejectSameBatchAggregateRowSwap()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId, 3);
        var firstPaper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            database,
            fixture.Actor,
            CorrectionNow,
            "membership_sale",
            CorrectionNow,
            lineNumber: 31,
            explanation: "First original paper sale");
        var secondPaper = await PostgreSqlPaperFallbackTestData.SeedRowInBatchAsync(
            database,
            firstPaper,
            fixture.Actor,
            CorrectionNow,
            "membership_sale",
            CorrectionNow,
            lineNumber: 32,
            explanation: "Second original paper sale");
        var firstIssue = await IssueCoveringMembershipAsync(
            database,
            dbContext,
            fixture,
            "first-paper-sale-to-replace",
            coverageCount: 1,
            firstPaper,
            fixture.VisitIds[2]);
        var secondIssue = await IssueCoveringMembershipAsync(
            database,
            dbContext,
            fixture,
            "second-paper-sale-to-replace",
            coverageCount: 1,
            secondPaper,
            fixture.VisitIds[3]);

        var firstReplacement = await CreateCorrectionHandler(dbContext)
            .ExecuteAsync(
                CreateReplaceMembershipCoverageCommand(
                    fixture,
                    firstIssue.ClosureId,
                    "replace-first-paper-sale-coverage",
                    fixture.VisitIds[2],
                    coverageCount: 1),
                CancellationToken.None);
        var secondReplacement = await CreateCorrectionHandler(dbContext)
            .ExecuteAsync(
                CreateReplaceMembershipCoverageCommand(
                    fixture,
                    secondIssue.ClosureId,
                    "replace-second-paper-sale-coverage",
                    fixture.VisitIds[3],
                    coverageCount: 1),
                CancellationToken.None);
        AssertSuccessful(firstReplacement, fixture.ClientId);
        AssertSuccessful(secondReplacement, fixture.ClientId);

        var coverageHandler = new GetClientNegativeVisitCoverageQueryHandler(
            dbContext,
            new MembershipNegativeVisitSelector(dbContext),
            new FixedTimeProvider(CorrectionNow));
        var valid = await coverageHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);
        Assert.Equal(GetClientNegativeVisitCoverageStatus.Success, valid.Status);
        Assert.Equal(2, valid.Coverage!.ActiveClosures.Count);

        await database.ExecuteScalarAsync<int>(
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
        var swapped = await coverageHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(
                fixture.Actor,
                fixture.ClientId),
            CancellationToken.None);
        Assert.Equal(
            GetClientNegativeVisitCoverageStatus.CanonicalStateInvalid,
            swapped.Status);
    }

    [PostgreSqlFact]
    public async Task ValidationStaleAndAuditFailureLeaveOriginalCoverageActive()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId, 3);
        var originalClosureId = await CloseOneOffAsync(
            dbContext,
            fixture,
            "one-off-protected",
            quantity: 2,
            fixture.OneOffTypeAId);
        var missingReason = CreateCancelCommand(
            fixture,
            originalClosureId,
            "missing-reason") with
        {
            Envelope = CreateCancelCommand(
                fixture,
                originalClosureId,
                "missing-reason").Envelope with
            {
                Reason = null,
            },
        };

        var missingReasonResult = await CreateCorrectionHandler(dbContext).ExecuteAsync(
            missingReason,
            CancellationToken.None);
        var invalidActorCommand = CreateCancelCommand(
            fixture,
            originalClosureId,
            "invalid-actor") with
        {
            Envelope = CreateCancelCommand(
                fixture,
                originalClosureId,
                "invalid-actor").Envelope with
            {
                Actor = fixture.Actor with
                {
                    Role = ActorRole.Owner,
                    AccountKind = AccountKind.NamedAdmin,
                },
            },
        };
        var invalidActorResult = await CreateCorrectionHandler(dbContext).ExecuteAsync(
            invalidActorCommand,
            CancellationToken.None);
        var staleResult = await CreateCorrectionHandler(dbContext).ExecuteAsync(
            CreateReplaceOneOffCommand(
                fixture,
                originalClosureId,
                "stale-replacement",
                Guid.NewGuid(),
                fixture.OneOffTypeBId,
                quantity: 1),
            CancellationToken.None);

        AssertError(missingReasonResult, CommandErrorCode.ReasonRequired, "reason");
        AssertError(invalidActorResult, CommandErrorCode.PermissionDenied, field: null);
        AssertError(
            staleResult,
            CommandErrorCode.StaleState,
            "expectedOldestOpenNegativeVisitId");
        await AssertOriginalActiveAsync(database, originalClosureId, expectedNegative: 1);

        await database.ExecuteScalarAsync<int>(
            $"""
            update bodylife.membership_types
            set is_active = false,
                updated_at = '{CorrectionNow:O}',
                deactivated_at = '{CorrectionNow:O}'
            where id = '{fixture.OneOffTypeBId}';
            select 1;
            """);
        var inactiveReplacementResult = await CreateCorrectionHandler(dbContext)
            .ExecuteAsync(
                CreateReplaceOneOffCommand(
                    fixture,
                    originalClosureId,
                    "inactive-replacement",
                    fixture.VisitIds[2],
                    fixture.OneOffTypeBId,
                    quantity: 1),
                CancellationToken.None);
        AssertError(
            inactiveReplacementResult,
            CommandErrorCode.MembershipTypeInactive,
            "replacementOneOffLines[0].membershipTypeId");
        await AssertOriginalActiveAsync(database, originalClosureId, expectedNegative: 1);

        await database.ExecuteScalarAsync<int>(
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_coverage_cancel_audit
            check (action_type <> 'membership_negative_closure.canceled');
            select 1;
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateCorrectionHandler(dbContext).ExecuteAsync(
                CreateCancelCommand(
                    fixture,
                    originalClosureId,
                    "audit-failure"),
                CancellationToken.None));
        await AssertOriginalActiveAsync(database, originalClosureId, expectedNegative: 1);
    }

    [PostgreSqlFact]
    public async Task ConcurrentCorrectionsSerializeAndOnlyOneCanWin()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner);
        Guid closureId;
        await using (var setupContext = database.CreateDbContext())
        {
            await RebuildSourceAsync(setupContext, fixture.SourceMembershipId, 3);
            closureId = await CloseOneOffAsync(
                setupContext,
                fixture,
                "concurrent-original",
                quantity: 1,
                fixture.OneOffTypeAId);
        }

        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        var results = await Task.WhenAll(
            CreateCorrectionHandler(firstContext).ExecuteAsync(
                CreateCancelCommand(fixture, closureId, "concurrent-cancel-a"),
                CancellationToken.None),
            CreateCorrectionHandler(secondContext).ExecuteAsync(
                CreateCancelCommand(fixture, closureId, "concurrent-cancel-b"),
                CancellationToken.None));

        Assert.Single(results, result => result.Status == CommandStatus.Success);
        var rejected = Assert.Single(results, result => result.Status == CommandStatus.Error);
        AssertError(
            rejected,
            CommandErrorCode.AlreadyCanceled,
            "originalNegativeClosureId");
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.membership_negative_closure_corrections where original_closure_id = '{closureId}'"));
        Assert.Equal(3, await ReadNegativeAsync(database, fixture.SourceMembershipId));
    }

    [Fact]
    public void PersistenceRegistrationExposesScopedCorrectionHandler()
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

        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType
                == typeof(IBodyLifeCommandHandler<CorrectNegativeVisitCoverageCommand>));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(
            typeof(CorrectNegativeVisitCoverageCommandHandler),
            descriptor.ImplementationType);
    }

    private static async Task<PostgreSqlTestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        return database;
    }

    private static async Task<CoverageFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        ActorRole role,
        AccountKind kind)
    {
        var fixture = new CoverageFixture(
            new ActorContext(
                AccountId.New(),
                role,
                kind,
                SessionId.New(),
                "coverage correction test"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray(),
            Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray());
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into bodylife.accounts (
                id, display_name, account_type, role, is_active, created_at, deactivated_at)
            values (@account_id, 'Correction actor', @account_type, @role, true, @now, null);

            insert into bodylife.sessions (
                id, account_id, device_label, started_at, expires_at, ended_at, last_seen_at)
            values (
                @session_id, @account_id, 'coverage correction test', @now,
                @expires_at, null, @now);

            insert into bodylife.clients (
                id, surname, name, patronymic, normalized_full_name,
                phone_raw, phone_normalized, phone_last4, comment,
                operational_status, created_at, created_by_account_id, updated_at)
            values (
                @client_id, 'Correction', 'Client', null, 'CORRECTION CLIENT',
                null, null, null, null, 'active', @now, @account_id, @now);

            insert into bodylife.membership_types (
                id, name, kind, duration_days, visits_limit, price_amount,
                price_currency, is_active, comment, created_at, updated_at, deactivated_at)
            values
                (@source_type_id, 'Two visits', 'ordinary', 30, 2, 900,
                    'UAH', true, null, @created_at, @catalog_updated_at, null),
                (@covering_type_id, 'Cover plan', 'ordinary', 30, 2, 1200,
                    'UAH', true, null, @created_at, @catalog_updated_at, null),
                (@one_off_a_id, 'Single A', 'one_off', 1, 1, 50,
                    'UAH', true, null, @created_at, @catalog_updated_at, null),
                (@one_off_b_id, 'Single B', 'one_off', 1, 1, 75,
                    'UAH', true, null, @created_at, @catalog_updated_at, null);

            insert into bodylife.issued_memberships (
                id, client_id, membership_type_id, issuance_mode, type_name_snapshot,
                duration_days_snapshot, visits_limit_snapshot, price_amount_snapshot,
                price_currency_snapshot, start_date, base_end_date, issued_at,
                issued_by_account_id, status, entry_origin, entry_batch_id, comment)
            values (
                @source_membership_id, @client_id, @source_type_id, 'sale',
                'Two visits', 30, 2, 900, 'UAH', date '2026-07-01',
                date '2026-07-30', @created_at, @account_id, 'active',
                'normal', null, null);

            insert into bodylife.payments (
                id, client_id, membership_id, negative_closure_id, amount, currency,
                method, payment_context, occurred_at, recorded_at,
                recorded_by_account_id, session_id, entry_origin, entry_batch_id,
                comment, status)
            values (
                gen_random_uuid(), @client_id, @source_membership_id, null, 900, 'UAH',
                'cash', 'membership_sale', @created_at, @created_at,
                @account_id, @session_id, 'normal', null, null, 'active');
            """,
            ("account_id", fixture.Actor.AccountId.Value),
            ("account_type", MapKind(kind)),
            ("role", MapRole(role)),
            ("session_id", fixture.Actor.SessionId.Value),
            ("client_id", fixture.ClientId),
            ("source_type_id", fixture.SourceTypeId),
            ("covering_type_id", fixture.CoveringTypeId),
            ("one_off_a_id", fixture.OneOffTypeAId),
            ("one_off_b_id", fixture.OneOffTypeBId),
            ("source_membership_id", fixture.SourceMembershipId),
            ("now", TestNow.AddHours(-2)),
            ("expires_at", CorrectionNow.AddHours(8)),
            ("created_at", TestNow.AddDays(-10)),
            ("catalog_updated_at", CatalogUpdatedAt));

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
                    @consumption_id, @visit_id, @client_id, 'membership',
                    @source_membership_id, 'counted', 'visit', @visit_id, @recorded_at,
                    @account_id, @session_id, 'active');
                """,
                ("visit_id", fixture.VisitIds[index]),
                ("consumption_id", fixture.ConsumptionIds[index]),
                ("client_id", fixture.ClientId),
                ("source_membership_id", fixture.SourceMembershipId),
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

    private static async Task<Guid> CloseOneOffAsync(
        BodyLifeDbContext dbContext,
        CoverageFixture fixture,
        string key,
        int quantity,
        Guid typeId)
    {
        var result = await CreateCloseHandler(dbContext).ExecuteAsync(
            new CloseNegativeVisitsOneOffCommand(
                CreateEnvelope(fixture.Actor, key, "Create original closure"),
                fixture.ClientId,
                fixture.VisitIds[2],
                [new NegativeVisitClosureLineSelection(
                    typeId,
                    CatalogUpdatedAt,
                    quantity)]),
            CancellationToken.None);
        Assert.Equal(CommandStatus.Success, result.Status);
        return result.PrimaryEntityId!.Value.Value;
    }

    private static async Task<IssuedCoverage> IssueCoveringMembershipAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext,
        CoverageFixture fixture,
        string key,
        int coverageCount,
        PaperFallbackRowFixture? paper = null,
        Guid? expectedOldestVisitId = null)
    {
        var envelope = CreateEnvelope(
            fixture.Actor,
            key,
            "Create original coverage");
        if (paper is not null)
        {
            envelope = envelope with
            {
                EntryOrigin = EntryOrigin.PaperFallback,
                OccurredAt = CorrectionNow,
                Reason = paper.Explanation,
                EntryBatchRowId = paper.EntryBatchRowId,
            };
        }

        var result = await CreateIssueHandler(dbContext).ExecuteAsync(
            new IssueMembershipCommand(
                envelope,
                fixture.ClientId,
                fixture.CoveringTypeId,
                CatalogUpdatedAt,
                new DateOnly(2026, 7, 20),
                MembershipNegativeHandlingDecision.CoverWithNewMembership,
                EntryBatchId: null,
                coverageCount,
                expectedOldestVisitId ?? fixture.VisitIds[2]),
            CancellationToken.None);
        Assert.Equal(CommandStatus.Success, result.Status);
        var membershipId = result.PrimaryEntityId!.Value.Value;
        var closureId = await database.ExecuteScalarAsync<Guid>(
            $"select id from bodylife.membership_negative_closures where covering_membership_id = '{membershipId}'");
        return new IssuedCoverage(membershipId, closureId);
    }

    private static CorrectNegativeVisitCoverageCommand CreateCancelCommand(
        CoverageFixture fixture,
        Guid closureId,
        string key)
    {
        return new CorrectNegativeVisitCoverageCommand(
            CreateEnvelope(fixture.Actor, key, "Coverage was recorded incorrectly"),
            closureId,
            NegativeVisitCoverageCorrectionMode.Cancel);
    }

    private static CorrectNegativeVisitCoverageCommand CreateReplaceOneOffCommand(
        CoverageFixture fixture,
        Guid closureId,
        string key,
        Guid expectedOldestVisitId,
        Guid typeId,
        int quantity)
    {
        return new CorrectNegativeVisitCoverageCommand(
            CreateEnvelope(fixture.Actor, key, "Wrong one-off selection"),
            closureId,
            NegativeVisitCoverageCorrectionMode.Replace,
            [new NegativeVisitClosureLineSelection(
                typeId,
                CatalogUpdatedAt,
                quantity)],
            ReplacementNewMembershipCoverageCount: null,
            expectedOldestVisitId);
    }

    private static CorrectNegativeVisitCoverageCommand
        CreateReplaceMembershipCoverageCommand(
            CoverageFixture fixture,
            Guid closureId,
            string key,
            Guid expectedOldestVisitId,
            int coverageCount)
    {
        return new CorrectNegativeVisitCoverageCommand(
            CreateEnvelope(fixture.Actor, key, "Wrong coverage quantity"),
            closureId,
            NegativeVisitCoverageCorrectionMode.Replace,
            ReplacementOneOffLines: null,
            coverageCount,
            expectedOldestVisitId);
    }

    private static CommandEnvelope CreateEnvelope(
        ActorContext actor,
        string key,
        string reason)
    {
        return new CommandEnvelope(
            actor,
            new RequestCorrelationId($"correlation-{key}"),
            EntryOrigin.Normal,
            CorrectionNow,
            key,
            reason,
            Comment: "Coverage correction test");
    }

    private static CloseNegativeVisitsOneOffCommandHandler CreateCloseHandler(
        BodyLifeDbContext dbContext)
    {
        var time = new FixedTimeProvider(TestNow);
        var audit = new BusinessAuditAppender(dbContext);
        return new CloseNegativeVisitsOneOffCommandHandler(
            dbContext,
            audit,
            new PaperFallbackEntryRowBinder(dbContext),
            new NegativeClosurePaymentWriter(dbContext, audit),
            new MembershipNegativeVisitSelector(dbContext),
            new MembershipStateCacheRebuilder(dbContext, time),
            time);
    }

    private static IssueMembershipCommandHandler CreateIssueHandler(
        BodyLifeDbContext dbContext)
    {
        var time = new FixedTimeProvider(TestNow);
        var audit = new BusinessAuditAppender(dbContext);
        return new IssueMembershipCommandHandler(
            dbContext,
            audit,
            new MembershipIssuePaymentWriter(dbContext, audit),
            new MembershipNegativeVisitSelector(dbContext),
            new MembershipStateCacheRebuilder(dbContext, time),
            time);
    }

    private static CorrectNegativeVisitCoverageCommandHandler CreateCorrectionHandler(
        BodyLifeDbContext dbContext)
    {
        var time = new FixedTimeProvider(CorrectionNow);
        var audit = new BusinessAuditAppender(dbContext);
        return new CorrectNegativeVisitCoverageCommandHandler(
            dbContext,
            audit,
            new NegativeClosurePaymentWriter(dbContext, audit),
            new MembershipNegativeVisitSelector(dbContext),
            new MembershipStateCacheRebuilder(dbContext, time),
            time);
    }

    private static GetClientPaymentRowsQueryHandler CreatePaymentRowsHandler(
        BodyLifeDbContext dbContext)
    {
        return new GetClientPaymentRowsQueryHandler(
            dbContext,
            new OpenPaymentDayStatusProvider(),
            new FixedTimeProvider(CorrectionNow));
    }

    private static GetDailyPaymentSourceRowsQueryHandler
        CreateDailyPaymentRowsHandler(BodyLifeDbContext dbContext)
    {
        return new GetDailyPaymentSourceRowsQueryHandler(
            dbContext,
            new OpenPaymentDayStatusProvider(),
            new FixedTimeProvider(CorrectionNow));
    }

    private static async Task RebuildSourceAsync(
        BodyLifeDbContext dbContext,
        Guid membershipId,
        int expectedNegative)
    {
        var result = await new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(TestNow))
            .RebuildAsync(membershipId);
        Assert.True(result.Succeeded);
        Assert.Equal(expectedNegative, result.State!.NegativeBalance);
    }

    private static Task<int> ReadNegativeAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        return database.ExecuteScalarAsync<int>(
            $"select negative_balance from bodylife.membership_state_cache where membership_id = '{membershipId}'");
    }

    private static Task<int> ReadRemainingAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        return database.ExecuteScalarAsync<int>(
            $"select remaining_visits from bodylife.membership_state_cache where membership_id = '{membershipId}'");
    }

    private static async Task AssertOriginalActiveAsync(
        PostgreSqlTestDatabase database,
        Guid closureId,
        int expectedNegative)
    {
        Assert.Equal(
            "active",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.membership_negative_closures where id = '{closureId}'"));
        Assert.Equal(
            "active",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.payments where negative_closure_id = '{closureId}'"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                $"select count(*) from bodylife.membership_negative_closure_corrections where original_closure_id = '{closureId}'"));
        var membershipId = await database.ExecuteScalarAsync<Guid>(
            $"select source_membership_id from bodylife.membership_negative_closure_items where negative_closure_id = '{closureId}' limit 1");
        Assert.Equal(expectedNegative, await ReadNegativeAsync(database, membershipId));
    }

    private static void AssertSuccessful(CommandResult result, Guid clientId)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Equal(
            CorrectNegativeVisitCoverageCommand.PrimaryEntityType,
            result.PrimaryEntityId!.Value.Type);
        Assert.Equal(new EntityId("client", clientId), result.RereadTargetId);
        Assert.True(result.AuditEntryId.HasValue);
        Assert.Empty(result.Errors);
    }

    private static void AssertError(
        CommandResult result,
        CommandErrorCode code,
        string? field)
    {
        Assert.Equal(CommandStatus.Error, result.Status);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(field, error.Field);
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

    private static string MapKind(AccountKind kind) => kind switch
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

    private sealed record CoverageFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid SourceTypeId,
        Guid CoveringTypeId,
        Guid OneOffTypeAId,
        Guid OneOffTypeBId,
        Guid SourceMembershipId,
        Guid[] VisitIds,
        Guid[] ConsumptionIds);

    private sealed record IssuedCoverage(Guid MembershipId, Guid ClosureId);

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
