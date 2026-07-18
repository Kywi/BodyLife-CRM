using System.Diagnostics;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlNonWorkingDayMassRecalculationTests
{
    private const int RealisticAffectedMembershipCount = 250;
    private const int RollbackAffectedMembershipCount = 120;
    private const int CompletedWritesBeforeCancellation = 25;
    private static readonly TimeSpan SynchronousCommandBudget =
        TimeSpan.FromSeconds(30);
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        18,
        10,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateRange ProposedPeriod = new(
        new DateOnly(2026, 1, 30),
        new DateOnly(2026, 2, 2));

    [PostgreSqlFact]
    public async Task RealisticScopeAddAndCorrectionMeetSynchronousBudget()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedMassFixtureAsync(
            database,
            dbContext,
            RealisticAffectedMembershipCount);
        var clock = new MutableTimeProvider(TestNow);
        var addTokenService = CreateAddTokenService(clock);
        var addPreview = await IssueAddPreviewAsync(
            dbContext,
            fixture.Actor,
            addTokenService,
            clock);
        Assert.Equal(RealisticAffectedMembershipCount, addPreview.AffectedCount);

        var addTimer = Stopwatch.StartNew();
        var addResult = await CreateAddHandler(
                dbContext,
                addTokenService,
                clock)
            .ExecuteAsync(
                CreateAddCommand(fixture, addPreview, "mass-add"),
                CancellationToken.None);
        addTimer.Stop();

        AssertWithinBudget("AddNonWorkingDay", addTimer.Elapsed);
        AssertSuccessfulAdd(addResult, fixture.MembershipIds);
        var periodId = addResult.PrimaryEntityId!.Value.Value;
        Assert.Equal(
            new AuditRecalculationSummary(
                RealisticAffectedMembershipCount,
                RealisticAffectedMembershipCount),
            await ReadAuditRecalculationAsync(
                database,
                addResult.AuditEntryId!.Value.Value));
        Assert.Equal(
            ExpectedAfterAdd(RealisticAffectedMembershipCount),
            await ReadMutationSummaryAsync(database));

        var correctionTokenService = CreateCorrectionTokenService(clock);
        var correctionPreview = await IssueReasonCorrectionPreviewAsync(
            dbContext,
            fixture.Actor,
            periodId,
            correctionTokenService,
            clock);
        Assert.Equal(
            RealisticAffectedMembershipCount,
            correctionPreview.OriginalAffectedCount);
        Assert.Equal(
            RealisticAffectedMembershipCount,
            correctionPreview.ConfirmedAffectedCount);

        var correctionTimer = Stopwatch.StartNew();
        var correctionResult = await CreateCorrectionHandler(
                dbContext,
                correctionTokenService,
                clock)
            .ExecuteAsync(
                CreateReasonCorrectionCommand(
                    fixture,
                    periodId,
                    correctionPreview,
                    "mass-correction"),
                CancellationToken.None);
        correctionTimer.Stop();

        AssertWithinBudget("CorrectNonWorkingDay", correctionTimer.Elapsed);
        AssertSuccessfulCorrection(
            correctionResult,
            periodId,
            fixture.MembershipIds);
        Assert.Equal(
            new AuditRecalculationSummary(
                RealisticAffectedMembershipCount,
                RealisticAffectedMembershipCount),
            await ReadAuditRecalculationAsync(
                database,
                correctionResult.AuditEntryId!.Value.Value));
        Assert.Equal(
            ExpectedAfterReasonCorrection(RealisticAffectedMembershipCount),
            await ReadMutationSummaryAsync(database));
    }

    [PostgreSqlFact]
    public async Task CancellationAfterPartialMassRecalculationRollsBackEverything()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedMassFixtureAsync(
            database,
            dbContext,
            RollbackAffectedMembershipCount);
        var clock = new MutableTimeProvider(TestNow);
        var addTokenService = CreateAddTokenService(clock);
        var addPreview = await IssueAddPreviewAsync(
            dbContext,
            fixture.Actor,
            addTokenService,
            clock);
        var addResult = await CreateAddHandler(
                dbContext,
                addTokenService,
                clock)
            .ExecuteAsync(
                CreateAddCommand(fixture, addPreview, "rollback-baseline"),
                CancellationToken.None);
        AssertSuccessfulAdd(addResult, fixture.MembershipIds);
        var periodId = addResult.PrimaryEntityId!.Value.Value;
        var baseline = await ReadMutationSummaryAsync(database);
        Assert.Equal(
            ExpectedAfterAdd(RollbackAffectedMembershipCount),
            baseline);

        clock.UtcNow = TestNow.AddMinutes(1);
        var correctionTokenService = CreateCorrectionTokenService(clock);
        var correctionPreview = await IssueReasonCorrectionPreviewAsync(
            dbContext,
            fixture.Actor,
            periodId,
            correctionTokenService,
            clock);
        using var timeout = new CancellationTokenSource();
        CancelAfterCanonicalWriteRecalculator? cancelingRecalculator = null;
        var handler = CreateCorrectionHandler(
            dbContext,
            correctionTokenService,
            clock,
            inner => cancelingRecalculator =
                new CancelAfterCanonicalWriteRecalculator(
                    inner,
                    timeout,
                    CompletedWritesBeforeCancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.ExecuteAsync(
                CreateReasonCorrectionCommand(
                    fixture,
                    periodId,
                    correctionPreview,
                    "canceled-mass-correction"),
                timeout.Token));

        Assert.True(timeout.IsCancellationRequested);
        Assert.NotNull(cancelingRecalculator);
        Assert.Equal(
            CompletedWritesBeforeCancellation,
            cancelingRecalculator.CompletedCount);
        Assert.Equal(baseline, await ReadMutationSummaryAsync(database));
        Assert.Equal(TestNow, baseline.NewestStateRecalculatedAt);
        Assert.Equal(TestNow, baseline.NewestExtensionRecalculatedAt);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    private static AddNonWorkingDayCommandHandler CreateAddHandler(
        BodyLifeDbContext dbContext,
        INonWorkingDayPreviewTokenService tokenService,
        TimeProvider timeProvider)
    {
        var rebuilder = CreateStateRebuilder(dbContext, timeProvider);
        return new AddNonWorkingDayCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new MembershipNonWorkingDayAffectedScopePreparer(
                dbContext,
                rebuilder),
            new MembershipStateRecalculator(rebuilder),
            tokenService,
            timeProvider);
    }

    private static CorrectNonWorkingDayCommandHandler CreateCorrectionHandler(
        BodyLifeDbContext dbContext,
        INonWorkingDayCorrectionTokenService tokenService,
        TimeProvider timeProvider,
        Func<IMembershipStateRecalculator, IMembershipStateRecalculator>?
            recalculatorDecorator = null)
    {
        var components = CreateMembershipComponents(dbContext, timeProvider);
        IMembershipStateRecalculator recalculator =
            new MembershipStateRecalculator(components.Rebuilder);
        if (recalculatorDecorator is not null)
        {
            recalculator = recalculatorDecorator(recalculator);
        }

        return new CorrectNonWorkingDayCommandHandler(
            dbContext,
            new BusinessAuditAppender(dbContext),
            new CorrectNonWorkingDayCommandRevalidationPreparer(
                dbContext,
                components.ReplacementPreparer,
                new CorrectNonWorkingDaySourcePreparer(dbContext),
                tokenService,
                timeProvider),
            recalculator,
            timeProvider);
    }

    private static MembershipComponents CreateMembershipComponents(
        BodyLifeDbContext dbContext,
        TimeProvider timeProvider)
    {
        var sourceReader = new MembershipNonWorkingDayExtensionSourceReader(
            dbContext);
        var rebuilder = CreateStateRebuilder(
            dbContext,
            timeProvider,
            sourceReader);
        var affectedScopePreparer =
            new MembershipNonWorkingDayAffectedScopePreparer(
                dbContext,
                rebuilder);
        return new MembershipComponents(
            rebuilder,
            new MembershipNonWorkingDayReplacementImpactPreparer(
                affectedScopePreparer,
                sourceReader));
    }

    private static MembershipStateCacheRebuilder CreateStateRebuilder(
        BodyLifeDbContext dbContext,
        TimeProvider timeProvider,
        MembershipNonWorkingDayExtensionSourceReader? sourceReader = null)
    {
        return new MembershipStateCacheRebuilder(
            dbContext,
            timeProvider,
            [
                new MembershipFreezeExtensionSourceReader(dbContext),
                sourceReader
                    ?? new MembershipNonWorkingDayExtensionSourceReader(
                        dbContext),
            ]);
    }

    private static async Task<NonWorkingDayImpactPreview> IssueAddPreviewAsync(
        BodyLifeDbContext dbContext,
        ActorContext actor,
        INonWorkingDayPreviewTokenService tokenService,
        TimeProvider timeProvider)
    {
        var result = await new PreviewNonWorkingDayImpactQueryHandler(
                dbContext,
                new MembershipNonWorkingDayAffectedScopePreparer(
                    dbContext,
                    CreateStateRebuilder(dbContext, timeProvider)),
                tokenService,
                timeProvider)
            .ExecuteAsync(
                new PreviewNonWorkingDayImpactQuery(
                    actor,
                    ProposedPeriod.StartDate,
                    ProposedPeriod.EndDate,
                    "weather_closure",
                    "Mass recalculation baseline"),
                CancellationToken.None);

        Assert.Equal(PreviewNonWorkingDayImpactStatus.Success, result.Status);
        return Assert.IsType<NonWorkingDayImpactPreview>(result.Preview);
    }

    private static async Task<NonWorkingDayCorrectionPreview>
        IssueReasonCorrectionPreviewAsync(
            BodyLifeDbContext dbContext,
            ActorContext actor,
            Guid periodId,
            INonWorkingDayCorrectionTokenService tokenService,
            TimeProvider timeProvider)
    {
        var components = CreateMembershipComponents(dbContext, timeProvider);
        var result = await new PreviewCorrectNonWorkingDayQueryHandler(
                dbContext,
                components.ReplacementPreparer,
                new CorrectNonWorkingDaySourcePreparer(dbContext),
                tokenService,
                timeProvider)
            .ExecuteAsync(
                new PreviewCorrectNonWorkingDayQuery(
                    actor,
                    periodId,
                    NonWorkingDayCorrectionMode.ReplaceReason,
                    ReplacementStartDate: null,
                    ReplacementEndDate: null,
                    "corrected_weather_closure",
                    "Corrected mass recalculation baseline"),
                CancellationToken.None);

        Assert.Equal(PreviewCorrectNonWorkingDayStatus.Success, result.Status);
        return Assert.IsType<NonWorkingDayCorrectionPreview>(result.Preview);
    }

    private static AddNonWorkingDayCommand CreateAddCommand(
        MassFixture fixture,
        NonWorkingDayImpactPreview preview,
        string idempotencyKey)
    {
        return new AddNonWorkingDayCommand(
            new CommandEnvelope(
                fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                TestNow,
                idempotencyKey,
                "Owner confirmed mass closure",
                "Synchronous transaction gate"),
            preview.Period,
            preview.ReasonCode,
            preview.ReasonComment,
            preview.Confirmation.ConfirmationToken);
    }

    private static CorrectNonWorkingDayCommand CreateReasonCorrectionCommand(
        MassFixture fixture,
        Guid periodId,
        NonWorkingDayCorrectionPreview preview,
        string idempotencyKey)
    {
        return new CorrectNonWorkingDayCommand(
            new CommandEnvelope(
                fixture.Actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                TestNow,
                idempotencyKey,
                "Owner corrected mass closure reason",
                "Synchronous transaction gate"),
            periodId,
            NonWorkingDayCorrectionMode.ReplaceReason,
            ReplacementStartDate: null,
            ReplacementEndDate: null,
            preview.ReplacementInput!.ReasonCode,
            preview.ReplacementInput.ReasonComment,
            preview.Confirmation.ConfirmationToken);
    }

    private static HmacNonWorkingDayPreviewTokenService CreateAddTokenService(
        TimeProvider timeProvider)
    {
        return new HmacNonWorkingDayPreviewTokenService(
            TokenOptions(),
            timeProvider);
    }

    private static HmacNonWorkingDayCorrectionTokenService
        CreateCorrectionTokenService(TimeProvider timeProvider)
    {
        return new HmacNonWorkingDayCorrectionTokenService(
            TokenOptions(),
            timeProvider);
    }

    private static NonWorkingDayPreviewTokenOptions TokenOptions()
    {
        return new NonWorkingDayPreviewTokenOptions(
            Convert.ToBase64String(
                Enumerable.Range(1, 32)
                    .Select(value => (byte)value)
                    .ToArray()),
            TimeSpan.FromMinutes(5));
    }

    private static async Task<MassFixture> SeedMassFixtureAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext,
        int affectedMembershipCount)
    {
        var bootstrap = await new OwnerBootstrapper(dbContext, new
                MutableTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var accountId = bootstrap.AccountId!.Value;
        var sessionId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(
            database.ConnectionString);
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
                'Owner performance gate',
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
                'Mass recalculation fixture',
                90,
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
            select
                (
                    'a0000000-0000-0000-0000-'
                    || lpad(sequence::text, 12, '0')
                )::uuid,
                'Mass',
                'Client ' || sequence,
                null,
                'MASS CLIENT ' || sequence,
                null,
                null,
                null,
                null,
                'active',
                @recorded_at,
                @account_id,
                @recorded_at
            from generate_series(1, @affected_count) as sequence;

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
            select
                (
                    'b0000000-0000-0000-0000-'
                    || lpad(sequence::text, 12, '0')
                )::uuid,
                (
                    'a0000000-0000-0000-0000-'
                    || lpad(sequence::text, 12, '0')
                )::uuid,
                @membership_type_id,
                'Mass recalculation fixture',
                90,
                8,
                1000,
                'UAH',
                '2026-01-01'::date,
                '2026-03-31'::date,
                @recorded_at,
                @account_id,
                'active',
                'normal',
                null,
                null
            from generate_series(1, @affected_count) as sequence
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(12));
        command.Parameters.AddWithValue(
            "affected_count",
            affectedMembershipCount);
        Assert.Equal(
            2 + (affectedMembershipCount * 2),
            await command.ExecuteNonQueryAsync());
        dbContext.ChangeTracker.Clear();

        return new MassFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Owner performance gate"),
            Enumerable.Range(1, affectedMembershipCount)
                .Select(CreateMembershipId)
                .ToArray());
    }

    private static Guid CreateMembershipId(int sequence)
    {
        return Guid.Parse(
            $"b0000000-0000-0000-0000-{sequence:D12}");
    }

    private static async Task<AuditRecalculationSummary>
        ReadAuditRecalculationAsync(
            PostgreSqlTestDatabase database,
            Guid auditEntryId)
    {
        await using var connection = new NpgsqlConnection(
            database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select after_summary::text
            from bodylife.business_audit_entries
            where id = @audit_entry_id
            """;
        command.Parameters.AddWithValue("audit_entry_id", auditEntryId);
        var json = Assert.IsType<string>(await command.ExecuteScalarAsync());
        using var document = JsonDocument.Parse(json);
        var recalculation = document.RootElement.GetProperty("recalculation");
        return new AuditRecalculationSummary(
            recalculation.GetProperty("requestedCount").GetInt32(),
            recalculation.GetProperty("succeededCount").GetInt32());
    }

    private static async Task<MassMutationSummary> ReadMutationSummaryAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(
            database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select
                (select count(*) from bodylife.non_working_periods),
                (
                    select count(*)
                    from bodylife.non_working_periods
                    where status = 'active'
                ),
                (
                    select count(*)
                    from bodylife.non_working_periods
                    where status = 'corrected'
                ),
                (select count(*) from bodylife.non_working_period_applications),
                (
                    select count(*)
                    from bodylife.non_working_period_applications
                    where status = 'active'
                ),
                (
                    select count(*)
                    from bodylife.non_working_period_applications
                    where status = 'corrected'
                ),
                (select count(*) from bodylife.membership_state_cache),
                (
                    select coalesce(sum(extension_days), 0)
                    from bodylife.membership_state_cache
                ),
                (select count(*) from bodylife.membership_extension_days),
                (
                    select count(*)
                    from bodylife.membership_extension_days
                    where is_active
                ),
                (
                    select count(*)
                    from bodylife.membership_extension_days
                    where not is_active
                ),
                (select count(*) from bodylife.business_audit_entries),
                (select count(*) from bodylife.command_idempotency_keys),
                (select max(recalculated_at) from bodylife.membership_state_cache),
                (select max(recalculated_at) from bodylife.membership_extension_days)
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new MassMutationSummary(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetInt64(9),
            reader.GetInt64(10),
            reader.GetInt64(11),
            reader.GetInt64(12),
            reader.GetFieldValue<DateTimeOffset>(13),
            reader.GetFieldValue<DateTimeOffset>(14));
    }

    private static MassMutationSummary ExpectedAfterAdd(int membershipCount)
    {
        return new MassMutationSummary(
            PeriodCount: 1,
            ActivePeriodCount: 1,
            CorrectedPeriodCount: 0,
            ApplicationCount: membershipCount,
            ActiveApplicationCount: membershipCount,
            CorrectedApplicationCount: 0,
            StateCount: membershipCount,
            StateExtensionDayTotal: membershipCount * ProposedPeriod.InclusiveDays,
            ExtensionRowCount: membershipCount * ProposedPeriod.InclusiveDays,
            ActiveExtensionRowCount: membershipCount * ProposedPeriod.InclusiveDays,
            InactiveExtensionRowCount: 0,
            AuditCount: 1,
            IdempotencyCount: 1,
            NewestStateRecalculatedAt: TestNow,
            NewestExtensionRecalculatedAt: TestNow);
    }

    private static MassMutationSummary ExpectedAfterReasonCorrection(
        int membershipCount)
    {
        var extensionCount = membershipCount * ProposedPeriod.InclusiveDays;
        return new MassMutationSummary(
            PeriodCount: 2,
            ActivePeriodCount: 1,
            CorrectedPeriodCount: 1,
            ApplicationCount: membershipCount * 2,
            ActiveApplicationCount: membershipCount,
            CorrectedApplicationCount: membershipCount,
            StateCount: membershipCount,
            StateExtensionDayTotal: extensionCount,
            ExtensionRowCount: extensionCount * 2,
            ActiveExtensionRowCount: extensionCount,
            InactiveExtensionRowCount: extensionCount,
            AuditCount: 2,
            IdempotencyCount: 2,
            NewestStateRecalculatedAt: TestNow,
            NewestExtensionRecalculatedAt: TestNow);
    }

    private static void AssertSuccessfulAdd(
        CommandResult result,
        IReadOnlyList<Guid> membershipIds)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Equal(
            AddNonWorkingDayCommand.PrimaryEntityType,
            result.PrimaryEntityId?.Type);
        Assert.Equal(result.PrimaryEntityId, result.RereadTargetId);
        Assert.Equal(
            membershipIds.Select(id => new EntityId(
                AddNonWorkingDayCommand.MembershipEntityType,
                id)),
            result.RelatedEntityIds);
        Assert.NotNull(result.AuditEntryId);
        Assert.Empty(result.Errors);
    }

    private static void AssertSuccessfulCorrection(
        CommandResult result,
        Guid originalPeriodId,
        IReadOnlyList<Guid> membershipIds)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Equal(
            CorrectNonWorkingDayCommand.PeriodEntityType,
            result.PrimaryEntityId?.Type);
        Assert.Equal(
            new EntityId(
                CorrectNonWorkingDayCommand.CanonicalRereadEntityType,
                originalPeriodId),
            result.RereadTargetId);
        Assert.Equal(
            new[]
            {
                new EntityId(
                    CorrectNonWorkingDayCommand.PeriodEntityType,
                    originalPeriodId),
            }.Concat(membershipIds.Select(id => new EntityId(
                CorrectNonWorkingDayCommand.MembershipEntityType,
                id))),
            result.RelatedEntityIds);
        Assert.NotNull(result.AuditEntryId);
        Assert.Empty(result.Errors);
    }

    private static void AssertWithinBudget(
        string commandName,
        TimeSpan elapsed)
    {
        Assert.True(
            elapsed <= SynchronousCommandBudget,
            $"{commandName} took {elapsed.TotalSeconds:F2}s for "
            + $"{RealisticAffectedMembershipCount} affected Memberships; "
            + $"the synchronous budget is {SynchronousCommandBudget.TotalSeconds:F0}s.");
    }

    private sealed record MassFixture(
        ActorContext Actor,
        IReadOnlyList<Guid> MembershipIds);

    private sealed record MembershipComponents(
        MembershipStateCacheRebuilder Rebuilder,
        MembershipNonWorkingDayReplacementImpactPreparer ReplacementPreparer);

    private sealed record AuditRecalculationSummary(
        int RequestedCount,
        int SucceededCount);

    private sealed record MassMutationSummary(
        long PeriodCount,
        long ActivePeriodCount,
        long CorrectedPeriodCount,
        long ApplicationCount,
        long ActiveApplicationCount,
        long CorrectedApplicationCount,
        long StateCount,
        long StateExtensionDayTotal,
        long ExtensionRowCount,
        long ActiveExtensionRowCount,
        long InactiveExtensionRowCount,
        long AuditCount,
        long IdempotencyCount,
        DateTimeOffset NewestStateRecalculatedAt,
        DateTimeOffset NewestExtensionRecalculatedAt);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class CancelAfterCanonicalWriteRecalculator(
        IMembershipStateRecalculator inner,
        CancellationTokenSource timeout,
        int completedWritesBeforeCancellation)
        : IMembershipStateRecalculator
    {
        public int CompletedCount { get; private set; }

        public async Task<MembershipStateRecalculationResult> RecalculateAsync(
            Guid membershipId,
            CancellationToken cancellationToken = default)
        {
            var completed = await inner.RecalculateAsync(
                membershipId,
                cancellationToken);
            Assert.True(completed.Succeeded);
            CompletedCount++;
            if (CompletedCount == completedWritesBeforeCancellation)
            {
                timeout.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return completed;
        }
    }
}
