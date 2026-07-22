using System.Reflection;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Pages.Audit;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Tests.Architecture;

public sealed class BusinessAuditMatrixTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        20,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void EveryDeclaredAuditActionIsAcceptedByTheCanonicalMatrix()
    {
        var declaredActions = BusinessAuditMatrixTestCases.ActionTypes
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field is { IsLiteral: true, IsInitOnly: false }
                && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(value => value.Contains('.', StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var matrixActions = BusinessAuditMatrixTestCases.All
            .Select(item => item.ActionType)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(declaredActions, matrixActions);
        Assert.Equal(matrixActions.Length, matrixActions.Distinct(StringComparer.Ordinal).Count());

        using var dbContext = CreateDbContext();
        var appender = new BusinessAuditAppender(dbContext);
        foreach (var item in BusinessAuditMatrixTestCases.All)
        {
            AppendComplete(appender, item.ActionType, item.EntityType);
        }

        Assert.Equal(matrixActions.Length, dbContext.ChangeTracker.Entries().Count());
    }

    [Fact]
    public void EveryCanonicalAuditActionIsFilterableAndOwnerReadable()
    {
        var canonicalActions = BusinessAuditMatrixTestCases.All
            .Select(item => item.ActionType)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var filterableActions = TimelineModel.ActionOptions
            .Select(option => option.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var readableActions = AuditEntryExplanationPresenter.ReadableActionTypes
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(canonicalActions, filterableActions);
        Assert.Equal(canonicalActions, readableActions);
        Assert.Equal(26, canonicalActions.Length);
    }

    [Fact]
    public void MatrixRejectsUnknownMismatchedAndIncompleteEventPayloads()
    {
        using var dbContext = CreateDbContext();
        var appender = new BusinessAuditAppender(dbContext);

        Assert.Throws<ArgumentException>(() =>
            AppendComplete(appender, "unknown.changed", ClientAuditActions.EntityType));
        Assert.Throws<ArgumentException>(() =>
            AppendComplete(appender, ClientAuditActions.Created, "membership"));
        Assert.Throws<ArgumentException>(() => appender.Append(
            CompleteEnvelope(),
            ClientAuditActions.Created,
            ClientAuditActions.EntityType,
            Guid.NewGuid(),
            TestNow,
            relatedEntityRefs: CompleteRelatedEntityRefs(),
            beforeSummary: CompleteSummary("before")));
        Assert.Throws<ArgumentException>(() => appender.Append(
            CompleteEnvelope(),
            ClientAuditActions.Updated,
            ClientAuditActions.EntityType,
            Guid.NewGuid(),
            TestNow,
            relatedEntityRefs: CompleteRelatedEntityRefs(),
            afterSummary: CompleteSummary("after")));
        Assert.Throws<ArgumentException>(() => appender.Append(
            CompleteEnvelope(),
            VisitAuditActions.Marked,
            VisitAuditActions.VisitEntityType,
            Guid.NewGuid(),
            TestNow,
            beforeSummary: CompleteSummary("before"),
            afterSummary: CompleteSummary("after")));
        Assert.Throws<ArgumentException>(() => appender.Append(
            CompleteEnvelope() with { Reason = null, Comment = null },
            ClientAuditActions.CardChanged,
            ClientAuditActions.EntityType,
            Guid.NewGuid(),
            TestNow,
            CompleteRelatedEntityRefs(),
            CompleteSummary("before"),
            CompleteSummary("after")));
        appender.Append(
            CompleteEnvelope() with { Reason = null },
            ClientAuditActions.CardChanged,
            ClientAuditActions.EntityType,
            Guid.NewGuid(),
            TestNow,
            CompleteRelatedEntityRefs(),
            CompleteSummary("before"),
            CompleteSummary("after"));
        Assert.Throws<ArgumentException>(() => appender.Append(
            CompleteEnvelope() with { IdempotencyKey = null },
            PaymentAuditActions.Created,
            PaymentAuditActions.EntityType,
            Guid.NewGuid(),
            TestNow,
            CompleteRelatedEntityRefs(),
            CompleteSummary("before"),
            CompleteSummary("after")));

        Assert.Single(dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public void AppenderRejectsIncompleteRequiredOperationalEnvelope()
    {
        using var dbContext = CreateDbContext();
        var appender = new BusinessAuditAppender(dbContext);
        var complete = CompleteEnvelope();

        Assert.Throws<ArgumentException>(() => AppendComplete(
            appender,
            ClientAuditActions.Created,
            ClientAuditActions.EntityType,
            complete with
            {
                Actor = complete.Actor with { AccountId = new AccountId(Guid.Empty) },
            }));
        Assert.Throws<ArgumentException>(() => AppendComplete(
            appender,
            ClientAuditActions.Created,
            ClientAuditActions.EntityType,
            complete with
            {
                Actor = complete.Actor with { SessionId = new SessionId(Guid.Empty) },
            }));
        Assert.Throws<ArgumentException>(() => AppendComplete(
            appender,
            ClientAuditActions.Created,
            ClientAuditActions.EntityType,
            complete with { RequestCorrelationId = new RequestCorrelationId(" ") }));
        Assert.Throws<ArgumentException>(() => AppendComplete(
            appender,
            ClientAuditActions.Created,
            ClientAuditActions.EntityType,
            complete with
            {
                EntryOrigin = EntryOrigin.ManualBackfill,
                OccurredAt = null,
            }));
        Assert.Throws<ArgumentException>(() => AppendComplete(
            appender,
            ClientAuditActions.Created,
            ClientAuditActions.EntityType,
            complete with
            {
                EntryOrigin = EntryOrigin.PaperFallback,
                Reason = null,
                Comment = null,
            }));
        Assert.Throws<ArgumentException>(() => appender.Append(
            complete,
            ClientAuditActions.Created,
            ClientAuditActions.EntityType,
            Guid.NewGuid(),
            default,
            CompleteRelatedEntityRefs(),
            CompleteSummary("before"),
            CompleteSummary("after")));

        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    private static BodyLifeDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BodyLifeDbContext>();
        BodyLifeDbContextOptions.Configure(
            optionsBuilder,
            "Host=localhost;Database=bodylife_audit_matrix;Username=bodylife;Password=not-used");
        return new BodyLifeDbContext(optionsBuilder.Options);
    }

    private static void AppendComplete(
        BusinessAuditAppender appender,
        string actionType,
        string entityType,
        CommandEnvelope? envelope = null)
    {
        appender.Append(
            envelope ?? CompleteEnvelope(),
            actionType,
            entityType,
            Guid.NewGuid(),
            TestNow,
            CompleteRelatedEntityRefs(),
            CompleteSummary("before"),
            CompleteSummary("after"),
            changedAfterClose: true);
    }

    internal static CommandEnvelope CompleteEnvelope()
    {
        return new CommandEnvelope(
            new ActorContext(
                new AccountId(Guid.Parse("0d6ca07d-d1da-430a-b0ed-b561aa2a5c5c")),
                ActorRole.Admin,
                AccountKind.SharedReceptionAdmin,
                new SessionId(Guid.Parse("2f56661e-ae24-4aa7-a52b-b6f15b90d557")),
                " reception tablet "),
            new RequestCorrelationId(" matrix-correlation "),
            EntryOrigin.Normal,
            TestNow.AddMinutes(-5),
            " matrix-idempotency ",
            " matrix reason ",
            " matrix comment ");
    }

    internal static object CompleteRelatedEntityRefs()
    {
        return new { ClientId = Guid.Parse("507f5b60-567a-4b66-ae68-c4c85a33d9a2") };
    }

    internal static object CompleteSummary(string state)
    {
        return new { State = state };
    }
}

internal static class BusinessAuditMatrixTestCases
{
    public static readonly Type[] ActionTypes =
    [
        typeof(ClientAuditActions),
        typeof(MembershipTypeAuditActions),
        typeof(MembershipAuditActions),
        typeof(VisitAuditActions),
        typeof(PaymentAuditActions),
        typeof(FreezeAuditActions),
        typeof(NonWorkingDayAuditActions),
        typeof(StaffAccountAuditActions),
    ];

    public static readonly BusinessAuditMatrixTestCase[] All =
    [
        new(ClientAuditActions.Created, ClientAuditActions.EntityType),
        new(ClientAuditActions.Updated, ClientAuditActions.EntityType),
        new(ClientAuditActions.CardAssigned, ClientAuditActions.EntityType),
        new(ClientAuditActions.CardChanged, ClientAuditActions.EntityType),
        new(ClientAuditActions.CardCleared, ClientAuditActions.EntityType),
        new(MembershipTypeAuditActions.Created, MembershipTypeAuditActions.EntityType),
        new(MembershipTypeAuditActions.Edited, MembershipTypeAuditActions.EntityType),
        new(MembershipTypeAuditActions.Deactivated, MembershipTypeAuditActions.EntityType),
        new(MembershipAuditActions.Issued, MembershipAuditActions.MembershipEntityType),
        new(
            MembershipAuditActions.OpeningStateCreated,
            MembershipAuditActions.OpeningStateEntityType),
        new(VisitAuditActions.Marked, VisitAuditActions.VisitEntityType),
        new(VisitAuditActions.Canceled, VisitAuditActions.VisitEntityType),
        new(PaymentAuditActions.Created, PaymentAuditActions.EntityType),
        new(PaymentAuditActions.Corrected, PaymentAuditActions.EntityType),
        new(PaymentAuditActions.Canceled, PaymentAuditActions.EntityType),
        new(FreezeAuditActions.Added, FreezeAuditActions.FreezeEntityType),
        new(FreezeAuditActions.Canceled, FreezeAuditActions.FreezeEntityType),
        new(NonWorkingDayAuditActions.Added, NonWorkingDayAuditActions.PeriodEntityType),
        new(NonWorkingDayAuditActions.Corrected, NonWorkingDayAuditActions.PeriodEntityType),
        new(NonWorkingDayAuditActions.Canceled, NonWorkingDayAuditActions.PeriodEntityType),
        new(StaffAccountAuditActions.Created, StaffAccountAuditActions.EntityType),
        new(StaffAccountAuditActions.DisplayNameUpdated, StaffAccountAuditActions.EntityType),
        new(StaffAccountAuditActions.Activated, StaffAccountAuditActions.EntityType),
        new(StaffAccountAuditActions.Deactivated, StaffAccountAuditActions.EntityType),
        new(StaffAccountAuditActions.CredentialsConfigured, StaffAccountAuditActions.EntityType),
        new(StaffAccountAuditActions.CredentialsReset, StaffAccountAuditActions.EntityType),
    ];
}

internal sealed record BusinessAuditMatrixTestCase(string ActionType, string EntityType);
