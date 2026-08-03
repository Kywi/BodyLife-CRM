using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Reports;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed partial class PostgreSqlIssueMembershipCommandTests
{
    [PostgreSqlFact]
    public async Task IssuedSaleCorrectionPreviewReturnsExactSourceAndReplacementTerms()
    {
        await using var database = await CreateIssuedSaleCorrectionDatabaseAsync();
        var fixture = await SeedIssuedSaleCorrectionFixtureAsync(database);
        await using var dbContext = database.CreateDbContext();

        var result = await CreateIssuedSaleCorrectionPreviewHandler(dbContext)
            .ExecuteAsync(
                new PreviewIssuedMembershipSaleCorrectionQuery(
                    fixture.Actor,
                    fixture.OriginalMembershipId,
                    fixture.MembershipTypeId,
                    NewStartDate),
                CancellationToken.None);

        Assert.Equal(PreviewIssuedMembershipSaleCorrectionStatus.Success, result.Status);
        var preview = Assert.IsType<IssuedMembershipSaleCorrectionPreview>(result.Preview);
        Assert.Equal(fixture.OriginalMembershipId, preview.OriginalSale.MembershipId);
        Assert.Equal(fixture.OriginalPaymentId, preview.OriginalSale.PaymentId);
        Assert.Equal("Historical two visits / 30 days", preview.OriginalSale.TypeNameSnapshot);
        Assert.Equal(30, preview.OriginalSale.DurationDaysSnapshot);
        Assert.Equal(2, preview.OriginalSale.VisitsLimitSnapshot);
        Assert.Equal(new Money(900m, "UAH"), preview.OriginalSale.PriceSnapshot);
        Assert.Empty(preview.Dependencies);
        Assert.Equal(64, preview.DependencyToken.Length);

        var replacement = Assert.IsType<IssuedMembershipSaleReplacementTerms>(
            preview.Replacement);
        Assert.Equal(fixture.MembershipTypeId, replacement.MembershipTypeId);
        Assert.Equal(fixture.MembershipTypeUpdatedAt, replacement.ExpectedMembershipTypeUpdatedAt);
        Assert.Equal("Eight visits / 30 days", replacement.TypeNameSnapshot);
        Assert.Equal(8, replacement.VisitsLimitSnapshot);
        Assert.Equal(new Money(1200m, "UAH"), replacement.PriceSnapshot);
        Assert.Equal(NewStartDate, replacement.StartDate);
        Assert.Equal(NewBaseEndDate, replacement.BaseEndDate);

        var exposedNames = typeof(IssuedMembershipSaleCorrectionPreview)
            .GetProperties()
            .Select(property => property.Name)
            .Concat(typeof(IssuedMembershipSaleReplacementTerms)
                .GetProperties()
                .Select(property => property.Name));
        Assert.DoesNotContain(exposedNames, name =>
            name.Contains("Refund", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Delta", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Surcharge", StringComparison.OrdinalIgnoreCase));
    }

    [PostgreSqlFact]
    public async Task CancelIssuedSaleCommitsLifecycleAuditAndIdempotentReplay()
    {
        await using var database = await CreateIssuedSaleCorrectionDatabaseAsync();
        var fixture = await SeedIssuedSaleCorrectionFixtureAsync(database);
        var preview = await PreviewIssuedSaleAsync(database, fixture);
        var command = new CancelIssuedMembershipSaleCommand(
            CreateIssuedSaleCorrectionEnvelope(fixture.Actor, "sale-cancel"),
            fixture.OriginalMembershipId,
            preview.DependencyToken);

        CommandResult first;
        await using (var firstContext = database.CreateDbContext())
        {
            first = await CreateCancelIssuedSaleHandler(firstContext).ExecuteAsync(
                command,
                CancellationToken.None);
        }

        AssertIssuedSaleCorrectionSuccess(first, fixture.ClientId);
        await using (var replayContext = database.CreateDbContext())
        {
            var replay = await CreateCancelIssuedSaleHandler(replayContext).ExecuteAsync(
                command,
                CancellationToken.None);
            Assert.Equal(first.PrimaryEntityId, replay.PrimaryEntityId);
            Assert.Equal(first.AuditEntryId, replay.AuditEntryId);
            AssertIssuedSaleCorrectionSuccess(replay, fixture.ClientId);
        }

        Assert.Equal(
            "canceled",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.issued_memberships where id = '{fixture.OriginalMembershipId}'"));
        Assert.Equal(
            "canceled",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.payments where id = '{fixture.OriginalPaymentId}'"));
        Assert.Equal(
            "cancel",
            await database.ExecuteScalarAsync<string>(
                $"select correction_mode from bodylife.issued_membership_sale_corrections where original_membership_id = '{fixture.OriginalMembershipId}'"));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.issued_membership_sale_corrections"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.payment_cancellations"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.payment_corrections"));
        Assert.Equal(
            new[] { "membership.sale_canceled", "payment.canceled" },
            await ReadIssuedSaleAuditActionsAsync(database));

        await AssertCanceledIssuedSaleProjectionsAsync(database, fixture);
    }

    [PostgreSqlFact]
    public async Task ReplaceIssuedSaleCreatesNewExactSaleAndPreservesOriginalHistory()
    {
        await using var database = await CreateIssuedSaleCorrectionDatabaseAsync();
        var fixture = await SeedIssuedSaleCorrectionFixtureAsync(database);
        var preview = await PreviewIssuedSaleAsync(
            database,
            fixture,
            fixture.MembershipTypeId,
            NewStartDate);
        var replacement = Assert.IsType<IssuedMembershipSaleReplacementTerms>(
            preview.Replacement);

        CommandResult result;
        await using (var dbContext = database.CreateDbContext())
        {
            result = await CreateReplaceIssuedSaleHandler(dbContext).ExecuteAsync(
                new ReplaceIssuedMembershipCommand(
                    CreateIssuedSaleCorrectionEnvelope(fixture.Actor, "sale-replace"),
                    fixture.OriginalMembershipId,
                    replacement.MembershipTypeId,
                    replacement.ExpectedMembershipTypeUpdatedAt,
                    replacement.StartDate,
                    preview.DependencyToken),
                CancellationToken.None);
        }

        AssertIssuedSaleCorrectionSuccess(result, fixture.ClientId);
        var correctionId = result.PrimaryEntityId!.Value.Value;
        var replacementMembershipId = await database.ExecuteScalarAsync<Guid>(
            $"select replacement_membership_id from bodylife.issued_membership_sale_corrections where id = '{correctionId}'");
        var replacementPaymentId = await database.ExecuteScalarAsync<Guid>(
            $"select replacement_payment_id from bodylife.issued_membership_sale_corrections where id = '{correctionId}'");

        Assert.Equal(
            "corrected",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.issued_memberships where id = '{fixture.OriginalMembershipId}'"));
        Assert.Equal(
            "replaced",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.payments where id = '{fixture.OriginalPaymentId}'"));

        var newMembership = await ReadIssuedMembershipAsync(
            database,
            replacementMembershipId);
        Assert.Equal(fixture.ClientId, newMembership.ClientId);
        Assert.Equal(fixture.MembershipTypeId, newMembership.MembershipTypeId);
        Assert.Equal("sale", newMembership.IssuanceMode);
        Assert.Equal("Eight visits / 30 days", newMembership.TypeNameSnapshot);
        Assert.Equal(8, newMembership.VisitsLimitSnapshot);
        Assert.Equal(1200m, newMembership.PriceAmountSnapshot);
        Assert.Equal(NewStartDate, newMembership.StartDate);
        Assert.Equal(NewBaseEndDate, newMembership.BaseEndDate);
        Assert.Equal("active", newMembership.Status);

        var newPayment = await ReadPaymentForMembershipAsync(
            database,
            replacementMembershipId);
        Assert.Equal(replacementPaymentId, newPayment.Id);
        Assert.Equal(1200m, newPayment.Amount);
        Assert.Equal("UAH", newPayment.Currency);
        Assert.Equal("cash", newPayment.Method);
        Assert.Equal("membership_sale", newPayment.PaymentContext);
        Assert.Equal("active", newPayment.Status);

        var originalCache = await ReadCacheAsync(database, fixture.OriginalMembershipId);
        var replacementCache = await ReadCacheAsync(database, replacementMembershipId);
        Assert.Equal(2, originalCache.RemainingVisits);
        Assert.Equal(8, replacementCache.RemainingVisits);
        Assert.Equal(
            new[] { "membership.replaced", "payment.corrected", "payment.created" },
            await ReadIssuedSaleAuditActionsAsync(database));

        await AssertReplacedIssuedSaleProjectionsAsync(
            database,
            fixture,
            replacementMembershipId,
            replacementPaymentId);
    }

    [Theory]
    [InlineData("visit", "visit")]
    [InlineData("freeze", "freeze")]
    [InlineData("non_working_day", "non_working_day_application")]
    [InlineData("negative_source", "negative_coverage")]
    [InlineData("negative_covering", "negative_coverage")]
    public async Task DependencyChangeMakesPreviewStaleThenBlocksCorrection(
        string scenario,
        string expectedDependencyType)
    {
        await using var database = await CreateIssuedSaleCorrectionDatabaseAsync();
        var fixture = await SeedIssuedSaleCorrectionFixtureAsync(database);
        var stalePreview = await PreviewIssuedSaleAsync(database, fixture);
        var dependencyFixture = await InsertIssuedSaleDependencyAsync(
            database,
            fixture,
            scenario);
        await RebuildAndAssertIssuedSaleDependencyCachesAsync(
            database,
            fixture,
            dependencyFixture,
            scenario);
        var dependencyId = dependencyFixture.DependencyId;

        await using (var staleContext = database.CreateDbContext())
        {
            var stale = await CreateCancelIssuedSaleHandler(staleContext).ExecuteAsync(
                new CancelIssuedMembershipSaleCommand(
                    CreateIssuedSaleCorrectionEnvelope(fixture.Actor, "sale-stale"),
                    fixture.OriginalMembershipId,
                    stalePreview.DependencyToken),
                CancellationToken.None);
            AssertIssuedSaleCorrectionError(stale, CommandErrorCode.StaleState);
        }

        var currentPreview = await PreviewIssuedSaleAsync(database, fixture);
        Assert.NotEqual(stalePreview.DependencyToken, currentPreview.DependencyToken);
        var dependency = Assert.Single(
            currentPreview.Dependencies,
            candidate => candidate.DependencyType == expectedDependencyType
                && candidate.DependencyId == dependencyId);
        Assert.Equal(expectedDependencyType, dependency.DependencyType);
        Assert.Equal(dependencyId, dependency.DependencyId);
        if (scenario is "negative_source" or "negative_covering")
        {
            Assert.StartsWith(
                scenario == "negative_source" ? "source:" : "covering:",
                dependency.Context,
                StringComparison.Ordinal);
        }

        await using var blockedContext = database.CreateDbContext();
        var blocked = await CreateCancelIssuedSaleHandler(blockedContext).ExecuteAsync(
            new CancelIssuedMembershipSaleCommand(
                CreateIssuedSaleCorrectionEnvelope(fixture.Actor, "sale-blocked"),
                fixture.OriginalMembershipId,
                currentPreview.DependencyToken),
            CancellationToken.None);
        AssertIssuedSaleCorrectionError(blocked, CommandErrorCode.MembershipNotEligible);
        Assert.Equal(
            "active",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.issued_memberships where id = '{fixture.OriginalMembershipId}'"));
        Assert.Equal(
            "active",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.payments where id = '{fixture.OriginalPaymentId}'"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.issued_membership_sale_corrections"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_replacement_dependency_items"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                """
                select count(*)
                from bodylife.business_audit_entries
                where action_type in (
                    'membership.replaced',
                    'membership.sale_canceled')
                """));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                """
                select count(*)
                from bodylife.command_idempotency_keys
                where command_name in (
                    'ReplaceIssuedMembership',
                    'CancelIssuedMembershipSale')
                """));
    }

    [PostgreSqlFact]
    public async Task ConcurrentIssuedSaleCorrectionsAllowExactlyOneWinner()
    {
        await using var database = await CreateIssuedSaleCorrectionDatabaseAsync();
        var fixture = await SeedIssuedSaleCorrectionFixtureAsync(database);
        var preview = await PreviewIssuedSaleAsync(database, fixture);
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateCancelIssuedSaleHandler(firstContext).ExecuteAsync(
                new CancelIssuedMembershipSaleCommand(
                    CreateIssuedSaleCorrectionEnvelope(fixture.Actor, "sale-race-a"),
                    fixture.OriginalMembershipId,
                    preview.DependencyToken),
                CancellationToken.None),
            CreateCancelIssuedSaleHandler(secondContext).ExecuteAsync(
                new CancelIssuedMembershipSaleCommand(
                    CreateIssuedSaleCorrectionEnvelope(fixture.Actor, "sale-race-b"),
                    fixture.OriginalMembershipId,
                    preview.DependencyToken),
                CancellationToken.None));

        Assert.Single(results, result => result.Status == CommandStatus.Success);
        var rejected = Assert.Single(results, result => result.Status == CommandStatus.Error);
        Assert.Equal(CommandErrorCode.StaleState, Assert.Single(rejected.Errors).Code);
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.issued_membership_sale_corrections"));
    }

    [PostgreSqlFact]
    public async Task ReplacementSaleCanBeReplacedAgainThenCanceled()
    {
        await using var database = await CreateIssuedSaleCorrectionDatabaseAsync();
        var fixture = await SeedIssuedSaleCorrectionFixtureAsync(database);

        var firstPreview = await PreviewIssuedSaleAsync(
            database,
            fixture,
            fixture.MembershipTypeId,
            NewStartDate);
        var firstReplacement = Assert.IsType<IssuedMembershipSaleReplacementTerms>(
            firstPreview.Replacement);
        Guid secondMembershipId;
        await using (var firstContext = database.CreateDbContext())
        {
            var first = await CreateReplaceIssuedSaleHandler(firstContext).ExecuteAsync(
                new ReplaceIssuedMembershipCommand(
                    CreateIssuedSaleCorrectionEnvelope(fixture.Actor, "sale-chain-a-b"),
                    fixture.OriginalMembershipId,
                    firstReplacement.MembershipTypeId,
                    firstReplacement.ExpectedMembershipTypeUpdatedAt,
                    firstReplacement.StartDate,
                    firstPreview.DependencyToken),
                CancellationToken.None);
            AssertIssuedSaleCorrectionSuccess(first, fixture.ClientId);
            secondMembershipId = await database.ExecuteScalarAsync<Guid>(
                $"select replacement_membership_id from bodylife.issued_membership_sale_corrections where id = '{first.PrimaryEntityId!.Value.Value}'");
        }

        var secondFixture = fixture with { OriginalMembershipId = secondMembershipId };
        var secondPayment = await ReadPaymentForMembershipAsync(database, secondMembershipId);
        secondFixture = secondFixture with { OriginalPaymentId = secondPayment.Id };
        var secondPreview = await PreviewIssuedSaleAsync(
            database,
            secondFixture,
            fixture.MembershipTypeId,
            NewStartDate.AddDays(1));
        var secondReplacement = Assert.IsType<IssuedMembershipSaleReplacementTerms>(
            secondPreview.Replacement);
        Guid thirdMembershipId;
        await using (var secondContext = database.CreateDbContext())
        {
            var second = await CreateReplaceIssuedSaleHandler(secondContext).ExecuteAsync(
                new ReplaceIssuedMembershipCommand(
                    CreateIssuedSaleCorrectionEnvelope(fixture.Actor, "sale-chain-b-c"),
                    secondMembershipId,
                    secondReplacement.MembershipTypeId,
                    secondReplacement.ExpectedMembershipTypeUpdatedAt,
                    secondReplacement.StartDate,
                    secondPreview.DependencyToken),
                CancellationToken.None);
            AssertIssuedSaleCorrectionSuccess(second, fixture.ClientId);
            thirdMembershipId = await database.ExecuteScalarAsync<Guid>(
                $"select replacement_membership_id from bodylife.issued_membership_sale_corrections where id = '{second.PrimaryEntityId!.Value.Value}'");
        }

        var thirdPayment = await ReadPaymentForMembershipAsync(database, thirdMembershipId);
        var thirdFixture = fixture with
        {
            OriginalMembershipId = thirdMembershipId,
            OriginalPaymentId = thirdPayment.Id,
        };
        var cancelPreview = await PreviewIssuedSaleAsync(database, thirdFixture);
        await using (var cancelContext = database.CreateDbContext())
        {
            var canceled = await CreateCancelIssuedSaleHandler(cancelContext).ExecuteAsync(
                new CancelIssuedMembershipSaleCommand(
                    CreateIssuedSaleCorrectionEnvelope(fixture.Actor, "sale-chain-c-cancel"),
                    thirdMembershipId,
                    cancelPreview.DependencyToken),
                CancellationToken.None);
            AssertIssuedSaleCorrectionSuccess(canceled, fixture.ClientId);
        }

        Assert.Equal(
            new[] { "corrected", "corrected", "canceled" },
            new[]
            {
                await database.ExecuteScalarAsync<string>(
                    $"select status from bodylife.issued_memberships where id = '{fixture.OriginalMembershipId}'"),
                await database.ExecuteScalarAsync<string>(
                    $"select status from bodylife.issued_memberships where id = '{secondMembershipId}'"),
                await database.ExecuteScalarAsync<string>(
                    $"select status from bodylife.issued_memberships where id = '{thirdMembershipId}'"),
            });
        Assert.Equal(
            3L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.issued_membership_sale_corrections"));
    }

    [PostgreSqlFact]
    public async Task ReconciledDayMarksAdminCancelAndReplacementWithoutBlocking()
    {
        await using var database = await CreateIssuedSaleCorrectionDatabaseAsync();
        var cancelFixture = await SeedIssuedSaleCorrectionFixtureAsync(database);
        var replaceFixture = await SeedIssuedSaleCorrectionFixtureAsync(database);
        var reconciledDate = BusinessTimeZone.GetBusinessDate(TestNow.AddDays(-3));
        var provider = new FixedPaymentDayReconciliationStatusProvider(reconciledDate);

        var cancelPreview = await PreviewIssuedSaleAsync(database, cancelFixture);
        CommandResult canceled;
        await using (var cancelContext = database.CreateDbContext())
        {
            canceled = await CreateCancelIssuedSaleHandler(cancelContext, provider)
                .ExecuteAsync(
                    new CancelIssuedMembershipSaleCommand(
                        CreateIssuedSaleCorrectionEnvelope(
                            cancelFixture.Actor,
                            "sale-closed-cancel"),
                        cancelFixture.OriginalMembershipId,
                        cancelPreview.DependencyToken),
                    CancellationToken.None);
        }

        var replacePreview = await PreviewIssuedSaleAsync(
            database,
            replaceFixture,
            replaceFixture.MembershipTypeId,
            NewStartDate);
        var replacement = Assert.IsType<IssuedMembershipSaleReplacementTerms>(
            replacePreview.Replacement);
        CommandResult replaced;
        await using (var replaceContext = database.CreateDbContext())
        {
            replaced = await CreateReplaceIssuedSaleHandler(replaceContext, provider)
                .ExecuteAsync(
                    new ReplaceIssuedMembershipCommand(
                        CreateIssuedSaleCorrectionEnvelope(
                            replaceFixture.Actor,
                            "sale-closed-replace"),
                        replaceFixture.OriginalMembershipId,
                        replacement.MembershipTypeId,
                        replacement.ExpectedMembershipTypeUpdatedAt,
                        replacement.StartDate,
                        replacePreview.DependencyToken),
                    CancellationToken.None);
        }

        AssertIssuedSaleCorrectionSuccess(canceled, cancelFixture.ClientId);
        AssertIssuedSaleCorrectionSuccess(replaced, replaceFixture.ClientId);
        Assert.True(canceled.ChangedAfterClose);
        Assert.True(replaced.ChangedAfterClose);
        Assert.Equal(
            5L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries where changed_after_close"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries where not changed_after_close"));
    }

    [PostgreSqlFact]
    public async Task AuthorizationAndReasonValidationFailWithoutSaleMutation()
    {
        await using var database = await CreateIssuedSaleCorrectionDatabaseAsync();
        var fixture = await SeedIssuedSaleCorrectionFixtureAsync(database);
        var owner = await SeedActorAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner);
        var shared = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin);
        var inactive = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            isActive: false);

        await using (var ownerContext = database.CreateDbContext())
        {
            var ownerResult = await CreateIssuedSaleCorrectionPreviewHandler(ownerContext)
                .ExecuteAsync(
                    new PreviewIssuedMembershipSaleCorrectionQuery(
                        owner,
                        fixture.OriginalMembershipId),
                    CancellationToken.None);
            Assert.Equal(
                PreviewIssuedMembershipSaleCorrectionStatus.Success,
                ownerResult.Status);
        }

        IssuedMembershipSaleCorrectionPreview sharedPreview;
        await using (var sharedContext = database.CreateDbContext())
        {
            var sharedResult = await CreateIssuedSaleCorrectionPreviewHandler(sharedContext)
                .ExecuteAsync(
                    new PreviewIssuedMembershipSaleCorrectionQuery(
                        shared,
                        fixture.OriginalMembershipId),
                    CancellationToken.None);
            Assert.Equal(
                PreviewIssuedMembershipSaleCorrectionStatus.Success,
                sharedResult.Status);
            sharedPreview = Assert.IsType<IssuedMembershipSaleCorrectionPreview>(
                sharedResult.Preview);
        }

        await using (var inactiveContext = database.CreateDbContext())
        {
            var deniedPreview = await CreateIssuedSaleCorrectionPreviewHandler(
                    inactiveContext)
                .ExecuteAsync(
                    new PreviewIssuedMembershipSaleCorrectionQuery(
                        inactive,
                        fixture.OriginalMembershipId),
                    CancellationToken.None);
            Assert.Equal(
                PreviewIssuedMembershipSaleCorrectionStatus.PermissionDenied,
                deniedPreview.Status);

            var deniedCommand = await CreateCancelIssuedSaleHandler(inactiveContext)
                .ExecuteAsync(
                    new CancelIssuedMembershipSaleCommand(
                        CreateIssuedSaleCorrectionEnvelope(inactive, "sale-inactive"),
                        fixture.OriginalMembershipId,
                        sharedPreview.DependencyToken),
                    CancellationToken.None);
            AssertIssuedSaleCorrectionError(
                deniedCommand,
                CommandErrorCode.PermissionDenied);
        }

        await using (var reasonContext = database.CreateDbContext())
        {
            var reasonlessEnvelope = CreateIssuedSaleCorrectionEnvelope(
                shared,
                "sale-reasonless") with
            {
                Reason = "  ",
            };
            var reasonless = await CreateCancelIssuedSaleHandler(reasonContext)
                .ExecuteAsync(
                    new CancelIssuedMembershipSaleCommand(
                        reasonlessEnvelope,
                        fixture.OriginalMembershipId,
                        sharedPreview.DependencyToken),
                    CancellationToken.None);
            AssertIssuedSaleCorrectionError(
                reasonless,
                CommandErrorCode.ReasonRequired);
        }

        Assert.Equal(
            "active",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.issued_memberships where id = '{fixture.OriginalMembershipId}'"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.issued_membership_sale_corrections"));
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackIssuedSaleCorrectionLifecycle()
    {
        await using var database = await CreateIssuedSaleCorrectionDatabaseAsync();
        var fixture = await SeedIssuedSaleCorrectionFixtureAsync(database);
        var preview = await PreviewIssuedSaleAsync(database, fixture);
        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_issued_sale_cancel_audit
            check (action_type <> 'membership.sale_canceled')
            """);

        await using var dbContext = database.CreateDbContext();
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateCancelIssuedSaleHandler(dbContext).ExecuteAsync(
                new CancelIssuedMembershipSaleCommand(
                    CreateIssuedSaleCorrectionEnvelope(
                        fixture.Actor,
                        "sale-audit-rollback"),
                    fixture.OriginalMembershipId,
                    preview.DependencyToken),
                CancellationToken.None));

        Assert.Equal(
            "active",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.issued_memberships where id = '{fixture.OriginalMembershipId}'"));
        Assert.Equal(
            "active",
            await database.ExecuteScalarAsync<string>(
                $"select status from bodylife.payments where id = '{fixture.OriginalPaymentId}'"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.issued_membership_sale_corrections"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task MalformedIssuedSaleCorrectionFailsClosedAcrossCanonicalReaders()
    {
        await using var database = await CreateIssuedSaleCorrectionDatabaseAsync();
        var fixture = await SeedIssuedSaleCorrectionFixtureAsync(database);
        var preview = await PreviewIssuedSaleAsync(
            database,
            fixture,
            fixture.MembershipTypeId,
            NewStartDate);
        var replacement = Assert.IsType<IssuedMembershipSaleReplacementTerms>(
            preview.Replacement);
        await using (var commandContext = database.CreateDbContext())
        {
            var result = await CreateReplaceIssuedSaleHandler(commandContext)
                .ExecuteAsync(
                    new ReplaceIssuedMembershipCommand(
                        CreateIssuedSaleCorrectionEnvelope(
                            fixture.Actor,
                            "sale-corrupt-source"),
                        fixture.OriginalMembershipId,
                        replacement.MembershipTypeId,
                        replacement.ExpectedMembershipTypeUpdatedAt,
                        replacement.StartDate,
                        preview.DependencyToken),
                    CancellationToken.None);
            AssertIssuedSaleCorrectionSuccess(result, fixture.ClientId);
        }

        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.issued_membership_sale_corrections
                disable trigger ck_issued_sale_corrections_lifecycle;
            alter table bodylife.issued_membership_sale_corrections
                drop constraint ck_issued_membership_sale_corrections_mode;
            alter table bodylife.issued_membership_sale_corrections
                drop constraint ck_issued_membership_sale_corrections_shape;
            update bodylife.issued_membership_sale_corrections
            set correction_mode = 'corrupt';
            """);

        await using var dbContext = database.CreateDbContext();
        var timeProvider = new FixedTimeProvider(TestNow);
        var dayStatus = new FixedPaymentDayReconciliationStatusProvider();
        var clientPayments = await new GetClientPaymentRowsQueryHandler(
                dbContext,
                dayStatus,
                timeProvider)
            .ExecuteAsync(
                new GetClientPaymentRowsQuery(fixture.Actor, fixture.ClientId),
                CancellationToken.None);
        var dailyPayments = await new GetDailyPaymentSourceRowsQueryHandler(
                dbContext,
                dayStatus,
                timeProvider)
            .ExecuteAsync(
                new GetDailyPaymentSourceRowsQuery(
                    fixture.Actor,
                    BusinessTimeZone.GetBusinessDate(TestNow)),
                CancellationToken.None);
        var auditEntries = new GetClientAuditEntriesQueryHandler(
            dbContext,
            timeProvider);
        var paymentHistory = await new GetClientPaymentHistorySourceRowsQueryHandler(
                dbContext,
                auditEntries)
            .ExecuteAsync(
                new GetClientPaymentHistorySourceRowsQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None);
        var membershipHistory = await new GetClientMembershipHistorySourceRowsQueryHandler(
                dbContext,
                auditEntries)
            .ExecuteAsync(
                new GetClientMembershipHistorySourceRowsQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None);

        Assert.Equal(GetClientPaymentRowsStatus.SourceInconsistent, clientPayments.Status);
        Assert.Equal(
            GetDailyPaymentSourceRowsStatus.SourceInconsistent,
            dailyPayments.Status);
        Assert.Equal(
            GetClientPaymentHistorySourceRowsStatus.SourceInconsistent,
            paymentHistory.Status);
        Assert.Equal(
            GetClientMembershipHistorySourceRowsStatus.SourceInconsistent,
            membershipHistory.Status);
    }

    [PostgreSqlFact]
    public async Task DeferredConstraintRejectsDirectSaleLifecycleMutation()
    {
        await using var database = await CreateIssuedSaleCorrectionDatabaseAsync();
        var fixture = await SeedIssuedSaleCorrectionFixtureAsync(database);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                update bodylife.issued_memberships
                set status = 'canceled'
                where id = @membership_id;

                update bodylife.payments
                set status = 'canceled'
                where id = @payment_id;
                """;
            command.Parameters.AddWithValue("membership_id", fixture.OriginalMembershipId);
            command.Parameters.AddWithValue("payment_id", fixture.OriginalPaymentId);
            Assert.Equal(2, await command.ExecuteNonQueryAsync());
        }

        var exception = await Assert.ThrowsAsync<PostgresException>(
            async () => await transaction.CommitAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(
            "ck_issued_sale_corrections_original_source",
            exception.ConstraintName);
    }

    private static async Task<PostgreSqlTestDatabase>
        CreateIssuedSaleCorrectionDatabaseAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        return database;
    }

    private static async Task<IssuedSaleCorrectionFixture>
        SeedIssuedSaleCorrectionFixtureAsync(PostgreSqlTestDatabase database)
    {
        var actor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            deviceLabel: "issued-sale correction tablet");
        var issue = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var original = await InsertIssuedMembershipAsync(
            database,
            issue,
            actor.AccountId.Value);
        await InsertCacheAsync(database, original, remainingVisits: 2, null);
        var payment = await ReadPaymentForMembershipAsync(database, original.MembershipId);
        var membershipTypeUpdatedAtValue = await database.ExecuteScalarAsync<DateTime>(
            $"select updated_at from bodylife.membership_types where id = '{issue.MembershipTypeId}'");
        var membershipTypeUpdatedAt = new DateTimeOffset(
            DateTime.SpecifyKind(membershipTypeUpdatedAtValue, DateTimeKind.Utc));
        return new IssuedSaleCorrectionFixture(
            actor,
            issue.ClientId,
            issue.MembershipTypeId,
            membershipTypeUpdatedAt,
            original.MembershipId,
            payment.Id);
    }

    private static async Task<IssuedMembershipSaleCorrectionPreview>
        PreviewIssuedSaleAsync(
            PostgreSqlTestDatabase database,
            IssuedSaleCorrectionFixture fixture,
            Guid? replacementMembershipTypeId = null,
            DateOnly? replacementStartDate = null)
    {
        await using var dbContext = database.CreateDbContext();
        var result = await CreateIssuedSaleCorrectionPreviewHandler(dbContext)
            .ExecuteAsync(
                new PreviewIssuedMembershipSaleCorrectionQuery(
                    fixture.Actor,
                    fixture.OriginalMembershipId,
                    replacementMembershipTypeId,
                    replacementStartDate),
                CancellationToken.None);
        Assert.Equal(PreviewIssuedMembershipSaleCorrectionStatus.Success, result.Status);
        return Assert.IsType<IssuedMembershipSaleCorrectionPreview>(result.Preview);
    }

    private static PreviewIssuedMembershipSaleCorrectionQueryHandler
        CreateIssuedSaleCorrectionPreviewHandler(BodyLifeDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(TestNow));

    private static ReplaceIssuedMembershipCommandHandler CreateReplaceIssuedSaleHandler(
        BodyLifeDbContext dbContext,
        IPaymentDayReconciliationStatusProvider? reconciliationStatusProvider = null) =>
        new(CreateIssuedSaleCorrectionExecutor(
            dbContext,
            reconciliationStatusProvider));

    private static CancelIssuedMembershipSaleCommandHandler CreateCancelIssuedSaleHandler(
        BodyLifeDbContext dbContext,
        IPaymentDayReconciliationStatusProvider? reconciliationStatusProvider = null) =>
        new(CreateIssuedSaleCorrectionExecutor(
            dbContext,
            reconciliationStatusProvider));

    private static IssuedMembershipSaleCorrectionCommandExecutor
        CreateIssuedSaleCorrectionExecutor(
            BodyLifeDbContext dbContext,
            IPaymentDayReconciliationStatusProvider? reconciliationStatusProvider = null)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        var auditAppender = new BusinessAuditAppender(dbContext);
        return new IssuedMembershipSaleCorrectionCommandExecutor(
            dbContext,
            auditAppender,
            new MembershipIssuePaymentWriter(dbContext, auditAppender),
            new MembershipStateCacheRebuilder(dbContext, timeProvider),
            reconciliationStatusProvider
                ?? new FixedPaymentDayReconciliationStatusProvider(),
            timeProvider);
    }

    private static CommandEnvelope CreateIssuedSaleCorrectionEnvelope(
        ActorContext actor,
        string idempotencyKey) => new(
            actor,
            new RequestCorrelationId($"correlation-{idempotencyKey}"),
            EntryOrigin.Normal,
            TestNow.AddMinutes(-5),
            idempotencyKey,
            "Incorrect Membership sale",
            "Replace or cancel exact sale");

    private static async Task<IssuedSaleDependencyFixture>
        InsertIssuedSaleDependencyAsync(
        PostgreSqlTestDatabase database,
        IssuedSaleCorrectionFixture fixture,
        string scenario)
    {
        if (scenario is "negative_source" or "negative_covering")
        {
            return await InsertIssuedSaleNegativeCoverageAsync(
                database,
                fixture,
                originalIsSource: scenario == "negative_source");
        }

        var dependencyId = scenario switch
        {
            "visit" => await InsertIssuedSaleCountedVisitAsync(database, fixture),
            "freeze" => await InsertIssuedSaleFreezeAsync(database, fixture),
            "non_working_day" => await InsertIssuedSaleNonWorkingDayAsync(
                database,
                fixture),
            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                "Issued-sale dependency scenario is not supported."),
        };
        return new IssuedSaleDependencyFixture(dependencyId, null);
    }

    private static async Task<Guid> InsertIssuedSaleFreezeAsync(
        PostgreSqlTestDatabase database,
        IssuedSaleCorrectionFixture fixture)
    {
        var freezeId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.freezes (
                id, client_id, membership_id, start_date, end_date, reason,
                occurred_at, recorded_at, recorded_by_account_id, session_id,
                entry_origin, entry_batch_id, status)
            values (
                @freeze_id, @client_id, @membership_id, @start_date, @end_date,
                'Issued-sale dependency', @occurred_at, @recorded_at,
                @account_id, @session_id, 'normal', null, 'active')
            """;
        command.Parameters.AddWithValue("freeze_id", freezeId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue(
            "membership_id",
            fixture.OriginalMembershipId);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            ExistingStartDate.AddDays(1));
        command.Parameters.AddWithValue(
            "end_date",
            NpgsqlDbType.Date,
            ExistingStartDate.AddDays(2));
        command.Parameters.AddWithValue("occurred_at", TestNow.AddHours(-2));
        command.Parameters.AddWithValue("recorded_at", TestNow.AddHours(-2));
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return freezeId;
    }

    private static async Task<Guid> InsertIssuedSaleNonWorkingDayAsync(
        PostgreSqlTestDatabase database,
        IssuedSaleCorrectionFixture fixture)
    {
        var periodId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var startDate = ExistingStartDate.AddDays(3);
        var endDate = ExistingStartDate.AddDays(4);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.non_working_periods (
                id, start_date, end_date, reason_code, reason_comment,
                created_at, created_by_account_id, session_id, status)
            values (
                @period_id, @start_date, @end_date, 'maintenance',
                'Issued-sale dependency', @recorded_at, @account_id,
                @session_id, 'active');

            insert into bodylife.non_working_period_applications (
                id, non_working_period_id, membership_id, client_id,
                applied_start_date, applied_end_date, previewed_at,
                confirmed_at, status)
            values (
                @application_id, @period_id, @membership_id, @client_id,
                @start_date, @end_date, @previewed_at, @recorded_at, 'active')
            """;
        command.Parameters.AddWithValue("period_id", periodId);
        command.Parameters.AddWithValue("application_id", applicationId);
        command.Parameters.AddWithValue("membership_id", fixture.OriginalMembershipId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, startDate);
        command.Parameters.AddWithValue("end_date", NpgsqlDbType.Date, endDate);
        command.Parameters.AddWithValue("previewed_at", TestNow.AddHours(-3));
        command.Parameters.AddWithValue("recorded_at", TestNow.AddHours(-2));
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
        return applicationId;
    }

    private static async Task<IssuedSaleDependencyFixture>
        InsertIssuedSaleNegativeCoverageAsync(
        PostgreSqlTestDatabase database,
        IssuedSaleCorrectionFixture fixture,
        bool originalIsSource)
    {
        var otherMembership = await InsertIssuedMembershipAsync(
            database,
            new IssueFixture(fixture.ClientId, fixture.MembershipTypeId),
            fixture.Actor.AccountId.Value,
            TestNow.AddDays(-2));
        var sourceMembershipId = originalIsSource
            ? fixture.OriginalMembershipId
            : otherMembership.MembershipId;
        var coveringMembershipId = originalIsSource
            ? otherMembership.MembershipId
            : fixture.OriginalMembershipId;
        var visitId = Guid.NewGuid();
        var oldConsumptionId = Guid.NewGuid();
        var newConsumptionId = Guid.NewGuid();
        var closureId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(
            2026,
            7,
            1,
            9,
            0,
            0,
            TimeSpan.Zero);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.visits (
                id, client_id, occurred_at, recorded_at, recorded_by_account_id,
                session_id, visit_kind, entry_origin, status)
            values (
                @visit_id, @client_id, @occurred_at, @occurred_at, @account_id,
                @session_id, 'membership', 'normal', 'active');

            insert into bodylife.visit_consumptions (
                id, visit_id, client_id, visit_kind, membership_id,
                consumption_type, source_fact_type, source_fact_id, recorded_at,
                recorded_by_account_id, recorded_session_id, status)
            values
                (
                    @old_consumption_id, @visit_id, @client_id, 'membership',
                    @source_membership_id, 'counted', 'visit', @visit_id,
                    @occurred_at, @account_id, @session_id, 'active'),
                (
                    @new_consumption_id, @visit_id, @client_id, 'membership',
                    @covering_membership_id, 'negative_coverage',
                    'negative_closure_item', @item_id, @occurred_at,
                    @account_id, @session_id, 'active');

            insert into bodylife.membership_negative_closures (
                id, client_id, closure_type, covering_membership_id,
                oldest_open_negative_visit_id, visits_count, occurred_at,
                recorded_at, recorded_by_account_id, session_id, entry_origin,
                entry_batch_id, idempotency_key, status)
            values (
                @closure_id, @client_id, 'new_membership',
                @covering_membership_id, @visit_id, 1, @occurred_at,
                @occurred_at, @account_id, @session_id, 'normal', null,
                @idempotency_key, 'active');

            insert into bodylife.membership_negative_closure_items (
                id, negative_closure_id, client_id, closure_line_id, sequence,
                visit_id, source_membership_id, old_consumption_id,
                covering_membership_id, new_consumption_id, status)
            values (
                @item_id, @closure_id, @client_id, null, 1, @visit_id,
                @source_membership_id, @old_consumption_id,
                @covering_membership_id, @new_consumption_id, 'active')
            """;
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("old_consumption_id", oldConsumptionId);
        command.Parameters.AddWithValue("new_consumption_id", newConsumptionId);
        command.Parameters.AddWithValue("closure_id", closureId);
        command.Parameters.AddWithValue("item_id", itemId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("source_membership_id", sourceMembershipId);
        command.Parameters.AddWithValue("covering_membership_id", coveringMembershipId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue(
            "idempotency_key",
            originalIsSource
                ? "issued-sale-negative-source"
                : "issued-sale-negative-covering");
        Assert.Equal(5, await command.ExecuteNonQueryAsync());
        return new IssuedSaleDependencyFixture(
            itemId,
            otherMembership.MembershipId);
    }

    private static async Task RebuildAndAssertIssuedSaleDependencyCachesAsync(
        PostgreSqlTestDatabase database,
        IssuedSaleCorrectionFixture fixture,
        IssuedSaleDependencyFixture dependencyFixture,
        string scenario)
    {
        var membershipIds = dependencyFixture.OtherMembershipId.HasValue
            ? new[]
            {
                fixture.OriginalMembershipId,
                dependencyFixture.OtherMembershipId.Value,
            }
            : [fixture.OriginalMembershipId];

        await using (var dbContext = database.CreateDbContext())
        {
            var rebuilder = new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(TestNow),
                [
                    new MembershipFreezeExtensionSourceReader(dbContext),
                    new MembershipNonWorkingDayExtensionSourceReader(dbContext),
                ]);
            foreach (var membershipId in membershipIds)
            {
                var rebuild = await rebuilder.RebuildAsync(membershipId);
                Assert.True(rebuild.Succeeded);
                Assert.NotNull(rebuild.State);
                Assert.Equal(
                    MembershipStateCacheRebuilder.CurrentRecalculationVersion,
                    rebuild.RecalculationVersion);
            }
        }

        var originalCache = await ReadCacheAsync(
            database,
            fixture.OriginalMembershipId);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            originalCache.RecalculationVersion);

        switch (scenario)
        {
            case "visit":
                AssertIssuedSaleDependencyCache(
                    originalCache,
                    countedVisits: 1,
                    remainingVisits: 1,
                    extensionDays: 0);
                break;
            case "freeze":
            case "non_working_day":
                AssertIssuedSaleDependencyCache(
                    originalCache,
                    countedVisits: 0,
                    remainingVisits: 2,
                    extensionDays: 2);
                break;
            case "negative_source":
            case "negative_covering":
                var otherCache = await ReadCacheAsync(
                    database,
                    dependencyFixture.OtherMembershipId!.Value);
                var originalIsSource = scenario == "negative_source";
                AssertIssuedSaleDependencyCache(
                    originalCache,
                    countedVisits: originalIsSource ? 0 : 1,
                    remainingVisits: originalIsSource ? 2 : 1,
                    extensionDays: 0);
                AssertIssuedSaleDependencyCache(
                    otherCache,
                    countedVisits: originalIsSource ? 1 : 0,
                    remainingVisits: originalIsSource ? 1 : 2,
                    extensionDays: 0);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    scenario,
                    "Issued-sale dependency scenario is not supported.");
        }
    }

    private static void AssertIssuedSaleDependencyCache(
        CacheRow cache,
        int countedVisits,
        int remainingVisits,
        int extensionDays)
    {
        Assert.Equal(countedVisits, cache.CountedVisits);
        Assert.Equal(remainingVisits, cache.RemainingVisits);
        Assert.Equal(0, cache.NegativeBalance);
        Assert.Null(cache.FirstNegativeVisitId);
        Assert.Null(cache.FirstNegativeVisitDate);
        Assert.Equal(extensionDays, cache.ExtensionDays);
        Assert.Equal(
            ExistingBaseEndDate.AddDays(extensionDays),
            cache.EffectiveEndDate);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            cache.RecalculationVersion);
    }

    private static async Task<Guid> InsertIssuedSaleCountedVisitAsync(
        PostgreSqlTestDatabase database,
        IssuedSaleCorrectionFixture fixture)
    {
        var visitId = Guid.NewGuid();
        var consumptionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.visits (
                id, client_id, occurred_at, recorded_at, recorded_by_account_id,
                session_id, visit_kind, entry_origin, status)
            values (
                @visit_id, @client_id, @occurred_at, @recorded_at, @account_id,
                @session_id, 'membership', 'normal', 'active');

            insert into bodylife.visit_consumptions (
                id, visit_id, client_id, visit_kind, membership_id,
                consumption_type, source_fact_type, source_fact_id, recorded_at,
                recorded_by_account_id, recorded_session_id, status)
            values (
                @consumption_id, @visit_id, @client_id, 'membership',
                @membership_id, 'counted', 'visit', @visit_id, @recorded_at,
                @account_id, @session_id, 'active');
            """;
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("consumption_id", consumptionId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_id", fixture.OriginalMembershipId);
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("occurred_at", TestNow.AddDays(-1));
        command.Parameters.AddWithValue("recorded_at", TestNow.AddDays(-1));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
        return visitId;
    }

    private static async Task<string[]> ReadIssuedSaleAuditActionsAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select action_type
            from bodylife.business_audit_entries
            order by action_type
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var actions = new List<string>();
        while (await reader.ReadAsync())
        {
            actions.Add(reader.GetString(0));
        }

        return actions.ToArray();
    }

    private static async Task AssertCanceledIssuedSaleProjectionsAsync(
        PostgreSqlTestDatabase database,
        IssuedSaleCorrectionFixture fixture)
    {
        await using var dbContext = database.CreateDbContext();
        var timeProvider = new FixedTimeProvider(TestNow);
        var dayStatus = new FixedPaymentDayReconciliationStatusProvider();

        var clientResult = await new GetClientPaymentRowsQueryHandler(
                dbContext,
                dayStatus,
                timeProvider)
            .ExecuteAsync(
                new GetClientPaymentRowsQuery(fixture.Actor, fixture.ClientId),
                CancellationToken.None);
        Assert.Equal(GetClientPaymentRowsStatus.Success, clientResult.Status);
        var canceledPayment = Assert.Single(clientResult.Page!.Items);
        Assert.Equal(fixture.OriginalPaymentId, canceledPayment.PaymentId);
        Assert.Equal(ClientPaymentRowStatus.Canceled, canceledPayment.Status);
        var cancellation = Assert.IsType<ClientPaymentCancellation>(
            canceledPayment.Cancellation);
        Assert.Equal("Incorrect Membership sale", cancellation.Reason);
        Assert.Null(canceledPayment.CorrectionFromOriginal);
        Assert.Null(canceledPayment.CorrectionToReplacement);

        var originalBusinessDate = BusinessTimeZone.GetBusinessDate(
            TestNow.AddDays(-3));
        var dailyResult = await new GetDailyPaymentSourceRowsQueryHandler(
                dbContext,
                dayStatus,
                timeProvider)
            .ExecuteAsync(
                new GetDailyPaymentSourceRowsQuery(
                    fixture.Actor,
                    originalBusinessDate),
                CancellationToken.None);
        Assert.Equal(GetDailyPaymentSourceRowsStatus.Success, dailyResult.Status);
        Assert.Equal(0, dailyResult.Snapshot!.ActivePaymentCount);
        Assert.Equal(new Money(0m, "UAH"), dailyResult.Snapshot.DailyCashSum);
        Assert.Equal(
            ClientPaymentRowStatus.Canceled,
            Assert.Single(dailyResult.Snapshot.Rows).Payment.Status);

        var auditEntries = new GetClientAuditEntriesQueryHandler(
            dbContext,
            timeProvider);
        var paymentHistoryResult = await new GetClientPaymentHistorySourceRowsQueryHandler(
                dbContext,
                auditEntries)
            .ExecuteAsync(
                new GetClientPaymentHistorySourceRowsQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None);
        Assert.Equal(
            GetClientPaymentHistorySourceRowsStatus.Success,
            paymentHistoryResult.Status);
        var paymentHistory = Assert.Single(paymentHistoryResult.Page!.Items);
        Assert.Equal(ClientPaymentHistorySourceKind.CanceledPayment, paymentHistory.Kind);
        Assert.Equal(
            "Incorrect Membership sale",
            Assert.IsType<PaymentCancellationHistorySource>(
                paymentHistory.Cancellation).Reason);

        var membershipHistoryResult = await new GetClientMembershipHistorySourceRowsQueryHandler(
                dbContext,
                auditEntries)
            .ExecuteAsync(
                new GetClientMembershipHistorySourceRowsQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None);
        Assert.Equal(
            GetClientMembershipHistorySourceRowsStatus.Success,
            membershipHistoryResult.Status);
        var membershipHistory = Assert.Single(membershipHistoryResult.Page!.Items);
        Assert.Equal(MembershipAuditActions.SaleCanceled, membershipHistory.AuditEntry.ActionType);
        Assert.Equal(
            IssuedMembershipLifecycleStatus.Canceled,
            membershipHistory.IssuedMembership!.Status);

        var receptionResult = await CreateIssuedSaleReceptionHandler(
                dbContext,
                timeProvider)
            .ExecuteAsync(
                new GetReceptionActivityQuery(
                    fixture.Actor,
                    BusinessTimeZone.GetBusinessDate(TestNow),
                    Limit: 10),
                CancellationToken.None);
        Assert.Equal(GetReceptionActivityStatus.Success, receptionResult.Status);
        var eventRow = Assert.Single(
            receptionResult.Page!.Items,
            item => item.EventType == ReceptionActivityEventType.MembershipSaleCanceled);
        Assert.True(eventRow.IsCorrectionOrCancellation);
        Assert.Equal(
            new ReceptionActivityRelatedEntity(
                ReceptionActivityRelatedEntityType.Payment,
                fixture.OriginalPaymentId),
            Assert.Single(eventRow.RelatedEntities));
    }

    private static async Task AssertReplacedIssuedSaleProjectionsAsync(
        PostgreSqlTestDatabase database,
        IssuedSaleCorrectionFixture fixture,
        Guid replacementMembershipId,
        Guid replacementPaymentId)
    {
        await using var dbContext = database.CreateDbContext();
        var timeProvider = new FixedTimeProvider(TestNow);
        var dayStatus = new FixedPaymentDayReconciliationStatusProvider();

        var clientResult = await new GetClientPaymentRowsQueryHandler(
                dbContext,
                dayStatus,
                timeProvider)
            .ExecuteAsync(
                new GetClientPaymentRowsQuery(fixture.Actor, fixture.ClientId),
                CancellationToken.None);
        Assert.Equal(GetClientPaymentRowsStatus.Success, clientResult.Status);
        var clientRows = clientResult.Page!.Items;
        Assert.Equal(2, clientRows.Count);
        var originalPayment = Assert.Single(
            clientRows,
            payment => payment.PaymentId == fixture.OriginalPaymentId);
        var replacementPayment = Assert.Single(
            clientRows,
            payment => payment.PaymentId == replacementPaymentId);
        Assert.Equal(ClientPaymentRowStatus.Replaced, originalPayment.Status);
        Assert.Equal(ClientPaymentRowStatus.Active, replacementPayment.Status);
        var outgoing = Assert.IsType<ClientPaymentCorrection>(
            originalPayment.CorrectionToReplacement);
        Assert.Equal(replacementPaymentId, outgoing.ReplacementPaymentId);
        Assert.Equal(["membership_sale"], outgoing.ChangedFields);
        var incoming = Assert.IsType<ClientPaymentCorrection>(
            replacementPayment.CorrectionFromOriginal);
        Assert.Equal(outgoing.CorrectionId, incoming.CorrectionId);
        Assert.Equal(outgoing.OriginalPaymentId, incoming.OriginalPaymentId);
        Assert.Equal(outgoing.ReplacementPaymentId, incoming.ReplacementPaymentId);
        Assert.Equal(outgoing.ChangedFields, incoming.ChangedFields);
        Assert.Empty(originalPayment.AllowedActions.Items);
        Assert.Empty(replacementPayment.AllowedActions.Items);

        var originalDailyResult = await new GetDailyPaymentSourceRowsQueryHandler(
                dbContext,
                dayStatus,
                timeProvider)
            .ExecuteAsync(
                new GetDailyPaymentSourceRowsQuery(
                    fixture.Actor,
                    BusinessTimeZone.GetBusinessDate(TestNow.AddDays(-3))),
                CancellationToken.None);
        Assert.Equal(
            GetDailyPaymentSourceRowsStatus.Success,
            originalDailyResult.Status);
        Assert.Equal(0, originalDailyResult.Snapshot!.ActivePaymentCount);
        Assert.Equal(new Money(0m, "UAH"), originalDailyResult.Snapshot.DailyCashSum);

        var replacementDailyResult = await new GetDailyPaymentSourceRowsQueryHandler(
                dbContext,
                dayStatus,
                timeProvider)
            .ExecuteAsync(
                new GetDailyPaymentSourceRowsQuery(
                    fixture.Actor,
                    BusinessTimeZone.GetBusinessDate(TestNow.AddMinutes(-5))),
                CancellationToken.None);
        Assert.Equal(
            GetDailyPaymentSourceRowsStatus.Success,
            replacementDailyResult.Status);
        Assert.Equal(1, replacementDailyResult.Snapshot!.ActivePaymentCount);
        Assert.Equal(
            new Money(1200m, "UAH"),
            replacementDailyResult.Snapshot.DailyCashSum);
        Assert.Equal(
            replacementPaymentId,
            Assert.Single(replacementDailyResult.Snapshot.Rows).Payment.PaymentId);

        var auditEntries = new GetClientAuditEntriesQueryHandler(
            dbContext,
            timeProvider);
        var paymentHistoryResult = await new GetClientPaymentHistorySourceRowsQueryHandler(
                dbContext,
                auditEntries)
            .ExecuteAsync(
                new GetClientPaymentHistorySourceRowsQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None);
        Assert.Equal(
            GetClientPaymentHistorySourceRowsStatus.Success,
            paymentHistoryResult.Status);
        Assert.Equal(2, paymentHistoryResult.Page!.Items.Count);
        var correctionHistory = Assert.Single(
            paymentHistoryResult.Page.Items,
            item => item.Kind == ClientPaymentHistorySourceKind.CorrectedPayment);
        var correction = Assert.IsType<PaymentCorrectionHistorySource>(
            correctionHistory.Correction);
        Assert.Equal(fixture.OriginalPaymentId, correction.OriginalPaymentId);
        Assert.Equal(replacementPaymentId, correction.ReplacementPaymentId);
        Assert.Equal(["membership_sale"], correction.ChangedFields);
        Assert.Equal(
            ClientPaymentRowStatus.Replaced,
            correction.OriginalPayment.CurrentStatus);
        Assert.Equal(
            ClientPaymentRowStatus.Active,
            correction.ReplacementPayment.CurrentStatus);

        var membershipHistoryResult = await new GetClientMembershipHistorySourceRowsQueryHandler(
                dbContext,
                auditEntries)
            .ExecuteAsync(
                new GetClientMembershipHistorySourceRowsQuery(
                    fixture.Actor,
                    fixture.ClientId),
                CancellationToken.None);
        Assert.Equal(
            GetClientMembershipHistorySourceRowsStatus.Success,
            membershipHistoryResult.Status);
        var membershipHistory = Assert.Single(membershipHistoryResult.Page!.Items);
        Assert.Equal(MembershipAuditActions.Replaced, membershipHistory.AuditEntry.ActionType);
        Assert.Equal(
            IssuedMembershipLifecycleStatus.Corrected,
            membershipHistory.IssuedMembership!.Status);

        var receptionResult = await CreateIssuedSaleReceptionHandler(
                dbContext,
                timeProvider)
            .ExecuteAsync(
                new GetReceptionActivityQuery(
                    fixture.Actor,
                    BusinessTimeZone.GetBusinessDate(TestNow),
                    Limit: 10),
                CancellationToken.None);
        Assert.Equal(GetReceptionActivityStatus.Success, receptionResult.Status);
        var eventRow = Assert.Single(
            receptionResult.Page!.Items,
            item => item.EventType == ReceptionActivityEventType.MembershipReplaced);
        Assert.True(eventRow.IsCorrectionOrCancellation);
        Assert.Equal(
            new[]
            {
                new ReceptionActivityRelatedEntity(
                    ReceptionActivityRelatedEntityType.Payment,
                    fixture.OriginalPaymentId),
                new ReceptionActivityRelatedEntity(
                    ReceptionActivityRelatedEntityType.Membership,
                    replacementMembershipId),
                new ReceptionActivityRelatedEntity(
                    ReceptionActivityRelatedEntityType.Payment,
                    replacementPaymentId),
            },
            eventRow.RelatedEntities);
    }

    private static GetReceptionActivityQueryHandler CreateIssuedSaleReceptionHandler(
        BodyLifeDbContext dbContext,
        TimeProvider timeProvider) => new(
        dbContext,
        timeProvider,
        new EmptyReceptionActivityCursorProtector(),
        new GetClientMembershipStatesQueryHandler(dbContext, timeProvider));

    private static void AssertIssuedSaleCorrectionSuccess(
        CommandResult result,
        Guid clientId)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Equal(
            ReplaceIssuedMembershipCommand.PrimaryEntityType,
            result.PrimaryEntityId!.Value.Type);
        Assert.NotEqual(Guid.Empty, result.PrimaryEntityId.Value.Value);
        Assert.Equal(
            new EntityId(
                ReplaceIssuedMembershipCommand.CanonicalRereadEntityType,
                clientId),
            result.RereadTargetId);
        Assert.True(result.AuditEntryId.HasValue);
        Assert.Empty(result.Errors);
    }

    private static void AssertIssuedSaleCorrectionError(
        CommandResult result,
        CommandErrorCode expectedCode)
    {
        Assert.Equal(CommandStatus.Error, result.Status);
        Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
        Assert.Null(result.AuditEntryId);
    }

    private sealed record IssuedSaleCorrectionFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid MembershipTypeId,
        DateTimeOffset MembershipTypeUpdatedAt,
        Guid OriginalMembershipId,
        Guid OriginalPaymentId);

    private sealed record IssuedSaleDependencyFixture(
        Guid DependencyId,
        Guid? OtherMembershipId);

    private sealed class FixedPaymentDayReconciliationStatusProvider(
        params DateOnly[] reconciledDates)
        : IPaymentDayReconciliationStatusProvider
    {
        private readonly HashSet<DateOnly> reconciled = [.. reconciledDates];

        public Task<PaymentDayReconciliationStatus> GetStatusAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(reconciled.Contains(businessDate)
                ? PaymentDayReconciliationStatus.Reconciled
                : PaymentDayReconciliationStatus.Open);
    }

    private sealed class EmptyReceptionActivityCursorProtector
        : IReceptionActivityCursorProtector
    {
        public string Encode(
            DateOnly date,
            DateTimeOffset recordedAt,
            Guid auditId) => throw new InvalidOperationException(
                "The focused issued-sale projection test does not paginate.");

        public bool TryDecode(
            string? value,
            DateOnly requestedDate,
            out ReceptionActivityCursor? cursor)
        {
            cursor = null;
            return value is null;
        }
    }
}
