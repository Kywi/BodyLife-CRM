using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlIssueMembershipCommandTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        13,
        14,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly NewStartDate = new(2026, 8, 1);
    private static readonly DateOnly NewBaseEndDate = new(2026, 8, 30);
    private static readonly DateOnly ExistingStartDate = new(2026, 7, 1);
    private static readonly DateOnly ExistingBaseEndDate = new(2026, 7, 30);

    [PostgreSqlFact]
    public async Task NamedAdminIssuesSnapshotCacheAuditAndIdempotencyAtomically()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            deviceLabel: "reception tablet");
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var command = CreateCommand(actor, fixture, "issue-success");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);

        AssertSuccessfulResult(result, fixture.ClientId);
        Assert.Empty(result.Warnings);
        var membershipId = result.PrimaryEntityId!.Value.Value;
        var membership = await ReadIssuedMembershipAsync(database, membershipId);
        Assert.Equal(fixture.ClientId, membership.ClientId);
        Assert.Equal(fixture.MembershipTypeId, membership.MembershipTypeId);
        Assert.Equal("Eight visits / 30 days", membership.TypeNameSnapshot);
        Assert.Equal(30, membership.DurationDaysSnapshot);
        Assert.Equal(8, membership.VisitsLimitSnapshot);
        Assert.Equal(1200m, membership.PriceAmountSnapshot);
        Assert.Equal("UAH", membership.PriceCurrencySnapshot);
        Assert.Equal(NewStartDate, membership.StartDate);
        Assert.Equal(NewBaseEndDate, membership.BaseEndDate);
        Assert.Equal(TestNow, membership.IssuedAt);
        Assert.Equal(actor.AccountId.Value, membership.IssuedByAccountId);
        Assert.Equal("active", membership.Status);
        Assert.Equal("normal", membership.EntryOrigin);
        Assert.Null(membership.EntryBatchId);
        Assert.Equal("Front desk issue", membership.Comment);

        var cache = await ReadCacheAsync(database, membershipId);
        Assert.Equal(0, cache.CountedVisits);
        Assert.Equal(8, cache.RemainingVisits);
        Assert.Equal(0, cache.NegativeBalance);
        Assert.Null(cache.FirstNegativeVisitId);
        Assert.Null(cache.FirstNegativeVisitDate);
        Assert.Equal(0, cache.ExtensionDays);
        Assert.Equal(NewBaseEndDate, cache.EffectiveEndDate);
        Assert.Null(cache.LastCountedVisitAt);
        Assert.Equal(TestNow, cache.RecalculatedAt);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            cache.RecalculationVersion);

        var audit = await ReadAuditAsync(database, result.AuditEntryId!.Value.Value);
        Assert.Equal(MembershipAuditActions.Issued, audit.ActionType);
        Assert.Equal(MembershipAuditActions.MembershipEntityType, audit.EntityType);
        Assert.Equal(membershipId, audit.EntityId);
        Assert.Equal(actor.AccountId.Value, audit.ActorAccountId);
        Assert.Equal("named_admin", audit.ActorAccountType);
        Assert.Equal("admin", audit.ActorRole);
        Assert.Equal(actor.SessionId.Value, audit.SessionId);
        Assert.Equal("reception tablet", audit.DeviceLabel);
        Assert.Equal(TestNow, audit.OccurredAt);
        Assert.Equal(TestNow, audit.RecordedAt);
        Assert.Equal("normal", audit.EntryOrigin);
        Assert.Equal(command.Envelope.IdempotencyKey, audit.IdempotencyKey);
        Assert.Equal(command.Envelope.RequestCorrelationId.Value, audit.RequestCorrelationId);
        Assert.Null(audit.Reason);
        Assert.Equal("Front desk issue", audit.Comment);
        Assert.Equal("{}", audit.BeforeSummary);

        using var related = JsonDocument.Parse(audit.RelatedEntityRefs);
        Assert.Equal(3, related.RootElement.EnumerateObject().Count());
        Assert.Equal(
            fixture.ClientId,
            related.RootElement.GetProperty("clientId").GetGuid());
        Assert.Equal(
            fixture.MembershipTypeId,
            related.RootElement.GetProperty("membershipTypeId").GetGuid());
        Assert.Equal(
            JsonValueKind.Null,
            related.RootElement.GetProperty("paymentId").ValueKind);

        using var after = JsonDocument.Parse(audit.AfterSummary);
        var summary = after.RootElement;
        Assert.Equal(12, summary.EnumerateObject().Count());
        Assert.Equal(membershipId, summary.GetProperty("membershipId").GetGuid());
        Assert.Equal(fixture.ClientId, summary.GetProperty("clientId").GetGuid());
        Assert.Equal(
            fixture.MembershipTypeId,
            summary.GetProperty("membershipTypeId").GetGuid());
        Assert.Equal("2026-08-01", summary.GetProperty("startDate").GetString());
        Assert.Equal("2026-08-30", summary.GetProperty("baseEndDate").GetString());
        Assert.Equal(TestNow, summary.GetProperty("issuedAt").GetDateTimeOffset());
        Assert.Equal("active", summary.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("negativeHandlingDecision").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("existingNegativeState").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("payment").ValueKind);
        var snapshot = summary.GetProperty("snapshot");
        Assert.Equal(5, snapshot.EnumerateObject().Count());
        Assert.Equal("Eight visits / 30 days", snapshot.GetProperty("typeName").GetString());
        Assert.Equal(30, snapshot.GetProperty("durationDays").GetInt32());
        Assert.Equal(8, snapshot.GetProperty("visitsLimit").GetInt32());
        Assert.Equal(1200m, snapshot.GetProperty("priceAmount").GetDecimal());
        Assert.Equal("UAH", snapshot.GetProperty("priceCurrency").GetString());
        var initialState = summary.GetProperty("initialState");
        Assert.Equal(8, initialState.EnumerateObject().Count());
        Assert.Equal(0, initialState.GetProperty("countedVisits").GetInt32());
        Assert.Equal(8, initialState.GetProperty("remainingVisits").GetInt32());
        Assert.Equal(0, initialState.GetProperty("negativeBalance").GetInt32());
        Assert.Equal(
            JsonValueKind.Null,
            initialState.GetProperty("firstNegativeVisitDate").ValueKind);
        Assert.Equal(0, initialState.GetProperty("extensionDays").GetInt32());
        Assert.Equal(
            "2026-08-30",
            initialState.GetProperty("effectiveEndDate").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            initialState.GetProperty("lastCountedVisitAt").ValueKind);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            initialState.GetProperty("recalculationVersion").GetInt32());

        var idempotency = await ReadIdempotencyAsync(
            database,
            "IssueMembership",
            "issue-success");
        Assert.Equal("succeeded", idempotency.Status);
        Assert.Equal(membershipId, idempotency.PrimaryEntityId);
        Assert.Equal(fixture.ClientId, idempotency.RereadTargetId);
        Assert.Equal(result.AuditEntryId.Value.Value, idempotency.AuditEntryId);
        Assert.Equal("normal", idempotency.EntryOrigin);
        Assert.Equal(64, idempotency.FingerprintLength);
        Assert.Equal(1L, await CountRowsAsync(database, "issued_memberships"));
        Assert.Equal(1L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(0L, await CountRowsAsync(database, "payments"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task OptionalCashPaymentCommitsSourceAndBothAuditsAtomically()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            deviceLabel: "reception tablet");
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var command = CreateCommand(
            actor,
            fixture,
            "issue-with-payment",
            payment: CreateIssuePayment());

        var result = await CreateHandler(dbContext).ExecuteAsync(
            command,
            CancellationToken.None);

        AssertSuccessfulResult(result, fixture.ClientId);
        var membershipId = result.PrimaryEntityId!.Value.Value;
        var payment = await ReadPaymentForMembershipAsync(database, membershipId);
        Assert.Equal(fixture.ClientId, payment.ClientId);
        Assert.Equal(membershipId, payment.MembershipId);
        Assert.Equal(1200m, payment.Amount);
        Assert.Equal("UAH", payment.Currency);
        Assert.Equal("cash", payment.Method);
        Assert.Equal("membership_sale", payment.PaymentContext);
        Assert.Equal(TestNow, payment.OccurredAt);
        Assert.Equal(TestNow, payment.RecordedAt);
        Assert.Equal(actor.AccountId.Value, payment.RecordedByAccountId);
        Assert.Equal(actor.SessionId.Value, payment.SessionId);
        Assert.Equal("normal", payment.EntryOrigin);
        Assert.Null(payment.EntryBatchId);
        Assert.Equal("Front desk issue", payment.Comment);
        Assert.Equal("active", payment.Status);

        var membershipAudit = await ReadAuditAsync(
            database,
            result.AuditEntryId!.Value.Value);
        using var membershipRelated = JsonDocument.Parse(
            membershipAudit.RelatedEntityRefs);
        Assert.Equal(
            payment.Id,
            membershipRelated.RootElement.GetProperty("paymentId").GetGuid());
        using var membershipAfter = JsonDocument.Parse(membershipAudit.AfterSummary);
        var paymentSummary = membershipAfter.RootElement.GetProperty("payment");
        Assert.Equal(payment.Id, paymentSummary.GetProperty("paymentId").GetGuid());
        Assert.Equal(1200m, paymentSummary.GetProperty("amount").GetDecimal());
        Assert.Equal("UAH", paymentSummary.GetProperty("currency").GetString());
        Assert.Equal("cash", paymentSummary.GetProperty("method").GetString());
        Assert.Equal(
            "membership_sale",
            paymentSummary.GetProperty("paymentContext").GetString());

        var paymentAuditId = paymentSummary
            .GetProperty("paymentAuditEntryId")
            .GetGuid();
        var paymentAudit = await ReadAuditAsync(database, paymentAuditId);
        Assert.Equal(PaymentAuditActions.Created, paymentAudit.ActionType);
        Assert.Equal(PaymentAuditActions.EntityType, paymentAudit.EntityType);
        Assert.Equal(payment.Id, paymentAudit.EntityId);
        Assert.Equal(actor.AccountId.Value, paymentAudit.ActorAccountId);
        Assert.Equal(actor.SessionId.Value, paymentAudit.SessionId);
        Assert.Equal(TestNow, paymentAudit.OccurredAt);
        Assert.Equal(TestNow, paymentAudit.RecordedAt);
        Assert.Equal(command.Envelope.IdempotencyKey, paymentAudit.IdempotencyKey);
        Assert.Equal(
            command.Envelope.RequestCorrelationId.Value,
            paymentAudit.RequestCorrelationId);
        using var paymentRelated = JsonDocument.Parse(paymentAudit.RelatedEntityRefs);
        Assert.Equal(
            fixture.ClientId,
            paymentRelated.RootElement.GetProperty("clientId").GetGuid());
        Assert.Equal(
            membershipId,
            paymentRelated.RootElement.GetProperty("membershipId").GetGuid());

        var idempotency = await ReadIdempotencyAsync(
            database,
            "IssueMembership",
            "issue-with-payment");
        Assert.Equal(membershipId, idempotency.PrimaryEntityId);
        Assert.Equal(result.AuditEntryId.Value.Value, idempotency.AuditEntryId);
        Assert.Equal(1L, await CountRowsAsync(database, "issued_memberships"));
        Assert.Equal(1L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(1L, await CountRowsAsync(database, "payments"));
        Assert.Equal(2L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task OwnerAndSharedReceptionActorsCanIssueMemberships()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var sharedReception = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin);
        var ownerFixture = await SeedIssueFixtureAsync(database, owner.AccountId.Value);
        var receptionFixture = await SeedIssueFixtureAsync(
            database,
            sharedReception.AccountId.Value);
        var handler = CreateHandler(dbContext);

        var ownerResult = await handler.ExecuteAsync(
            CreateCommand(owner, ownerFixture, "owner-issue"),
            CancellationToken.None);
        var receptionResult = await handler.ExecuteAsync(
            CreateCommand(sharedReception, receptionFixture, "reception-issue"),
            CancellationToken.None);

        AssertSuccessfulResult(ownerResult, ownerFixture.ClientId);
        AssertSuccessfulResult(receptionResult, receptionFixture.ClientId);
        Assert.Equal(2L, await CountRowsAsync(database, "issued_memberships"));
        Assert.Equal(2L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(2L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(2L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ForgedInactiveExpiredAndUnknownActorsAreDeniedWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, owner.AccountId.Value);
        var inactive = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            isActive: false);
        var expired = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            sessionExpiresAt: TestNow);
        var unknownSession = owner with { SessionId = SessionId.New() };
        var forgedShape = owner with
        {
            Role = ActorRole.Admin,
            AccountKind = AccountKind.Owner,
        };
        var handler = CreateHandler(dbContext);

        var results = new[]
        {
            await handler.ExecuteAsync(
                CreateCommand(inactive, fixture, "inactive-actor"),
                CancellationToken.None),
            await handler.ExecuteAsync(
                CreateCommand(expired, fixture, "expired-actor"),
                CancellationToken.None),
            await handler.ExecuteAsync(
                CreateCommand(unknownSession, fixture, "unknown-session"),
                CancellationToken.None),
            await handler.ExecuteAsync(
                CreateCommand(forgedShape, fixture, "forged-shape"),
                CancellationToken.None),
        };

        Assert.All(results, result => AssertError(result, CommandErrorCode.PermissionDenied));
        await AssertNoIssueMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task InvalidSelectorsEnvelopeOriginBatchAndDecisionAreRejected()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var valid = CreateCommand(actor, fixture, "valid-shape");
        var handler = CreateHandler(dbContext);

        var results = new[]
        {
            (
                await handler.ExecuteAsync(
                    valid with { ClientId = Guid.Empty },
                    CancellationToken.None),
                "clientId"),
            (
                await handler.ExecuteAsync(
                    valid with { MembershipTypeId = Guid.Empty },
                    CancellationToken.None),
                "membershipTypeId"),
            (
                await handler.ExecuteAsync(
                    valid with { StartDate = default },
                    CancellationToken.None),
                "startDate"),
            (
                await handler.ExecuteAsync(
                    valid with
                    {
                        Envelope = valid.Envelope with { IdempotencyKey = null },
                    },
                    CancellationToken.None),
                "idempotencyKey"),
            (
                await handler.ExecuteAsync(
                    valid with
                    {
                        Envelope = valid.Envelope with
                        {
                            EntryOrigin = EntryOrigin.ManualBackfill,
                            OccurredAt = TestNow.AddDays(-1),
                            Reason = "Backfill is deferred",
                        },
                    },
                    CancellationToken.None),
                "entryOrigin"),
            (
                await handler.ExecuteAsync(
                    valid with { EntryBatchId = Guid.NewGuid() },
                    CancellationToken.None),
                "entryBatchId"),
            (
                await handler.ExecuteAsync(
                    valid with
                    {
                        Payment = new MembershipIssuePayment(
                            new Money(0m, "UAH"),
                            PaymentContext.MembershipSale),
                    },
                    CancellationToken.None),
                "payment.amount"),
            (
                await handler.ExecuteAsync(
                    valid with
                    {
                        Payment = new MembershipIssuePayment(
                            new Money(1200m, "UAH"),
                            PaymentContext.OneOff),
                    },
                    CancellationToken.None),
                "payment.paymentContext"),
            (
                await handler.ExecuteAsync(
                    valid with
                    {
                        Envelope = valid.Envelope with
                        {
                            Comment = new string('x', 1001),
                        },
                        Payment = CreateIssuePayment(),
                    },
                    CancellationToken.None),
                "envelope.comment"),
            (
                await handler.ExecuteAsync(
                    valid with
                    {
                        NegativeHandlingDecision =
                            (MembershipNegativeHandlingDecision)999,
                    },
                    CancellationToken.None),
                "negativeHandlingDecision"),
            (
                await handler.ExecuteAsync(
                    valid with
                    {
                        Envelope = valid.Envelope with
                        {
                            Comment = new string('x', 2001),
                        },
                    },
                    CancellationToken.None),
                "envelope.comment"),
        };

        Assert.All(
            results,
            item => AssertError(
                item.Item1,
                CommandErrorCode.ValidationFailed,
                item.Item2));
        await AssertNoIssueMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task MissingClientTypeAndInactiveTypeReturnStableFailures()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var inactiveTypeId = await InsertMembershipTypeAsync(database, isActive: false);
        var handler = CreateHandler(dbContext);

        var missingClient = await handler.ExecuteAsync(
            CreateCommand(actor, fixture, "missing-client") with
            {
                ClientId = Guid.NewGuid(),
            },
            CancellationToken.None);
        var missingType = await handler.ExecuteAsync(
            CreateCommand(actor, fixture, "missing-type") with
            {
                MembershipTypeId = Guid.NewGuid(),
            },
            CancellationToken.None);
        var inactiveType = await handler.ExecuteAsync(
            CreateCommand(actor, fixture, "inactive-type") with
            {
                MembershipTypeId = inactiveTypeId,
            },
            CancellationToken.None);
        var unnecessaryDecision = await handler.ExecuteAsync(
            CreateCommand(
                actor,
                fixture,
                "unnecessary-decision",
                MembershipNegativeHandlingDecision.LeaveVisible),
            CancellationToken.None);

        AssertError(missingClient, CommandErrorCode.NotFound, "clientId");
        AssertError(missingType, CommandErrorCode.NotFound, "membershipTypeId");
        AssertError(
            inactiveType,
            CommandErrorCode.MembershipTypeInactive,
            "membershipTypeId");
        AssertError(
            unnecessaryDecision,
            CommandErrorCode.ValidationFailed,
            "negativeHandlingDecision");
        await AssertNoIssueMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task ExistingNegativeRequiresDecisionAndLeaveVisiblePreservesWarning()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var existing = await InsertIssuedMembershipAsync(
            database,
            fixture,
            actor.AccountId.Value);
        await InsertCacheAsync(
            database,
            existing,
            remainingVisits: -2,
            firstNegativeVisitDate: new DateOnly(2026, 7, 29));
        var cacheBefore = await ReadCacheAsync(database, existing.MembershipId);
        var handler = CreateHandler(dbContext);

        var missingDecision = await handler.ExecuteAsync(
            CreateCommand(actor, fixture, "negative-missing"),
            CancellationToken.None);
        var deferredDecision = await handler.ExecuteAsync(
            CreateCommand(
                actor,
                fixture,
                "negative-deferred",
                MembershipNegativeHandlingDecision.CoverWithNewMembership),
            CancellationToken.None);
        var leaveVisible = await handler.ExecuteAsync(
            CreateCommand(
                actor,
                fixture,
                "negative-visible",
                MembershipNegativeHandlingDecision.LeaveVisible),
            CancellationToken.None);

        AssertError(
            missingDecision,
            CommandErrorCode.NegativeDecisionRequired,
            "negativeHandlingDecision");
        AssertError(
            deferredDecision,
            CommandErrorCode.MembershipNotEligible,
            "negativeHandlingDecision");
        AssertSuccessfulResult(leaveVisible, fixture.ClientId);
        Assert.Equal([MembershipWarningCodes.NegativeBalance], leaveVisible.Warnings);
        Assert.Equal(
            cacheBefore,
            await ReadCacheAsync(database, existing.MembershipId));
        var newCache = await ReadCacheAsync(
            database,
            leaveVisible.PrimaryEntityId!.Value.Value);
        Assert.Equal(8, newCache.RemainingVisits);
        Assert.Equal(0, newCache.NegativeBalance);

        var audit = await ReadAuditAsync(
            database,
            leaveVisible.AuditEntryId!.Value.Value);
        using var after = JsonDocument.Parse(audit.AfterSummary);
        Assert.Equal(
            "leave_visible",
            after.RootElement.GetProperty("negativeHandlingDecision").GetString());
        var negativeState = after.RootElement.GetProperty("existingNegativeState");
        Assert.Equal(2, negativeState.GetProperty("negativeBalance").GetInt32());
        Assert.Equal(
            "2026-07-29",
            negativeState.GetProperty("firstNegativeVisitDate").GetString());
        Assert.Equal(2L, await CountRowsAsync(database, "issued_memberships"));
        Assert.Equal(2L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(1L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task MissingStaleAndInconsistentExistingCacheFailWithoutRepair()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var existing = await InsertIssuedMembershipAsync(
            database,
            fixture,
            actor.AccountId.Value);
        var handler = CreateHandler(dbContext);

        var missing = await handler.ExecuteAsync(
            CreateCommand(actor, fixture, "cache-missing"),
            CancellationToken.None);
        await InsertCacheAsync(
            database,
            existing,
            remainingVisits: 1,
            firstNegativeVisitDate: null,
            recalculationVersion:
                MembershipStateCacheRebuilder.CurrentRecalculationVersion - 1);
        var staleBefore = await ReadCacheAsync(database, existing.MembershipId);
        var stale = await handler.ExecuteAsync(
            CreateCommand(actor, fixture, "cache-stale"),
            CancellationToken.None);
        await UpdateCacheVersionAndEffectiveEndAsync(
            database,
            existing.MembershipId,
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            ExistingBaseEndDate.AddDays(1));
        var inconsistentBefore = await ReadCacheAsync(database, existing.MembershipId);
        var inconsistent = await handler.ExecuteAsync(
            CreateCommand(actor, fixture, "cache-inconsistent"),
            CancellationToken.None);

        AssertError(missing, CommandErrorCode.RecalculationFailed);
        AssertError(stale, CommandErrorCode.RecalculationFailed);
        AssertError(inconsistent, CommandErrorCode.RecalculationFailed);
        Assert.Equal(staleBefore with
        {
            RecalculationVersion =
                MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            EffectiveEndDate = ExistingBaseEndDate.AddDays(1),
        }, inconsistentBefore);
        Assert.Equal(
            inconsistentBefore,
            await ReadCacheAsync(database, existing.MembershipId));
        Assert.Equal(1L, await CountRowsAsync(database, "issued_memberships"));
        Assert.Equal(1L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task MultipleNegativeMembershipsRequireExplicitSelectionPolicy()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var first = await InsertIssuedMembershipAsync(
            database,
            fixture,
            actor.AccountId.Value,
            issuedAt: TestNow.AddDays(-3));
        var second = await InsertIssuedMembershipAsync(
            database,
            fixture,
            actor.AccountId.Value,
            issuedAt: TestNow.AddDays(-2));
        await InsertCacheAsync(
            database,
            first,
            remainingVisits: -1,
            firstNegativeVisitDate: new DateOnly(2026, 7, 28));
        await InsertCacheAsync(
            database,
            second,
            remainingVisits: -2,
            firstNegativeVisitDate: new DateOnly(2026, 7, 29));

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                actor,
                fixture,
                "ambiguous-negative",
                MembershipNegativeHandlingDecision.LeaveVisible),
            CancellationToken.None);

        AssertError(result, CommandErrorCode.ValidationFailed, "clientId");
        Assert.Equal(2L, await CountRowsAsync(database, "issued_memberships"));
        Assert.Equal(2L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task CalendarOverflowReturnsStartDateValidationFailure()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var clientId = await InsertClientAsync(database, actor.AccountId.Value);
        var membershipTypeId = await InsertMembershipTypeAsync(
            database,
            durationDays: 2);
        var fixture = new IssueFixture(clientId, membershipTypeId);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(actor, fixture, "calendar-overflow") with
            {
                StartDate = DateOnly.MaxValue,
            },
            CancellationToken.None);

        AssertError(result, CommandErrorCode.ValidationFailed, "startDate");
        await AssertNoIssueMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task IdempotentReplayReturnsOriginalAndChangedPayloadIsDuplicate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var handler = CreateHandler(dbContext);
        var command = CreateCommand(
            actor,
            fixture,
            "issue-replay",
            payment: CreateIssuePayment());

        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var changed = await handler.ExecuteAsync(
            command with
            {
                Payment = new MembershipIssuePayment(
                    new Money(1300m, "UAH"),
                    PaymentContext.MembershipSale),
            },
            CancellationToken.None);

        AssertSuccessfulResult(first, fixture.ClientId);
        AssertSuccessfulResult(replay, fixture.ClientId);
        Assert.Equal(first.PrimaryEntityId, replay.PrimaryEntityId);
        Assert.Equal(first.RereadTargetId, replay.RereadTargetId);
        Assert.Equal(first.AuditEntryId, replay.AuditEntryId);
        Assert.Equal(first.Warnings, replay.Warnings);
        AssertError(changed, CommandErrorCode.DuplicateSubmission, "idempotencyKey");
        Assert.Equal(1L, await CountRowsAsync(database, "issued_memberships"));
        Assert.Equal(1L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(1L, await CountRowsAsync(database, "payments"));
        Assert.Equal(2L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ConcurrentSameKeySerializesToOneCompleteWorkflow()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var migrationContext = database.CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        var actor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        var command = CreateCommand(
            actor,
            fixture,
            "concurrent-same-key",
            payment: CreateIssuePayment());
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();

        var results = await Task.WhenAll(
            CreateHandler(firstContext).ExecuteAsync(command, CancellationToken.None),
            CreateHandler(secondContext).ExecuteAsync(command, CancellationToken.None));

        Assert.All(results, result => AssertSuccessfulResult(result, fixture.ClientId));
        Assert.Equal(results[0].PrimaryEntityId, results[1].PrimaryEntityId);
        Assert.Equal(results[0].AuditEntryId, results[1].AuditEntryId);
        Assert.Equal(1L, await CountRowsAsync(database, "issued_memberships"));
        Assert.Equal(1L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(1L, await CountRowsAsync(database, "payments"));
        Assert.Equal(2L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(1L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task MissingSourceDuringRebuildReturnsRecalculationFailureAndRollsBack()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        await ExecuteNonQueryAsync(
            database,
            """
            create function bodylife.test_remove_issued_membership()
            returns trigger
            language plpgsql
            as $function$
            begin
                delete from bodylife.issued_memberships where id = new.id;
                return new;
            end;
            $function$;

            create trigger tr_test_remove_issued_membership
            after insert on bodylife.issued_memberships
            for each row execute function bodylife.test_remove_issued_membership()
            """);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            CreateCommand(
                actor,
                fixture,
                "recalculation-failure",
                payment: CreateIssuePayment()),
            CancellationToken.None);

        AssertError(result, CommandErrorCode.RecalculationFailed);
        await AssertNoIssueMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task PaymentPersistenceFailureRollsBackMembershipCacheAuditsAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        await ExecuteNonQueryAsync(
            database,
            """
            create function bodylife.test_reject_issue_payment()
            returns trigger
            language plpgsql
            as $function$
            begin
                raise exception 'test Payment persistence failure'
                    using errcode = '23514';
            end;
            $function$;

            create trigger tr_test_reject_issue_payment
            before insert on bodylife.payments
            for each row execute function bodylife.test_reject_issue_payment()
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext).ExecuteAsync(
                CreateCommand(
                    actor,
                    fixture,
                    "payment-persistence-failure",
                    payment: CreateIssuePayment()),
                CancellationToken.None));

        await AssertNoIssueMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task PaymentAuditFailureRollsBackMembershipPaymentCacheAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_issue_payment_audit
            check (action_type <> 'payment.created')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext).ExecuteAsync(
                CreateCommand(
                    actor,
                    fixture,
                    "payment-audit-failure",
                    payment: CreateIssuePayment()),
                CancellationToken.None));

        await AssertNoIssueMutationAsync(database);
    }

    [PostgreSqlFact]
    public async Task AuditFailureRollsBackIssuedSourceCacheAndIdempotency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedIssueFixtureAsync(database, actor.AccountId.Value);
        await ExecuteNonQueryAsync(
            database,
            """
            alter table bodylife.business_audit_entries
            add constraint ck_test_reject_membership_issue_audit
            check (action_type <> 'membership.issued')
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateHandler(dbContext).ExecuteAsync(
                CreateCommand(actor, fixture, "audit-failure"),
                CancellationToken.None));

        await AssertNoIssueMutationAsync(database);
    }

    [Fact]
    public void PersistenceRegistrationExposesScopedIssueMembershipHandlerAndPaymentWriter()
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

        var serviceType = typeof(IBodyLifeCommandHandler<IssueMembershipCommand>);
        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == serviceType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(IssueMembershipCommandHandler), descriptor.ImplementationType);

        var writerDescriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType
                == typeof(IMembershipIssuePaymentWriter));
        Assert.Equal(ServiceLifetime.Scoped, writerDescriptor.Lifetime);
        Assert.Equal(
            typeof(MembershipIssuePaymentWriter),
            writerDescriptor.ImplementationType);
    }

    private static IssueMembershipCommandHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        var auditAppender = new BusinessAuditAppender(dbContext);
        return new IssueMembershipCommandHandler(
            dbContext,
            auditAppender,
            new MembershipIssuePaymentWriter(dbContext, auditAppender),
            new MembershipStateCacheRebuilder(dbContext, timeProvider),
            timeProvider);
    }

    private static IssueMembershipCommand CreateCommand(
        ActorContext actor,
        IssueFixture fixture,
        string idempotencyKey,
        MembershipNegativeHandlingDecision? decision = null,
        MembershipIssuePayment? payment = null)
    {
        return new IssueMembershipCommand(
            new CommandEnvelope(
                actor,
                new RequestCorrelationId($"correlation-{idempotencyKey}"),
                EntryOrigin.Normal,
                OccurredAt: null,
                idempotencyKey,
                Reason: null,
                Comment: "  Front desk issue  "),
            fixture.ClientId,
            fixture.MembershipTypeId,
            NewStartDate,
            decision,
            Payment: payment);
    }

    private static MembershipIssuePayment CreateIssuePayment()
    {
        return new MembershipIssuePayment(
            new Money(1200m, "uah"),
            PaymentContext.MembershipSale);
    }

    private static void AssertSuccessfulResult(CommandResult result, Guid clientId)
    {
        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.True(result.PrimaryEntityId.HasValue);
        Assert.Equal("membership", result.PrimaryEntityId.Value.Type);
        Assert.NotEqual(Guid.Empty, result.PrimaryEntityId.Value.Value);
        Assert.Equal(new EntityId("client", clientId), result.RereadTargetId);
        Assert.True(result.AuditEntryId.HasValue);
        Assert.Empty(result.Errors);
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

        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
        Assert.Null(result.AuditEntryId);
    }

    private static async Task<ActorContext> SeedActorAsync(
        PostgreSqlTestDatabase database,
        ActorRole role,
        AccountKind accountKind,
        bool isActive = true,
        DateTimeOffset? sessionExpiresAt = null,
        DateTimeOffset? sessionEndedAt = null,
        string? deviceLabel = "test device")
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.accounts (
                id,
                display_name,
                account_type,
                role,
                is_active,
                created_at,
                deactivated_at)
            values (
                @account_id,
                @display_name,
                @account_type,
                @role,
                @is_active,
                @created_at,
                @deactivated_at);

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
                @device_label,
                @started_at,
                @expires_at,
                @ended_at,
                @last_seen_at)
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("display_name", $"{accountKind} issue actor");
        command.Parameters.AddWithValue("account_type", MapAccountKind(accountKind));
        command.Parameters.AddWithValue("role", MapRole(role));
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.AddWithValue("created_at", TestNow.AddHours(-2));
        command.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value = isActive
            ? DBNull.Value
            : TestNow.AddHours(-1);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.Add("device_label", NpgsqlDbType.Varchar).Value =
            deviceLabel ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue(
            "expires_at",
            sessionExpiresAt ?? TestNow.AddHours(10));
        command.Parameters.Add("ended_at", NpgsqlDbType.TimestampTz).Value =
            sessionEndedAt ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        return new ActorContext(
            new AccountId(accountId),
            role,
            accountKind,
            new SessionId(sessionId),
            deviceLabel);
    }

    private static async Task<IssueFixture> SeedIssueFixtureAsync(
        PostgreSqlTestDatabase database,
        Guid createdByAccountId)
    {
        var clientId = await InsertClientAsync(database, createdByAccountId);
        var membershipTypeId = await InsertMembershipTypeAsync(database);
        return new IssueFixture(clientId, membershipTypeId);
    }

    private static async Task<Guid> InsertClientAsync(
        PostgreSqlTestDatabase database,
        Guid createdByAccountId)
    {
        var clientId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
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
                'Issue',
                'Client',
                null,
                'ISSUE CLIENT',
                null,
                null,
                null,
                null,
                'active',
                @recorded_at,
                @created_by_account_id,
                @recorded_at)
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddDays(-5));
        command.Parameters.AddWithValue("created_by_account_id", createdByAccountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return clientId;
    }

    private static async Task<Guid> InsertMembershipTypeAsync(
        PostgreSqlTestDatabase database,
        bool isActive = true,
        int durationDays = 30)
    {
        var membershipTypeId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                'Eight visits / 30 days',
                @duration_days,
                8,
                1200,
                'UAH',
                @is_active,
                'Reception issue option',
                @created_at,
                @updated_at,
                @deactivated_at)
            """;
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("duration_days", durationDays);
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-10));
        command.Parameters.AddWithValue("updated_at", TestNow.AddDays(-1));
        command.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value = isActive
            ? DBNull.Value
            : TestNow.AddDays(-1);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return membershipTypeId;
    }

    private static async Task<ExistingMembershipFixture> InsertIssuedMembershipAsync(
        PostgreSqlTestDatabase database,
        IssueFixture fixture,
        Guid issuedByAccountId,
        DateTimeOffset? issuedAt = null)
    {
        var membershipId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                'Historical two visits / 30 days',
                30,
                2,
                900,
                'UAH',
                @start_date,
                @base_end_date,
                @issued_at,
                @issued_by_account_id,
                'active',
                'normal',
                null,
                null)
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, ExistingStartDate);
        command.Parameters.AddWithValue("base_end_date", NpgsqlDbType.Date, ExistingBaseEndDate);
        command.Parameters.AddWithValue("issued_at", issuedAt ?? TestNow.AddDays(-3));
        command.Parameters.AddWithValue("issued_by_account_id", issuedByAccountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return new ExistingMembershipFixture(membershipId, ExistingBaseEndDate);
    }

    private static async Task InsertCacheAsync(
        PostgreSqlTestDatabase database,
        ExistingMembershipFixture membership,
        int remainingVisits,
        DateOnly? firstNegativeVisitDate,
        int recalculationVersion = MembershipStateCacheRebuilder.CurrentRecalculationVersion)
    {
        var negativeBalance = Math.Max(0, -remainingVisits);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                @counted_visits,
                @remaining_visits,
                @negative_balance,
                null,
                @first_negative_visit_date,
                0,
                @effective_end_date,
                @last_counted_visit_at,
                @recalculated_at,
                @recalculation_version)
            """;
        command.Parameters.AddWithValue("membership_id", membership.MembershipId);
        command.Parameters.AddWithValue("counted_visits", 2 - remainingVisits);
        command.Parameters.AddWithValue("remaining_visits", remainingVisits);
        command.Parameters.AddWithValue("negative_balance", negativeBalance);
        command.Parameters.Add("first_negative_visit_date", NpgsqlDbType.Date).Value =
            firstNegativeVisitDate ?? (object)DBNull.Value;
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            membership.BaseEndDate);
        command.Parameters.AddWithValue("last_counted_visit_at", TestNow.AddDays(-1));
        command.Parameters.AddWithValue("recalculated_at", TestNow.AddMinutes(-10));
        command.Parameters.AddWithValue("recalculation_version", recalculationVersion);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task UpdateCacheVersionAndEffectiveEndAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        int recalculationVersion,
        DateOnly effectiveEndDate)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_state_cache
            set recalculation_version = @recalculation_version,
                effective_end_date = @effective_end_date
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("recalculation_version", recalculationVersion);
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            effectiveEndDate);
        command.Parameters.AddWithValue("membership_id", membershipId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<IssuedMembershipRow> ReadIssuedMembershipAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select client_id,
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
                   comment
            from bodylife.issued_memberships
            where id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new IssuedMembershipRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetDecimal(5),
            reader.GetString(6),
            reader.GetFieldValue<DateOnly>(7),
            reader.GetFieldValue<DateOnly>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetGuid(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetGuid(13),
            reader.IsDBNull(14) ? null : reader.GetString(14));
    }

    private static async Task<PaymentRow> ReadPaymentForMembershipAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select id,
                   client_id,
                   membership_id,
                   amount,
                   currency,
                   method,
                   payment_context,
                   occurred_at,
                   recorded_at,
                   recorded_by_account_id,
                   session_id,
                   entry_origin,
                   entry_batch_id,
                   comment,
                   status
            from bodylife.payments
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var row = new PaymentRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetDecimal(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetGuid(9),
            reader.GetGuid(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetGuid(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.GetString(14));
        Assert.False(await reader.ReadAsync());
        return row;
    }

    private static async Task<CacheRow> ReadCacheAsync(
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
                   extension_days,
                   effective_end_date,
                   last_counted_visit_at,
                   recalculated_at,
                   recalculation_version
            from bodylife.membership_state_cache
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new CacheRow(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
            reader.GetInt32(5),
            reader.GetFieldValue<DateOnly>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetInt32(9));
    }

    private static async Task<AuditRow> ReadAuditAsync(
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
                   related_entity_refs::text,
                   actor_account_id,
                   actor_account_type,
                   actor_role,
                   session_id,
                   device_label,
                   occurred_at,
                   recorded_at,
                   reason,
                   comment,
                   before_summary::text,
                   after_summary::text,
                   request_correlation_id,
                   entry_origin,
                   idempotency_key
            from bodylife.business_audit_entries
            where id = @audit_entry_id
            """;
        command.Parameters.AddWithValue("audit_entry_id", auditEntryId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new AuditRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17));
    }

    private static async Task<IdempotencyRow> ReadIdempotencyAsync(
        PostgreSqlTestDatabase database,
        string commandName,
        string idempotencyKey)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select status,
                   primary_entity_id,
                   reread_target_id,
                   audit_entry_id,
                   entry_origin,
                   length(result_fingerprint)
            from bodylife.command_idempotency_keys
            where command_name = @command_name
              and idempotency_key = @idempotency_key
            """;
        command.Parameters.AddWithValue("command_name", commandName);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new IdempotencyRow(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetInt32(5));
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

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{tableName}")!;
    }

    private static async Task AssertNoIssueMutationAsync(
        PostgreSqlTestDatabase database)
    {
        Assert.Equal(0L, await CountRowsAsync(database, "issued_memberships"));
        Assert.Equal(0L, await CountRowsAsync(database, "membership_state_cache"));
        Assert.Equal(0L, await CountRowsAsync(database, "payments"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static string MapAccountKind(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.Owner => "owner",
            AccountKind.NamedAdmin => "named_admin",
            AccountKind.SharedReceptionAdmin => "shared_reception_admin",
            _ => throw new ArgumentOutOfRangeException(nameof(accountKind), accountKind, null),
        };
    }

    private static string MapRole(ActorRole role)
    {
        return role switch
        {
            ActorRole.Owner => "owner",
            ActorRole.Admin => "admin",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private sealed record IssueFixture(Guid ClientId, Guid MembershipTypeId);

    private sealed record ExistingMembershipFixture(Guid MembershipId, DateOnly BaseEndDate);

    private sealed record IssuedMembershipRow(
        Guid ClientId,
        Guid MembershipTypeId,
        string TypeNameSnapshot,
        int DurationDaysSnapshot,
        int VisitsLimitSnapshot,
        decimal PriceAmountSnapshot,
        string PriceCurrencySnapshot,
        DateOnly StartDate,
        DateOnly BaseEndDate,
        DateTimeOffset IssuedAt,
        Guid IssuedByAccountId,
        string Status,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment);

    private sealed record CacheRow(
        int CountedVisits,
        int RemainingVisits,
        int NegativeBalance,
        Guid? FirstNegativeVisitId,
        DateOnly? FirstNegativeVisitDate,
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        DateTimeOffset? LastCountedVisitAt,
        DateTimeOffset RecalculatedAt,
        int RecalculationVersion);

    private sealed record PaymentRow(
        Guid Id,
        Guid ClientId,
        Guid MembershipId,
        decimal Amount,
        string Currency,
        string Method,
        string PaymentContext,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status);

    private sealed record AuditRow(
        string ActionType,
        string EntityType,
        Guid EntityId,
        string RelatedEntityRefs,
        Guid ActorAccountId,
        string ActorAccountType,
        string ActorRole,
        Guid SessionId,
        string? DeviceLabel,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string? Reason,
        string? Comment,
        string BeforeSummary,
        string AfterSummary,
        string RequestCorrelationId,
        string EntryOrigin,
        string? IdempotencyKey);

    private sealed record IdempotencyRow(
        string Status,
        Guid PrimaryEntityId,
        Guid RereadTargetId,
        Guid AuditEntryId,
        string EntryOrigin,
        int FingerprintLength);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
