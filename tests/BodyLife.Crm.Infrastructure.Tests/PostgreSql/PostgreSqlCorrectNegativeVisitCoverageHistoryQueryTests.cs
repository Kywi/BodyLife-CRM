using System.Text.Json.Nodes;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Reports;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed partial class PostgreSqlCorrectNegativeVisitCoverageCommandTests
{
    [PostgreSqlFact]
    public async Task NewMembershipHistoryPreservesSaleRowAndComposesThroughClientHistory()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId, 3);
        var salePaper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            database,
            fixture.Actor,
            CorrectionNow,
            "membership_sale",
            TestNow,
            explanation: "Paper Membership sale with negative coverage");
        var original = await IssueCoveringMembershipAsync(
            database,
            dbContext,
            fixture,
            "paper-history-membership-original",
            coverageCount: 2,
            salePaper);
        var correctionPaper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            database,
            fixture.Actor,
            CorrectionNow,
            "correction_or_cancellation",
            CorrectionNow,
            explanation: "Paper replacement of Membership coverage");
        var replacement = CreateReplaceMembershipCoverageCommand(
            fixture,
            original.ClosureId,
            "paper-history-membership-replacement",
            fixture.VisitIds[2],
            coverageCount: 1) with
        {
            Envelope = CreateEnvelope(
                fixture.Actor,
                "paper-history-membership-replacement",
                correctionPaper.Explanation) with
            {
                EntryOrigin = EntryOrigin.PaperFallback,
                EntryBatchRowId = correctionPaper.EntryBatchRowId,
            },
        };
        var replacementResult = await CreateCorrectionHandler(dbContext).ExecuteAsync(
            replacement,
            CancellationToken.None);
        Assert.Equal(CommandStatus.Success, replacementResult.Status);
        var correctionId = replacementResult.PrimaryEntityId!.Value.Value;
        var replacementClosureId = await database.ExecuteScalarAsync<Guid>(
            $"select replacement_closure_id from bodylife.membership_negative_closure_corrections where id = '{correctionId}'");
        var salePaymentId = await database.ExecuteScalarAsync<Guid>(
            $"select id from bodylife.payments where membership_id = '{original.MembershipId}' and payment_context = 'membership_sale'");

        var auditHandler = new GetClientAuditEntriesQueryHandler(
            dbContext,
            new FixedTimeProvider(CorrectionNow));
        var coverageHandler = new GetClientNegativeVisitCoverageHistorySourceRowsQueryHandler(
            dbContext,
            auditHandler);
        var sourceResult = await coverageHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 10),
            CancellationToken.None);
        var historyHandler = new GetClientHistoryQueryHandler(
            auditHandler,
            new GetClientMembershipHistorySourceRowsQueryHandler(dbContext, auditHandler),
            coverageHandler,
            new GetClientVisitHistorySourceRowsQueryHandler(dbContext, auditHandler),
            new GetClientPaymentHistorySourceRowsQueryHandler(dbContext, auditHandler),
            new GetClientFreezeHistorySourceRowsQueryHandler(dbContext, auditHandler),
            new GetClientNonWorkingDayHistorySourceRowsQueryHandler(dbContext, auditHandler));
        var historyResult = await historyHandler.ExecuteAsync(
            new GetClientHistoryQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 10,
                EntityFilters: [ClientHistoryEntityFilter.NegativeCoverage]),
            CancellationToken.None);

        Assert.Equal(
            GetClientNegativeVisitCoverageHistorySourceRowsStatus.Success,
            sourceResult.Status);
        Assert.Equal(GetClientHistoryStatus.Success, historyResult.Status);
        var rows = sourceResult.Page!.Items;
        Assert.Equal(3, rows.Count);
        Assert.Equal(
            rows.Select(row => row.AuditEntry.AuditEntryId),
            historyResult.Page!.Items.Select(row => row.AuditEntry.AuditEntryId));
        Assert.All(historyResult.Page.Items, row =>
            Assert.NotNull(row.NegativeCoverageSourceRow));
        Assert.Equal(
            rows.Select(row => row.Kind switch
            {
                ClientNegativeVisitCoverageHistorySourceKind.Created
                    => ClientHistorySourceKind.NegativeCoverageCreated,
                ClientNegativeVisitCoverageHistorySourceKind.Replaced
                    => ClientHistorySourceKind.NegativeCoverageReplaced,
                _ => throw new InvalidOperationException(),
            }),
            historyResult.Page.Items.Select(row => row.Kind));

        var originalCreation = Assert.Single(rows, row =>
            row.Kind == ClientNegativeVisitCoverageHistorySourceKind.Created
            && row.Closure.ClosureId == original.ClosureId);
        var replacementCreation = Assert.Single(rows, row =>
            row.Kind == ClientNegativeVisitCoverageHistorySourceKind.Created
            && row.Closure.ClosureId == replacementClosureId);
        AssertPaperReference(
            originalCreation.PaperReference,
            salePaper,
            PaperFallbackEventType.MembershipSale);
        AssertPaperReference(
            replacementCreation.PaperReference,
            correctionPaper,
            PaperFallbackEventType.CorrectionOrCancellation);
        Assert.Equal(
            original.MembershipId,
            originalCreation.Closure.CoveringMembership!.MembershipId);
        Assert.Equal(
            original.MembershipId,
            replacementCreation.Closure.CoveringMembership!.MembershipId);
        Assert.Null(originalCreation.Closure.Payment);
        Assert.Null(replacementCreation.Closure.Payment);
        var saleLinks = await PostgreSqlPaperFallbackTestData.ReadLinksAsync(
            database,
            salePaper.EntryBatchRowId);
        var correctionLinks = await PostgreSqlPaperFallbackTestData.ReadLinksAsync(
            database,
            correctionPaper.EntryBatchRowId);
        Assert.Contains(saleLinks, link =>
            link.EntityType == MembershipAuditActions.MembershipEntityType
            && link.EntityId == original.MembershipId);
        Assert.Contains(saleLinks, link =>
            link.EntityType == PaymentAuditActions.EntityType
            && link.EntityId == salePaymentId);
        Assert.DoesNotContain(correctionLinks, link =>
            link.EntityId is var id
            && (id == original.MembershipId || id == salePaymentId));

        await AssertAuditJsonMutationFailsClosedAsync(
            database,
            coverageHandler,
            fixture.Actor,
            fixture.ClientId,
            originalCreation.AuditEntry.AuditEntryId.Value,
            "related_entity_refs",
            json => json["salePaymentAuditEntryId"] = JsonValue.Create(Guid.NewGuid()));
        await AssertAuditJsonMutationFailsClosedAsync(
            database,
            coverageHandler,
            fixture.Actor,
            fixture.ClientId,
            replacementCreation.AuditEntry.AuditEntryId.Value,
            "after_summary",
            json => json["coveringMembershipId"] = JsonValue.Create(Guid.NewGuid()));

        Assert.Equal(
            1,
            await database.ExecuteScalarAsync<int>(
                $"""
                with deleted as (
                    delete from bodylife.entry_batch_row_entities
                    where entry_batch_row_id = '{salePaper.EntryBatchRowId}'
                      and entity_type = 'payment'
                      and entity_id = '{salePaymentId}'
                    returning 1
                )
                select count(*)::int from deleted
                """));
        var inconsistent = await coverageHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 10),
            CancellationToken.None);
        Assert.Equal(
            GetClientNegativeVisitCoverageHistorySourceRowsStatus.SourceInconsistent,
            inconsistent.Status);
    }

    [PostgreSqlFact]
    public async Task HistoryShowsPaperCancellationAndFailsClosedWithoutItsCorrectionLink()
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
            "history-cancel-original",
            quantity: 1,
            fixture.OneOffTypeAId);
        var paper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            database,
            fixture.Actor,
            CorrectionNow,
            "correction_or_cancellation",
            CorrectionNow,
            explanation: "Paper cancellation of one-off coverage");
        var cancel = CreateCancelCommand(
            fixture,
            originalClosureId,
            "history-paper-cancel") with
        {
            Envelope = CreateEnvelope(
                fixture.Actor,
                "history-paper-cancel",
                paper.Explanation) with
            {
                EntryOrigin = EntryOrigin.PaperFallback,
                EntryBatchRowId = paper.EntryBatchRowId,
            },
        };
        var cancelResult = await CreateCorrectionHandler(dbContext).ExecuteAsync(
            cancel,
            CancellationToken.None);
        Assert.Equal(CommandStatus.Success, cancelResult.Status);
        var correctionId = cancelResult.PrimaryEntityId!.Value.Value;

        var handler = new GetClientNegativeVisitCoverageHistorySourceRowsQueryHandler(
            dbContext,
            new GetClientAuditEntriesQueryHandler(
                dbContext,
                new FixedTimeProvider(CorrectionNow)));
        var result = await handler.ExecuteAsync(
            new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 10),
            CancellationToken.None);

        Assert.Equal(
            GetClientNegativeVisitCoverageHistorySourceRowsStatus.Success,
            result.Status);
        var canceled = Assert.Single(result.Page!.Items, row =>
            row.Kind == ClientNegativeVisitCoverageHistorySourceKind.Canceled);
        Assert.Equal(originalClosureId, canceled.Closure.ClosureId);
        Assert.Equal(
            NegativeVisitCoverageClosureHistoryStatus.Canceled,
            canceled.Closure.Status);
        Assert.Equal(
            NegativeVisitCoverageClosureHistoryStatus.Canceled,
            canceled.Closure.Payment!.Status);
        Assert.Null(canceled.ReplacementClosure);
        Assert.Equal(correctionId, canceled.Correction!.CorrectionId);
        Assert.Equal(NegativeVisitCoverageCorrectionHistoryMode.Cancel, canceled.Correction.Mode);
        AssertPaperReference(
            canceled.PaperReference,
            paper,
            PaperFallbackEventType.CorrectionOrCancellation);

        await AssertAuditJsonMutationFailsClosedAsync(
            database,
            handler,
            fixture.Actor,
            fixture.ClientId,
            canceled.AuditEntry.AuditEntryId.Value,
            "related_entity_refs",
            json => json["originalPaymentId"] = JsonValue.Create(Guid.NewGuid()));
        await AssertAuditJsonMutationFailsClosedAsync(
            database,
            handler,
            fixture.Actor,
            fixture.ClientId,
            canceled.AuditEntry.AuditEntryId.Value,
            "after_summary",
            json =>
            {
                var correction = Assert.IsType<JsonObject>(json["correction"]);
                var changedAfterClose = correction["changedAfterClose"]!.GetValue<bool>();
                correction["changedAfterClose"] = JsonValue.Create(!changedAfterClose);
            });

        Assert.Equal(
            1,
            await database.ExecuteScalarAsync<int>(
                $"""
                with deleted as (
                    delete from bodylife.entry_batch_row_entities
                    where entry_batch_row_id = '{paper.EntryBatchRowId}'
                      and entity_type = 'membership_negative_closure_correction'
                      and entity_id = '{correctionId}'
                    returning 1
                )
                select count(*)::int from deleted
                """));
        var inconsistent = await handler.ExecuteAsync(
            new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 10),
            CancellationToken.None);
        Assert.Equal(
            GetClientNegativeVisitCoverageHistorySourceRowsStatus.SourceInconsistent,
            inconsistent.Status);
    }

    [PostgreSqlFact]
    public async Task HistoryPreservesDistinctPaperCreationAndReplacementRowsAndFailsClosedOnMovedFact()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedFixtureAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner);
        await using var dbContext = database.CreateDbContext();
        await RebuildSourceAsync(dbContext, fixture.SourceMembershipId, 3);

        var initialOccurredAt = TestNow.AddMinutes(-30);
        var initialPaper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            database,
            fixture.Actor,
            initialOccurredAt,
            "negative_coverage",
            TestNow,
            explanation: "Paper one-off coverage");
        var initialResult = await CreateCloseHandler(dbContext).ExecuteAsync(
            new CloseNegativeVisitsOneOffCommand(
                CreateEnvelope(
                    fixture.Actor,
                    "paper-history-original",
                    initialPaper.Explanation) with
                {
                    EntryOrigin = EntryOrigin.PaperFallback,
                    OccurredAt = initialOccurredAt,
                    EntryBatchRowId = initialPaper.EntryBatchRowId,
                },
                fixture.ClientId,
                fixture.VisitIds[2],
                [new NegativeVisitClosureLineSelection(
                    fixture.OneOffTypeAId,
                    CatalogUpdatedAt,
                    2)]),
            CancellationToken.None);
        Assert.Equal(CommandStatus.Success, initialResult.Status);
        var originalClosureId = initialResult.PrimaryEntityId!.Value.Value;

        var correctionPaper = await PostgreSqlPaperFallbackTestData.SeedRowAsync(
            database,
            fixture.Actor,
            CorrectionNow,
            "correction_or_cancellation",
            CorrectionNow,
            explanation: "Paper replacement of one-off coverage");
        var replacementCommand = CreateReplaceOneOffCommand(
            fixture,
            originalClosureId,
            "paper-history-replacement",
            fixture.VisitIds[2],
            fixture.OneOffTypeBId,
            quantity: 1) with
        {
            Envelope = CreateEnvelope(
                fixture.Actor,
                "paper-history-replacement",
                correctionPaper.Explanation) with
            {
                EntryOrigin = EntryOrigin.PaperFallback,
                EntryBatchRowId = correctionPaper.EntryBatchRowId,
            },
        };
        var replacementResult = await CreateCorrectionHandler(dbContext).ExecuteAsync(
            replacementCommand,
            CancellationToken.None);
        Assert.Equal(CommandStatus.Success, replacementResult.Status);
        var correctionId = replacementResult.PrimaryEntityId!.Value.Value;
        var replacementClosureId = await database.ExecuteScalarAsync<Guid>(
            $"select replacement_closure_id from bodylife.membership_negative_closure_corrections where id = '{correctionId}'");

        var auditHandler = new GetClientAuditEntriesQueryHandler(
            dbContext,
            new FixedTimeProvider(CorrectionNow));
        var sourceHandler = new GetClientNegativeVisitCoverageHistorySourceRowsQueryHandler(
            dbContext,
            auditHandler);
        var sourceResult = await sourceHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 10),
            CancellationToken.None);
        var auditResult = await auditHandler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                fixture.Actor,
                fixture.ClientId,
                EntityFilters: [ClientAuditEntityFilter.MembershipNegativeClosure],
                ActionTypes:
                [
                    MembershipNegativeClosureAuditActions.Created,
                    MembershipNegativeClosureAuditActions.Canceled,
                    MembershipNegativeClosureAuditActions.Replaced,
                ],
                Limit: 10),
            CancellationToken.None);

        Assert.Equal(
            GetClientNegativeVisitCoverageHistorySourceRowsStatus.Success,
            sourceResult.Status);
        Assert.Equal(GetClientAuditEntriesStatus.Success, auditResult.Status);
        var rows = sourceResult.Page!.Items;
        Assert.Equal(3, rows.Count);
        Assert.Equal(
            auditResult.Page!.Items.Select(entry => entry.AuditEntryId),
            rows.Select(row => row.AuditEntry.AuditEntryId));

        var originalCreation = Assert.Single(rows, row =>
            row.Kind == ClientNegativeVisitCoverageHistorySourceKind.Created
            && row.Closure.ClosureId == originalClosureId);
        var replacementLifecycle = Assert.Single(rows, row =>
            row.Kind == ClientNegativeVisitCoverageHistorySourceKind.Replaced
            && row.Closure.ClosureId == originalClosureId);
        var replacementCreation = Assert.Single(rows, row =>
            row.Kind == ClientNegativeVisitCoverageHistorySourceKind.Created
            && row.Closure.ClosureId == replacementClosureId);

        AssertPaperReference(
            originalCreation.PaperReference,
            initialPaper,
            PaperFallbackEventType.NegativeCoverage);
        AssertPaperReference(
            replacementLifecycle.PaperReference,
            correctionPaper,
            PaperFallbackEventType.CorrectionOrCancellation);
        AssertPaperReference(
            replacementCreation.PaperReference,
            correctionPaper,
            PaperFallbackEventType.CorrectionOrCancellation);
        Assert.Equal(
            NegativeVisitCoverageClosureHistoryStatus.Replaced,
            originalCreation.Closure.Status);
        Assert.Equal(
            NegativeVisitCoverageClosureHistoryStatus.Replaced,
            originalCreation.Closure.Payment!.Status);
        Assert.Equal(correctionId, replacementLifecycle.Correction!.CorrectionId);
        Assert.Equal(
            replacementClosureId,
            replacementLifecycle.ReplacementClosure!.ClosureId);
        Assert.Equal(
            NegativeVisitCoverageClosureHistoryStatus.Active,
            replacementLifecycle.ReplacementClosure.Status);
        Assert.Equal(
            NegativeVisitCoverageClosureHistoryStatus.Active,
            replacementLifecycle.ReplacementClosure.Payment!.Status);
        Assert.Equal(
            fixture.OneOffTypeBId,
            Assert.Single(replacementCreation.Closure.Lines).MembershipTypeId);
        Assert.Equal(fixture.VisitIds[2], replacementCreation.Closure.OldestOpenNegativeVisitId);

        var firstPage = await sourceHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 1),
            CancellationToken.None);
        Assert.Equal(
            GetClientNegativeVisitCoverageHistorySourceRowsStatus.Success,
            firstPage.Status);
        var firstSourcePage = Assert.IsType<
            ClientNegativeVisitCoverageHistorySourceRowsPage>(firstPage.Page);
        var secondPage = await sourceHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 1,
                Offset: firstSourcePage.NextOffset!.Value),
            CancellationToken.None);
        Assert.Equal(
            GetClientNegativeVisitCoverageHistorySourceRowsStatus.Success,
            secondPage.Status);
        var secondSourcePage = Assert.IsType<
            ClientNegativeVisitCoverageHistorySourceRowsPage>(secondPage.Page);
        var thirdPage = await sourceHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 1,
                Offset: secondSourcePage.NextOffset!.Value),
            CancellationToken.None);
        Assert.Equal(
            GetClientNegativeVisitCoverageHistorySourceRowsStatus.Success,
            thirdPage.Status);
        var thirdSourcePage = Assert.IsType<
            ClientNegativeVisitCoverageHistorySourceRowsPage>(thirdPage.Page);
        Assert.True(firstSourcePage.HasMore);
        Assert.Equal(1, firstSourcePage.NextOffset);
        Assert.True(secondSourcePage.HasMore);
        Assert.Equal(2, secondSourcePage.NextOffset);
        Assert.False(thirdSourcePage.HasMore);
        Assert.Null(thirdSourcePage.NextOffset);
        Assert.Equal(
            rows.Select(row => row.AuditEntry.AuditEntryId),
            firstSourcePage.Items
                .Concat(secondSourcePage.Items)
                .Concat(thirdSourcePage.Items)
                .Select(row => row.AuditEntry.AuditEntryId));

        var clientHistoryHandler = CreateClientHistoryHandler(
            dbContext,
            auditHandler,
            sourceHandler);
        var firstHistoryPage = await clientHistoryHandler.ExecuteAsync(
            new GetClientHistoryQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 1,
                EntityFilters: [ClientHistoryEntityFilter.NegativeCoverage]),
            CancellationToken.None);
        Assert.Equal(GetClientHistoryStatus.Success, firstHistoryPage.Status);
        var firstCanonicalHistoryPage = Assert.IsType<ClientHistoryPage>(
            firstHistoryPage.Page);
        var secondHistoryPage = await clientHistoryHandler.ExecuteAsync(
            new GetClientHistoryQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 1,
                Offset: firstCanonicalHistoryPage.NextOffset!.Value,
                EntityFilters: [ClientHistoryEntityFilter.NegativeCoverage]),
            CancellationToken.None);

        Assert.Equal(GetClientHistoryStatus.Success, secondHistoryPage.Status);
        var secondCanonicalHistoryPage = Assert.IsType<ClientHistoryPage>(
            secondHistoryPage.Page);
        Assert.True(firstCanonicalHistoryPage.HasMore);
        Assert.Equal(1, firstCanonicalHistoryPage.NextOffset);
        Assert.Equal(1, secondCanonicalHistoryPage.Offset);
        Assert.Equal(
            firstSourcePage.Items[0].AuditEntry.AuditEntryId,
            firstCanonicalHistoryPage.Items[0].AuditEntry.AuditEntryId);
        Assert.Equal(
            secondSourcePage.Items[0].AuditEntry.AuditEntryId,
            secondCanonicalHistoryPage.Items[0].AuditEntry.AuditEntryId);

        var comparisonOffset = TimeSpan.FromHours(3);
        var initialRange = await sourceHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                OccurredFromInclusive: initialOccurredAt.ToOffset(comparisonOffset),
                OccurredBeforeExclusive: CorrectionNow.ToOffset(comparisonOffset),
                Limit: 10),
            CancellationToken.None);
        var correctionRange = await sourceHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                OccurredFromInclusive: CorrectionNow.ToOffset(comparisonOffset),
                OccurredBeforeExclusive: CorrectionNow.AddMinutes(1).ToOffset(comparisonOffset),
                Limit: 10),
            CancellationToken.None);

        Assert.Equal(
            originalCreation.AuditEntry.AuditEntryId,
            Assert.Single(initialRange.Page!.Items).AuditEntry.AuditEntryId);
        var expectedCorrectionOrder = await ReadTiedNegativeCoverageAuditIdsAsync(
            database,
            CorrectionNow);
        Assert.Equal(2, expectedCorrectionOrder.Length);
        Assert.Equal(
            expectedCorrectionOrder,
            correctionRange.Page!.Items
                .Select(row => row.AuditEntry.AuditEntryId.Value)
                .ToArray());

        await AssertAuditJsonMutationFailsClosedAsync(
            database,
            sourceHandler,
            fixture.Actor,
            fixture.ClientId,
            originalCreation.AuditEntry.AuditEntryId.Value,
            "related_entity_refs",
            json => json["paymentAuditEntryId"] = JsonValue.Create(Guid.NewGuid()));
        await AssertAuditJsonMutationFailsClosedAsync(
            database,
            sourceHandler,
            fixture.Actor,
            fixture.ClientId,
            replacementCreation.AuditEntry.AuditEntryId.Value,
            "related_entity_refs",
            json => json["replacementPaymentAuditEntryId"] =
                JsonValue.Create(Guid.NewGuid()));
        await AssertAuditJsonMutationFailsClosedAsync(
            database,
            sourceHandler,
            fixture.Actor,
            fixture.ClientId,
            replacementLifecycle.AuditEntry.AuditEntryId.Value,
            "after_summary",
            json =>
            {
                var replacement = Assert.IsType<JsonObject>(json["replacement"]);
                var visitsCount = replacement["visitsCount"]!.GetValue<int>();
                replacement["visitsCount"] = JsonValue.Create(visitsCount + 1);
            });

        var replacementPaymentId = replacementCreation.Closure.Payment!.PaymentId;
        Assert.Equal(
            1,
            await database.ExecuteScalarAsync<int>(
                $"""
                with deleted as (
                    delete from bodylife.entry_batch_row_entities
                    where entry_batch_row_id = '{correctionPaper.EntryBatchRowId}'
                      and entity_type = 'payment'
                      and entity_id = '{replacementPaymentId}'
                    returning 1
                )
                select count(*)::int from deleted
                """));

        var inconsistent = await sourceHandler.ExecuteAsync(
            new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
                fixture.Actor,
                fixture.ClientId,
                Limit: 10),
            CancellationToken.None);
        Assert.Equal(
            GetClientNegativeVisitCoverageHistorySourceRowsStatus.SourceInconsistent,
            inconsistent.Status);
        Assert.Null(inconsistent.Page);
    }

    private static GetClientHistoryQueryHandler CreateClientHistoryHandler(
        BodyLifeDbContext dbContext,
        GetClientAuditEntriesQueryHandler auditHandler,
        GetClientNegativeVisitCoverageHistorySourceRowsQueryHandler coverageHandler) => new(
            auditHandler,
            new GetClientMembershipHistorySourceRowsQueryHandler(dbContext, auditHandler),
            coverageHandler,
            new GetClientVisitHistorySourceRowsQueryHandler(dbContext, auditHandler),
            new GetClientPaymentHistorySourceRowsQueryHandler(dbContext, auditHandler),
            new GetClientFreezeHistorySourceRowsQueryHandler(dbContext, auditHandler),
            new GetClientNonWorkingDayHistorySourceRowsQueryHandler(dbContext, auditHandler));

    private static async Task AssertAuditJsonMutationFailsClosedAsync(
        PostgreSqlTestDatabase database,
        GetClientNegativeVisitCoverageHistorySourceRowsQueryHandler handler,
        ActorContext actor,
        Guid clientId,
        Guid auditEntryId,
        string columnName,
        Action<JsonObject> mutate)
    {
        var originalJson = await ReadAuditJsonAsync(database, auditEntryId, columnName);
        var mutatedJson = JsonNode.Parse(originalJson)?.AsObject()
            ?? throw new InvalidOperationException("Audit JSON must be an object.");
        mutate(mutatedJson);
        await WriteAuditJsonAsync(database, auditEntryId, columnName, mutatedJson.ToJsonString());

        try
        {
            var result = await handler.ExecuteAsync(
                new GetClientNegativeVisitCoverageHistorySourceRowsQuery(
                    actor,
                    clientId,
                    Limit: 10),
                CancellationToken.None);
            Assert.Equal(
                GetClientNegativeVisitCoverageHistorySourceRowsStatus.SourceInconsistent,
                result.Status);
            Assert.Null(result.Page);
        }
        finally
        {
            await WriteAuditJsonAsync(database, auditEntryId, columnName, originalJson);
        }
    }

    private static async Task<string> ReadAuditJsonAsync(
        PostgreSqlTestDatabase database,
        Guid auditEntryId,
        string columnName)
    {
        var column = GetAuditJsonColumn(columnName);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select {column}::text from bodylife.business_audit_entries where id = @id";
        command.Parameters.AddWithValue("id", auditEntryId);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task WriteAuditJsonAsync(
        PostgreSqlTestDatabase database,
        Guid auditEntryId,
        string columnName,
        string json)
    {
        var column = GetAuditJsonColumn(columnName);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "alter table bodylife.business_audit_entries disable trigger trg_business_audit_entries_append_only";
        await command.ExecuteNonQueryAsync();

        command.CommandText =
            $"update bodylife.business_audit_entries set {column} = @json where id = @id";
        command.Parameters.Add("json", NpgsqlDbType.Jsonb).Value = json;
        command.Parameters.AddWithValue("id", auditEntryId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        command.Parameters.Clear();
        command.CommandText =
            "alter table bodylife.business_audit_entries enable trigger trg_business_audit_entries_append_only";
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static string GetAuditJsonColumn(string columnName) => columnName switch
    {
        "related_entity_refs" => "related_entity_refs",
        "after_summary" => "after_summary",
        _ => throw new ArgumentOutOfRangeException(nameof(columnName)),
    };

    private static async Task<Guid[]> ReadTiedNegativeCoverageAuditIdsAsync(
        PostgreSqlTestDatabase database,
        DateTimeOffset instant)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select coalesce(array_agg(id order by id desc), array[]::uuid[])
            from bodylife.business_audit_entries
            where entity_type = 'membership_negative_closure'
              and action_type in (
                  'membership_negative_closure.created',
                  'membership_negative_closure.canceled',
                  'membership_negative_closure.replaced')
              and occurred_at = @instant
              and recorded_at = @instant
            """;
        command.Parameters.AddWithValue("instant", instant);
        return Assert.IsType<Guid[]>(await command.ExecuteScalarAsync());
    }

    private static void AssertPaperReference(
        PaperFallbackEntryRowReference? actual,
        PaperFallbackRowFixture expected,
        PaperFallbackEventType expectedEventType)
    {
        var reference = Assert.IsType<PaperFallbackEntryRowReference>(actual);
        Assert.Equal(expected.EntryBatchId, reference.EntryBatchId);
        Assert.Equal(expected.EntryBatchRowId, reference.EntryBatchRowId);
        Assert.Equal(expected.PaperSheetNumber, reference.PaperSheetNumber);
        Assert.Equal(expected.LineNumber, reference.LineNumber);
        Assert.Equal(expected.Explanation, reference.Explanation);
        Assert.Equal(expectedEventType, reference.EventType);
    }
}
