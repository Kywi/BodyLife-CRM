using System.Data;
using BodyLife.Crm.Application.Queries;
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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlNonWorkingDayAffectedScopePreparerTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        17,
        12,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateRange ProposedPeriod = new(
        new DateOnly(2026, 1, 30),
        new DateOnly(2026, 2, 2));
    private static readonly DateRange ReplacementPeriod = new(
        new DateOnly(2026, 2, 3),
        new DateOnly(2026, 2, 4));

    [Fact]
    public void ServicesRegisterAffectedScopeImpactAndPreviewQuery()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] =
                    BodyLifeDbContextOptions.LocalDevelopmentConnectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddBodyLifePersistence(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var concrete = Assert.IsType<MembershipNonWorkingDayAffectedScopePreparer>(
            scope.ServiceProvider.GetRequiredService<
                MembershipNonWorkingDayAffectedScopePreparer>());
        Assert.Same(
            concrete,
            scope.ServiceProvider.GetRequiredService<
                IMembershipNonWorkingDayAffectedScopePreparer>());
        Assert.Same(
            concrete,
            scope.ServiceProvider.GetRequiredService<
                IMembershipNonWorkingDayImpactPreparer>());
        var nonWorkingDaySourceReader = scope.ServiceProvider.GetRequiredService<
            MembershipNonWorkingDayExtensionSourceReader>();
        Assert.Same(
            nonWorkingDaySourceReader,
            scope.ServiceProvider.GetRequiredService<
                IMembershipNonWorkingDayApplicationSourceProvider>());
        var replacementPreparer = Assert.IsType<
            MembershipNonWorkingDayReplacementImpactPreparer>(
                scope.ServiceProvider.GetRequiredService<
                    MembershipNonWorkingDayReplacementImpactPreparer>());
        Assert.Same(
            replacementPreparer,
            scope.ServiceProvider.GetRequiredService<
                IMembershipNonWorkingDayReplacementImpactPreparer>());

        var queryServiceType = typeof(IBodyLifeQueryHandler<
            PreviewNonWorkingDayImpactQuery,
            PreviewNonWorkingDayImpactResult>);
        var queryDescriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == queryServiceType);
        Assert.Equal(ServiceLifetime.Scoped, queryDescriptor.Lifetime);
        Assert.Equal(
            typeof(PreviewNonWorkingDayImpactQueryHandler),
            queryDescriptor.ImplementationType);

        var correctionQueryServiceType = typeof(IBodyLifeQueryHandler<
            PreviewCorrectNonWorkingDayQuery,
            PreviewCorrectNonWorkingDayResult>);
        var correctionQueryDescriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == correctionQueryServiceType);
        Assert.Equal(ServiceLifetime.Scoped, correctionQueryDescriptor.Lifetime);
        Assert.Equal(
            typeof(PreviewCorrectNonWorkingDayQueryHandler),
            correctionQueryDescriptor.ImplementationType);
    }

    [PostgreSqlFact]
    public async Task PreparationRequiresCallerOwnedConsistentTransaction()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var preparer = CreatePreparer(dbContext);

        var missingTransaction = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preparer.PrepareImpactAsync(ProposedPeriod));

        Assert.Contains(
            "caller-owned",
            missingTransaction.Message,
            StringComparison.OrdinalIgnoreCase);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted);
        var weakIsolation = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preparer.PrepareImpactAsync(ProposedPeriod));

        Assert.Contains(
            "RepeatableRead",
            weakIsolation.Message,
            StringComparison.Ordinal);
        await transaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task ReplacementPreparationRequiresConsistentTransactionAndValidInput()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var preparer = CreateReplacementPreparer(dbContext);
        var periodId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            preparer.PrepareReplacementImpactAsync(
                Guid.Empty,
                ReplacementPeriod));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            preparer.PrepareReplacementImpactAsync(
                periodId,
                new DateRange(default, default)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preparer.PrepareReplacementImpactAsync(
                periodId,
                ReplacementPeriod));

        await using (var readCommitted = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            var weakIsolation = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                preparer.PrepareReplacementImpactAsync(
                    periodId,
                    ReplacementPeriod));
            Assert.Contains("RepeatableRead", weakIsolation.Message, StringComparison.Ordinal);
            await readCommitted.RollbackAsync();
        }

        await using var repeatableRead = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var missingSource = await preparer.PrepareReplacementImpactAsync(
            periodId,
            ReplacementPeriod);

        Assert.Equal(periodId, missingSource.ReplacedPeriodId);
        Assert.Equal(ReplacementPeriod, missingSource.ReplacementPeriod);
        Assert.Empty(missingSource.ExcludedApplicationIds);
        Assert.Equal(0, missingSource.AffectedCount);
        await repeatableRead.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task ReplacementPreparationExcludesOnlyOldPeriodAndLocksAllCandidates()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedScopeFixtureAsync(
            database,
            dbContext,
            includeReplacedPeriod: true);
        var before = await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead);
        var preparation = await CreateReplacementPreparer(dbContext)
            .PrepareReplacementImpactAsync(
                fixture.ReplacedPeriodId,
                ReplacementPeriod);

        Assert.Equal(fixture.ReplacedPeriodId, preparation.ReplacedPeriodId);
        Assert.Equal(ReplacementPeriod, preparation.ReplacementPeriod);
        Assert.Equal(
            [
                fixture.StartBoundaryReplacedApplicationId,
                fixture.ExtendedReplacedApplicationId,
                fixture.EndBoundaryReplacedApplicationId,
            ],
            preparation.ExcludedApplicationIds);
        Assert.Equal(
            [
                fixture.StartBoundaryMembershipId,
                fixture.NoOverlapMembershipId,
                fixture.ExtendedMembershipId,
            ],
            preparation.AffectedMemberships.Select(item => item.MembershipId));
        Assert.DoesNotContain(
            preparation.AffectedMemberships,
            item => item.MembershipId == fixture.EndBoundaryMembershipId);
        Assert.All(
            preparation.AffectedMemberships,
            item => Assert.Equal(ReplacementPeriod, item.AppliedRange));

        var startBoundary = preparation.AffectedMemberships[0];
        Assert.Equal(0, startBoundary.Estimate.BeforeExtensionDays);
        Assert.Equal(
            new DateOnly(2026, 2, 11),
            startBoundary.Estimate.BeforeEffectiveEndDate);
        Assert.Equal(2, startBoundary.Estimate.EstimatedAfterExtensionDays);
        Assert.Equal(
            new DateOnly(2026, 2, 13),
            startBoundary.Estimate.EstimatedAfterEffectiveEndDate);

        var newlyAffected = preparation.AffectedMemberships[1];
        Assert.Equal(fixture.NoOverlapClientId, newlyAffected.ClientId);
        Assert.Equal(0, newlyAffected.Estimate.BeforeExtensionDays);
        Assert.Equal(2, newlyAffected.Estimate.AddedUniqueExtensionDays);

        var extended = preparation.AffectedMemberships[2];
        Assert.Equal(7, extended.Estimate.BeforeExtensionDays);
        Assert.Equal(
            new DateOnly(2026, 2, 5),
            extended.Estimate.BeforeEffectiveEndDate);
        Assert.Equal(9, extended.Estimate.EstimatedAfterExtensionDays);
        Assert.Equal(
            new DateOnly(2026, 2, 7),
            extended.Estimate.EstimatedAfterEffectiveEndDate);
        Assert.Equal(2, extended.Estimate.AddedUniqueExtensionDays);
        Assert.Equal(0, extended.Estimate.ExistingOverlapDays);
        Assert.Empty(extended.Estimate.OverlapWarnings);

        var oldSource = await new CorrectNonWorkingDaySourcePreparer(dbContext)
            .PrepareAsync(
                fixture.ReplacedPeriodId,
                NonWorkingDayCorrectionMode.ReplaceRange);
        Assert.True(oldSource.IsPrepared);
        Assert.Equal(
            preparation.ExcludedApplicationIds,
            oldSource.Source!.Applications
                .Select(application => application.ApplicationId)
                .Order());

        Assert.Equal(
            before,
            await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId));
        Assert.False(dbContext.ChangeTracker.HasChanges());
        Assert.Empty(dbContext.ChangeTracker.Entries());
        Assert.Equal(
            PostgresErrorCodes.LockNotAvailable,
            (await AssertMembershipUpdateBlockedAsync(
                database.ConnectionString,
                fixture.EndBoundaryMembershipId)).SqlState);
        Assert.Equal(
            PostgresErrorCodes.LockNotAvailable,
            (await AssertMembershipUpdateBlockedAsync(
                database.ConnectionString,
                fixture.NoOverlapMembershipId)).SqlState);
        Assert.Equal(
            1,
            await UpdateMembershipCommentAsync(
                database.ConnectionString,
                fixture.CanceledMembershipId,
                "Canceled Membership remains unlocked"));

        await transaction.RollbackAsync();
        Assert.Equal(
            1,
            await UpdateMembershipCommentAsync(
                database.ConnectionString,
                fixture.EndBoundaryMembershipId,
                "Active Membership lock released"));
        Assert.Equal(
            before,
            await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId));
    }

    [PostgreSqlFact]
    public async Task PreparationReturnsExactCanonicalScopeWithoutWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedScopeFixtureAsync(database, dbContext);
        var before = await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead);
        var impact = await CreatePreparer(dbContext).PrepareImpactAsync(ProposedPeriod);
        var result = impact.AffectedScope;

        Assert.Equal(ProposedPeriod, result.Period);
        Assert.Equal(3, result.AffectedCount);
        Assert.Equal(
            [
                fixture.EndBoundaryMembershipId,
                fixture.StartBoundaryMembershipId,
                fixture.ExtendedMembershipId,
            ],
            result.AffectedMemberships.Select(item => item.MembershipId));
        Assert.Equal(
            [
                fixture.EndBoundaryClientId,
                fixture.StartBoundaryClientId,
                fixture.ExtendedClientId,
            ],
            result.AffectedMemberships.Select(item => item.ClientId));
        Assert.All(
            result.AffectedMemberships,
            item =>
            {
                Assert.Equal(ProposedPeriod, item.AppliedRange);
                Assert.Equal(4, item.AppliedRange.InclusiveDays);
            });
        Assert.DoesNotContain(
            result.AffectedMemberships,
            item => item.MembershipId == fixture.NoOverlapMembershipId);
        Assert.DoesNotContain(
            result.AffectedMemberships,
            item => item.MembershipId == fixture.CanceledMembershipId);
        Assert.Equal(result.AffectedCount, impact.AffectedCount);
        Assert.Equal(
            result.AffectedMemberships.Select(item => item.MembershipId),
            impact.AffectedMemberships.Select(item => item.MembershipId));

        var extendedImpact = Assert.Single(
            impact.AffectedMemberships,
            item => item.MembershipId == fixture.ExtendedMembershipId);
        Assert.Equal(7, extendedImpact.Estimate.BeforeExtensionDays);
        Assert.Equal(new DateOnly(2026, 2, 5), extendedImpact.Estimate.BeforeEffectiveEndDate);
        Assert.Equal(9, extendedImpact.Estimate.EstimatedAfterExtensionDays);
        Assert.Equal(
            new DateOnly(2026, 2, 7),
            extendedImpact.Estimate.EstimatedAfterEffectiveEndDate);
        Assert.Equal(2, extendedImpact.Estimate.AddedUniqueExtensionDays);
        Assert.Equal(2, extendedImpact.Estimate.ExistingOverlapDays);
        var overlap = Assert.Single(extendedImpact.Estimate.OverlapWarnings);
        Assert.Equal("freeze", overlap.SourceType);
        Assert.Equal(fixture.FreezeId, overlap.SourceId);
        Assert.Equal(
            new DateRange(
                new DateOnly(2026, 1, 30),
                new DateOnly(2026, 1, 31)),
            overlap.OverlapRange);
        Assert.Equal(2, overlap.OverlapDays);

        var during = await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId);
        Assert.Equal(before, during);
        Assert.False(dbContext.ChangeTracker.HasChanges());
        Assert.Empty(dbContext.ChangeTracker.Entries());

        var eligibleLock = await AssertMembershipUpdateBlockedAsync(
            database.ConnectionString,
            fixture.EndBoundaryMembershipId);
        var ineligibleCandidateLock = await AssertMembershipUpdateBlockedAsync(
            database.ConnectionString,
            fixture.NoOverlapMembershipId);
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, eligibleLock.SqlState);
        Assert.Equal(
            PostgresErrorCodes.LockNotAvailable,
            ineligibleCandidateLock.SqlState);
        Assert.Equal(
            1,
            await UpdateMembershipCommentAsync(
                database.ConnectionString,
                fixture.CanceledMembershipId,
                "Canceled Membership remains unlocked"));

        await transaction.RollbackAsync();
        Assert.Equal(
            1,
            await UpdateMembershipCommentAsync(
                database.ConnectionString,
                fixture.EndBoundaryMembershipId,
                "Active Membership lock released"));

        var after = await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId);
        Assert.Equal(before, after);
    }

    [PostgreSqlFact]
    public async Task OwnerPreviewReturnsExactImpactAndBoundTokenWithoutWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedScopeFixtureAsync(database, dbContext);
        var before = await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId);
        var tokenService = CreatePreviewTokenService();
        var handler = new PreviewNonWorkingDayImpactQueryHandler(
            dbContext,
            CreatePreparer(dbContext),
            tokenService,
            new FixedTimeProvider(TestNow));
        var actor = OwnerActor(fixture);

        var result = await handler.ExecuteAsync(
            new PreviewNonWorkingDayImpactQuery(
                actor,
                ProposedPeriod.StartDate,
                ProposedPeriod.EndDate,
                "  weather_closure  ",
                "  Severe weather  "),
            CancellationToken.None);

        Assert.Equal(PreviewNonWorkingDayImpactStatus.Success, result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
        var preview = Assert.IsType<NonWorkingDayImpactPreview>(result.Preview);
        Assert.Equal(ProposedPeriod, preview.Period);
        Assert.Equal("weather_closure", preview.ReasonCode);
        Assert.Equal("Severe weather", preview.ReasonComment);
        Assert.Equal(3, preview.AffectedCount);
        Assert.Equal(1, preview.OverlapWarningCount);
        Assert.True(preview.HasOverlapWarnings);
        Assert.Equal(
            [
                fixture.EndBoundaryMembershipId,
                fixture.StartBoundaryMembershipId,
                fixture.ExtendedMembershipId,
            ],
            preview.AffectedMemberships.Select(item => item.MembershipId));
        Assert.All(
            preview.AffectedMemberships,
            item => Assert.Equal(ProposedPeriod, item.AppliedRange));

        var endBoundary = preview.AffectedMemberships[0];
        Assert.Equal("Scope End boundary", endBoundary.ClientDisplayName);
        Assert.Equal(0, endBoundary.BeforeExtensionDays);
        Assert.Equal(new DateOnly(2026, 1, 31), endBoundary.BeforeEffectiveEndDate);
        Assert.Equal(4, endBoundary.EstimatedAfterExtensionDays);
        Assert.Equal(new DateOnly(2026, 2, 4), endBoundary.EstimatedAfterEffectiveEndDate);
        Assert.Equal(4, endBoundary.AddedUniqueExtensionDays);
        Assert.Empty(endBoundary.OverlapWarnings);

        var extended = preview.AffectedMemberships[2];
        Assert.Equal(fixture.ExtendedClientId, extended.ClientId);
        Assert.Equal("Scope Accepted extension", extended.ClientDisplayName);
        Assert.Equal(7, extended.BeforeExtensionDays);
        Assert.Equal(9, extended.EstimatedAfterExtensionDays);
        Assert.Equal(2, extended.AddedUniqueExtensionDays);
        Assert.Equal(2, extended.ExistingOverlapDays);
        var warning = Assert.Single(extended.OverlapWarnings);
        Assert.Equal("freeze", warning.SourceType);
        Assert.Equal(fixture.FreezeId, warning.SourceId);
        Assert.Equal(
            "Freeze 2026-01-25..2026-01-31: Accepted extension source",
            warning.SourceLabel);
        Assert.Equal(2, warning.OverlapDays);

        Assert.Equal(TestNow, preview.Confirmation.IssuedAt);
        Assert.Equal(TestNow.AddMinutes(5), preview.Confirmation.ExpiresAt);
        var tokenScope = new MembershipNonWorkingDayAffectedScope(
            preview.Period,
            preview.AffectedMemberships.Select(item =>
                new MembershipNonWorkingDayAffectedScopeItem(
                    item.MembershipId,
                    item.ClientId,
                    item.AppliedRange)));
        var tokenInput = new NonWorkingDayPreviewInput(
            preview.Period,
            preview.ReasonCode,
            preview.ReasonComment);
        Assert.Equal(
            NonWorkingDayPreviewTokenValidationStatus.Valid,
            tokenService.Validate(
                preview.Confirmation.ConfirmationToken,
                tokenInput,
                tokenScope).Status);

        Assert.Equal(
            before,
            await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId));
        Assert.False(dbContext.ChangeTracker.HasChanges());
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task OwnerRangeCorrectionPreviewReturnsExactOldAndNewScopesWithoutWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedScopeFixtureAsync(
            database,
            dbContext,
            includeReplacedPeriod: true);
        var before = await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId);
        var tokenService = CreateCorrectionTokenService();
        var handler = CreateCorrectionPreviewHandler(dbContext, tokenService);

        var result = await handler.ExecuteAsync(
            new PreviewCorrectNonWorkingDayQuery(
                OwnerActor(fixture),
                fixture.ReplacedPeriodId,
                NonWorkingDayCorrectionMode.ReplaceRange,
                ReplacementPeriod.StartDate,
                ReplacementPeriod.EndDate,
                "  maintenance  ",
                "  Boiler replacement  "),
            CancellationToken.None);

        Assert.Equal(PreviewCorrectNonWorkingDayStatus.Success, result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
        var preview = Assert.IsType<NonWorkingDayCorrectionPreview>(result.Preview);
        Assert.Equal(fixture.ReplacedPeriodId, preview.PeriodId);
        Assert.Equal(NonWorkingDayCorrectionMode.ReplaceRange, preview.Mode);
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.RecomputeReplacement,
            preview.ScopeBehavior);
        Assert.Equal(ProposedPeriod, preview.OriginalSource.Period);
        Assert.Equal(3, preview.OriginalAffectedCount);
        Assert.Equal(
            [
                fixture.EndBoundaryMembershipId,
                fixture.StartBoundaryMembershipId,
                fixture.ExtendedMembershipId,
            ],
            preview.OriginalSource.Applications
                .Select(application => application.MembershipId));
        Assert.Equal(
            [
                fixture.StartBoundaryReplacedApplicationId,
                fixture.ExtendedReplacedApplicationId,
                fixture.EndBoundaryReplacedApplicationId,
            ],
            preview.Material.OriginalApplicationIds);
        Assert.Equal(ReplacementPeriod, preview.ReplacementInput!.Period);
        Assert.Equal("maintenance", preview.ReplacementInput.ReasonCode);
        Assert.Equal("Boiler replacement", preview.ReplacementInput.ReasonComment);
        Assert.Equal(3, preview.ConfirmedAffectedCount);
        Assert.Equal(
            [
                fixture.StartBoundaryMembershipId,
                fixture.NoOverlapMembershipId,
                fixture.ExtendedMembershipId,
            ],
            preview.ConfirmedScope!.AffectedMemberships
                .Select(item => item.MembershipId));
        Assert.Equal(
            preview.ConfirmedScope.AffectedMemberships
                .Select(item => item.MembershipId),
            preview.ReplacementImpact.Select(item => item.MembershipId));
        Assert.All(
            preview.ReplacementImpact,
            item => Assert.Equal(ReplacementPeriod, item.AppliedRange));
        Assert.Equal(2, preview.ReplacementImpact[0].AddedUniqueExtensionDays);
        Assert.Equal(2, preview.ReplacementImpact[1].AddedUniqueExtensionDays);
        Assert.Equal(2, preview.ReplacementImpact[2].AddedUniqueExtensionDays);
        Assert.Equal(TestNow, preview.Confirmation.IssuedAt);
        Assert.Equal(TestNow.AddMinutes(5), preview.Confirmation.ExpiresAt);
        Assert.Equal(
            NonWorkingDayCorrectionTokenValidationStatus.Valid,
            tokenService.Validate(
                preview.Confirmation.ConfirmationToken,
                preview.Material).Status);

        Assert.Equal(
            before,
            await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId));
        Assert.False(dbContext.ChangeTracker.HasChanges());
        Assert.Empty(dbContext.ChangeTracker.Entries());
        Assert.Null(dbContext.Database.CurrentTransaction);
    }

    [PostgreSqlFact]
    public async Task ReasonAndCancelCorrectionPreviewsPreserveModeSpecificScopeWithoutWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedScopeFixtureAsync(
            database,
            dbContext,
            includeReplacedPeriod: true);
        var before = await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId);
        var tokenService = CreateCorrectionTokenService();
        var handler = CreateCorrectionPreviewHandler(dbContext, tokenService);

        var reasonResult = await handler.ExecuteAsync(
            new PreviewCorrectNonWorkingDayQuery(
                OwnerActor(fixture),
                fixture.ReplacedPeriodId,
                NonWorkingDayCorrectionMode.ReplaceReason,
                ReplacementReasonCode: "  corrected_weather  ",
                ReplacementReasonComment: "  Corrected explanation  "),
            CancellationToken.None);

        Assert.Equal(PreviewCorrectNonWorkingDayStatus.Success, reasonResult.Status);
        var reasonPreview = Assert.IsType<NonWorkingDayCorrectionPreview>(
            reasonResult.Preview);
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.PreserveConfirmedApplications,
            reasonPreview.ScopeBehavior);
        Assert.Equal(ProposedPeriod, reasonPreview.ReplacementInput!.Period);
        Assert.Equal("corrected_weather", reasonPreview.ReplacementInput.ReasonCode);
        Assert.Equal(
            "Corrected explanation",
            reasonPreview.ReplacementInput.ReasonComment);
        Assert.Equal(3, reasonPreview.OriginalAffectedCount);
        Assert.Equal(3, reasonPreview.ConfirmedAffectedCount);
        Assert.Same(reasonPreview.Material.PreservedScope, reasonPreview.ConfirmedScope);
        Assert.Equal(
            reasonPreview.OriginalSource.Applications
                .Select(application => application.MembershipId),
            reasonPreview.ConfirmedScope!.AffectedMemberships
                .Select(item => item.MembershipId));
        Assert.Empty(reasonPreview.ReplacementImpact);
        Assert.Equal(
            NonWorkingDayCorrectionTokenValidationStatus.Valid,
            tokenService.Validate(
                reasonPreview.Confirmation.ConfirmationToken,
                reasonPreview.Material).Status);

        var cancelResult = await handler.ExecuteAsync(
            new PreviewCorrectNonWorkingDayQuery(
                OwnerActor(fixture),
                fixture.ReplacedPeriodId,
                NonWorkingDayCorrectionMode.Cancel),
            CancellationToken.None);

        Assert.Equal(PreviewCorrectNonWorkingDayStatus.Success, cancelResult.Status);
        var cancelPreview = Assert.IsType<NonWorkingDayCorrectionPreview>(
            cancelResult.Preview);
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.NoReplacement,
            cancelPreview.ScopeBehavior);
        Assert.Equal(3, cancelPreview.OriginalAffectedCount);
        Assert.Equal(0, cancelPreview.ConfirmedAffectedCount);
        Assert.Null(cancelPreview.ReplacementInput);
        Assert.Null(cancelPreview.ConfirmedScope);
        Assert.Empty(cancelPreview.ReplacementImpact);
        Assert.Equal(
            NonWorkingDayCorrectionTokenValidationStatus.Valid,
            tokenService.Validate(
                cancelPreview.Confirmation.ConfirmationToken,
                cancelPreview.Material).Status);

        Assert.Equal(
            before,
            await ReadSnapshotAsync(dbContext, fixture.ExtendedMembershipId));
        Assert.False(dbContext.ChangeTracker.HasChanges());
        Assert.Empty(dbContext.ChangeTracker.Entries());
        Assert.Null(dbContext.Database.CurrentTransaction);
    }

    [PostgreSqlFact]
    public async Task CorrectionPreviewRequiresCanonicalOwnerValidModeShapeAndSource()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedScopeFixtureAsync(
            database,
            dbContext,
            includeReplacedPeriod: true);
        var handler = CreateCorrectionPreviewHandler(
            dbContext,
            CreateCorrectionTokenService());
        var owner = OwnerActor(fixture);
        var adminShape = owner with
        {
            Role = ActorRole.Admin,
            AccountKind = AccountKind.NamedAdmin,
        };
        var unknownOwner = new ActorContext(
            AccountId.New(),
            ActorRole.Owner,
            AccountKind.Owner,
            SessionId.New(),
            "Unknown device");

        foreach (var deniedActor in new[] { adminShape, unknownOwner })
        {
            var denied = await handler.ExecuteAsync(
                new PreviewCorrectNonWorkingDayQuery(
                    deniedActor,
                    fixture.ReplacedPeriodId,
                    NonWorkingDayCorrectionMode.Cancel),
                CancellationToken.None);

            Assert.Equal(
                PreviewCorrectNonWorkingDayStatus.PermissionDenied,
                denied.Status);
            Assert.Equal("permission_denied", denied.ErrorCode);
            Assert.Null(denied.Preview);
        }

        var invalidQueries = new[]
        {
            (
                new PreviewCorrectNonWorkingDayQuery(
                    owner,
                    Guid.Empty,
                    NonWorkingDayCorrectionMode.Cancel),
                "periodId"),
            (
                new PreviewCorrectNonWorkingDayQuery(
                    owner,
                    fixture.ReplacedPeriodId,
                    (NonWorkingDayCorrectionMode)99),
                "mode"),
            (
                new PreviewCorrectNonWorkingDayQuery(
                    owner,
                    fixture.ReplacedPeriodId,
                    NonWorkingDayCorrectionMode.ReplaceRange,
                    ReplacementEndDate: ReplacementPeriod.EndDate,
                    ReplacementReasonCode: "maintenance"),
                "replacementStartDate"),
            (
                new PreviewCorrectNonWorkingDayQuery(
                    owner,
                    fixture.ReplacedPeriodId,
                    NonWorkingDayCorrectionMode.ReplaceRange,
                    ReplacementStartDate: ReplacementPeriod.StartDate,
                    ReplacementReasonCode: "maintenance"),
                "replacementEndDate"),
            (
                new PreviewCorrectNonWorkingDayQuery(
                    owner,
                    fixture.ReplacedPeriodId,
                    NonWorkingDayCorrectionMode.ReplaceRange,
                    ReplacementPeriod.EndDate,
                    ReplacementPeriod.StartDate,
                    "maintenance"),
                "replacementEndDate"),
            (
                new PreviewCorrectNonWorkingDayQuery(
                    owner,
                    fixture.ReplacedPeriodId,
                    NonWorkingDayCorrectionMode.ReplaceRange,
                    ReplacementPeriod.StartDate,
                    ReplacementPeriod.EndDate),
                "replacementReasonCode"),
            (
                new PreviewCorrectNonWorkingDayQuery(
                    owner,
                    fixture.ReplacedPeriodId,
                    NonWorkingDayCorrectionMode.ReplaceReason,
                    ReplacementStartDate: ReplacementPeriod.StartDate,
                    ReplacementReasonCode: "corrected_weather"),
                "replacementStartDate"),
            (
                new PreviewCorrectNonWorkingDayQuery(
                    owner,
                    fixture.ReplacedPeriodId,
                    NonWorkingDayCorrectionMode.Cancel,
                    ReplacementReasonCode: "not_applicable"),
                "replacementReasonCode"),
        };

        foreach (var (query, errorField) in invalidQueries)
        {
            var invalid = await handler.ExecuteAsync(query, CancellationToken.None);

            Assert.Equal(
                PreviewCorrectNonWorkingDayStatus.ValidationFailed,
                invalid.Status);
            Assert.Equal("validation_failed", invalid.ErrorCode);
            Assert.Equal(errorField, invalid.ErrorField);
            Assert.Null(invalid.Preview);
        }

        var missing = await handler.ExecuteAsync(
            new PreviewCorrectNonWorkingDayQuery(
                owner,
                Guid.NewGuid(),
                NonWorkingDayCorrectionMode.ReplaceReason,
                ReplacementReasonCode: "corrected_weather"),
            CancellationToken.None);

        Assert.Equal(PreviewCorrectNonWorkingDayStatus.NotFound, missing.Status);
        Assert.Equal("not_found", missing.ErrorCode);
        Assert.Null(missing.Preview);
        Assert.Null(dbContext.Database.CurrentTransaction);
    }

    [PostgreSqlFact]
    public async Task PreviewRequiresCanonicalOwnerAndValidInput()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedScopeFixtureAsync(database, dbContext);
        var handler = new PreviewNonWorkingDayImpactQueryHandler(
            dbContext,
            CreatePreparer(dbContext),
            CreatePreviewTokenService(),
            new FixedTimeProvider(TestNow));
        var owner = OwnerActor(fixture);
        var adminShape = owner with
        {
            Role = ActorRole.Admin,
            AccountKind = AccountKind.NamedAdmin,
        };
        var unknownOwner = new ActorContext(
            AccountId.New(),
            ActorRole.Owner,
            AccountKind.Owner,
            SessionId.New(),
            "Unknown device");

        foreach (var deniedActor in new[] { adminShape, unknownOwner })
        {
            var denied = await handler.ExecuteAsync(
                Query(deniedActor, ProposedPeriod.StartDate, ProposedPeriod.EndDate, "weather"),
                CancellationToken.None);

            Assert.Equal(PreviewNonWorkingDayImpactStatus.PermissionDenied, denied.Status);
            Assert.Equal("permission_denied", denied.ErrorCode);
            Assert.Null(denied.Preview);
        }

        var invalidQueries = new[]
        {
            (
                Query(owner, default, ProposedPeriod.EndDate, "weather"),
                "proposedStartDate"),
            (
                Query(owner, ProposedPeriod.StartDate, default, "weather"),
                "proposedEndDate"),
            (
                Query(owner, ProposedPeriod.EndDate, ProposedPeriod.StartDate, "weather"),
                "proposedEndDate"),
            (
                Query(owner, ProposedPeriod.StartDate, ProposedPeriod.EndDate, "  "),
                "reasonCode"),
            (
                Query(
                    owner,
                    ProposedPeriod.StartDate,
                    ProposedPeriod.EndDate,
                    "weather",
                    new string('c', NonWorkingDayPreviewInput.ReasonCommentMaxLength + 1)),
                "reasonComment"),
        };

        foreach (var (query, errorField) in invalidQueries)
        {
            var invalid = await handler.ExecuteAsync(query, CancellationToken.None);

            Assert.Equal(PreviewNonWorkingDayImpactStatus.ValidationFailed, invalid.Status);
            Assert.Equal("validation_failed", invalid.ErrorCode);
            Assert.Equal(errorField, invalid.ErrorField);
            Assert.Null(invalid.Preview);
        }

        var failingHandler = new PreviewNonWorkingDayImpactQueryHandler(
            dbContext,
            new ThrowingImpactPreparer(),
            CreatePreviewTokenService(),
            new FixedTimeProvider(TestNow));
        var failed = await failingHandler.ExecuteAsync(
            Query(
                owner,
                ProposedPeriod.StartDate,
                ProposedPeriod.EndDate,
                "weather"),
            CancellationToken.None);

        Assert.Equal(PreviewNonWorkingDayImpactStatus.RecalculationFailed, failed.Status);
        Assert.Equal("recalculation_failed", failed.ErrorCode);
        Assert.Null(failed.Preview);
        Assert.Null(dbContext.Database.CurrentTransaction);
    }

    private static MembershipNonWorkingDayAffectedScopePreparer CreatePreparer(
        BodyLifeDbContext dbContext)
    {
        return new MembershipNonWorkingDayAffectedScopePreparer(
            dbContext,
            new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(TestNow),
                [
                    new MembershipFreezeExtensionSourceReader(dbContext),
                    new MembershipNonWorkingDayExtensionSourceReader(dbContext),
                ]));
    }

    private static PreviewCorrectNonWorkingDayQueryHandler
        CreateCorrectionPreviewHandler(
            BodyLifeDbContext dbContext,
            INonWorkingDayCorrectionTokenService tokenService)
    {
        return new PreviewCorrectNonWorkingDayQueryHandler(
            dbContext,
            CreateReplacementPreparer(dbContext),
            new CorrectNonWorkingDaySourcePreparer(dbContext),
            tokenService,
            new FixedTimeProvider(TestNow));
    }

    private static MembershipNonWorkingDayReplacementImpactPreparer
        CreateReplacementPreparer(BodyLifeDbContext dbContext)
    {
        var nonWorkingDaySourceReader =
            new MembershipNonWorkingDayExtensionSourceReader(dbContext);
        var affectedScopePreparer = new MembershipNonWorkingDayAffectedScopePreparer(
            dbContext,
            new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(TestNow),
                [
                    new MembershipFreezeExtensionSourceReader(dbContext),
                    nonWorkingDaySourceReader,
                ]));
        return new MembershipNonWorkingDayReplacementImpactPreparer(
            affectedScopePreparer,
            nonWorkingDaySourceReader);
    }

    private static HmacNonWorkingDayPreviewTokenService CreatePreviewTokenService()
    {
        var signingKey = Convert.ToBase64String(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        return new HmacNonWorkingDayPreviewTokenService(
            new NonWorkingDayPreviewTokenOptions(
                signingKey,
                TimeSpan.FromMinutes(5)),
            new FixedTimeProvider(TestNow));
    }

    private static HmacNonWorkingDayCorrectionTokenService
        CreateCorrectionTokenService()
    {
        var signingKey = Convert.ToBase64String(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        return new HmacNonWorkingDayCorrectionTokenService(
            new NonWorkingDayPreviewTokenOptions(
                signingKey,
                TimeSpan.FromMinutes(5)),
            new FixedTimeProvider(TestNow));
    }

    private static ActorContext OwnerActor(ScopeFixture fixture)
    {
        return new ActorContext(
            new AccountId(fixture.ActorAccountId),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(fixture.SessionId),
            "Owner laptop");
    }

    private static PreviewNonWorkingDayImpactQuery Query(
        ActorContext actor,
        DateOnly startDate,
        DateOnly endDate,
        string? reasonCode,
        string? reasonComment = null)
    {
        return new PreviewNonWorkingDayImpactQuery(
            actor,
            startDate,
            endDate,
            reasonCode,
            reasonComment);
    }

    private static async Task<ScopeFixture> SeedScopeFixtureAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext,
        bool includeReplacedPeriod = false)
    {
        var bootstrap = await new OwnerBootstrapper(
            dbContext,
            new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var actorAccountId = bootstrap.AccountId!.Value;
        var sessionId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var fixture = ScopeFixture.Create(actorAccountId, sessionId, membershipTypeId);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await InsertSessionAndMembershipTypeAsync(connection, fixture);
        await InsertMembershipAsync(
            connection,
            fixture,
            fixture.EndBoundaryMembershipId,
            fixture.EndBoundaryClientId,
            "End boundary",
            new DateOnly(2026, 1, 22),
            "active",
            "active");
        await InsertMembershipAsync(
            connection,
            fixture,
            fixture.StartBoundaryMembershipId,
            fixture.StartBoundaryClientId,
            "Start boundary",
            new DateOnly(2026, 2, 2),
            "active",
            "inactive");
        await InsertMembershipAsync(
            connection,
            fixture,
            fixture.NoOverlapMembershipId,
            fixture.NoOverlapClientId,
            "No overlap",
            new DateOnly(2026, 2, 3),
            "active",
            "active");
        await InsertMembershipAsync(
            connection,
            fixture,
            fixture.ExtendedMembershipId,
            fixture.ExtendedClientId,
            "Accepted extension",
            new DateOnly(2026, 1, 20),
            "active",
            "active");
        await InsertMembershipAsync(
            connection,
            fixture,
            fixture.CanceledMembershipId,
            fixture.CanceledClientId,
            "Canceled lifecycle",
            new DateOnly(2026, 1, 22),
            "canceled",
            "active");
        await InsertFreezeAndStaleCacheAsync(connection, fixture);
        if (includeReplacedPeriod)
        {
            await InsertReplacedPeriodAsync(connection, fixture);
        }

        dbContext.ChangeTracker.Clear();

        return fixture;
    }

    private static async Task InsertSessionAndMembershipTypeAsync(
        NpgsqlConnection connection,
        ScopeFixture fixture)
    {
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
                'Owner laptop',
                @started_at,
                @expires_at,
                null,
                @started_at);

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
                'Affected scope fixture',
                10,
                8,
                1000,
                'UAH',
                true,
                null,
                @started_at,
                @started_at,
                null)
            """;
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("actor_account_id", fixture.ActorAccountId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("started_at", TestNow);
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(12));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertMembershipAsync(
        NpgsqlConnection connection,
        ScopeFixture fixture,
        Guid membershipId,
        Guid clientId,
        string label,
        DateOnly startDate,
        string membershipStatus,
        string clientStatus)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
            values (
                @client_id,
                'Scope',
                @label,
                null,
                @normalized_name,
                null,
                null,
                null,
                null,
                @client_status,
                @recorded_at,
                @actor_account_id,
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
            values (
                @membership_id,
                @client_id,
                @membership_type_id,
                'Affected scope fixture',
                10,
                8,
                1000,
                'UAH',
                @start_date,
                @base_end_date,
                @recorded_at,
                @actor_account_id,
                @membership_status,
                'normal',
                null,
                null)
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("label", label);
        command.Parameters.AddWithValue("normalized_name", $"SCOPE {label.ToUpperInvariant()}");
        command.Parameters.AddWithValue("client_status", clientStatus);
        command.Parameters.AddWithValue("membership_status", membershipStatus);
        command.Parameters.AddWithValue("actor_account_id", fixture.ActorAccountId);
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, startDate);
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            startDate.AddDays(9));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertFreezeAndStaleCacheAsync(
        NpgsqlConnection connection,
        ScopeFixture fixture)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.freezes (
                id,
                client_id,
                membership_id,
                start_date,
                end_date,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id,
                status)
            values (
                @freeze_id,
                @client_id,
                @membership_id,
                @freeze_start_date,
                @freeze_end_date,
                'Accepted extension source',
                @recorded_at,
                @recorded_at,
                @actor_account_id,
                @session_id,
                'normal',
                null,
                'active');

            insert into bodylife.membership_state_cache (
                membership_id,
                counted_visits,
                remaining_visits,
                negative_balance,
                first_negative_visit_id,
                first_negative_visit_date,
                extension_days,
                effective_end_date,
                last_counted_visit_at,
                recalculated_at,
                recalculation_version)
            values (
                @membership_id,
                0,
                8,
                0,
                null,
                null,
                0,
                @stale_effective_end_date,
                null,
                @stale_recalculated_at,
                1)
            """;
        command.Parameters.AddWithValue("freeze_id", fixture.FreezeId);
        command.Parameters.AddWithValue("client_id", fixture.ExtendedClientId);
        command.Parameters.AddWithValue("membership_id", fixture.ExtendedMembershipId);
        command.Parameters.AddWithValue("actor_account_id", fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue(
            "freeze_start_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 1, 25));
        command.Parameters.AddWithValue(
            "freeze_end_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 1, 31));
        command.Parameters.AddWithValue(
            "stale_effective_end_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 1, 29));
        command.Parameters.AddWithValue("stale_recalculated_at", TestNow.AddDays(-1));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertReplacedPeriodAsync(
        NpgsqlConnection connection,
        ScopeFixture fixture)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                @start_date,
                @end_date,
                'weather_closure',
                'Original closure',
                @recorded_at,
                @actor_account_id,
                @session_id,
                'active');

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
                    @end_boundary_application_id,
                    @period_id,
                    @end_boundary_membership_id,
                    @end_boundary_client_id,
                    @start_date,
                    @end_date,
                    @previewed_at,
                    @recorded_at,
                    'active'),
                (
                    @start_boundary_application_id,
                    @period_id,
                    @start_boundary_membership_id,
                    @start_boundary_client_id,
                    @start_date,
                    @end_date,
                    @previewed_at,
                    @recorded_at,
                    'active'),
                (
                    @extended_application_id,
                    @period_id,
                    @extended_membership_id,
                    @extended_client_id,
                    @start_date,
                    @end_date,
                    @previewed_at,
                    @recorded_at,
                    'active')
            """;
        command.Parameters.AddWithValue("period_id", fixture.ReplacedPeriodId);
        command.Parameters.AddWithValue(
            "end_boundary_application_id",
            fixture.EndBoundaryReplacedApplicationId);
        command.Parameters.AddWithValue(
            "start_boundary_application_id",
            fixture.StartBoundaryReplacedApplicationId);
        command.Parameters.AddWithValue(
            "extended_application_id",
            fixture.ExtendedReplacedApplicationId);
        command.Parameters.AddWithValue(
            "end_boundary_membership_id",
            fixture.EndBoundaryMembershipId);
        command.Parameters.AddWithValue(
            "start_boundary_membership_id",
            fixture.StartBoundaryMembershipId);
        command.Parameters.AddWithValue(
            "extended_membership_id",
            fixture.ExtendedMembershipId);
        command.Parameters.AddWithValue(
            "end_boundary_client_id",
            fixture.EndBoundaryClientId);
        command.Parameters.AddWithValue(
            "start_boundary_client_id",
            fixture.StartBoundaryClientId);
        command.Parameters.AddWithValue(
            "extended_client_id",
            fixture.ExtendedClientId);
        command.Parameters.AddWithValue("actor_account_id", fixture.ActorAccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("previewed_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            ProposedPeriod.StartDate);
        command.Parameters.AddWithValue(
            "end_date",
            NpgsqlDbType.Date,
            ProposedPeriod.EndDate);
        Assert.Equal(4, await command.ExecuteNonQueryAsync());
    }

    private static async Task<DatabaseSnapshot> ReadSnapshotAsync(
        BodyLifeDbContext dbContext,
        Guid extendedMembershipId)
    {
        if (dbContext.Database.GetDbConnection().State != ConnectionState.Open)
        {
            await dbContext.Database.OpenConnectionAsync();
        }

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            """
            select
                (select count(*) from bodylife.non_working_periods),
                (select count(*) from bodylife.non_working_period_applications),
                (select count(*) from bodylife.non_working_period_cancellations),
                (select count(*) from bodylife.membership_state_cache),
                (select count(*) from bodylife.membership_extension_days),
                (select count(*) from bodylife.business_audit_entries),
                (select count(*) from bodylife.command_idempotency_keys),
                (select count(*) from bodylife.freezes),
                cache.extension_days,
                cache.effective_end_date,
                cache.recalculation_version
            from bodylife.membership_state_cache as cache
            where cache.membership_id = @membership_id
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "membership_id";
        parameter.DbType = DbType.Guid;
        parameter.Value = extendedMembershipId;
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new DatabaseSnapshot(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt32(8),
            reader.GetFieldValue<DateOnly>(9),
            reader.GetInt32(10));
    }

    private static async Task<PostgresException> AssertMembershipUpdateBlockedAsync(
        string connectionString,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            set lock_timeout = '250ms';
            update bodylife.issued_memberships
            set comment = 'Concurrent edit'
            where id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        return await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
    }

    private static async Task<int> UpdateMembershipCommentAsync(
        string connectionString,
        Guid membershipId,
        string comment)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.issued_memberships
            set comment = @comment
            where id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("comment", comment);
        return await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ThrowingImpactPreparer : IMembershipNonWorkingDayImpactPreparer
    {
        public Task<MembershipNonWorkingDayImpactPreparation> PrepareImpactAsync(
            DateRange period,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Synthetic canonical calculation failure.");
        }
    }

    private sealed record ScopeFixture(
        Guid ActorAccountId,
        Guid SessionId,
        Guid MembershipTypeId,
        Guid FreezeId,
        Guid ReplacedPeriodId,
        Guid EndBoundaryReplacedApplicationId,
        Guid StartBoundaryReplacedApplicationId,
        Guid ExtendedReplacedApplicationId,
        Guid EndBoundaryMembershipId,
        Guid EndBoundaryClientId,
        Guid StartBoundaryMembershipId,
        Guid StartBoundaryClientId,
        Guid NoOverlapMembershipId,
        Guid NoOverlapClientId,
        Guid ExtendedMembershipId,
        Guid ExtendedClientId,
        Guid CanceledMembershipId,
        Guid CanceledClientId)
    {
        public static ScopeFixture Create(
            Guid actorAccountId,
            Guid sessionId,
            Guid membershipTypeId)
        {
            return new ScopeFixture(
                actorAccountId,
                sessionId,
                membershipTypeId,
                Guid.Parse("40000000-0000-0000-0000-000000000001"),
                Guid.Parse("50000000-0000-0000-0000-000000000001"),
                Guid.Parse("60000000-0000-0000-0000-000000000003"),
                Guid.Parse("60000000-0000-0000-0000-000000000001"),
                Guid.Parse("60000000-0000-0000-0000-000000000002"),
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Guid.Parse("10000000-0000-0000-0000-000000000002"),
                Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Guid.Parse("10000000-0000-0000-0000-000000000003"),
                Guid.Parse("00000000-0000-0000-0000-000000000004"),
                Guid.Parse("10000000-0000-0000-0000-000000000004"),
                Guid.Parse("00000000-0000-0000-0000-000000000005"),
                Guid.Parse("10000000-0000-0000-0000-000000000005"));
        }
    }

    private sealed record DatabaseSnapshot(
        long NonWorkingPeriodCount,
        long NonWorkingApplicationCount,
        long NonWorkingCancellationCount,
        long MembershipCacheCount,
        long ExtensionDayCount,
        long AuditCount,
        long IdempotencyCount,
        long FreezeCount,
        int ExtendedMembershipCacheDays,
        DateOnly ExtendedMembershipCacheEndDate,
        int ExtendedMembershipCacheVersion);
}
