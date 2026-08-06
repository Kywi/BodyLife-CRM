using System.Data.Common;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed partial class PostgreSqlGetClientNegativeVisitCoverageQueryTests
{
    [PostgreSqlFact]
    public async Task OneOffPreviewReturnsExactTotalOldestVisitsAndPartialRemainder()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        var addedVisits = await AddSourceVisitsAsync(database, fixture, 3);
        await using var dbContext = database.CreateDbContext();

        var result = await CreateClosePreviewHandler(dbContext).ExecuteAsync(
            new PreviewCloseNegativeVisitsOneOffQuery(
                fixture.Owner,
                fixture.ClientId,
                addedVisits[0],
                [new NegativeVisitClosureLineSelection(
                    fixture.OtherOneOffTypeId,
                    Now,
                    2)]),
            CancellationToken.None);

        Assert.Equal(PreviewCloseNegativeVisitsOneOffStatus.Success, result.Status);
        var preview = result.Preview!;
        Assert.Equal(new Money(150m, "UAH"), preview.ExactPaymentTotal);
        Assert.Equal(addedVisits[..2], preview.CoveredVisits.Select(item => item.VisitId));
        Assert.Equal(1, preview.RemainingTotalNegativeBalance);
        Assert.Equal(0, preview.RemainingUnknownNegativeBalance);
        Assert.Equal(addedVisits[0], preview.CurrentSelectors.CurrentOldestOpenNegativeVisitId);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task CorrectionCancelAndReplaceProjectOneOffAndNewMembershipParity()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        var addedVisits = await AddSourceVisitsAsync(database, fixture, 1);

        await using (var oneOffContext = database.CreateDbContext())
        {
            var cancel = await CreateCorrectionPreviewHandler(oneOffContext).ExecuteAsync(
                new PreviewCorrectNegativeVisitCoverageQuery(
                    fixture.Owner,
                    fixture.OneOffClosureId,
                    NegativeVisitCoverageCorrectionMode.Cancel,
                    "Wrong closure"),
                CancellationToken.None);
            Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.Success, cancel.Status);
            Assert.Equal(fixture.VisitIds[1], Assert.Single(cancel.Preview!.RestoredVisits).VisitId);
            Assert.Equal(new Money(50m, "UAH"), cancel.Preview.OriginalPayment!.Amount);
            Assert.Null(cancel.Preview.ReplacementPayment);
            Assert.Equal(2, cancel.Preview.ResultingRemainingNegativeBalance);

            var replace = await CreateCorrectionPreviewHandler(oneOffContext).ExecuteAsync(
                new PreviewCorrectNegativeVisitCoverageQuery(
                    fixture.Owner,
                    fixture.OneOffClosureId,
                    NegativeVisitCoverageCorrectionMode.Replace,
                    "Use current catalog",
                    [new NegativeVisitClosureLineSelection(
                        fixture.OtherOneOffTypeId,
                        Now,
                        1)],
                    ExpectedOldestOpenNegativeVisitId: fixture.VisitIds[1]),
                CancellationToken.None);
            Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.Success, replace.Status);
            Assert.Equal(new Money(75m, "UAH"), replace.Preview!.ReplacementPayment!.Amount);
            Assert.Equal(fixture.VisitIds[1], Assert.Single(replace.Preview.ReplacementCoveredVisits).VisitId);
            Assert.Equal(1, replace.Preview.ResultingRemainingNegativeBalance);
        }

        await using var newMembershipContext = database.CreateDbContext();
        var cancelNew = await CreateCorrectionPreviewHandler(newMembershipContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.Owner,
                fixture.NewMembershipClosureId,
                NegativeVisitCoverageCorrectionMode.Cancel,
                "Wrong allocation"),
            CancellationToken.None);
        Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.Success, cancelNew.Status);
        Assert.Equal(
            new[] { fixture.VisitIds[2], fixture.VisitIds[3] },
            cancelNew.Preview!.RestoredVisits.Select(item => item.VisitId));
        Assert.Equal(3, cancelNew.Preview.CoveringMembership!.RestoredRemainingVisits);
        Assert.Equal(3, cancelNew.Preview.ResultingRemainingNegativeBalance);

        var replaceNew = await CreateCorrectionPreviewHandler(newMembershipContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.Owner,
                fixture.NewMembershipClosureId,
                NegativeVisitCoverageCorrectionMode.Replace,
                "Cover one",
                ReplacementNewMembershipCoverageCount: 1,
                ExpectedOldestOpenNegativeVisitId: fixture.VisitIds[2]),
            CancellationToken.None);
        Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.Success, replaceNew.Status);
        Assert.Equal(
            new[] { fixture.VisitIds[2] },
            replaceNew.Preview!.ReplacementCoveredVisits.Select(item => item.VisitId));
        Assert.Equal(2, replaceNew.Preview.CoveringMembership!.ReplacementRemainingVisits);
        Assert.Equal(2, replaceNew.Preview.ResultingRemainingNegativeBalance);
        Assert.Null(replaceNew.Preview.OriginalPayment);
        Assert.Null(replaceNew.Preview.ReplacementPayment);
    }

    [PostgreSqlFact]
    public async Task NewMembershipTwoToOnePreviewMatchesExecutedCorrection()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        await using var previewContext = database.CreateDbContext();

        var preview = await CreateCorrectionPreviewHandler(previewContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.Owner,
                fixture.NewMembershipClosureId,
                NegativeVisitCoverageCorrectionMode.Replace,
                "Reduce mistaken coverage",
                ReplacementNewMembershipCoverageCount: 1,
                ExpectedOldestOpenNegativeVisitId: fixture.VisitIds[2]),
            CancellationToken.None);

        Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.Success, preview.Status);
        Assert.Equal(
            new[] { fixture.VisitIds[2], fixture.VisitIds[3] },
            preview.Preview!.RestoredVisits.Select(item => item.VisitId));
        Assert.Equal(fixture.VisitIds[2], Assert.Single(
            preview.Preview.ReplacementCoveredVisits).VisitId);
        Assert.Equal(2, preview.Preview.RestoredTotalNegativeBalance);
        Assert.Equal(1, preview.Preview.ResultingRemainingNegativeBalance);
        Assert.Equal(1, preview.Preview.CoveringMembership!.CurrentRemainingVisits);
        Assert.Equal(3, preview.Preview.CoveringMembership.RestoredRemainingVisits);
        Assert.Equal(2, preview.Preview.CoveringMembership.ReplacementRemainingVisits);

        await using var commandContext = database.CreateDbContext();
        var commandResult = await CreateCorrectionCommandHandler(commandContext).ExecuteAsync(
            new CorrectNegativeVisitCoverageCommand(
                new CommandEnvelope(
                    fixture.Owner,
                    new RequestCorrelationId("correlation-preview-two-to-one"),
                    EntryOrigin.Normal,
                    Now,
                    "preview-two-to-one",
                    "Reduce mistaken coverage",
                    "Preview/command parity"),
                fixture.NewMembershipClosureId,
                NegativeVisitCoverageCorrectionMode.Replace,
                ReplacementNewMembershipCoverageCount: 1,
                ExpectedOldestOpenNegativeVisitId: fixture.VisitIds[2]),
            CancellationToken.None);

        Assert.Equal(CommandStatus.Success, commandResult.Status);
        Assert.Equal(
            1,
            await database.ExecuteScalarAsync<int>(
                $"select negative_balance from bodylife.membership_state_cache where membership_id = '{fixture.SourceMembershipId}'"));
        Assert.Equal(
            2,
            await database.ExecuteScalarAsync<int>(
                $"select remaining_visits from bodylife.membership_state_cache where membership_id = '{fixture.CoveringMembershipId}'"));
        Assert.Equal(
            "replaced",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.membership_negative_closures where id = '{fixture.NewMembershipClosureId}'"));
        var replacementClosureId = await database.ExecuteScalarAsync<Guid>(
            $"select replacement_closure_id from bodylife.membership_negative_closure_corrections where original_closure_id = '{fixture.NewMembershipClosureId}'");
        Assert.Equal(
            1,
            await database.ExecuteScalarAsync<int>(
                $"select visits_count from bodylife.membership_negative_closures where id = '{replacementClosureId}'"));
        Assert.Equal(
            fixture.VisitIds[2],
            await database.ExecuteScalarAsync<Guid>(
                $"select visit_id from bodylife.membership_negative_closure_items where negative_closure_id = '{replacementClosureId}'"));

        await using var rereadContext = database.CreateDbContext();
        var reread = await CreateHandler(rereadContext).ExecuteAsync(
            new GetClientNegativeVisitCoverageQuery(fixture.Owner, fixture.ClientId),
            CancellationToken.None);
        Assert.Equal(GetClientNegativeVisitCoverageStatus.Success, reread.Status);
        Assert.Equal(1, reread.Coverage!.TotalNegativeBalance);
        Assert.Equal(fixture.VisitIds[3], Assert.Single(
            reread.Coverage.OpenConcreteVisits).VisitId);
    }

    [PostgreSqlFact]
    public async Task StaleCatalogAndOldestReturnCurrentSelectors()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        var addedVisits = await AddSourceVisitsAsync(database, fixture, 1);
        await using var dbContext = database.CreateDbContext();
        var handler = CreateClosePreviewHandler(dbContext);

        var staleOldest = await handler.ExecuteAsync(
            new PreviewCloseNegativeVisitsOneOffQuery(
                fixture.Owner,
                fixture.ClientId,
                Guid.NewGuid(),
                [new NegativeVisitClosureLineSelection(
                    fixture.OtherOneOffTypeId,
                    Now,
                    1)]),
            CancellationToken.None);
        Assert.Equal(PreviewCloseNegativeVisitsOneOffStatus.StaleState, staleOldest.Status);
        Assert.Equal(addedVisits[0], staleOldest.CurrentSelectors!.CurrentOldestOpenNegativeVisitId);

        var staleCatalog = await handler.ExecuteAsync(
            new PreviewCloseNegativeVisitsOneOffQuery(
                fixture.Owner,
                fixture.ClientId,
                addedVisits[0],
                [new NegativeVisitClosureLineSelection(
                    fixture.OtherOneOffTypeId,
                    Now.AddMinutes(-1),
                    1)]),
            CancellationToken.None);
        Assert.Equal(PreviewCloseNegativeVisitsOneOffStatus.StaleState, staleCatalog.Status);
        var currentType = Assert.Single(staleCatalog.CurrentSelectors!.ActiveOneOffTypes);
        Assert.Equal(Now, currentType.CurrentUpdatedAt);
    }

    [PostgreSqlFact]
    public async Task MissingCacheAndMalformedClosureFailClosed()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        await database.ExecuteScalarAsync<int>(
            $"delete from bodylife.membership_state_cache where membership_id = '{fixture.SourceMembershipId}'; select 1;");
        await using (var missingContext = database.CreateDbContext())
        {
            var missing = await CreateCorrectionPreviewHandler(missingContext).ExecuteAsync(
                new PreviewCorrectNegativeVisitCoverageQuery(
                    fixture.Owner,
                    fixture.OneOffClosureId,
                    NegativeVisitCoverageCorrectionMode.Cancel,
                    "Preview"),
                CancellationToken.None);
            Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.RecalculationFailed, missing.Status);
            Assert.Null(missing.Preview);
        }

        await using (var rebuildContext = database.CreateDbContext())
        {
            Assert.True((await new MembershipStateCacheRebuilder(
                rebuildContext,
                new FixedTimeProvider(Now)).RebuildAsync(fixture.SourceMembershipId)).Succeeded);
        }

        await database.ExecutePrivilegedConstraintCorruptionAsync<int>(
            $"""
            update bodylife.membership_negative_closure_items
            set status = 'canceled'
            where id = '{fixture.OneOffItemId}';
            select 1;
            """);
        await using var malformedContext = database.CreateDbContext();
        var malformed = await CreateCorrectionPreviewHandler(malformedContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.Owner,
                fixture.OneOffClosureId,
                NegativeVisitCoverageCorrectionMode.Cancel,
                "Preview"),
            CancellationToken.None);
        Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.CanonicalStateInvalid, malformed.Status);
        Assert.Null(malformed.Preview);
    }

    [PostgreSqlFact]
    public async Task OneOffPaymentLifecycleMismatchFailsClosed()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        await database.ExecuteScalarAsync<int>(
            """
            alter table bodylife.payments
                disable trigger ck_negative_closure_payments_consistent;
            select 1;
            """);
        try
        {
            await database.ExecuteScalarAsync<int>(
                $"""
                update bodylife.payments
                set recorded_at = recorded_at + interval '1 second'
                where id = '{fixture.OneOffPaymentId}';
                select 1;
                """);
        }
        finally
        {
            await database.ExecuteScalarAsync<int>(
                """
                alter table bodylife.payments
                    enable trigger ck_negative_closure_payments_consistent;
                select 1;
                """);
        }
        await using var dbContext = database.CreateDbContext();

        var result = await CreateCorrectionPreviewHandler(dbContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.Owner,
                fixture.OneOffClosureId,
                NegativeVisitCoverageCorrectionMode.Cancel,
                "Inspect corrupted payment"),
            CancellationToken.None);

        Assert.Equal(
            PreviewCorrectNegativeVisitCoverageStatus.CanonicalStateInvalid,
            result.Status);
        Assert.Null(result.Preview);
    }

    [PostgreSqlFact]
    public async Task NonContiguousOneOffLineSequenceFailsClosed()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        await database.ExecutePrivilegedConstraintCorruptionAsync<int>(
            $"""
            update bodylife.membership_negative_closure_lines
            set sequence = 2
            where id = '{fixture.OneOffLineId}';
            select 1;
            """);
        await using var dbContext = database.CreateDbContext();

        var result = await CreateCorrectionPreviewHandler(dbContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.Owner,
                fixture.OneOffClosureId,
                NegativeVisitCoverageCorrectionMode.Cancel,
                "Inspect corrupted line"),
            CancellationToken.None);

        Assert.Equal(
            PreviewCorrectNegativeVisitCoverageStatus.CanonicalStateInvalid,
            result.Status);
        Assert.Null(result.Preview);
    }

    [PostgreSqlFact]
    public async Task NonContiguousNewMembershipItemSequenceFailsClosed()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        await database.ExecutePrivilegedConstraintCorruptionAsync<int>(
            $"""
            update bodylife.membership_negative_closure_items
            set sequence = 3
            where id = '{fixture.NewMembershipItemIds[1]}';
            select 1;
            """);
        await using var dbContext = database.CreateDbContext();

        var result = await CreateCorrectionPreviewHandler(dbContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.Owner,
                fixture.NewMembershipClosureId,
                NegativeVisitCoverageCorrectionMode.Cancel,
                "Inspect corrupted allocation"),
            CancellationToken.None);

        Assert.Equal(
            PreviewCorrectNegativeVisitCoverageStatus.CanonicalStateInvalid,
            result.Status);
        Assert.Null(result.Preview);
    }

    [PostgreSqlFact]
    public async Task NewMembershipReplacementRejectsCountBeyondRestoredCapacity()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        await AddSourceVisitsAsync(database, fixture, 3);
        await using var dbContext = database.CreateDbContext();

        var result = await CreateCorrectionPreviewHandler(dbContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.Owner,
                fixture.NewMembershipClosureId,
                NegativeVisitCoverageCorrectionMode.Replace,
                "Too many",
                ReplacementNewMembershipCoverageCount: 4,
                ExpectedOldestOpenNegativeVisitId: fixture.VisitIds[2]),
            CancellationToken.None);

        Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.MembershipNotEligible, result.Status);
        Assert.Equal("membership_not_eligible", result.ErrorCode);
        Assert.Null(result.Preview);
    }

    [PostgreSqlFact]
    public async Task PreviewUsesOneRepeatableReadSnapshotAcrossSelectorAndCatalog()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        var addedVisits = await AddSourceVisitsAsync(database, fixture, 1);
        var interceptor = new PauseBeforePreviewCatalogInterceptor();
        await using var queryContext = CreateDbContext(database.ConnectionString, interceptor);
        var queryTask = CreateClosePreviewHandler(queryContext).ExecuteAsync(
            new PreviewCloseNegativeVisitsOneOffQuery(
                fixture.Owner,
                fixture.ClientId,
                addedVisits[0],
                [new NegativeVisitClosureLineSelection(
                    fixture.OtherOneOffTypeId,
                    Now,
                    1)]),
            CancellationToken.None);

        await interceptor.CatalogReadReached.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await database.ExecuteScalarAsync<int>(
                $"""
                update bodylife.membership_types
                set price_amount = 80, updated_at = '{Now.AddMinutes(1):O}'
                where id = '{fixture.OtherOneOffTypeId}';
                select 1;
                """);
        }
        finally
        {
            interceptor.ReleaseCatalogRead();
        }

        var snapshot = await queryTask;
        Assert.Equal(PreviewCloseNegativeVisitsOneOffStatus.Success, snapshot.Status);
        Assert.Equal(new Money(75m, "UAH"), snapshot.Preview!.ExactPaymentTotal);

        await using var freshContext = database.CreateDbContext();
        var fresh = await CreateClosePreviewHandler(freshContext).ExecuteAsync(
            new PreviewCloseNegativeVisitsOneOffQuery(
                fixture.Owner,
                fixture.ClientId,
                addedVisits[0],
                [new NegativeVisitClosureLineSelection(
                    fixture.OtherOneOffTypeId,
                    Now,
                    1)]),
            CancellationToken.None);
        Assert.Equal(PreviewCloseNegativeVisitsOneOffStatus.StaleState, fresh.Status);
        Assert.Equal(new Money(80m, "UAH"), Assert.Single(
            fresh.CurrentSelectors!.ActiveOneOffTypes).UnitPrice);
    }

    [PostgreSqlFact]
    public async Task CorrectionPreviewUsesOneRepeatableReadSnapshotAcrossSourceAndCatalog()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedClosureProjectionFixtureAsync(database);
        var interceptor = new PauseBeforePreviewCatalogInterceptor();
        await using var queryContext = CreateDbContext(database.ConnectionString, interceptor);
        var queryTask = CreateCorrectionPreviewHandler(queryContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.Owner,
                fixture.OneOffClosureId,
                NegativeVisitCoverageCorrectionMode.Replace,
                "Use current catalog",
                [new NegativeVisitClosureLineSelection(
                    fixture.OtherOneOffTypeId,
                    Now,
                    1)],
                ExpectedOldestOpenNegativeVisitId: fixture.VisitIds[1]),
            CancellationToken.None);

        await interceptor.CatalogReadReached.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await database.ExecuteScalarAsync<int>(
                $"""
                update bodylife.membership_types
                set price_amount = 80, updated_at = '{Now.AddMinutes(1):O}'
                where id = '{fixture.OtherOneOffTypeId}';
                select 1;
                """);
        }
        finally
        {
            interceptor.ReleaseCatalogRead();
        }

        var snapshot = await queryTask;
        Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.Success, snapshot.Status);
        Assert.Equal(new Money(75m, "UAH"), snapshot.Preview!.ReplacementPayment!.Amount);

        await using var freshContext = database.CreateDbContext();
        var fresh = await CreateCorrectionPreviewHandler(freshContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.Owner,
                fixture.OneOffClosureId,
                NegativeVisitCoverageCorrectionMode.Replace,
                "Use stale catalog",
                [new NegativeVisitClosureLineSelection(
                    fixture.OtherOneOffTypeId,
                    Now,
                    1)],
                ExpectedOldestOpenNegativeVisitId: fixture.VisitIds[1]),
            CancellationToken.None);
        Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.StaleState, fresh.Status);
        Assert.Equal(new Money(80m, "UAH"), Assert.Single(
            fresh.CurrentSelectors!.ActiveOneOffTypes).UnitPrice);
    }

    [PostgreSqlFact]
    public async Task InactiveActorAndReasonOrCancelShapeAreRejectedWithoutPreview()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var fixture = await SeedReadFixtureAsync(database);
        await using var dbContext = database.CreateDbContext();

        var denied = await CreateClosePreviewHandler(dbContext).ExecuteAsync(
            new PreviewCloseNegativeVisitsOneOffQuery(
                fixture.InactiveAdmin,
                Guid.Empty,
                Guid.Empty,
                null),
            CancellationToken.None);
        Assert.Equal(PreviewCloseNegativeVisitsOneOffStatus.PermissionDenied, denied.Status);

        var deniedCorrection = await CreateCorrectionPreviewHandler(dbContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.InactiveAdmin,
                Guid.Empty,
                (NegativeVisitCoverageCorrectionMode)int.MaxValue,
                null),
            CancellationToken.None);
        Assert.Equal(
            PreviewCorrectNegativeVisitCoverageStatus.PermissionDenied,
            deniedCorrection.Status);

        var reasonRequired = await CreateCorrectionPreviewHandler(dbContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.Owner,
                Guid.NewGuid(),
                NegativeVisitCoverageCorrectionMode.Cancel,
                "  "),
            CancellationToken.None);
        Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.ReasonRequired, reasonRequired.Status);

        var invalidCancel = await CreateCorrectionPreviewHandler(dbContext).ExecuteAsync(
            new PreviewCorrectNegativeVisitCoverageQuery(
                fixture.Owner,
                Guid.NewGuid(),
                NegativeVisitCoverageCorrectionMode.Cancel,
                "Cancel",
                ReplacementNewMembershipCoverageCount: 1),
            CancellationToken.None);
        Assert.Equal(PreviewCorrectNegativeVisitCoverageStatus.ValidationFailed, invalidCancel.Status);
    }

    [Fact]
    public void PersistenceRegistrationExposesBothScopedPreviewHandlers()
    {
        var services = new ServiceCollection();
        services.AddBodyLifePersistence(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] = "Host=localhost;Database=bodylife",
            }).Build());

        AssertScoped<
            PreviewCloseNegativeVisitsOneOffQuery,
            PreviewCloseNegativeVisitsOneOffResult,
            PreviewCloseNegativeVisitsOneOffQueryHandler>(services);
        AssertScoped<
            PreviewCorrectNegativeVisitCoverageQuery,
            PreviewCorrectNegativeVisitCoverageResult,
            PreviewCorrectNegativeVisitCoverageQueryHandler>(services);
    }

    private static PreviewCloseNegativeVisitsOneOffQueryHandler CreateClosePreviewHandler(
        BodyLifeDbContext dbContext) => new(
        dbContext,
        new MembershipNegativeVisitSelector(dbContext),
        new FixedTimeProvider(Now));

    private static PreviewCorrectNegativeVisitCoverageQueryHandler CreateCorrectionPreviewHandler(
        BodyLifeDbContext dbContext) => new(
        dbContext,
        new MembershipNegativeVisitSelector(dbContext),
        new FixedTimeProvider(Now));

    private static CorrectNegativeVisitCoverageCommandHandler CreateCorrectionCommandHandler(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(Now);
        var auditAppender = new BusinessAuditAppender(dbContext);
        return new CorrectNegativeVisitCoverageCommandHandler(
            dbContext,
            auditAppender,
            new NegativeClosurePaymentWriter(dbContext, auditAppender),
            new MembershipNegativeVisitSelector(dbContext),
            new MembershipStateCacheRebuilder(dbContext, timeProvider),
            new OpenPaymentDayStatusProvider(),
            timeProvider);
    }

    private static async Task<Guid[]> AddSourceVisitsAsync(
        PostgreSqlTestDatabase database,
        ClosureProjectionFixture fixture,
        int count)
    {
        var visitIds = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        for (var index = 0; index < visitIds.Length; index++)
        {
            await using var command = new NpgsqlCommand(
                """
                insert into bodylife.visits (
                    id, client_id, occurred_at, recorded_at, recorded_by_account_id,
                    session_id, visit_kind, entry_origin, status)
                values (
                    @visit, @client, @occurred, @occurred, @account, @session,
                    'membership', 'normal', 'active');
                insert into bodylife.visit_consumptions (
                    id, visit_id, client_id, visit_kind, membership_id,
                    consumption_type, source_fact_type, source_fact_id, recorded_at,
                    recorded_by_account_id, recorded_session_id, status)
                values (
                    @consumption, @visit, @client, 'membership', @membership,
                    'counted', 'visit', @visit, @occurred, @account, @session, 'active');
                """,
                connection);
            command.Parameters.AddWithValue("visit", visitIds[index]);
            command.Parameters.AddWithValue("consumption", Guid.NewGuid());
            command.Parameters.AddWithValue("client", fixture.ClientId);
            command.Parameters.AddWithValue("membership", fixture.SourceMembershipId);
            command.Parameters.AddWithValue("account", fixture.Owner.AccountId.Value);
            command.Parameters.AddWithValue("session", fixture.Owner.SessionId.Value);
            command.Parameters.AddWithValue("occurred", Now.AddHours(-12 + index));
            await command.ExecuteNonQueryAsync();
        }

        await using var dbContext = database.CreateDbContext();
        Assert.True((await new MembershipStateCacheRebuilder(
            dbContext,
            new FixedTimeProvider(Now)).RebuildAsync(fixture.SourceMembershipId)).Succeeded);
        return visitIds;
    }

    private static void AssertScoped<TQuery, TResult, THandler>(
        IServiceCollection services)
        where TQuery : IBodyLifeQuery<TResult>
    {
        var descriptor = Assert.Single(services, service => service.ServiceType
            == typeof(IBodyLifeQueryHandler<TQuery, TResult>));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(THandler), descriptor.ImplementationType);
    }

    private sealed class PauseBeforePreviewCatalogInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> reached = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> released = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int paused;

        public Task CatalogReadReached => reached.Task;

        public void ReleaseCatalogRead() => released.TrySetResult(true);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("membership_types", StringComparison.Ordinal)
                && Interlocked.Exchange(ref paused, 1) == 0)
            {
                reached.TrySetResult(true);
                await released.Task.WaitAsync(cancellationToken);
            }

            return result;
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
}
