using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetClientProfileQueryTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SeedCreatedAt = TestNow.AddDays(-10);
    private static readonly DateTimeOffset SeedUpdatedAt = TestNow.AddDays(-2);
    private static readonly DateTimeOffset SeedAssignedAt = TestNow.AddDays(-5);

    [PostgreSqlFact]
    public async Task AcceptedActorsReadCanonicalIdentityCardVersionAndImplementedActions()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var namedAdmin = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var sharedAdmin = await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            owner.AccountId.Value,
            "Profile",
            "Client",
            "Middle",
            "+38 (067) 123-45-67",
            "Reception comment");
        var assignmentId = await InsertCardAsync(
            database,
            clientId,
            owner.AccountId.Value,
            "bl - profile");
        var membershipAsOfDate = new DateOnly(2026, 7, 12);
        var handler = CreateHandler(dbContext);
        var results = new List<GetClientProfileResult>();

        foreach (var actor in new[] { owner, namedAdmin, sharedAdmin })
        {
            results.Add(await handler.ExecuteAsync(
                new GetClientProfileQuery(actor, clientId, membershipAsOfDate),
                CancellationToken.None));
        }

        Assert.All(results, AssertSuccessful);
        var profile = results[0].Profile!;
        Assert.Equal(clientId, profile.ClientId);
        Assert.Equal("Profile", profile.Surname);
        Assert.Equal("Client", profile.Name);
        Assert.Equal("Middle", profile.Patronymic);
        Assert.Equal("Profile Client Middle", profile.DisplayName);
        Assert.Equal("+38 (067) 123-45-67", profile.Phone);
        Assert.Equal("Reception comment", profile.Comment);
        Assert.Equal(ClientOperationalStatus.Active, profile.OperationalStatus);
        Assert.Equal(SeedCreatedAt, profile.CreatedAt);
        Assert.Equal(SeedUpdatedAt, profile.UpdatedAt);
        Assert.Equal(membershipAsOfDate, profile.MembershipAsOfDate);
        Assert.NotNull(profile.CurrentCard);
        Assert.Equal(assignmentId, profile.CurrentCard.AssignmentId);
        Assert.Equal("bl - profile", profile.CurrentCard.CardNumber);
        Assert.Equal(SeedAssignedAt, profile.CurrentCard.AssignedAt);
        Assert.Null(profile.Membership.CurrentMembership);
        Assert.Empty(profile.Membership.Timeline);
        Assert.Empty(profile.Membership.Warnings);
        Assert.Null(profile.RecentVisits);
        Assert.Null(profile.RecentPayments);
        Assert.Empty(profile.Warnings);
        Assert.Equal(4, profile.AllowedActions.Items.Count);
        Assert.True(profile.AllowedActions.IsAllowed(ClientProfileActionKeys.UpdateClient));
        Assert.True(profile.AllowedActions.IsAllowed(ClientProfileActionKeys.AssignOrChangeCard));
        Assert.True(profile.AllowedActions.IsAllowed(MembershipActionKeys.Issue));
        Assert.True(profile.AllowedActions.IsAllowed(PaymentActionKeys.Create));
        Assert.All(
            profile.AllowedActions.Items,
            permission => Assert.Equal(
                ClientProfileActionKeys.AdminOrOwnerPolicy,
                permission.RequiredPolicy));
        Assert.Equal(
            TestNow.AddMinutes(-5).UtcDateTime,
            await ReadSessionLastSeenAsync(database, owner.SessionId.Value));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task InactiveClientWithoutCardReturnsBothClientsOwnedWarnings()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Inactive",
            "Client",
            operationalStatus: "inactive");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientProfileQuery(actor, clientId),
            CancellationToken.None);

        AssertSuccessful(result);
        var profile = result.Profile!;
        Assert.Equal(ClientOperationalStatus.Inactive, profile.OperationalStatus);
        Assert.Null(profile.CurrentCard);
        Assert.Equal(
            new[] { "client_inactive", "no_current_card" },
            profile.Warnings.Select(warning => warning.Code));
        Assert.Equal(
            DateOnly.FromDateTime(TestNow.UtcDateTime),
            profile.MembershipAsOfDate);
    }

    [PostgreSqlFact]
    public async Task HistoricalCardIsNotExposedAsCurrentProfileState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database, clientId, actor.AccountId.Value, "Historical", "Card");
        await InsertCardAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "BL-HISTORICAL",
            isCurrent: false);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientProfileQuery(actor, clientId),
            CancellationToken.None);

        AssertSuccessful(result);
        Assert.Null(result.Profile!.CurrentCard);
        Assert.Contains(result.Profile.Warnings, warning => warning.Code == "no_current_card");
    }

    [PostgreSqlFact]
    public async Task InactiveExpiredAndMismatchedActorsAreDeniedWithoutProfileData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var inactiveActor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            isActive: false);
        var expiredActor = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            sessionExpiresAt: TestNow.AddMinutes(-1));
        var validActor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var mismatchedActor = validActor with { Role = ActorRole.Owner };
        var clientId = Guid.NewGuid();
        await InsertClientAsync(database, clientId, validActor.AccountId.Value, "Protected", "Profile");
        var handler = CreateHandler(dbContext);
        var results = new List<GetClientProfileResult>();

        foreach (var actor in new[] { inactiveActor, expiredActor, mismatchedActor })
        {
            results.Add(await handler.ExecuteAsync(
                new GetClientProfileQuery(actor, clientId),
                CancellationToken.None));
        }

        Assert.All(results, result =>
        {
            Assert.Equal(GetClientProfileStatus.PermissionDenied, result.Status);
            Assert.Equal("permission_denied", result.ErrorCode);
            Assert.Null(result.Profile);
        });
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
    }

    [PostgreSqlFact]
    public async Task EmptyMissingAndUnsupportedDrillDownRequestsReturnStableErrors()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var handler = CreateHandler(dbContext);

        var emptyId = await handler.ExecuteAsync(
            new GetClientProfileQuery(actor, Guid.Empty),
            CancellationToken.None);
        var missing = await handler.ExecuteAsync(
            new GetClientProfileQuery(actor, Guid.NewGuid()),
            CancellationToken.None);
        var drillDowns = await handler.ExecuteAsync(
            new GetClientProfileQuery(actor, Guid.NewGuid(), IncludeDrillDowns: true),
            CancellationToken.None);
        var invalidAsOfDate = await handler.ExecuteAsync(
            new GetClientProfileQuery(actor, Guid.NewGuid(), default(DateOnly)),
            CancellationToken.None);

        Assert.Equal(GetClientProfileStatus.ValidationFailed, emptyId.Status);
        Assert.Equal("clientId", emptyId.ErrorField);
        Assert.Equal(GetClientProfileStatus.NotFound, missing.Status);
        Assert.Equal("not_found", missing.ErrorCode);
        Assert.Equal(GetClientProfileStatus.ValidationFailed, drillDowns.Status);
        Assert.Equal("includeDrillDowns", drillDowns.ErrorField);
        Assert.Equal(GetClientProfileStatus.ValidationFailed, invalidAsOfDate.Status);
        Assert.Equal("membershipAsOfDate", invalidAsOfDate.ErrorField);
        Assert.All(
            new[] { emptyId, missing, drillDowns, invalidAsOfDate },
            result => Assert.Null(result.Profile));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task RequestedHistoryComposesCanonicalVisitAndPaymentRowsIntoProfile()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Visit",
            "History");
        var visitId = Guid.NewGuid();
        var visitPage = new ClientVisitRowsPage(
            clientId,
            [
                new ClientVisitRow(
                    visitId,
                    clientId,
                    TestNow.AddHours(-2),
                    TestNow.AddHours(-1),
                    actor.AccountId.Value,
                    actor.SessionId.Value,
                    VisitKind.OneOff,
                    EntryOrigin.PaperFallback,
                    EntryBatchId: null,
                    "Paper register row",
                    ClientVisitRowStatus.Active,
                    Consumption: null,
                    Cancellation: null,
                    new QueryPermissionSet(
                    [
                        QueryPermissionResult.Allowed(
                            VisitActionKeys.Cancel,
                            VisitActionKeys.AdminOrOwnerPolicy),
                    ])),
            ],
            HasMore: false);
        var visitRowsHandler = new StubClientVisitRowsQueryHandler(
            _ => GetClientVisitRowsResult.Succeeded(visitPage));
        var paymentId = Guid.NewGuid();
        var paymentPage = new ClientPaymentRowsPage(
            clientId,
            [
                new ClientPaymentRow(
                    PaymentId: paymentId,
                    ClientId: clientId,
                    MembershipId: null,
                    MembershipTypeNameSnapshot: null,
                    Amount: new Money(350m, "UAH"),
                    Method: PaymentMethod.Cash,
                    PaymentContext: PaymentContext.OneOff,
                    OccurredAt: TestNow.AddHours(-3),
                    RecordedAt: TestNow.AddHours(-2),
                    RecordedByAccountId: actor.AccountId.Value,
                    SessionId: actor.SessionId.Value,
                    EntryOrigin: EntryOrigin.PaperFallback,
                    EntryBatchId: null,
                    Comment: "Paper cash row",
                    Status: ClientPaymentRowStatus.Active,
                    Cancellation: null,
                    CorrectionFromOriginal: null,
                    CorrectionToReplacement: null),
            ],
            HasMore: false);
        var paymentRowsHandler = new StubClientPaymentRowsQueryHandler(
            _ => GetClientPaymentRowsResult.Succeeded(paymentPage));

        var result = await CreateHandler(
            dbContext,
            visitRowsQueryHandler: visitRowsHandler,
            paymentRowsQueryHandler: paymentRowsHandler).ExecuteAsync(
                new GetClientProfileQuery(actor, clientId, IncludeHistory: true),
                CancellationToken.None);

        AssertSuccessful(result);
        Assert.Same(visitPage, result.Profile!.RecentVisits);
        var recentVisits = Assert.IsType<ClientVisitRowsPage>(result.Profile.RecentVisits);
        Assert.Single(recentVisits.Items);
        Assert.Equal(visitId, recentVisits.Items[0].VisitId);
        var capturedQuery = Assert.IsType<GetClientVisitRowsQuery>(visitRowsHandler.LastQuery);
        Assert.Equal(actor, capturedQuery.Actor);
        Assert.Equal(clientId, capturedQuery.ClientId);
        Assert.Equal(GetClientVisitRowsQuery.DefaultLimit, capturedQuery.Limit);
        Assert.Same(paymentPage, result.Profile.RecentPayments);
        var recentPayments = Assert.IsType<ClientPaymentRowsPage>(
            result.Profile.RecentPayments);
        Assert.Single(recentPayments.Items);
        Assert.Equal(paymentId, recentPayments.Items[0].PaymentId);
        var capturedPaymentQuery = Assert.IsType<GetClientPaymentRowsQuery>(
            paymentRowsHandler.LastQuery);
        Assert.Equal(actor, capturedPaymentQuery.Actor);
        Assert.Equal(clientId, capturedPaymentQuery.ClientId);
        Assert.Equal(
            GetClientPaymentRowsQuery.DefaultLimit,
            capturedPaymentQuery.Limit);
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
    }

    [PostgreSqlFact]
    public async Task ProfileRereadsCanonicalStateAfterIdentityAndCardCommands()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Before",
            "Client",
            phone: "067 111 22 33");
        var oldAssignmentId = await InsertCardAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "BL-BEFORE");
        var auditAppender = new BusinessAuditAppender(dbContext);
        var timeProvider = new FixedTimeProvider(TestNow);
        var updateResult = await new UpdateClientCommandHandler(
            dbContext,
            new FindClientDuplicateCandidatesQueryHandler(dbContext),
            auditAppender,
            timeProvider).ExecuteAsync(
                new UpdateClientCommand(
                    CommandEnvelope(actor, "profile-update"),
                    clientId,
                    SeedUpdatedAt,
                    "After",
                    "Client",
                    "Middle",
                    "050 999 88 77",
                    "Updated comment",
                    ClientOperationalStatus.Active,
                    []),
                CancellationToken.None);
        var cardResult = await new AssignOrChangeCardCommandHandler(
            dbContext,
            auditAppender,
            timeProvider).ExecuteAsync(
                new AssignOrChangeCardCommand(
                    CommandEnvelope(actor, "profile-card", reason: "Damaged card"),
                    clientId,
                    oldAssignmentId,
                    "BL-AFTER",
                    ClearCurrentCard: false),
                CancellationToken.None);
        var auditCountBeforeQuery = await CountRowsAsync(database, "business_audit_entries");
        var idempotencyCountBeforeQuery = await CountRowsAsync(database, "command_idempotency_keys");

        var profileResult = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientProfileQuery(actor, clientId),
            CancellationToken.None);

        Assert.Equal(CommandStatus.Success, updateResult.Status);
        Assert.Equal(CommandStatus.Success, cardResult.Status);
        AssertSuccessful(profileResult);
        var profile = profileResult.Profile!;
        Assert.Equal("After Client Middle", profile.DisplayName);
        Assert.Equal("050 999 88 77", profile.Phone);
        Assert.Equal("Updated comment", profile.Comment);
        Assert.Equal(TestNow, profile.UpdatedAt);
        Assert.NotNull(profile.CurrentCard);
        Assert.NotEqual(oldAssignmentId, profile.CurrentCard.AssignmentId);
        Assert.Equal("BL-AFTER", profile.CurrentCard.CardNumber);
        Assert.Equal(TestNow, profile.CurrentCard.AssignedAt);
        Assert.Equal(updateResult.PrimaryEntityId, updateResult.RereadTargetId);
        Assert.Equal(cardResult.PrimaryEntityId, cardResult.RereadTargetId);
        Assert.Equal(2L, auditCountBeforeQuery);
        Assert.Equal(2L, idempotencyCountBeforeQuery);
        Assert.Equal(auditCountBeforeQuery, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(idempotencyCountBeforeQuery, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ProfileComposesSingleCanonicalMembershipAndDeterministicTimeline()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Membership",
            "Profile");
        var membershipTypeId = await InsertMembershipTypeAsync(database);
        var currentMembershipId = await InsertMembershipAsync(
            database,
            clientId,
            membershipTypeId,
            actor.AccountId.Value,
            new DateOnly(2026, 7, 1),
            issuedAt: TestNow.AddDays(-11),
            status: "active",
            remainingVisits: 1);
        var canceledMembershipId = await InsertMembershipAsync(
            database,
            clientId,
            membershipTypeId,
            actor.AccountId.Value,
            new DateOnly(2026, 7, 5),
            issuedAt: TestNow.AddDays(-7),
            status: "canceled",
            remainingVisits: 6);
        var asOfDate = new DateOnly(2026, 7, 12);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientProfileQuery(actor, clientId, asOfDate),
            CancellationToken.None);

        AssertSuccessful(result);
        var profile = result.Profile!;
        Assert.Equal(asOfDate, profile.MembershipAsOfDate);
        Assert.NotNull(profile.Membership.CurrentMembership);
        Assert.Equal(currentMembershipId, profile.Membership.CurrentMembership.MembershipId);
        Assert.Equal(
            ClientMembershipSummaryStatusCodes.Active,
            profile.Membership.CurrentMembership.Status);
        Assert.Equal(1, profile.Membership.CurrentMembership.RemainingVisits);
        Assert.Equal(
            new DateOnly(2026, 7, 30),
            profile.Membership.CurrentMembership.EffectiveEndDate);
        Assert.Collection(
            profile.Membership.Timeline,
            item =>
            {
                Assert.Equal(canceledMembershipId, item.MembershipId);
                Assert.Equal(ClientMembershipSummaryStatusCodes.Canceled, item.Status);
                Assert.Equal(6, item.RemainingVisits);
            },
            item =>
            {
                Assert.Equal(currentMembershipId, item.MembershipId);
                Assert.Equal(ClientMembershipSummaryStatusCodes.Active, item.Status);
                Assert.Equal(1, item.RemainingVisits);
            });
        var warning = Assert.Single(profile.Membership.Warnings);
        Assert.Equal(MembershipWarningCodes.LowRemaining, warning.Code);
        Assert.True(profile.AllowedActions.IsAllowed(MembershipActionKeys.Issue));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task AmbiguousActiveMembershipsExposeNoCurrentSummaryAndServerWarning()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Ambiguous",
            "Memberships");
        var membershipTypeId = await InsertMembershipTypeAsync(database);
        var olderMembershipId = await InsertMembershipAsync(
            database,
            clientId,
            membershipTypeId,
            actor.AccountId.Value,
            new DateOnly(2026, 7, 1),
            issuedAt: TestNow.AddDays(-11),
            status: "active",
            remainingVisits: 5);
        var newerMembershipId = await InsertMembershipAsync(
            database,
            clientId,
            membershipTypeId,
            actor.AccountId.Value,
            new DateOnly(2026, 7, 5),
            issuedAt: TestNow.AddDays(-7),
            status: "active",
            remainingVisits: 4);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientProfileQuery(
                actor,
                clientId,
                new DateOnly(2026, 7, 12)),
            CancellationToken.None);

        AssertSuccessful(result);
        Assert.Null(result.Profile!.Membership.CurrentMembership);
        Assert.Equal(
            [newerMembershipId, olderMembershipId],
            result.Profile.Membership.Timeline.Select(item => item.MembershipId));
        var warning = Assert.Single(result.Profile.Membership.Warnings);
        Assert.Equal(
            ClientProfileMembershipWarningCodes.AmbiguousCurrentMembership,
            warning.Code);
        Assert.Equal(
            "Multiple active memberships require explicit selection.",
            warning.Message);
    }

    [PostgreSqlFact]
    public async Task MissingMembershipCacheFailsProfileWithoutPartialData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Unavailable",
            "Membership");
        var membershipTypeId = await InsertMembershipTypeAsync(database);
        await InsertMembershipAsync(
            database,
            clientId,
            membershipTypeId,
            actor.AccountId.Value,
            new DateOnly(2026, 7, 1),
            issuedAt: TestNow.AddDays(-11),
            status: "active",
            remainingVisits: 8,
            includeCache: false);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetClientProfileQuery(
                actor,
                clientId,
                new DateOnly(2026, 7, 12)),
            CancellationToken.None);

        Assert.Equal(GetClientProfileStatus.RecalculationFailed, result.Status);
        Assert.Equal("recalculation_failed", result.ErrorCode);
        Assert.Null(result.Profile);
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task MembershipQueryFailuresNeverReturnPartialProfileData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Atomic",
            "Profile");
        var cases = new[]
        {
            (
                MembershipResult: GetClientMembershipStatesResult.Denied(),
                ProfileStatus: GetClientProfileStatus.PermissionDenied,
                ErrorCode: "permission_denied",
                ErrorField: (string?)null),
            (
                MembershipResult: GetClientMembershipStatesResult.MissingClient(),
                ProfileStatus: GetClientProfileStatus.NotFound,
                ErrorCode: "not_found",
                ErrorField: "clientId"),
            (
                MembershipResult: GetClientMembershipStatesResult.Invalid(
                    "As-of date is required.",
                    "asOfDate"),
                ProfileStatus: GetClientProfileStatus.ValidationFailed,
                ErrorCode: "validation_failed",
                ErrorField: "membershipAsOfDate"),
            (
                MembershipResult: GetClientMembershipStatesResult.RecalculationFailed(),
                ProfileStatus: GetClientProfileStatus.RecalculationFailed,
                ErrorCode: "recalculation_failed",
                ErrorField: (string?)null),
        };

        foreach (var testCase in cases)
        {
            var result = await CreateHandler(
                dbContext,
                new StubMembershipStatesQueryHandler(testCase.MembershipResult)).ExecuteAsync(
                    new GetClientProfileQuery(
                        actor,
                        clientId,
                        new DateOnly(2026, 7, 12)),
                    CancellationToken.None);

            Assert.Equal(testCase.ProfileStatus, result.Status);
            Assert.Equal(testCase.ErrorCode, result.ErrorCode);
            Assert.Equal(testCase.ErrorField, result.ErrorField);
            Assert.Null(result.Profile);
        }

        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task VisitHistoryFailuresNeverReturnPartialProfileData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Atomic",
            "VisitHistory");
        var cases = new[]
        {
            (
                VisitResult: GetClientVisitRowsResult.Denied(),
                ProfileStatus: GetClientProfileStatus.PermissionDenied,
                ErrorCode: "permission_denied",
                ErrorField: (string?)null),
            (
                VisitResult: GetClientVisitRowsResult.MissingClient(),
                ProfileStatus: GetClientProfileStatus.NotFound,
                ErrorCode: "not_found",
                ErrorField: "clientId"),
            (
                VisitResult: GetClientVisitRowsResult.Invalid("Limit is invalid.", "limit"),
                ProfileStatus: GetClientProfileStatus.ValidationFailed,
                ErrorCode: "validation_failed",
                ErrorField: "limit"),
            (
                VisitResult: GetClientVisitRowsResult.InconsistentSource(),
                ProfileStatus: GetClientProfileStatus.SourceInconsistent,
                ErrorCode: "source_inconsistent",
                ErrorField: (string?)null),
        };

        foreach (var testCase in cases)
        {
            var result = await CreateHandler(
                dbContext,
                visitRowsQueryHandler: new StubClientVisitRowsQueryHandler(
                    _ => testCase.VisitResult)).ExecuteAsync(
                    new GetClientProfileQuery(actor, clientId, IncludeHistory: true),
                    CancellationToken.None);

            Assert.Equal(testCase.ProfileStatus, result.Status);
            Assert.Equal(testCase.ErrorCode, result.ErrorCode);
            Assert.Equal(testCase.ErrorField, result.ErrorField);
            Assert.Null(result.Profile);
        }

        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task PaymentHistoryFailuresNeverReturnPartialProfileData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var actor = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var clientId = Guid.NewGuid();
        await InsertClientAsync(
            database,
            clientId,
            actor.AccountId.Value,
            "Atomic",
            "PaymentHistory");
        var cases = new[]
        {
            (
                PaymentResult: GetClientPaymentRowsResult.Denied(),
                ProfileStatus: GetClientProfileStatus.PermissionDenied,
                ErrorCode: "permission_denied",
                ErrorField: (string?)null),
            (
                PaymentResult: GetClientPaymentRowsResult.MissingClient(),
                ProfileStatus: GetClientProfileStatus.NotFound,
                ErrorCode: "not_found",
                ErrorField: "clientId"),
            (
                PaymentResult: GetClientPaymentRowsResult.Invalid(
                    "Limit is invalid.",
                    "limit"),
                ProfileStatus: GetClientProfileStatus.ValidationFailed,
                ErrorCode: "validation_failed",
                ErrorField: "limit"),
            (
                PaymentResult: GetClientPaymentRowsResult.InconsistentSource(),
                ProfileStatus: GetClientProfileStatus.SourceInconsistent,
                ErrorCode: "source_inconsistent",
                ErrorField: (string?)null),
            (
                PaymentResult: GetClientPaymentRowsResult.Succeeded(
                    new ClientPaymentRowsPage(
                        Guid.NewGuid(),
                        [],
                        HasMore: false)),
                ProfileStatus: GetClientProfileStatus.SourceInconsistent,
                ErrorCode: "source_inconsistent",
                ErrorField: (string?)null),
        };

        foreach (var testCase in cases)
        {
            var result = await CreateHandler(
                dbContext,
                paymentRowsQueryHandler: new StubClientPaymentRowsQueryHandler(
                    _ => testCase.PaymentResult)).ExecuteAsync(
                    new GetClientProfileQuery(
                        actor,
                        clientId,
                        IncludeHistory: true),
                    CancellationToken.None);

            Assert.Equal(testCase.ProfileStatus, result.Status);
            Assert.Equal(testCase.ErrorCode, result.ErrorCode);
            Assert.Equal(testCase.ErrorField, result.ErrorField);
            Assert.Null(result.Profile);
        }

        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static GetClientProfileQueryHandler CreateHandler(
        BodyLifeDbContext dbContext,
        IBodyLifeQueryHandler<GetClientMembershipStatesQuery, GetClientMembershipStatesResult>?
            membershipStatesQueryHandler = null,
        IBodyLifeQueryHandler<GetClientVisitRowsQuery, GetClientVisitRowsResult>?
            visitRowsQueryHandler = null,
        IBodyLifeQueryHandler<GetClientPaymentRowsQuery, GetClientPaymentRowsResult>?
            paymentRowsQueryHandler = null)
    {
        var timeProvider = new FixedTimeProvider(TestNow);

        return new GetClientProfileQueryHandler(
            dbContext,
            membershipStatesQueryHandler
                ?? new GetClientMembershipStatesQueryHandler(dbContext, timeProvider),
            visitRowsQueryHandler
                ?? new StubClientVisitRowsQueryHandler(
                    query => GetClientVisitRowsResult.Succeeded(
                        new ClientVisitRowsPage(query.ClientId, [], HasMore: false))),
            paymentRowsQueryHandler
                ?? new StubClientPaymentRowsQueryHandler(
                    query => GetClientPaymentRowsResult.Succeeded(
                        new ClientPaymentRowsPage(query.ClientId, [], HasMore: false))),
            timeProvider);
    }

    private static CommandEnvelope CommandEnvelope(
        ActorContext actor,
        string idempotencyKey,
        string? reason = null)
    {
        return new CommandEnvelope(
            actor,
            new RequestCorrelationId($"correlation-{idempotencyKey}"),
            EntryOrigin.Normal,
            TestNow,
            idempotencyKey,
            reason,
            Comment: null);
    }

    private static async Task<ActorContext> SeedActorAsync(
        PostgreSqlTestDatabase database,
        ActorRole role,
        AccountKind accountKind,
        bool isActive = true,
        DateTimeOffset? sessionExpiresAt = null,
        string? deviceLabel = "test device")
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var accountCommand = connection.CreateCommand())
        {
            accountCommand.CommandText =
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
                    @id,
                    @display_name,
                    @account_type,
                    @role,
                    @is_active,
                    @created_at,
                    @deactivated_at)
                """;
            accountCommand.Parameters.AddWithValue("id", accountId);
            accountCommand.Parameters.AddWithValue("display_name", $"{accountKind} profile actor");
            accountCommand.Parameters.AddWithValue("account_type", MapAccountKind(accountKind));
            accountCommand.Parameters.AddWithValue("role", MapRole(role));
            accountCommand.Parameters.AddWithValue("is_active", isActive);
            accountCommand.Parameters.AddWithValue("created_at", TestNow.AddHours(-1));
            accountCommand.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value = isActive
                ? DBNull.Value
                : TestNow;
            await accountCommand.ExecuteNonQueryAsync();
        }

        await using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.CommandText =
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
                    @id,
                    @account_id,
                    @device_label,
                    @started_at,
                    @expires_at,
                    @ended_at,
                    @last_seen_at)
                """;
            sessionCommand.Parameters.AddWithValue("id", sessionId);
            sessionCommand.Parameters.AddWithValue("account_id", accountId);
            sessionCommand.Parameters.Add("device_label", NpgsqlDbType.Varchar).Value =
                deviceLabel ?? (object)DBNull.Value;
            sessionCommand.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
            sessionCommand.Parameters.AddWithValue(
                "expires_at",
                sessionExpiresAt ?? TestNow.AddHours(11));
            sessionCommand.Parameters.Add("ended_at", NpgsqlDbType.TimestampTz).Value = DBNull.Value;
            sessionCommand.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
            await sessionCommand.ExecuteNonQueryAsync();
        }

        return new ActorContext(
            new AccountId(accountId),
            role,
            accountKind,
            new SessionId(sessionId),
            deviceLabel);
    }

    private static async Task InsertClientAsync(
        PostgreSqlTestDatabase database,
        Guid clientId,
        Guid actorAccountId,
        string surname,
        string name,
        string? patronymic = null,
        string? phone = null,
        string? comment = null,
        string operationalStatus = "active")
    {
        var normalizedPhone = phone is null ? null : ClientSearchNormalizer.NormalizePhone(phone);
        var phoneLastFour = normalizedPhone is null
            ? null
            : ClientSearchNormalizer.ExtractPhoneLastFour(normalizedPhone);
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
                operational_status,
                comment,
                created_at,
                created_by_account_id,
                updated_at)
            values (
                @id,
                @surname,
                @name,
                @patronymic,
                @normalized_full_name,
                @phone_raw,
                @phone_normalized,
                @phone_last4,
                @operational_status,
                @comment,
                @created_at,
                @created_by_account_id,
                @updated_at)
            """;
        command.Parameters.AddWithValue("id", clientId);
        command.Parameters.AddWithValue("surname", surname);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.Add("patronymic", NpgsqlDbType.Text).Value = patronymic ?? (object)DBNull.Value;
        command.Parameters.AddWithValue(
            "normalized_full_name",
            ClientSearchNormalizer.NormalizeFullName(surname, name, patronymic));
        command.Parameters.Add("phone_raw", NpgsqlDbType.Text).Value = phone ?? (object)DBNull.Value;
        command.Parameters.Add("phone_normalized", NpgsqlDbType.Text).Value =
            normalizedPhone ?? (object)DBNull.Value;
        command.Parameters.Add("phone_last4", NpgsqlDbType.Text).Value = phoneLastFour ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("operational_status", operationalStatus);
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value = comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("created_at", SeedCreatedAt);
        command.Parameters.AddWithValue("created_by_account_id", actorAccountId);
        command.Parameters.AddWithValue("updated_at", SeedUpdatedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> InsertMembershipTypeAsync(
        PostgreSqlTestDatabase database)
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
                @id,
                'Eight visits / 30 days',
                30,
                8,
                1200,
                'UAH',
                true,
                null,
                @recorded_at,
                @recorded_at,
                null)
            """;
        command.Parameters.AddWithValue("id", membershipTypeId);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddDays(-30));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return membershipTypeId;
    }

    private static async Task<Guid> InsertMembershipAsync(
        PostgreSqlTestDatabase database,
        Guid clientId,
        Guid membershipTypeId,
        Guid issuedByAccountId,
        DateOnly startDate,
        DateTimeOffset issuedAt,
        string status,
        int remainingVisits,
        bool includeCache = true)
    {
        const int visitsLimit = 8;
        var membershipId = Guid.NewGuid();
        var baseEndDate = startDate.AddDays(29);
        var countedVisits = visitsLimit - remainingVisits;
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        await using (var membershipCommand = connection.CreateCommand())
        {
            membershipCommand.CommandText =
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
                    @id,
                    @client_id,
                    @membership_type_id,
                    'Eight visits / 30 days',
                    30,
                    8,
                    1200,
                    'UAH',
                    @start_date,
                    @base_end_date,
                    @issued_at,
                    @issued_by_account_id,
                    @status,
                    'normal',
                    null,
                    null)
                """;
            membershipCommand.Parameters.AddWithValue("id", membershipId);
            membershipCommand.Parameters.AddWithValue("client_id", clientId);
            membershipCommand.Parameters.AddWithValue("membership_type_id", membershipTypeId);
            membershipCommand.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, startDate);
            membershipCommand.Parameters.AddWithValue("base_end_date", NpgsqlDbType.Date, baseEndDate);
            membershipCommand.Parameters.AddWithValue("issued_at", issuedAt);
            membershipCommand.Parameters.AddWithValue("issued_by_account_id", issuedByAccountId);
            membershipCommand.Parameters.AddWithValue("status", status);
            Assert.Equal(1, await membershipCommand.ExecuteNonQueryAsync());
        }

        if (!includeCache)
        {
            return membershipId;
        }

        await using var cacheCommand = connection.CreateCommand();
        cacheCommand.CommandText =
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
                0,
                null,
                null,
                0,
                @effective_end_date,
                @last_counted_visit_at,
                @recalculated_at,
                @recalculation_version)
            """;
        cacheCommand.Parameters.AddWithValue("membership_id", membershipId);
        cacheCommand.Parameters.AddWithValue("counted_visits", countedVisits);
        cacheCommand.Parameters.AddWithValue("remaining_visits", remainingVisits);
        cacheCommand.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            baseEndDate);
        cacheCommand.Parameters.Add("last_counted_visit_at", NpgsqlDbType.TimestampTz).Value =
            countedVisits == 0
                ? DBNull.Value
                : TestNow.AddDays(-1);
        cacheCommand.Parameters.AddWithValue("recalculated_at", TestNow.AddMinutes(-20));
        cacheCommand.Parameters.AddWithValue(
            "recalculation_version",
            MembershipStateCacheRebuilder.CurrentRecalculationVersion);
        Assert.Equal(1, await cacheCommand.ExecuteNonQueryAsync());
        return membershipId;
    }

    private static async Task<Guid> InsertCardAsync(
        PostgreSqlTestDatabase database,
        Guid clientId,
        Guid actorAccountId,
        string cardNumber,
        bool isCurrent = true)
    {
        var assignmentId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.client_card_assignments (
                id,
                client_id,
                card_number_raw,
                card_number_normalized,
                assigned_at,
                assigned_by_account_id,
                ended_at,
                ended_by_account_id,
                end_reason,
                is_current)
            values (
                @id,
                @client_id,
                @card_number_raw,
                @card_number_normalized,
                @assigned_at,
                @assigned_by_account_id,
                @ended_at,
                @ended_by_account_id,
                @end_reason,
                @is_current)
            """;
        command.Parameters.AddWithValue("id", assignmentId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("card_number_raw", cardNumber);
        command.Parameters.AddWithValue(
            "card_number_normalized",
            ClientSearchNormalizer.NormalizeCardNumber(cardNumber));
        command.Parameters.AddWithValue("assigned_at", SeedAssignedAt);
        command.Parameters.AddWithValue("assigned_by_account_id", actorAccountId);
        command.Parameters.Add("ended_at", NpgsqlDbType.TimestampTz).Value = isCurrent
            ? DBNull.Value
            : SeedAssignedAt.AddDays(1);
        command.Parameters.Add("ended_by_account_id", NpgsqlDbType.Uuid).Value = isCurrent
            ? DBNull.Value
            : actorAccountId;
        command.Parameters.Add("end_reason", NpgsqlDbType.Text).Value = isCurrent
            ? DBNull.Value
            : "Historical profile card";
        command.Parameters.AddWithValue("is_current", isCurrent);
        await command.ExecuteNonQueryAsync();
        return assignmentId;
    }

    private static async Task<DateTime> ReadSessionLastSeenAsync(
        PostgreSqlTestDatabase database,
        Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select last_seen_at from bodylife.sessions where id = @id";
        command.Parameters.AddWithValue("id", sessionId);
        return (DateTime)(await command.ExecuteScalarAsync())!;
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>($"select count(*) from bodylife.{tableName}");
    }

    private static void AssertSuccessful(GetClientProfileResult result)
    {
        Assert.Equal(GetClientProfileStatus.Success, result.Status);
        Assert.NotNull(result.Profile);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
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

    private sealed class StubMembershipStatesQueryHandler(
        GetClientMembershipStatesResult result)
        : IBodyLifeQueryHandler<GetClientMembershipStatesQuery, GetClientMembershipStatesResult>
    {
        public Task<GetClientMembershipStatesResult> ExecuteAsync(
            GetClientMembershipStatesQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class StubClientVisitRowsQueryHandler(
        Func<GetClientVisitRowsQuery, GetClientVisitRowsResult> resultFactory)
        : IBodyLifeQueryHandler<GetClientVisitRowsQuery, GetClientVisitRowsResult>
    {
        public GetClientVisitRowsQuery? LastQuery { get; private set; }

        public Task<GetClientVisitRowsResult> ExecuteAsync(
            GetClientVisitRowsQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(resultFactory(query));
        }
    }

    private sealed class StubClientPaymentRowsQueryHandler(
        Func<GetClientPaymentRowsQuery, GetClientPaymentRowsResult> resultFactory)
        : IBodyLifeQueryHandler<GetClientPaymentRowsQuery, GetClientPaymentRowsResult>
    {
        public GetClientPaymentRowsQuery? LastQuery { get; private set; }

        public Task<GetClientPaymentRowsResult> ExecuteAsync(
            GetClientPaymentRowsQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(resultFactory(query));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
