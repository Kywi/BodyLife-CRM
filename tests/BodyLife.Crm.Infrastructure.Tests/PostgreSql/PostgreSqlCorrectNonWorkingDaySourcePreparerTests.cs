using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlCorrectNonWorkingDaySourcePreparerTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        17,
        14,
        0,
        0,
        TimeSpan.Zero);
    private static readonly Guid FirstClientId = Guid.Parse(
        "10000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondClientId = Guid.Parse(
        "10000000-0000-0000-0000-000000000002");
    private static readonly Guid ThirdClientId = Guid.Parse(
        "10000000-0000-0000-0000-000000000003");
    private static readonly Guid FirstMembershipId = Guid.Parse(
        "20000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondMembershipId = Guid.Parse(
        "20000000-0000-0000-0000-000000000002");
    private static readonly Guid ThirdMembershipId = Guid.Parse(
        "20000000-0000-0000-0000-000000000003");
    private static readonly Guid FirstApplicationId = Guid.Parse(
        "30000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondApplicationId = Guid.Parse(
        "30000000-0000-0000-0000-000000000002");
    private static readonly DateRange ReplacementPeriod = new(
        new DateOnly(2026, 2, 3),
        new DateOnly(2026, 2, 4));

    [PostgreSqlFact]
    public async Task PreparationRequiresAConsistentCallerTransaction()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var preparer = new CorrectNonWorkingDaySourcePreparer(dbContext);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            preparer.PrepareAsync(
                Guid.Empty,
                NonWorkingDayCorrectionMode.ReplaceRange));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            preparer.PrepareAsync(
                fixture.PeriodId,
                (NonWorkingDayCorrectionMode)99));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preparer.PrepareAsync(
                fixture.PeriodId,
                NonWorkingDayCorrectionMode.ReplaceRange));

        await using (var readCommitted = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                preparer.PrepareAsync(
                    fixture.PeriodId,
                    NonWorkingDayCorrectionMode.ReplaceRange));
            await readCommitted.RollbackAsync();
        }

        var missingPeriodId = Guid.NewGuid();
        await using var repeatableRead = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var missing = await preparer.PrepareAsync(
            missingPeriodId,
            NonWorkingDayCorrectionMode.ReplaceReason);

        Assert.Equal(
            CorrectNonWorkingDaySourcePreparationStatus.NotFound,
            missing.Status);
        Assert.Equal(missingPeriodId, missing.PeriodId);
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.PreserveConfirmedApplications,
            missing.ScopeBehavior);
        Assert.Null(missing.Source);
        await repeatableRead.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task ActivePreparationReturnsExactOrderedSourceAndLocksEverySourceRow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var preparer = new CorrectNonWorkingDaySourcePreparer(dbContext);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead);
        var result = await preparer.PrepareAsync(
            fixture.PeriodId,
            NonWorkingDayCorrectionMode.ReplaceRange);

        Assert.True(result.IsPrepared);
        Assert.Equal(
            CorrectNonWorkingDaySourcePreparationStatus.Prepared,
            result.Status);
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.RecomputeReplacement,
            result.ScopeBehavior);
        var source = Assert.IsType<NonWorkingDayCorrectionSource>(result.Source);
        Assert.Equal(fixture.PeriodId, source.PeriodId);
        Assert.Equal(new DateOnly(2026, 1, 30), source.Period.StartDate);
        Assert.Equal(new DateOnly(2026, 2, 2), source.Period.EndDate);
        Assert.Equal("weather_closure", source.ReasonCode);
        Assert.Equal("Severe weather", source.ReasonComment);
        Assert.Equal(TestNow, source.CreatedAt);
        Assert.Equal(fixture.AccountId, source.CreatedByAccountId);
        Assert.Equal(fixture.SessionId, source.SessionId);
        Assert.Equal(NonWorkingDayCorrectionSourceStatus.Active, source.Status);
        Assert.Null(source.ExistingCancellationId);
        Assert.Equal(
            [FirstMembershipId, SecondMembershipId],
            source.Applications.Select(application => application.MembershipId));
        Assert.Equal(
            [FirstApplicationId, SecondApplicationId],
            source.Applications.Select(application => application.ApplicationId));
        Assert.Equal(
            [FirstClientId, SecondClientId],
            source.Applications.Select(application => application.ClientId));
        Assert.All(source.Applications, application =>
        {
            Assert.Equal(source.Period, application.AppliedRange);
            Assert.Equal(TestNow.AddMinutes(-5), application.PreviewedAt);
            Assert.Equal(TestNow, application.ConfirmedAt);
            Assert.Equal(NonWorkingDayCorrectionSourceStatus.Active, application.Status);
        });
        Assert.False(dbContext.ChangeTracker.HasChanges());

        await AssertRowLockUnavailableAsync(
            database,
            "issued_memberships",
            FirstMembershipId);
        await AssertRowLockUnavailableAsync(
            database,
            "issued_memberships",
            SecondMembershipId);
        await AssertRowLockUnavailableAsync(
            database,
            "non_working_periods",
            fixture.PeriodId);
        await AssertRowLockUnavailableAsync(
            database,
            "non_working_period_applications",
            FirstApplicationId);
        await AssertRowLockUnavailableAsync(
            database,
            "non_working_period_applications",
            SecondApplicationId);

        await transaction.RollbackAsync();
        Assert.Equal(1, await CountRowsAsync(database, "non_working_periods"));
        Assert.Equal(
            2,
            await CountRowsAsync(database, "non_working_period_applications"));
        Assert.Equal(
            0,
            await CountRowsAsync(database, "non_working_period_cancellations"));
    }

    [PostgreSqlFact]
    public async Task CanceledPreparationReturnsTerminalOutcomeAndLocksCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(
            database,
            dbContext,
            periodStatus: "canceled",
            applicationStatus: "canceled",
            includeCancellation: true);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable);
        var result = await new CorrectNonWorkingDaySourcePreparer(dbContext)
            .PrepareAsync(fixture.PeriodId, NonWorkingDayCorrectionMode.Cancel);

        Assert.Equal(
            CorrectNonWorkingDaySourcePreparationStatus.AlreadyCanceled,
            result.Status);
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.NoReplacement,
            result.ScopeBehavior);
        var source = Assert.IsType<NonWorkingDayCorrectionSource>(result.Source);
        Assert.Equal(NonWorkingDayCorrectionSourceStatus.Canceled, source.Status);
        Assert.Equal(fixture.CancellationId, source.ExistingCancellationId);
        await AssertRowLockUnavailableAsync(
            database,
            "non_working_period_cancellations",
            fixture.CancellationId!.Value);
        await transaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task CorrectedPreparationReturnsTerminalOutcomeWithOriginalSnapshot()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(
            database,
            dbContext,
            periodStatus: "corrected",
            applicationStatus: "corrected");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead);
        var result = await new CorrectNonWorkingDaySourcePreparer(dbContext)
            .PrepareAsync(
                fixture.PeriodId,
                NonWorkingDayCorrectionMode.ReplaceReason);

        Assert.Equal(
            CorrectNonWorkingDaySourcePreparationStatus.AlreadyCorrected,
            result.Status);
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.PreserveConfirmedApplications,
            result.ScopeBehavior);
        var source = Assert.IsType<NonWorkingDayCorrectionSource>(result.Source);
        Assert.Equal(NonWorkingDayCorrectionSourceStatus.Corrected, source.Status);
        Assert.Null(source.ExistingCancellationId);
        Assert.Equal(2, source.Applications.Count);
        Assert.All(source.Applications, application =>
            Assert.Equal(
                NonWorkingDayCorrectionSourceStatus.Corrected,
                application.Status));
        await transaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task PreparationRejectsStatusDriftAndMissingCancellationFact()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(
            database,
            dbContext,
            periodStatus: "active",
            applicationStatus: "corrected");
        var preparer = new CorrectNonWorkingDaySourcePreparer(dbContext);

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead))
        {
            var drift = await preparer.PrepareAsync(
                fixture.PeriodId,
                NonWorkingDayCorrectionMode.ReplaceRange);
            Assert.Equal(
                CorrectNonWorkingDaySourcePreparationStatus.InconsistentSource,
                drift.Status);
            Assert.Null(drift.Source);
            await transaction.RollbackAsync();
        }

        await SetStatusesAsync(database, fixture.PeriodId, "canceled");
        await using var canceledTransaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var missingCancellation = await preparer.PrepareAsync(
            fixture.PeriodId,
            NonWorkingDayCorrectionMode.Cancel);

        Assert.Equal(
            CorrectNonWorkingDaySourcePreparationStatus.InconsistentSource,
            missingCancellation.Status);
        Assert.Null(missingCancellation.Source);
        await canceledTransaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task CommandRevalidationRequiresCallerOwnedConsistentTransaction()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var commandPreparation = CreateCommandPreparation(
            fixture,
            NonWorkingDayCorrectionMode.Cancel,
            "bodylife-nwd-correction-v1.invalid.signature");
        var preparer = CreateCommandRevalidationPreparer(
            dbContext,
            CreateCorrectionTokenService(TestNow),
            TestNow);

        var missingTransaction = await Assert.ThrowsAsync<InvalidOperationException>(
            () => preparer.PrepareAsync(commandPreparation));
        Assert.Contains(
            "caller-owned",
            missingTransaction.Message,
            StringComparison.OrdinalIgnoreCase);

        await using var readCommitted = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var weakIsolation = await Assert.ThrowsAsync<InvalidOperationException>(
            () => preparer.PrepareAsync(commandPreparation));
        Assert.Contains(
            "RepeatableRead",
            weakIsolation.Message,
            StringComparison.Ordinal);
        await readCommitted.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task RangeCommandRevalidationAuthenticatesExactCurrentScopeWithoutWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var tokenService = CreateCorrectionTokenService(TestNow);
        var confirmation = await IssueConfirmationAsync(
            dbContext,
            fixture,
            NonWorkingDayCorrectionMode.ReplaceRange,
            tokenService);
        var commandPreparation = CreateCommandPreparation(
            fixture,
            NonWorkingDayCorrectionMode.ReplaceRange,
            confirmation.ConfirmationToken);
        var before = await ReadWriteCountsAsync(database);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead);
        var result = await CreateCommandRevalidationPreparer(
                dbContext,
                tokenService,
                TestNow)
            .PrepareAsync(commandPreparation);

        Assert.True(result.IsPrepared);
        Assert.Empty(result.Errors);
        Assert.Same(commandPreparation, result.CommandPreparation);
        var material = Assert.IsType<NonWorkingDayCorrectionConfirmationMaterial>(
            result.ConfirmationMaterial);
        Assert.Equal(fixture.PeriodId, material.PeriodId);
        Assert.Equal(NonWorkingDayCorrectionMode.ReplaceRange, material.Mode);
        Assert.Equal(ReplacementPeriod, material.ReplacementInput!.Period);
        Assert.Equal("maintenance", material.ReplacementInput.ReasonCode);
        Assert.Equal(2, material.OriginalSource.Applications.Count);
        Assert.Equal(3, material.ReplacementScope!.AffectedMemberships.Count);
        Assert.Contains(
            material.ReplacementScope.AffectedMemberships,
            item => item.MembershipId == ThirdMembershipId);
        var replacementImpact = Assert.IsType<
            MembershipNonWorkingDayReplacementImpactPreparation>(
                result.ReplacementImpact);
        Assert.Equal(
            [FirstApplicationId, SecondApplicationId],
            replacementImpact.ExcludedApplicationIds);
        Assert.Equal(3, replacementImpact.AffectedCount);
        var tokenValidation = Assert.IsType<NonWorkingDayCorrectionTokenValidation>(
            result.TokenValidation);
        Assert.Equal(NonWorkingDayCorrectionTokenValidationStatus.Valid,
            tokenValidation.Status);
        Assert.Equal(confirmation.ConfirmationFingerprint,
            tokenValidation.ConfirmationFingerprint);
        Assert.False(dbContext.ChangeTracker.HasChanges());
        Assert.Equal(before, await ReadWriteCountsAsync(database));

        await AssertRowLockUnavailableAsync(
            database,
            "issued_memberships",
            ThirdMembershipId);
        await AssertRowLockUnavailableAsync(
            database,
            "non_working_periods",
            fixture.PeriodId);
        await AssertRowLockUnavailableAsync(
            database,
            "non_working_period_applications",
            FirstApplicationId);

        await transaction.RollbackAsync();
        Assert.Equal(before, await ReadWriteCountsAsync(database));
    }

    [PostgreSqlFact]
    public async Task ReasonAndCancelCommandRevalidationPreserveModeSpecificScope()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var tokenService = CreateCorrectionTokenService(TestNow);
        var before = await ReadWriteCountsAsync(database);

        foreach (var mode in new[]
                 {
                     NonWorkingDayCorrectionMode.ReplaceReason,
                     NonWorkingDayCorrectionMode.Cancel,
                 })
        {
            var confirmation = await IssueConfirmationAsync(
                dbContext,
                fixture,
                mode,
                tokenService);
            var commandPreparation = CreateCommandPreparation(
                fixture,
                mode,
                confirmation.ConfirmationToken);

            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.RepeatableRead);
            var result = await CreateCommandRevalidationPreparer(
                    dbContext,
                    tokenService,
                    TestNow)
                .PrepareAsync(commandPreparation);

            Assert.True(result.IsPrepared);
            Assert.Empty(result.Errors);
            Assert.Null(result.ReplacementImpact);
            Assert.Equal(
                NonWorkingDayCorrectionTokenValidationStatus.Valid,
                result.TokenValidation!.Status);
            if (mode == NonWorkingDayCorrectionMode.ReplaceReason)
            {
                Assert.Equal(
                    NonWorkingDayCorrectionScopeBehavior
                        .PreserveConfirmedApplications,
                    result.ConfirmationMaterial!.ScopeBehavior);
                Assert.Equal(
                    result.ConfirmationMaterial.OriginalSource.Period,
                    result.ConfirmationMaterial.ReplacementInput!.Period);
                Assert.Equal(
                    [FirstMembershipId, SecondMembershipId],
                    result.ConfirmationMaterial.PreservedScope!.AffectedMemberships
                        .Select(item => item.MembershipId));
            }
            else
            {
                Assert.Equal(
                    NonWorkingDayCorrectionScopeBehavior.NoReplacement,
                    result.ConfirmationMaterial!.ScopeBehavior);
                Assert.Null(result.ConfirmationMaterial.ReplacementInput);
                Assert.Null(result.ConfirmationMaterial.ConfirmedScope);
            }

            Assert.Equal(before, await ReadWriteCountsAsync(database));
            await transaction.RollbackAsync();
        }

        var owner = OwnerActor(fixture);
        var adminPreparation = CreateCommandPreparation(
            fixture,
            NonWorkingDayCorrectionMode.Cancel,
            (await IssueConfirmationAsync(
                dbContext,
                fixture,
                NonWorkingDayCorrectionMode.Cancel,
                tokenService)).ConfirmationToken,
            owner with
            {
                Role = ActorRole.Admin,
                AccountKind = AccountKind.NamedAdmin,
            });
        var denied = await ExecuteRevalidationAsync(
            dbContext,
            tokenService,
            TestNow,
            adminPreparation);

        AssertRejected(denied, CommandErrorCode.PermissionDenied, field: null);
        Assert.Equal(before, await ReadWriteCountsAsync(database));
    }

    [PostgreSqlFact]
    public async Task CommandRevalidationMapsSourceAndTokenFailuresToStableErrors()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var issuingTokenService = CreateCorrectionTokenService(TestNow);
        var confirmation = await IssueConfirmationAsync(
            dbContext,
            fixture,
            NonWorkingDayCorrectionMode.ReplaceRange,
            issuingTokenService);
        var before = await ReadWriteCountsAsync(database);

        var invalidToken = await ExecuteRevalidationAsync(
            dbContext,
            issuingTokenService,
            TestNow,
            CreateCommandPreparation(
                fixture,
                NonWorkingDayCorrectionMode.ReplaceRange,
                "bodylife-nwd-correction-v1.invalid.signature"));
        AssertRejected(
            invalidToken,
            CommandErrorCode.ValidationFailed,
            "confirmationToken");

        var changedMaterial = await ExecuteRevalidationAsync(
            dbContext,
            issuingTokenService,
            TestNow,
            CreateCommandPreparation(
                fixture,
                NonWorkingDayCorrectionMode.ReplaceRange,
                confirmation.ConfirmationToken,
                replacementReasonCode: "changed_maintenance"));
        AssertRejected(
            changedMaterial,
            CommandErrorCode.AffectedScopeChanged,
            "confirmationToken");

        var expired = await ExecuteRevalidationAsync(
            dbContext,
            CreateCorrectionTokenService(TestNow.AddMinutes(6)),
            TestNow.AddMinutes(6),
            CreateCommandPreparation(
                fixture,
                NonWorkingDayCorrectionMode.ReplaceRange,
                confirmation.ConfirmationToken));
        AssertRejected(
            expired,
            CommandErrorCode.PreviewExpired,
            "confirmationToken");

        var missing = await ExecuteRevalidationAsync(
            dbContext,
            issuingTokenService,
            TestNow,
            CreateCommandPreparation(
                fixture,
                NonWorkingDayCorrectionMode.ReplaceRange,
                confirmation.ConfirmationToken,
                periodId: Guid.NewGuid()));
        AssertRejected(missing, CommandErrorCode.NotFound, "periodId");

        Assert.Equal(before, await ReadWriteCountsAsync(database));
    }

    [Fact]
    public void PersistenceRegistrationResolvesCorrectionPreparers()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] =
                    "Host=localhost;Database=bodylife;Username=bodylife;Password=test",
                [$"{NonWorkingDayPreviewTokenOptions.SectionName}:"
                    + NonWorkingDayPreviewTokenOptions.SigningKeyName] =
                    SigningKey(),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddBodyLifePersistence(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<CorrectNonWorkingDaySourcePreparer>(
            scope.ServiceProvider
                .GetRequiredService<CorrectNonWorkingDaySourcePreparer>());
        Assert.IsType<CorrectNonWorkingDayCommandRevalidationPreparer>(
            scope.ServiceProvider.GetRequiredService<
                CorrectNonWorkingDayCommandRevalidationPreparer>());
    }

    private static async Task<SourceFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext,
        string periodStatus = "active",
        string applicationStatus = "active",
        bool includeCancellation = false)
    {
        var bootstrap = await new OwnerBootstrapper(
                dbContext,
                new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var accountId = bootstrap.AccountId!.Value;
        var sessionId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var cancellationId = includeCancellation ? Guid.NewGuid() : (Guid?)null;
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
                @account_id,
                'Owner laptop',
                @recorded_at,
                @expires_at,
                null,
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
                'Correction source fixture',
                30,
                8,
                1000,
                'UAH',
                true,
                null,
                @recorded_at,
                @recorded_at,
                null);

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
                    @first_client_id,
                    'Correction',
                    'First',
                    null,
                    'CORRECTION FIRST',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @recorded_at,
                    @account_id,
                    @recorded_at),
                (
                    @second_client_id,
                    'Correction',
                    'Second',
                    null,
                    'CORRECTION SECOND',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @recorded_at,
                    @account_id,
                    @recorded_at),
                (
                    @third_client_id,
                    'Correction',
                    'Third',
                    null,
                    'CORRECTION THIRD',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @recorded_at,
                    @account_id,
                    @recorded_at);

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
                    @first_membership_id,
                    @first_client_id,
                    @membership_type_id,
                    'Correction source fixture',
                    30,
                    8,
                    1000,
                    'UAH',
                    '2026-01-10'::date,
                    '2026-02-08'::date,
                    @recorded_at,
                    @account_id,
                    'active',
                    'normal',
                    null,
                    null),
                (
                    @second_membership_id,
                    @second_client_id,
                    @membership_type_id,
                    'Correction source fixture',
                    30,
                    8,
                    1000,
                    'UAH',
                    '2026-01-15'::date,
                    '2026-02-13'::date,
                    @recorded_at,
                    @account_id,
                    'active',
                    'normal',
                    null,
                    null),
                (
                    @third_membership_id,
                    @third_client_id,
                    @membership_type_id,
                    'Correction source fixture',
                    30,
                    8,
                    1000,
                    'UAH',
                    '2026-02-03'::date,
                    '2026-03-04'::date,
                    @recorded_at,
                    @account_id,
                    'active',
                    'normal',
                    null,
                    null);

            insert into bodylife.non_working_periods (
                id,
                start_date,
                end_date,
                reason_code,
                reason_comment,
                created_at,
                created_by_account_id,
                session_id,
                status)
            values (
                @period_id,
                '2026-01-30'::date,
                '2026-02-02'::date,
                'weather_closure',
                'Severe weather',
                @recorded_at,
                @account_id,
                @session_id,
                @period_status);

            insert into bodylife.non_working_period_applications (
                id,
                non_working_period_id,
                membership_id,
                client_id,
                applied_start_date,
                applied_end_date,
                previewed_at,
                confirmed_at,
                status)
            values
                (
                    @second_application_id,
                    @period_id,
                    @second_membership_id,
                    @second_client_id,
                    '2026-01-30'::date,
                    '2026-02-02'::date,
                    @previewed_at,
                    @recorded_at,
                    @application_status),
                (
                    @first_application_id,
                    @period_id,
                    @first_membership_id,
                    @first_client_id,
                    '2026-01-30'::date,
                    '2026-02-02'::date,
                    @previewed_at,
                    @recorded_at,
                    @application_status);

            insert into bodylife.non_working_period_cancellations (
                id,
                non_working_period_id,
                reason,
                recorded_at,
                recorded_by_account_id,
                session_id)
            select
                @cancellation_id,
                @period_id,
                'Canceled by Owner',
                @recorded_at,
                @account_id,
                @session_id
            where @include_cancellation;
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("first_client_id", FirstClientId);
        command.Parameters.AddWithValue("second_client_id", SecondClientId);
        command.Parameters.AddWithValue("third_client_id", ThirdClientId);
        command.Parameters.AddWithValue("first_membership_id", FirstMembershipId);
        command.Parameters.AddWithValue("second_membership_id", SecondMembershipId);
        command.Parameters.AddWithValue("third_membership_id", ThirdMembershipId);
        command.Parameters.AddWithValue("first_application_id", FirstApplicationId);
        command.Parameters.AddWithValue("second_application_id", SecondApplicationId);
        command.Parameters.AddWithValue("period_id", periodId);
        command.Parameters.AddWithValue("cancellation_id", cancellationId ?? Guid.NewGuid());
        command.Parameters.AddWithValue("period_status", periodStatus);
        command.Parameters.AddWithValue("application_status", applicationStatus);
        command.Parameters.AddWithValue("include_cancellation", includeCancellation);
        command.Parameters.AddWithValue("previewed_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(12));
        Assert.Equal(11 + (includeCancellation ? 1 : 0),
            await command.ExecuteNonQueryAsync());
        dbContext.ChangeTracker.Clear();

        return new SourceFixture(
            accountId,
            sessionId,
            periodId,
            cancellationId);
    }

    private static CorrectNonWorkingDayCommandRevalidationPreparer
        CreateCommandRevalidationPreparer(
            BodyLifeDbContext dbContext,
            INonWorkingDayCorrectionTokenService tokenService,
            DateTimeOffset now)
    {
        return new CorrectNonWorkingDayCommandRevalidationPreparer(
            dbContext,
            CreateReplacementPreparer(dbContext, now),
            new CorrectNonWorkingDaySourcePreparer(dbContext),
            tokenService,
            new FixedTimeProvider(now));
    }

    private static MembershipNonWorkingDayReplacementImpactPreparer
        CreateReplacementPreparer(
            BodyLifeDbContext dbContext,
            DateTimeOffset now)
    {
        var nonWorkingDaySourceReader =
            new MembershipNonWorkingDayExtensionSourceReader(dbContext);
        var affectedScopePreparer = new MembershipNonWorkingDayAffectedScopePreparer(
            dbContext,
            new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(now),
                [
                    new MembershipFreezeExtensionSourceReader(dbContext),
                    nonWorkingDaySourceReader,
                ]));
        return new MembershipNonWorkingDayReplacementImpactPreparer(
            affectedScopePreparer,
            nonWorkingDaySourceReader);
    }

    private static HmacNonWorkingDayCorrectionTokenService
        CreateCorrectionTokenService(DateTimeOffset now)
    {
        return new HmacNonWorkingDayCorrectionTokenService(
            new NonWorkingDayPreviewTokenOptions(
                SigningKey(),
                TimeSpan.FromMinutes(5)),
            new FixedTimeProvider(now));
    }

    private static async Task<NonWorkingDayCorrectionConfirmation>
        IssueConfirmationAsync(
            BodyLifeDbContext dbContext,
            SourceFixture fixture,
            NonWorkingDayCorrectionMode mode,
            INonWorkingDayCorrectionTokenService tokenService)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead);
        MembershipNonWorkingDayReplacementImpactPreparation? replacementImpact = null;
        NonWorkingDayPreviewInput? replacementInput = null;
        if (mode == NonWorkingDayCorrectionMode.ReplaceRange)
        {
            replacementInput = new NonWorkingDayPreviewInput(
                ReplacementPeriod,
                "maintenance",
                "Boiler replacement");
            replacementImpact = await CreateReplacementPreparer(dbContext, TestNow)
                .PrepareReplacementImpactAsync(
                    fixture.PeriodId,
                    ReplacementPeriod);
        }

        var sourcePreparation = await new CorrectNonWorkingDaySourcePreparer(dbContext)
            .PrepareAsync(fixture.PeriodId, mode);
        var source = Assert.IsType<NonWorkingDayCorrectionSource>(
            sourcePreparation.Source);
        if (mode == NonWorkingDayCorrectionMode.ReplaceReason)
        {
            replacementInput = new NonWorkingDayPreviewInput(
                source.Period,
                "corrected_weather",
                "Corrected explanation");
        }

        var material = mode switch
        {
            NonWorkingDayCorrectionMode.ReplaceRange =>
                NonWorkingDayCorrectionConfirmationMaterial.ForReplaceRange(
                    source,
                    replacementInput!,
                    replacementImpact!),
            NonWorkingDayCorrectionMode.ReplaceReason =>
                NonWorkingDayCorrectionConfirmationMaterial.ForReplaceReason(
                    source,
                    replacementInput!),
            NonWorkingDayCorrectionMode.Cancel =>
                NonWorkingDayCorrectionConfirmationMaterial.ForCancel(source),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
        var confirmation = tokenService.Issue(material);
        await transaction.RollbackAsync();
        return confirmation;
    }

    private static CorrectNonWorkingDayPreparation CreateCommandPreparation(
        SourceFixture fixture,
        NonWorkingDayCorrectionMode mode,
        string confirmationToken,
        ActorContext? actor = null,
        string? replacementReasonCode = null,
        Guid? periodId = null)
    {
        var command = new CorrectNonWorkingDayCommand(
            new CommandEnvelope(
                actor ?? OwnerActor(fixture),
                new RequestCorrelationId("correct-non-working-day-revalidation"),
                EntryOrigin.Normal,
                TestNow,
                "correct-non-working-day-revalidation-key",
                "Owner corrected the closure schedule",
                "Confirmed against the current correction preview"),
            periodId ?? fixture.PeriodId,
            mode,
            mode == NonWorkingDayCorrectionMode.ReplaceRange
                ? ReplacementPeriod.StartDate
                : null,
            mode == NonWorkingDayCorrectionMode.ReplaceRange
                ? ReplacementPeriod.EndDate
                : null,
            mode switch
            {
                NonWorkingDayCorrectionMode.ReplaceRange =>
                    replacementReasonCode ?? "maintenance",
                NonWorkingDayCorrectionMode.ReplaceReason =>
                    replacementReasonCode ?? "corrected_weather",
                _ => null,
            },
            mode switch
            {
                NonWorkingDayCorrectionMode.ReplaceRange => "Boiler replacement",
                NonWorkingDayCorrectionMode.ReplaceReason => "Corrected explanation",
                _ => null,
            },
            confirmationToken);
        var result = CorrectNonWorkingDayPreparationPolicy.Prepare(command);

        Assert.True(result.IsPrepared);
        Assert.Empty(result.Errors);
        return Assert.IsType<CorrectNonWorkingDayPreparation>(result.Preparation);
    }

    private static async Task<CorrectNonWorkingDayCommandRevalidationResult>
        ExecuteRevalidationAsync(
            BodyLifeDbContext dbContext,
            INonWorkingDayCorrectionTokenService tokenService,
            DateTimeOffset now,
            CorrectNonWorkingDayPreparation commandPreparation)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead);
        var result = await CreateCommandRevalidationPreparer(
                dbContext,
                tokenService,
                now)
            .PrepareAsync(commandPreparation);
        await transaction.RollbackAsync();
        return result;
    }

    private static ActorContext OwnerActor(SourceFixture fixture)
    {
        return new ActorContext(
            new AccountId(fixture.AccountId),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(fixture.SessionId),
            "Owner laptop");
    }

    private static void AssertRejected(
        CorrectNonWorkingDayCommandRevalidationResult result,
        CommandErrorCode code,
        string? field)
    {
        Assert.False(result.IsPrepared);
        Assert.Null(result.CommandPreparation);
        Assert.Null(result.ConfirmationMaterial);
        Assert.Null(result.ReplacementImpact);
        Assert.Null(result.TokenValidation);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(field, error.Field);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private static async Task<CorrectionWriteCounts> ReadWriteCountsAsync(
        PostgreSqlTestDatabase database)
    {
        return new CorrectionWriteCounts(
            await CountRowsAsync(database, "non_working_periods"),
            await CountRowsAsync(database, "non_working_period_applications"),
            await CountRowsAsync(database, "non_working_period_cancellations"),
            await CountRowsAsync(database, "membership_state_cache"),
            await CountRowsAsync(database, "membership_extension_days"),
            await CountRowsAsync(database, "business_audit_entries"),
            await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static string SigningKey()
    {
        return Convert.ToBase64String(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
    }

    private static async Task SetStatusesAsync(
        PostgreSqlTestDatabase database,
        Guid periodId,
        string status)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.non_working_periods
            set status = @status
            where id = @period_id;

            update bodylife.non_working_period_applications
            set status = @status
            where non_working_period_id = @period_id;
            """;
        command.Parameters.AddWithValue("period_id", periodId);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(3, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertRowLockUnavailableAsync(
        PostgreSqlTestDatabase database,
        string tableName,
        Guid id)
    {
        var commandText = tableName switch
        {
            "issued_memberships" =>
                "select id from bodylife.issued_memberships where id = @id for update",
            "non_working_periods" =>
                "select id from bodylife.non_working_periods where id = @id for update",
            "non_working_period_applications" =>
                "select id from bodylife.non_working_period_applications "
                + "where id = @id for update",
            "non_working_period_cancellations" =>
                "select id from bodylife.non_working_period_cancellations "
                + "where id = @id for update",
            _ => throw new ArgumentOutOfRangeException(nameof(tableName)),
        };
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "set local lock_timeout = '250ms'; " + commandText;
        command.Parameters.AddWithValue("id", id);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
        await transaction.RollbackAsync();
    }

    private static async Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return await database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{tableName}");
    }

    private sealed record SourceFixture(
        Guid AccountId,
        Guid SessionId,
        Guid PeriodId,
        Guid? CancellationId);

    private sealed record CorrectionWriteCounts(
        long Periods,
        long Applications,
        long Cancellations,
        long StateCacheRows,
        long ExtensionRows,
        long AuditEntries,
        long IdempotencyRows);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
