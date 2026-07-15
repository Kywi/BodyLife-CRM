using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlVisitsStorageTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        14,
        14,
        30,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset VisitOccurredAt = TestNow.AddHours(-2);
    private static readonly DateTimeOffset CancellationOccurredAt = TestNow.AddMinutes(30);
    private static readonly DateTimeOffset CancellationRecordedAt = TestNow.AddHours(1);

    [PostgreSqlFact]
    public async Task MigrationCreatesVisitSourceTablesConstraintsAndIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();

        await dbContext.Database.MigrateAsync();

        Assert.Equal(
            [
                "id",
                "client_id",
                "occurred_at",
                "recorded_at",
                "recorded_by_account_id",
                "session_id",
                "visit_kind",
                "entry_origin",
                "entry_batch_id",
                "comment",
                "status",
            ],
            await ReadColumnNamesAsync(database, "visits"));
        Assert.Equal(
            [
                "id",
                "visit_id",
                "client_id",
                "visit_kind",
                "membership_id",
                "consumption_type",
                "source_fact_type",
                "source_fact_id",
                "recorded_at",
                "recorded_by_account_id",
                "recorded_session_id",
                "status",
            ],
            await ReadColumnNamesAsync(database, "visit_consumptions"));
        Assert.Equal(
            [
                "id",
                "visit_id",
                "reason",
                "occurred_at",
                "recorded_at",
                "recorded_by_account_id",
                "session_id",
                "entry_origin",
                "entry_batch_id",
            ],
            await ReadColumnNamesAsync(database, "visit_cancellations"));

        var expectedConstraints = new[]
        {
            "AK_issued_memberships_id_client_id",
            "AK_visits_id_client_id_visit_kind",
            "FK_visit_consumptions_issued_memberships_membership_client",
            "FK_visit_consumptions_visits_visit_client_kind",
            "ck_visit_consumptions_consumption_type",
            "ck_visit_consumptions_source_fact_identity",
            "ck_visit_consumptions_source_fact_type",
            "ck_visit_consumptions_status",
            "ck_visit_consumptions_visit_kind",
            "ck_visit_cancellations_entry_origin",
            "ck_visit_cancellations_reason_not_empty",
            "ck_visits_comment_not_empty",
            "ck_visits_entry_origin",
            "ck_visits_status",
            "ck_visits_visit_kind",
        };
        foreach (var constraint in expectedConstraints)
        {
            Assert.True(
                await ConstraintExistsAsync(database, constraint),
                $"Expected constraint '{constraint}' was not found.");
        }

        var activeConsumptionIndex = await ReadIndexDefinitionAsync(
            database,
            "ux_visit_consumptions_active_counted_visit");
        Assert.Contains("UNIQUE INDEX", activeConsumptionIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(visit_id)", activeConsumptionIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", activeConsumptionIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active", activeConsumptionIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("counted", activeConsumptionIndex, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "(membership_id, status, recorded_at, visit_id)",
            await ReadIndexDefinitionAsync(
                database,
                "ix_visit_consumptions_membership_recalculation"),
            StringComparison.OrdinalIgnoreCase);

        var dailyIndex = await ReadIndexDefinitionAsync(
            database,
            "ix_visits_active_daily_report");
        Assert.Contains("(occurred_at, client_id)", dailyIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", dailyIndex, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active", dailyIndex, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "(occurred_at, status, client_id)",
            await ReadIndexDefinitionAsync(database, "ix_visits_daily_source"),
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "UNIQUE INDEX",
            await ReadIndexDefinitionAsync(database, "ux_visit_cancellations_visit_id"),
            StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    public async Task MembershipVisitPreservesAccountabilityAndCancellationHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var entryBatchId = Guid.NewGuid();
        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "membership",
            entryOrigin: "paper_fallback",
            entryBatchId: entryBatchId,
            comment: "Recovered from the reception paper sheet");
        var consumptionId = await InsertConsumptionAsync(
            database.ConnectionString,
            fixture,
            visitId,
            fixture.ClientId,
            fixture.MembershipId);
        var cancellationId = await InsertCancellationAsync(
            database.ConnectionString,
            fixture,
            visitId,
            entryOrigin: "manual_backfill",
            entryBatchId: entryBatchId);

        await SetVisitAndConsumptionCanceledAsync(
            database.ConnectionString,
            visitId,
            consumptionId);

        var visit = await ReadVisitAsync(database.ConnectionString, visitId);
        Assert.Equal(fixture.ClientId, visit.ClientId);
        Assert.Equal(VisitOccurredAt, visit.OccurredAt);
        Assert.Equal(TestNow, visit.RecordedAt);
        Assert.Equal(fixture.ActorAccountId, visit.RecordedByAccountId);
        Assert.Equal(fixture.SessionId, visit.SessionId);
        Assert.Equal("membership", visit.VisitKind);
        Assert.Equal("paper_fallback", visit.EntryOrigin);
        Assert.Equal(entryBatchId, visit.EntryBatchId);
        Assert.Equal("Recovered from the reception paper sheet", visit.Comment);
        Assert.Equal("canceled", visit.Status);

        var consumption = await ReadConsumptionAsync(
            database.ConnectionString,
            consumptionId);
        Assert.Equal(visitId, consumption.VisitId);
        Assert.Equal(fixture.ClientId, consumption.ClientId);
        Assert.Equal(fixture.MembershipId, consumption.MembershipId);
        Assert.Equal("counted", consumption.ConsumptionType);
        Assert.Equal("visit", consumption.SourceFactType);
        Assert.Equal(visitId, consumption.SourceFactId);
        Assert.Equal(fixture.ActorAccountId, consumption.RecordedByAccountId);
        Assert.Equal(fixture.SessionId, consumption.RecordedSessionId);
        Assert.Equal("canceled", consumption.Status);

        var cancellation = await ReadCancellationAsync(
            database.ConnectionString,
            cancellationId);
        Assert.Equal(visitId, cancellation.VisitId);
        Assert.Equal("Mistaken reception entry", cancellation.Reason);
        Assert.Equal(fixture.ActorAccountId, cancellation.RecordedByAccountId);
        Assert.Equal(fixture.SessionId, cancellation.SessionId);
        Assert.Equal("manual_backfill", cancellation.EntryOrigin);
        Assert.Equal(entryBatchId, cancellation.EntryBatchId);
    }

    [PostgreSqlFact]
    public async Task OneOffAndTrialVisitsCannotHaveMembershipConsumption()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var oneOffVisitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "one_off");
        var trialVisitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "trial",
            recordedAt: TestNow.AddMinutes(1));

        await AssertPostgresViolationAsync(
            () => InsertConsumptionAsync(
                database.ConnectionString,
                fixture,
                oneOffVisitId,
                fixture.ClientId,
                fixture.MembershipId),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_visit_consumptions_visits_visit_client_kind");
        await AssertPostgresViolationAsync(
            () => InsertConsumptionAsync(
                database.ConnectionString,
                fixture,
                trialVisitId,
                fixture.ClientId,
                fixture.MembershipId),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_visit_consumptions_visits_visit_client_kind");

        Assert.Equal(2L, await CountRowsAsync(database, "visits"));
        Assert.Equal(0L, await CountRowsAsync(database, "visit_consumptions"));
    }

    [PostgreSqlFact]
    public async Task CompositeForeignKeysRejectCrossClientConsumption()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "membership");

        await AssertPostgresViolationAsync(
            () => InsertConsumptionAsync(
                database.ConnectionString,
                fixture,
                visitId,
                fixture.ClientId,
                fixture.OtherMembershipId),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_visit_consumptions_issued_memberships_membership_client");
        await AssertPostgresViolationAsync(
            () => InsertConsumptionAsync(
                database.ConnectionString,
                fixture,
                visitId,
                fixture.OtherClientId,
                fixture.OtherMembershipId),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_visit_consumptions_visits_visit_client_kind");

        Assert.Equal(0L, await CountRowsAsync(database, "visit_consumptions"));
    }

    [PostgreSqlFact]
    public async Task OneActiveCountedConsumptionCoexistsWithCanceledHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "membership");
        await InsertConsumptionAsync(
            database.ConnectionString,
            fixture,
            visitId,
            fixture.ClientId,
            fixture.MembershipId,
            status: "canceled");
        await InsertConsumptionAsync(
            database.ConnectionString,
            fixture,
            visitId,
            fixture.ClientId,
            fixture.MembershipId,
            recordedAt: TestNow.AddMinutes(1));

        await AssertPostgresViolationAsync(
            () => InsertConsumptionAsync(
                database.ConnectionString,
                fixture,
                visitId,
                fixture.ClientId,
                fixture.MembershipId,
                recordedAt: TestNow.AddMinutes(2)),
            PostgresErrorCodes.UniqueViolation,
            "ux_visit_consumptions_active_counted_visit");

        Assert.Equal(2L, await CountRowsAsync(database, "visit_consumptions"));
        Assert.Equal(
            1L,
            await CountRowsAsync(
                database,
                "visit_consumptions",
                "status = 'active'"));
    }

    [PostgreSqlFact]
    public async Task ChecksRejectUnsupportedVisitConsumptionAndCancellationShapes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);

        await AssertPostgresViolationAsync(
            () => InsertVisitAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                "drop_in"),
            PostgresErrorCodes.CheckViolation,
            "ck_visits_visit_kind");
        await AssertPostgresViolationAsync(
            () => InsertVisitAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                "membership",
                entryOrigin: "spreadsheet"),
            PostgresErrorCodes.CheckViolation,
            "ck_visits_entry_origin");
        await AssertPostgresViolationAsync(
            () => InsertVisitAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                "membership",
                comment: "   "),
            PostgresErrorCodes.CheckViolation,
            "ck_visits_comment_not_empty");

        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "membership");
        await AssertPostgresViolationAsync(
            () => InsertConsumptionAsync(
                database.ConnectionString,
                fixture,
                visitId,
                fixture.ClientId,
                fixture.MembershipId,
                consumptionType: "complimentary"),
            PostgresErrorCodes.CheckViolation,
            "ck_visit_consumptions_consumption_type");
        await AssertPostgresViolationAsync(
            () => InsertConsumptionAsync(
                database.ConnectionString,
                fixture,
                visitId,
                fixture.ClientId,
                fixture.MembershipId,
                sourceFactType: "manual"),
            PostgresErrorCodes.CheckViolation,
            "ck_visit_consumptions_source_fact_type");
        await AssertPostgresViolationAsync(
            () => InsertConsumptionAsync(
                database.ConnectionString,
                fixture,
                visitId,
                fixture.ClientId,
                fixture.MembershipId,
                sourceFactId: Guid.NewGuid()),
            PostgresErrorCodes.CheckViolation,
            "ck_visit_consumptions_source_fact_identity");
        await AssertPostgresViolationAsync(
            () => InsertCancellationAsync(
                database.ConnectionString,
                fixture,
                visitId,
                reason: "   "),
            PostgresErrorCodes.CheckViolation,
            "ck_visit_cancellations_reason_not_empty");
        await AssertPostgresViolationAsync(
            () => InsertCancellationAsync(
                database.ConnectionString,
                fixture,
                visitId,
                entryOrigin: "spreadsheet"),
            PostgresErrorCodes.CheckViolation,
            "ck_visit_cancellations_entry_origin");
    }

    [PostgreSqlFact]
    public async Task CancellationIsUniqueAndSourceFactsUseRestrictiveDeletes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "membership");
        await InsertConsumptionAsync(
            database.ConnectionString,
            fixture,
            visitId,
            fixture.ClientId,
            fixture.MembershipId);
        await InsertCancellationAsync(
            database.ConnectionString,
            fixture,
            visitId);

        await AssertPostgresViolationAsync(
            () => InsertCancellationAsync(
                database.ConnectionString,
                fixture,
                visitId,
                recordedAt: TestNow.AddMinutes(2)),
            PostgresErrorCodes.UniqueViolation,
            "ux_visit_cancellations_visit_id");
        await AssertPostgresViolationAsync(
            () => DeleteByIdAsync(database.ConnectionString, "visits", visitId),
            PostgresErrorCodes.ForeignKeyViolation);
        await AssertPostgresViolationAsync(
            () => DeleteByIdAsync(
                database.ConnectionString,
                "issued_memberships",
                fixture.MembershipId),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_visit_consumptions_issued_memberships_membership_client");
    }

    [PostgreSqlFact]
    public async Task CancelVisitSourcePreparationRequiresCallerTransaction()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var preparer = new CancelVisitSourcePreparer(dbContext);

        var missingVisitId = await Assert.ThrowsAsync<ArgumentException>(() =>
            preparer.PrepareAsync(Guid.Empty));
        var missingTransaction = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preparer.PrepareAsync(Guid.NewGuid()));

        Assert.Equal("visitId", missingVisitId.ParamName);
        Assert.Contains(
            "caller-owned",
            missingTransaction.Message,
            StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task CancelVisitSourcePreparationProjectsOwnershipAndLocksActiveSources()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var entryBatchId = Guid.NewGuid();
        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "membership",
            entryOrigin: "paper_fallback",
            entryBatchId: entryBatchId,
            comment: "Paper batch visit");
        var consumptionId = await InsertConsumptionAsync(
            database.ConnectionString,
            fixture,
            visitId,
            fixture.ClientId,
            fixture.MembershipId);
        var preparer = new CancelVisitSourcePreparer(dbContext);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var result = await preparer.PrepareAsync(visitId);

        Assert.True(result.IsPrepared);
        Assert.Equal(CancelVisitSourcePreparationStatus.Prepared, result.Status);
        Assert.Equal(visitId, result.VisitId);
        var source = Assert.IsType<VisitCancellationSource>(result.Source);
        Assert.Equal(visitId, source.VisitId);
        Assert.Equal(fixture.ClientId, source.ClientId);
        Assert.Equal(VisitOccurredAt, source.OccurredAt);
        Assert.Equal(TestNow, source.RecordedAt);
        Assert.Equal(fixture.ActorAccountId, source.RecordedByAccountId);
        Assert.Equal(fixture.SessionId, source.SessionId);
        Assert.Equal(VisitKind.Membership, source.VisitKind);
        Assert.Equal(EntryOrigin.PaperFallback, source.EntryOrigin);
        Assert.Equal(entryBatchId, source.EntryBatchId);
        Assert.Equal("Paper batch visit", source.Comment);
        Assert.Equal(VisitCancellationSourceStatus.Active, source.Status);
        Assert.Equal(consumptionId, source.ActiveConsumptionId);
        Assert.Equal(fixture.MembershipId, source.MembershipId);
        Assert.Null(source.ExistingCancellationId);

        var visitLockFailure = await AssertSourceUpdateBlockedAsync(
            database.ConnectionString,
            "visits",
            visitId);
        var consumptionLockFailure = await AssertSourceUpdateBlockedAsync(
            database.ConnectionString,
            "visit_consumptions",
            consumptionId);

        Assert.Equal(PostgresErrorCodes.LockNotAvailable, visitLockFailure.SqlState);
        Assert.Equal(
            PostgresErrorCodes.LockNotAvailable,
            consumptionLockFailure.SqlState);
        await transaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task CancelVisitSourcePreparationSupportsNonMembershipVisits()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var oneOffVisitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "one_off");
        var trialVisitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "trial",
            recordedAt: TestNow.AddMinutes(1));
        var preparer = new CancelVisitSourcePreparer(dbContext);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var oneOff = await preparer.PrepareAsync(oneOffVisitId);
        var trial = await preparer.PrepareAsync(trialVisitId);

        Assert.True(oneOff.IsPrepared);
        Assert.Equal(VisitKind.OneOff, oneOff.Source!.VisitKind);
        Assert.Null(oneOff.Source.ActiveConsumptionId);
        Assert.Null(oneOff.Source.MembershipId);
        Assert.True(trial.IsPrepared);
        Assert.Equal(VisitKind.Trial, trial.Source!.VisitKind);
        Assert.Null(trial.Source.ActiveConsumptionId);
        Assert.Null(trial.Source.MembershipId);
        await transaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task CancelVisitSourcePreparationDistinguishesMissingAndCanceledVisits()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var canceledVisitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "membership");
        var consumptionId = await InsertConsumptionAsync(
            database.ConnectionString,
            fixture,
            canceledVisitId,
            fixture.ClientId,
            fixture.MembershipId);
        var cancellationId = await InsertCancellationAsync(
            database.ConnectionString,
            fixture,
            canceledVisitId);
        await SetVisitAndConsumptionCanceledAsync(
            database.ConnectionString,
            canceledVisitId,
            consumptionId);
        var activeVisitWithCancellationId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "one_off",
            recordedAt: TestNow.AddMinutes(2));
        var activeCancellationId = await InsertCancellationAsync(
            database.ConnectionString,
            fixture,
            activeVisitWithCancellationId,
            recordedAt: TestNow.AddMinutes(3));
        var missingVisitId = Guid.NewGuid();
        var preparer = new CancelVisitSourcePreparer(dbContext);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var missing = await preparer.PrepareAsync(missingVisitId);
        var canceled = await preparer.PrepareAsync(canceledVisitId);
        var cancellationWins = await preparer.PrepareAsync(
            activeVisitWithCancellationId);

        Assert.Equal(CancelVisitSourcePreparationStatus.NotFound, missing.Status);
        Assert.Equal(missingVisitId, missing.VisitId);
        Assert.Null(missing.Source);
        Assert.Equal(
            CancelVisitSourcePreparationStatus.AlreadyCanceled,
            canceled.Status);
        Assert.Equal(
            VisitCancellationSourceStatus.Canceled,
            canceled.Source!.Status);
        Assert.Equal(cancellationId, canceled.Source.ExistingCancellationId);
        Assert.Equal(
            CancelVisitSourcePreparationStatus.AlreadyCanceled,
            cancellationWins.Status);
        Assert.Equal(
            VisitCancellationSourceStatus.Active,
            cancellationWins.Source!.Status);
        Assert.Equal(
            activeCancellationId,
            cancellationWins.Source.ExistingCancellationId);

        var cancellationLockFailure = await AssertSourceUpdateBlockedAsync(
            database.ConnectionString,
            "visit_cancellations",
            cancellationId);
        Assert.Equal(
            PostgresErrorCodes.LockNotAvailable,
            cancellationLockFailure.SqlState);
        await transaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task CancelVisitSourcePreparationRejectsIncompleteMembershipSource()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "membership");
        var preparer = new CancelVisitSourcePreparer(dbContext);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var result = await preparer.PrepareAsync(visitId);

        Assert.False(result.IsPrepared);
        Assert.Equal(
            CancelVisitSourcePreparationStatus.InconsistentSource,
            result.Status);
        Assert.NotNull(result.Source);
        Assert.Null(result.Source.ActiveConsumptionId);
        Assert.Null(result.Source.MembershipId);
        await transaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task CancelVisitPersistsCancellationRecalculationAuditAndRereadAtomically()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext, visitsLimit: 1);
        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "membership");
        var consumptionId = await InsertConsumptionAsync(
            database.ConnectionString,
            fixture,
            visitId,
            fixture.ClientId,
            fixture.MembershipId);

        var result = await CreateCancelVisitHandler(dbContext).ExecuteAsync(
            CreateCancelVisitCommand(fixture, visitId, "cancel-membership"),
            CancellationToken.None);

        AssertSuccessfulCancellation(result, fixture.ClientId, visitId);
        Assert.False(result.ChangedAfterClose);
        var cancellationId = result.PrimaryEntityId!.Value.Value;
        Assert.Equal("canceled", (await ReadVisitAsync(
            database.ConnectionString,
            visitId)).Status);
        Assert.Equal("canceled", (await ReadConsumptionAsync(
            database.ConnectionString,
            consumptionId)).Status);

        var cancellation = await ReadCancellationAsync(
            database.ConnectionString,
            cancellationId);
        Assert.Equal(visitId, cancellation.VisitId);
        Assert.Equal("Mistaken reception entry", cancellation.Reason);
        Assert.Equal(CancellationOccurredAt, cancellation.OccurredAt);
        Assert.Equal(CancellationRecordedAt, cancellation.RecordedAt);
        Assert.Equal(fixture.Actor.AccountId.Value, cancellation.RecordedByAccountId);
        Assert.Equal(fixture.Actor.SessionId.Value, cancellation.SessionId);
        Assert.Equal("normal", cancellation.EntryOrigin);
        Assert.Null(cancellation.EntryBatchId);

        var cache = await ReadCacheAsync(database, fixture.MembershipId);
        Assert.Equal(0, cache.CountedVisits);
        Assert.Equal(1, cache.RemainingVisits);
        Assert.Equal(0, cache.NegativeBalance);
        Assert.Null(cache.FirstNegativeVisitId);
        Assert.Null(cache.FirstNegativeVisitDate);
        Assert.Null(cache.LastCountedVisitAt);
        Assert.Equal(CancellationRecordedAt, cache.RecalculatedAt);

        var audit = await ReadCancellationAuditAsync(
            database,
            result.AuditEntryId!.Value.Value);
        Assert.Equal(VisitAuditActions.Canceled, audit.ActionType);
        Assert.Equal(VisitAuditActions.VisitEntityType, audit.EntityType);
        Assert.Equal(visitId, audit.EntityId);
        Assert.Equal(CancellationOccurredAt, audit.OccurredAt);
        Assert.Equal(CancellationRecordedAt, audit.RecordedAt);
        Assert.Equal("Mistaken reception entry", audit.Reason);
        Assert.Equal("Cancellation requested at reception", audit.Comment);
        Assert.Equal("cancel-membership", audit.IdempotencyKey);
        Assert.False(audit.ChangedAfterClose);
        using (var before = JsonDocument.Parse(audit.BeforeSummary))
        {
            Assert.Equal(
                0,
                before.RootElement
                    .GetProperty("membershipState")
                    .GetProperty("remainingVisits")
                    .GetInt32());
        }

        using (var after = JsonDocument.Parse(audit.AfterSummary))
        {
            Assert.Equal(
                cancellationId,
                after.RootElement
                    .GetProperty("cancellation")
                    .GetProperty("cancellationId")
                    .GetGuid());
            Assert.Equal(
                1,
                after.RootElement
                    .GetProperty("membershipState")
                    .GetProperty("remainingVisits")
                    .GetInt32());
        }

        Assert.Equal(1L, await CountRowsAsync(database, "visit_cancellations"));
        Assert.Equal(1L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task CancelVisitMovesFirstNegativeMetadataToNextCountedVisit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext, visitsLimit: 0);
        var firstVisitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "membership");
        await InsertConsumptionAsync(
            database.ConnectionString,
            fixture,
            firstVisitId,
            fixture.ClientId,
            fixture.MembershipId);
        var secondVisitOccurredAt = VisitOccurredAt.AddMinutes(10);
        var secondVisitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "membership",
            recordedAt: TestNow.AddMinutes(1),
            occurredAt: secondVisitOccurredAt);
        await InsertConsumptionAsync(
            database.ConnectionString,
            fixture,
            secondVisitId,
            fixture.ClientId,
            fixture.MembershipId,
            recordedAt: TestNow.AddMinutes(1));

        var result = await CreateCancelVisitHandler(dbContext).ExecuteAsync(
            CreateCancelVisitCommand(fixture, firstVisitId, "cancel-first-negative"),
            CancellationToken.None);

        AssertSuccessfulCancellation(result, fixture.ClientId, firstVisitId);
        var cache = await ReadCacheAsync(database, fixture.MembershipId);
        Assert.Equal(1, cache.CountedVisits);
        Assert.Equal(-1, cache.RemainingVisits);
        Assert.Equal(1, cache.NegativeBalance);
        Assert.Equal(secondVisitId, cache.FirstNegativeVisitId);
        Assert.Equal(DateOnly.FromDateTime(secondVisitOccurredAt.DateTime), cache.FirstNegativeVisitDate);
        Assert.Equal(secondVisitOccurredAt, cache.LastCountedVisitAt);
    }

    [PostgreSqlFact]
    public async Task CancelVisitReplayReturnsOriginalAndDifferentRequestIsRejected()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "one_off");
        var handler = CreateCancelVisitHandler(dbContext);
        var command = CreateCancelVisitCommand(fixture, visitId, "cancel-replay");

        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var changedPayload = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with
                {
                    Comment = "Changed cancellation context",
                },
            },
            CancellationToken.None);
        var differentKey = await handler.ExecuteAsync(
            command with
            {
                Envelope = command.Envelope with
                {
                    IdempotencyKey = "cancel-already-canceled",
                    RequestCorrelationId = new RequestCorrelationId(
                        "correlation-cancel-already-canceled"),
                },
            },
            CancellationToken.None);

        AssertSuccessfulCancellation(first, fixture.ClientId, visitId);
        AssertSuccessfulCancellation(replay, fixture.ClientId, visitId);
        Assert.Equal(first.PrimaryEntityId, replay.PrimaryEntityId);
        Assert.Equal(first.AuditEntryId, replay.AuditEntryId);
        AssertCommandError(
            changedPayload,
            CommandErrorCode.DuplicateSubmission,
            "idempotencyKey");
        AssertCommandError(
            differentKey,
            CommandErrorCode.AlreadyCanceled,
            "visitId");
        Assert.Equal(0L, await CountRowsAsync(database, "visit_consumptions"));
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(1L, await CountRowsAsync(database, "visit_cancellations"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentCancelVisitWithSameKeySerializesToOneWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        VisitStorageFixture fixture;
        Guid visitId;
        await using (var setupContext = database.CreateDbContext())
        {
            await setupContext.Database.MigrateAsync();
            fixture = await SeedFixtureAsync(database, setupContext);
            visitId = await InsertVisitAsync(
                database.ConnectionString,
                fixture,
                fixture.ClientId,
                "one_off");
        }

        var command = CreateCancelVisitCommand(
            fixture,
            visitId,
            "concurrent-cancel");
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateCancelVisitHandler(firstContext).ExecuteAsync(
                command,
                CancellationToken.None),
            CreateCancelVisitHandler(secondContext).ExecuteAsync(
                command,
                CancellationToken.None));

        Assert.All(
            results,
            result => AssertSuccessfulCancellation(
                result,
                fixture.ClientId,
                visitId));
        Assert.Equal(results[0].PrimaryEntityId, results[1].PrimaryEntityId);
        Assert.Equal(results[0].AuditEntryId, results[1].AuditEntryId);
        Assert.Equal(1L, await CountRowsAsync(database, "visit_cancellations"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task CancelVisitValidationMissingSourceAndInactiveActorDoNotMutate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "one_off");
        var handler = CreateCancelVisitHandler(dbContext);
        var valid = CreateCancelVisitCommand(fixture, visitId, "cancel-valid");

        var missingReason = await handler.ExecuteAsync(
            valid with
            {
                Envelope = valid.Envelope with
                {
                    IdempotencyKey = "cancel-missing-reason",
                    Reason = null,
                },
            },
            CancellationToken.None);
        var missingVisit = await handler.ExecuteAsync(
            CreateCancelVisitCommand(
                fixture,
                Guid.NewGuid(),
                "cancel-missing-visit"),
            CancellationToken.None);
        await DeactivateActorAsync(database, fixture.Actor.AccountId.Value);
        var inactiveActor = await handler.ExecuteAsync(
            valid,
            CancellationToken.None);

        AssertCommandError(
            missingReason,
            CommandErrorCode.ReasonRequired,
            "reason");
        AssertCommandError(missingVisit, CommandErrorCode.NotFound, "visitId");
        AssertCommandError(inactiveActor, CommandErrorCode.PermissionDenied);
        await AssertNoCancellationMutationAsync(database, visitId);
    }

    [PostgreSqlFact]
    public async Task ReconciledVisitDayRequiresOwnerAndMarksOwnerCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "one_off");
        var adminActor = fixture.Actor with
        {
            Role = ActorRole.Admin,
            AccountKind = AccountKind.NamedAdmin,
        };
        await UpdateActorIdentityAsync(
            database,
            fixture.Actor.AccountId.Value,
            "named_admin",
            "admin");
        var reconciledDay = new StaticVisitDayReconciliationStatusProvider(
            VisitDayReconciliationStatus.Reconciled);

        var denied = await CreateCancelVisitHandler(
            dbContext,
            reconciledDay).ExecuteAsync(
                CreateCancelVisitCommand(
                    fixture,
                    visitId,
                    "admin-reconciled-cancel",
                    adminActor),
                CancellationToken.None);

        AssertCommandError(
            denied,
            CommandErrorCode.DayClosedRequiresOwner,
            "visitId");
        await AssertNoCancellationMutationAsync(database, visitId);

        await UpdateActorIdentityAsync(
            database,
            fixture.Actor.AccountId.Value,
            "owner",
            "owner");
        var allowed = await CreateCancelVisitHandler(
            dbContext,
            reconciledDay).ExecuteAsync(
                CreateCancelVisitCommand(
                    fixture,
                    visitId,
                    "owner-reconciled-cancel"),
                CancellationToken.None);

        AssertSuccessfulCancellation(allowed, fixture.ClientId, visitId);
        Assert.True(allowed.ChangedAfterClose);
        var audit = await ReadCancellationAuditAsync(
            database,
            allowed.AuditEntryId!.Value.Value);
        Assert.True(audit.ChangedAfterClose);
    }

    [PostgreSqlFact]
    public async Task CompetingVisitLockReturnsConcurrencyConflictWithoutCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var migrationContext = database.CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var dbContext = database.CreateDbContext();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "one_off");
        await using var lockConnection = new NpgsqlConnection(database.ConnectionString);
        await lockConnection.OpenAsync();
        await using var lockTransaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.Transaction = lockTransaction;
            lockCommand.CommandText =
                "select id from bodylife.visits where id = @id for update";
            lockCommand.Parameters.AddWithValue("id", visitId);
            Assert.Equal(visitId, await lockCommand.ExecuteScalarAsync());
        }

        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("set lock_timeout = '250ms'");

        var result = await CreateCancelVisitHandler(dbContext).ExecuteAsync(
            CreateCancelVisitCommand(fixture, visitId, "cancel-lock-conflict"),
            CancellationToken.None);

        await lockTransaction.RollbackAsync();
        AssertCommandError(result, CommandErrorCode.ConcurrencyConflict);
        await AssertNoCancellationMutationAsync(database, visitId);
    }

    [PostgreSqlFact]
    public async Task RecalculationAndAuditFailuresRollBackEntireCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var visitId = await InsertVisitAsync(
            database.ConnectionString,
            fixture,
            fixture.ClientId,
            "membership");
        var consumptionId = await InsertConsumptionAsync(
            database.ConnectionString,
            fixture,
            visitId,
            fixture.ClientId,
            fixture.MembershipId);
        var realRecalculator = CreateMembershipStateRecalculator(dbContext);
        var failingRecalculator = new FailOnSecondMembershipRecalculation(
            realRecalculator);

        var recalculationFailure = await CreateCancelVisitHandler(
            dbContext,
            membershipStateRecalculator: failingRecalculator).ExecuteAsync(
                CreateCancelVisitCommand(
                    fixture,
                    visitId,
                    "cancel-recalculation-failure"),
                CancellationToken.None);

        AssertCommandError(
            recalculationFailure,
            CommandErrorCode.RecalculationFailed);
        await AssertNoCancellationMutationAsync(database, visitId, consumptionId);

        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_visit_canceled_audit
            check (action_type <> 'visit.canceled')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateCancelVisitHandler(dbContext).ExecuteAsync(
                CreateCancelVisitCommand(
                    fixture,
                    visitId,
                    "cancel-audit-failure"),
                CancellationToken.None));

        await AssertNoCancellationMutationAsync(database, visitId, consumptionId);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    private static CancelVisitCommandHandler CreateCancelVisitHandler(
        BodyLifeDbContext dbContext,
        IVisitDayReconciliationStatusProvider? dayReconciliationStatusProvider = null,
        IMembershipStateRecalculator? membershipStateRecalculator = null)
    {
        var timeProvider = new FixedTimeProvider(CancellationRecordedAt);
        var recalculator = membershipStateRecalculator
            ?? CreateMembershipStateRecalculator(dbContext);

        return new CancelVisitCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new CancelVisitSourcePreparer(dbContext),
            recalculator,
            new GetMembershipStateQueryHandler(dbContext, timeProvider),
            dayReconciliationStatusProvider
                ?? new StaticVisitDayReconciliationStatusProvider(
                    VisitDayReconciliationStatus.Open),
            timeProvider);
    }

    private static IMembershipStateRecalculator CreateMembershipStateRecalculator(
        BodyLifeDbContext dbContext)
    {
        return new MembershipStateRecalculator(
            new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(CancellationRecordedAt)));
    }

    private static CancelVisitCommand CreateCancelVisitCommand(
        VisitStorageFixture fixture,
        Guid visitId,
        string idempotencyKey,
        ActorContext? actor = null,
        EntryOrigin origin = EntryOrigin.Normal,
        DateTimeOffset? occurredAt = null,
        string? reason = "Mistaken reception entry",
        string? comment = "Cancellation requested at reception",
        Guid? entryBatchId = null)
    {
        return new CancelVisitCommand(
            new CommandEnvelope(
                actor ?? fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                origin,
                occurredAt ?? CancellationOccurredAt,
                idempotencyKey,
                reason,
                comment),
            visitId,
            entryBatchId);
    }

    private static void AssertSuccessfulCancellation(
        CommandResult result,
        Guid clientId,
        Guid visitId)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.True(result.PrimaryEntityId.HasValue);
        Assert.Equal(
            CancelVisitCommand.PrimaryEntityType,
            result.PrimaryEntityId.Value.Type);
        Assert.NotEqual(Guid.Empty, result.PrimaryEntityId.Value.Value);
        Assert.Equal(
            new EntityId(CancelVisitCommand.CanonicalRereadEntityType, clientId),
            result.RereadTargetId);
        Assert.Equal(
            [new EntityId(CancelVisitCommand.SourceVisitEntityType, visitId)],
            result.RelatedEntityIds);
        Assert.True(result.AuditEntryId.HasValue);
        Assert.Empty(result.Errors);
    }

    private static void AssertCommandError(
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

        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
        Assert.Null(result.AuditEntryId);
    }

    private static async Task<VisitStorageFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext,
        int visitsLimit = 8)
    {
        var bootstrap = await new OwnerBootstrapper(dbContext, new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var actorAccountId = bootstrap.AccountId!.Value;
        var sessionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var otherMembershipId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.sessions (
                id,
                account_id,
                device_label,
                started_at,
                expires_at,
                ended_at,
                last_seen_at)
            values (
                @session_id,
                @actor_account_id,
                'Reception tablet',
                @session_started_at,
                @session_expires_at,
                null,
                @recorded_at);

            insert into bodylife.clients (
                id,
                surname,
                name,
                patronymic,
                normalized_full_name,
                phone_raw,
                phone_normalized,
                phone_last4,
                comment,
                operational_status,
                created_at,
                created_by_account_id,
                updated_at)
            values
                (
                    @client_id,
                    'Ivanenko',
                    'Ivan',
                    null,
                    'IVANENKO IVAN',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @recorded_at,
                    @actor_account_id,
                    @recorded_at),
                (
                    @other_client_id,
                    'Petrenko',
                    'Olena',
                    null,
                    'PETRENKO OLENA',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @recorded_at,
                    @actor_account_id,
                    @recorded_at);

            insert into bodylife.membership_types (
                id,
                name,
                duration_days,
                visits_limit,
                price_amount,
                price_currency,
                is_active,
                comment,
                created_at,
                updated_at,
                deactivated_at)
            values (
                @membership_type_id,
                'Visits storage fixture',
                30,
                @visits_limit,
                1000,
                'UAH',
                true,
                null,
                @recorded_at,
                @recorded_at,
                null);

            insert into bodylife.issued_memberships (
                id,
                client_id,
                membership_type_id,
                type_name_snapshot,
                duration_days_snapshot,
                visits_limit_snapshot,
                price_amount_snapshot,
                price_currency_snapshot,
                start_date,
                base_end_date,
                issued_at,
                issued_by_account_id,
                status,
                entry_origin,
                entry_batch_id,
                comment)
            values
                (
                    @membership_id,
                    @client_id,
                    @membership_type_id,
                    'Visits storage fixture',
                    30,
                    @visits_limit,
                    1000,
                    'UAH',
                    @start_date,
                    @base_end_date,
                    @recorded_at,
                    @actor_account_id,
                    'active',
                    'normal',
                    null,
                    null),
                (
                    @other_membership_id,
                    @other_client_id,
                    @membership_type_id,
                    'Visits storage fixture',
                    30,
                    @visits_limit,
                    1000,
                    'UAH',
                    @start_date,
                    @base_end_date,
                    @recorded_at,
                    @actor_account_id,
                    'active',
                    'normal',
                    null,
                    null)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("actor_account_id", actorAccountId);
        command.Parameters.AddWithValue("session_started_at", TestNow.AddMinutes(-1));
        command.Parameters.AddWithValue("session_expires_at", TestNow.AddHours(12));
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("other_client_id", otherClientId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("other_membership_id", otherMembershipId);
        command.Parameters.AddWithValue("visits_limit", visitsLimit);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 1));
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 30));
        Assert.Equal(6, await command.ExecuteNonQueryAsync());

        var actor = new ActorContext(
            new AccountId(actorAccountId),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(sessionId),
            "Reception tablet");
        return new VisitStorageFixture(
            actor,
            clientId,
            otherClientId,
            membershipId,
            otherMembershipId);
    }

    private static async Task<Guid> InsertVisitAsync(
        string connectionString,
        VisitStorageFixture fixture,
        Guid clientId,
        string visitKind,
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        string? comment = null,
        string status = "active",
        DateTimeOffset? recordedAt = null,
        DateTimeOffset? occurredAt = null)
    {
        var visitId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.visits (
                id,
                client_id,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                visit_kind,
                entry_origin,
                entry_batch_id,
                comment,
                status)
            values (
                @id,
                @client_id,
                @occurred_at,
                @recorded_at,
                @recorded_by_account_id,
                @session_id,
                @visit_kind,
                @entry_origin,
                @entry_batch_id,
                @comment,
                @status)
            """;
        command.Parameters.AddWithValue("id", visitId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("occurred_at", occurredAt ?? VisitOccurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt ?? TestNow);
        command.Parameters.AddWithValue("recorded_by_account_id", fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("visit_kind", visitKind);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Varchar).Value =
            comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return visitId;
    }

    private static async Task<Guid> InsertConsumptionAsync(
        string connectionString,
        VisitStorageFixture fixture,
        Guid visitId,
        Guid clientId,
        Guid membershipId,
        string visitKind = "membership",
        string consumptionType = "counted",
        string sourceFactType = "visit",
        Guid? sourceFactId = null,
        string status = "active",
        DateTimeOffset? recordedAt = null)
    {
        var consumptionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.visit_consumptions (
                id,
                visit_id,
                client_id,
                visit_kind,
                membership_id,
                consumption_type,
                source_fact_type,
                source_fact_id,
                recorded_at,
                recorded_by_account_id,
                recorded_session_id,
                status)
            values (
                @id,
                @visit_id,
                @client_id,
                @visit_kind,
                @membership_id,
                @consumption_type,
                @source_fact_type,
                @source_fact_id,
                @recorded_at,
                @recorded_by_account_id,
                @recorded_session_id,
                @status)
            """;
        command.Parameters.AddWithValue("id", consumptionId);
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("visit_kind", visitKind);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("consumption_type", consumptionType);
        command.Parameters.AddWithValue("source_fact_type", sourceFactType);
        command.Parameters.AddWithValue("source_fact_id", sourceFactId ?? visitId);
        command.Parameters.AddWithValue("recorded_at", recordedAt ?? TestNow);
        command.Parameters.AddWithValue("recorded_by_account_id", fixture.ActorAccountId);
        command.Parameters.AddWithValue("recorded_session_id", fixture.SessionId);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return consumptionId;
    }

    private static async Task<Guid> InsertCancellationAsync(
        string connectionString,
        VisitStorageFixture fixture,
        Guid visitId,
        string reason = "Mistaken reception entry",
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        DateTimeOffset? recordedAt = null)
    {
        var cancellationId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.visit_cancellations (
                id,
                visit_id,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id)
            values (
                @id,
                @visit_id,
                @reason,
                @occurred_at,
                @recorded_at,
                @recorded_by_account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id)
            """;
        command.Parameters.AddWithValue("id", cancellationId);
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("occurred_at", VisitOccurredAt.AddMinutes(30));
        command.Parameters.AddWithValue("recorded_at", recordedAt ?? TestNow.AddMinutes(1));
        command.Parameters.AddWithValue("recorded_by_account_id", fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        return cancellationId;
    }

    private static async Task SetVisitAndConsumptionCanceledAsync(
        string connectionString,
        Guid visitId,
        Guid consumptionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.visits
            set status = 'canceled'
            where id = @visit_id;

            update bodylife.visit_consumptions
            set status = 'canceled'
            where id = @consumption_id
            """;
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("consumption_id", consumptionId);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task<PersistedVisit> ReadVisitAsync(
        string connectionString,
        Guid visitId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                client_id,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                visit_kind,
                entry_origin,
                entry_batch_id,
                comment,
                status
            from bodylife.visits
            where id = @id
            """;
        command.Parameters.AddWithValue("id", visitId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new PersistedVisit(
            reader.GetGuid(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetString(9));
    }

    private static async Task<PersistedConsumption> ReadConsumptionAsync(
        string connectionString,
        Guid consumptionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                visit_id,
                client_id,
                membership_id,
                consumption_type,
                source_fact_type,
                source_fact_id,
                recorded_by_account_id,
                recorded_session_id,
                status
            from bodylife.visit_consumptions
            where id = @id
            """;
        command.Parameters.AddWithValue("id", consumptionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new PersistedConsumption(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetGuid(5),
            reader.GetGuid(6),
            reader.GetGuid(7),
            reader.GetString(8));
    }

    private static async Task<PersistedCancellation> ReadCancellationAsync(
        string connectionString,
        Guid cancellationId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                visit_id,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id
            from bodylife.visit_cancellations
            where id = @id
            """;
        command.Parameters.AddWithValue("id", cancellationId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new PersistedCancellation(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetGuid(4),
            reader.GetGuid(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7));
    }

    private static async Task<PersistedMembershipStateCache> ReadCacheAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select counted_visits,
                   remaining_visits,
                   negative_balance,
                   first_negative_visit_id,
                   first_negative_visit_date,
                   last_counted_visit_at,
                   recalculated_at
            from bodylife.membership_state_cache
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new PersistedMembershipStateCache(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
            reader.IsDBNull(5)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6));
    }

    private static async Task<PersistedCancellationAudit> ReadCancellationAuditAsync(
        PostgreSqlTestDatabase database,
        Guid auditEntryId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select action_type,
                   entity_type,
                   entity_id,
                   occurred_at,
                   recorded_at,
                   reason,
                   comment,
                   idempotency_key,
                   before_summary::text,
                   after_summary::text,
                   changed_after_close
            from bodylife.business_audit_entries
            where id = @id
            """;
        command.Parameters.AddWithValue("id", auditEntryId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new PersistedCancellationAudit(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetBoolean(10));
    }

    private static async Task DeactivateActorAsync(
        PostgreSqlTestDatabase database,
        Guid accountId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.accounts
            set is_active = false,
                deactivated_at = @deactivated_at
            where id = @id
            """;
        command.Parameters.AddWithValue("deactivated_at", CancellationRecordedAt);
        command.Parameters.AddWithValue("id", accountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task UpdateActorIdentityAsync(
        PostgreSqlTestDatabase database,
        Guid accountId,
        string accountType,
        string role)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.accounts
            set account_type = @account_type,
                role = @role
            where id = @id
            """;
        command.Parameters.AddWithValue("account_type", accountType);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("id", accountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task ExecuteNonQueryAsync(
        PostgreSqlTestDatabase database,
        string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertNoCancellationMutationAsync(
        PostgreSqlTestDatabase database,
        Guid visitId,
        Guid? consumptionId = null)
    {
        Assert.Equal(
            "active",
            (await ReadVisitAsync(database.ConnectionString, visitId)).Status);
        if (consumptionId is { } activeConsumptionId)
        {
            Assert.Equal(
                "active",
                (await ReadConsumptionAsync(
                    database.ConnectionString,
                    activeConsumptionId)).Status);
        }

        Assert.Equal(0L, await CountRowsAsync(database, "visit_cancellations"));
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static async Task DeleteByIdAsync(
        string connectionString,
        string tableName,
        Guid id)
    {
        var allowedTableName = tableName switch
        {
            "issued_memberships" => tableName,
            "visits" => tableName,
            _ => throw new ArgumentOutOfRangeException(nameof(tableName)),
        };
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"delete from bodylife.{allowedTableName} where id = @id";
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PostgresException> AssertSourceUpdateBlockedAsync(
        string connectionString,
        string tableName,
        Guid id)
    {
        var updateStatement = tableName switch
        {
            "visits" =>
                "update bodylife.visits set comment = 'Concurrent update' where id = @id",
            "visit_consumptions" =>
                "update bodylife.visit_consumptions set status = 'canceled' where id = @id",
            "visit_cancellations" =>
                "update bodylife.visit_cancellations set reason = 'Concurrent update' where id = @id",
            _ => throw new ArgumentOutOfRangeException(nameof(tableName)),
        };
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"set lock_timeout = '250ms'; {updateStatement}";
        command.Parameters.AddWithValue("id", id);

        return await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
    }

    private static async Task AssertPostgresViolationAsync(
        Func<Task> action,
        string sqlState,
        string? constraintName = null)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);

        Assert.Equal(sqlState, exception.SqlState);
        if (constraintName is not null)
        {
            Assert.Equal(constraintName, exception.ConstraintName);
        }
    }

    private static async Task<IReadOnlyList<string>> ReadColumnNamesAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select column_name
            from information_schema.columns
            where table_schema = 'bodylife'
              and table_name = @table_name
            order by ordinal_position
            """;
        command.Parameters.AddWithValue("table_name", tableName);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<bool> ConstraintExistsAsync(
        PostgreSqlTestDatabase database,
        string constraintName)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select exists (
                select 1
                from pg_constraint constraint_row
                join pg_namespace schema_row
                  on schema_row.oid = constraint_row.connamespace
                where schema_row.nspname = 'bodylife'
                  and constraint_row.conname = @constraint_name)
            """;
        command.Parameters.AddWithValue("constraint_name", constraintName);

        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadIndexDefinitionAsync(
        PostgreSqlTestDatabase database,
        string indexName)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select indexdef
            from pg_indexes
            where schemaname = 'bodylife'
              and indexname = @index_name
            """;
        command.Parameters.AddWithValue("index_name", indexName);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName,
        string? predicate = null)
    {
        var allowedTableName = tableName switch
        {
            "business_audit_entries" => tableName,
            "command_idempotency_keys" => tableName,
            "membership_state_cache" => tableName,
            "visit_cancellations" => tableName,
            "visit_consumptions" => tableName,
            "visits" => tableName,
            _ => throw new ArgumentOutOfRangeException(nameof(tableName)),
        };
        var allowedPredicate = predicate switch
        {
            null => string.Empty,
            "status = 'active'" => $" where {predicate}",
            _ => throw new ArgumentOutOfRangeException(nameof(predicate)),
        };

        return database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{allowedTableName}{allowedPredicate}");
    }

    private sealed record VisitStorageFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId,
        Guid OtherMembershipId)
    {
        public Guid ActorAccountId => Actor.AccountId.Value;

        public Guid SessionId => Actor.SessionId.Value;
    }

    private sealed record PersistedVisit(
        Guid ClientId,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string VisitKind,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status);

    private sealed record PersistedConsumption(
        Guid VisitId,
        Guid ClientId,
        Guid MembershipId,
        string ConsumptionType,
        string SourceFactType,
        Guid SourceFactId,
        Guid RecordedByAccountId,
        Guid RecordedSessionId,
        string Status);

    private sealed record PersistedCancellation(
        Guid VisitId,
        string Reason,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId);

    private sealed record PersistedMembershipStateCache(
        int CountedVisits,
        int RemainingVisits,
        int NegativeBalance,
        Guid? FirstNegativeVisitId,
        DateOnly? FirstNegativeVisitDate,
        DateTimeOffset? LastCountedVisitAt,
        DateTimeOffset RecalculatedAt);

    private sealed record PersistedCancellationAudit(
        string ActionType,
        string EntityType,
        Guid EntityId,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string? Reason,
        string? Comment,
        string? IdempotencyKey,
        string BeforeSummary,
        string AfterSummary,
        bool ChangedAfterClose);

    private sealed class StaticVisitDayReconciliationStatusProvider(
        VisitDayReconciliationStatus status)
        : IVisitDayReconciliationStatusProvider
    {
        public Task<VisitDayReconciliationStatus> GetStatusAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(status);
        }
    }

    private sealed class FailOnSecondMembershipRecalculation(
        IMembershipStateRecalculator inner)
        : IMembershipStateRecalculator
    {
        private int callCount;

        public async Task<MembershipStateRecalculationResult> RecalculateAsync(
            Guid membershipId,
            CancellationToken cancellationToken = default)
        {
            callCount++;
            if (callCount == 2)
            {
                return new MembershipStateRecalculationResult(
                    membershipId,
                    MembershipStateRecalculationStatus.InvalidSourceState);
            }

            return await inner.RecalculateAsync(membershipId, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
