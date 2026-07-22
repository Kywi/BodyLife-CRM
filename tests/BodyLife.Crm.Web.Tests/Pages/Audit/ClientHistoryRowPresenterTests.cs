using System.Globalization;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Localization;
using BodyLife.Crm.Web.Pages.Audit;
using BodyLife.Crm.Web.Tests.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace BodyLife.Crm.Web.Tests.Pages.Audit;

[Collection(nameof(LocalizationCollection))]
public sealed class ClientHistoryRowPresenterTests
{
    private static readonly Guid ClientId = Id(1);
    private static readonly Guid MembershipId = Id(2);
    private static readonly Guid MembershipTypeId = Id(3);
    private static readonly Guid BatchId = Id(4);
    private static readonly AccountId ActorAccountId = new(Id(5));
    private static readonly SessionId SessionId = new(Id(6));
    private static readonly DateTimeOffset OccurredAt = new(
        2026,
        7,
        10,
        10,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedAt = OccurredAt.AddMinutes(5);
    private static readonly DateRange Period = new(
        new DateOnly(2026, 7, 5),
        new DateOnly(2026, 7, 7));

    public static IEnumerable<object[]> ExpectedRows()
    {
        yield return RowExpectation(
            ClientHistorySourceKind.MembershipIssued,
            "history-group-membership",
            "status-active",
            "Membership",
            "Membership issued",
            "Active source",
            "Абонемент",
            "Абонемент оформлено",
            "Активний факт-джерело");
        yield return RowExpectation(
            ClientHistorySourceKind.MembershipOpeningStateCreated,
            "history-group-opening",
            "status-warning",
            "Opening state",
            "Opening state recorded",
            "Opening declaration",
            "Початковий стан",
            "Початковий стан зафіксовано",
            "Початкова декларація");
        yield return RowExpectation(
            ClientHistorySourceKind.VisitMarked,
            "history-group-visit",
            "status-active",
            "Visit",
            "Visit marked",
            "Active source",
            "Відвідування",
            "Відвідування зафіксовано",
            "Активний факт-джерело");
        yield return RowExpectation(
            ClientHistorySourceKind.VisitCanceled,
            "history-group-visit",
            "status-canceled",
            "Visit",
            "Visit canceled",
            "Canceled",
            "Відвідування",
            "Відвідування скасовано",
            "Скасовано");
        yield return RowExpectation(
            ClientHistorySourceKind.PaymentCreated,
            "history-group-payment",
            "status-active",
            "Payment",
            "Payment recorded",
            "Active source",
            "Платіж",
            "Платіж зафіксовано",
            "Активний факт-джерело");
        yield return RowExpectation(
            ClientHistorySourceKind.PaymentCorrected,
            "history-group-payment",
            "status-warning",
            "Payment",
            "Payment corrected",
            "Replacement active",
            "Платіж",
            "Платіж виправлено",
            "Заміна активна");
        yield return RowExpectation(
            ClientHistorySourceKind.PaymentCanceled,
            "history-group-payment",
            "status-canceled",
            "Payment",
            "Payment canceled",
            "Canceled",
            "Платіж",
            "Платіж скасовано",
            "Скасовано");
        yield return RowExpectation(
            ClientHistorySourceKind.FreezeAdded,
            "history-group-freeze",
            "status-active",
            "Freeze",
            "Freeze added",
            "Active source",
            "Замороження",
            "Замороження додано",
            "Активний факт-джерело");
        yield return RowExpectation(
            ClientHistorySourceKind.FreezeCanceled,
            "history-group-freeze",
            "status-canceled",
            "Freeze",
            "Freeze canceled",
            "Canceled",
            "Замороження",
            "Замороження скасовано",
            "Скасовано");
        yield return RowExpectation(
            ClientHistorySourceKind.NonWorkingDayAdded,
            "history-group-non-working",
            "status-active",
            "Non-working day",
            "Non-working period applied",
            "Active application",
            "Неробочий день",
            "Неробочий період застосовано",
            "Активне застосування");
        yield return RowExpectation(
            ClientHistorySourceKind.NonWorkingDayCorrected,
            "history-group-non-working",
            "status-warning",
            "Non-working day",
            "Non-working period corrected",
            "Replacement active",
            "Неробочий день",
            "Неробочий період виправлено",
            "Заміна активна");
        yield return RowExpectation(
            ClientHistorySourceKind.NonWorkingDayCanceled,
            "history-group-non-working",
            "status-canceled",
            "Non-working day",
            "Non-working period canceled",
            "Canceled",
            "Неробочий день",
            "Неробочий період скасовано",
            "Скасовано");
    }

    [Theory]
    [MemberData(nameof(ExpectedRows))]
    public void EverySourceKindHasEnglishAndUkrainianPresentation(
        ClientHistorySourceKind kind,
        string groupClass,
        string statusClass,
        string englishGroup,
        string englishTitle,
        string englishStatus,
        string ukrainianGroup,
        string ukrainianTitle,
        string ukrainianStatus)
    {
        var source = CreateRow(kind);

        var english = Present(source, WebCultures.English);
        var ukrainian = Present(source, WebCultures.Ukrainian);

        AssertPresentation(
            english,
            kind,
            groupClass,
            statusClass,
            englishGroup,
            englishTitle,
            englishStatus);
        AssertPresentation(
            ukrainian,
            kind,
            groupClass,
            statusClass,
            ukrainianGroup,
            ukrainianTitle,
            ukrainianStatus);
    }

    [Theory]
    [InlineData("en-US", "7/1/2026", "125.50 UAH", "30 days", "10 visits")]
    [InlineData("uk-UA", "01.07.2026", "125,50 UAH", "30 днів", "10 відвідувань")]
    public void MembershipFactsUseCultureFormattingWithoutTranslatingSourceData(
        string culture,
        string expectedDate,
        string expectedMoney,
        string expectedDays,
        string expectedVisits)
    {
        var row = Present(CreateMembershipIssuedRow(), culture);
        var values = row.Facts.Select(fact => fact.Value).ToArray();

        Assert.Contains("Standard", values);
        Assert.Contains(expectedDate, values);
        Assert.Contains(expectedMoney, values);
        Assert.Contains(expectedDays, values);
        Assert.Contains(expectedVisits, values);
        Assert.Equal("issued comment", row.Narrative);
    }

    [Theory]
    [InlineData("en-US", "7/5/2026 to 7/7/2026")]
    [InlineData("uk-UA", "05.07.2026 – 07.07.2026")]
    public void SourceRangesAreCultureFormattedAndRawReasonIsPreserved(
        string culture,
        string expectedRange)
    {
        var freeze = Present(CreateFreezeAddedRow(), culture);
        var nonWorkingDay = Present(CreateNonWorkingDayAddedRow(), culture);

        Assert.Contains(expectedRange, freeze.Facts.Select(fact => fact.Value));
        Assert.Equal("freeze reason", freeze.Narrative);
        Assert.Contains("HOLIDAY", nonWorkingDay.Facts.Select(fact => fact.Value));
        Assert.Equal("holiday comment", nonWorkingDay.Narrative);
    }

    [Fact]
    public void CorrectedMembershipAndOpeningStateUseWarningStyling()
    {
        var membership = CreateMembershipIssuedRow();
        membership = membership with
        {
            MembershipSourceRow = membership.MembershipSourceRow! with
            {
                IssuedMembership = membership.MembershipSourceRow.IssuedMembership! with
                {
                    Status = IssuedMembershipLifecycleStatus.Corrected,
                },
            },
        };
        var opening = CreateMembershipOpeningRow();
        opening = opening with
        {
            MembershipSourceRow = opening.MembershipSourceRow! with
            {
                OpeningState = opening.MembershipSourceRow.OpeningState! with
                {
                    Status = MembershipOpeningStateSourceStatus.Corrected,
                },
            },
        };

        Assert.Equal("status-warning", Present(membership, WebCultures.English).StatusClass);
        Assert.Equal("status-warning", Present(opening, WebCultures.English).StatusClass);
    }

    [Theory]
    [InlineData("membership_status")]
    [InlineData("opening_status")]
    [InlineData("visit_status")]
    [InlineData("consumption_status")]
    [InlineData("visit_kind")]
    [InlineData("payment_status")]
    [InlineData("payment_method")]
    [InlineData("payment_context")]
    [InlineData("freeze_status")]
    public void UndefinedSourceEnumsFailClosed(string scenario)
    {
        var row = CreateUndefinedEnumRow(scenario);

        var exception = Assert.Throws<InvalidOperationException>(
            () => Present(row, WebCultures.English));

        Assert.Contains("Unsupported Client history", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownChangedFieldFailsClosed()
    {
        var row = CreatePaymentCorrectedRow();
        row = row with
        {
            PaymentSourceRow = row.PaymentSourceRow! with
            {
                Correction = row.PaymentSourceRow.Correction! with
                {
                    ChangedFields = ["future_field"],
                },
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => Present(row, WebCultures.English));

        Assert.Equal(
            "Unsupported Client history changed-field code 'future_field'.",
            exception.Message);
    }

    [Fact]
    public void MissingRequiredProjectionFailsClosed()
    {
        var row = CreatePaymentCreatedRow() with { PaymentSourceRow = null };

        var exception = Assert.Throws<InvalidOperationException>(
            () => Present(row, WebCultures.English));

        Assert.Equal("Client history source projection is incomplete.", exception.Message);
    }

    [Fact]
    public void UnknownRootKindFailsClosed()
    {
        var row = CreateMembershipIssuedRow() with
        {
            Kind = (ClientHistorySourceKind)0,
        };

        Assert.Throws<InvalidOperationException>(
            () => Present(row, WebCultures.English));
    }

    [Theory]
    [InlineData("ReplaceRange", "Range correction", "Виправлення діапазону")]
    [InlineData("ReplaceReason", "Reason correction", "Виправлення причини")]
    public void NonWorkingDayCorrectionModeHasLocalizedChangeLabel(
        string modeName,
        string english,
        string ukrainian)
    {
        var mode = Enum.Parse<NonWorkingDayCorrectionMode>(modeName);
        var row = CreateNonWorkingDayCorrectedRow(mode);

        Assert.Equal(english, Present(row, WebCultures.English).Change!.Label);
        Assert.Equal(ukrainian, Present(row, WebCultures.Ukrainian).Change!.Label);
    }

    [Fact]
    public void NonWorkingDaySourceKindAndCorrectionModeMustAgree()
    {
        var canceledAsCorrected = CreateNonWorkingDayCanceledRow() with
        {
            Kind = ClientHistorySourceKind.NonWorkingDayCorrected,
        };
        var correctedAsCanceled = CreateNonWorkingDayCorrectedRow(
            NonWorkingDayCorrectionMode.ReplaceRange) with
        {
            Kind = ClientHistorySourceKind.NonWorkingDayCanceled,
        };

        Assert.Throws<InvalidOperationException>(
            () => Present(canceledAsCorrected, WebCultures.English));
        Assert.Throws<InvalidOperationException>(
            () => Present(correctedAsCanceled, WebCultures.English));
    }

    [Fact]
    public void NonWorkingDaySourceConstructorsRejectUndefinedEnums()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreatePeriod(
            Id(70),
            (NonWorkingDayCorrectionSourceStatus)0,
            currentCancellationId: null,
            createdAt: OccurredAt));

        var original = CreatePeriod(
            Id(71),
            NonWorkingDayCorrectionSourceStatus.Corrected,
            currentCancellationId: null,
            createdAt: OccurredAt);
        Assert.Throws<ArgumentOutOfRangeException>(() => new NonWorkingDayCorrectionHistorySource(
            (NonWorkingDayCorrectionMode)0,
            original,
            replacementPeriod: null,
            cancellation: null,
            "invalid mode",
            "invalid mode comment",
            OccurredAt,
            RecordedAt,
            ActorAccountId,
            SessionId,
            EntryOrigin.PaperFallback,
            [MembershipId]));
    }

    private static ClientHistoryRowViewModel Present(
        ClientHistorySourceRow row,
        string culture)
    {
        using var cultureScope = new CultureScope(culture);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBodyLifeLocalization();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider
            .GetRequiredService<ClientHistoryRowPresenter>()
            .Present(row);
    }

    private static void AssertPresentation(
        ClientHistoryRowViewModel row,
        ClientHistorySourceKind kind,
        string groupClass,
        string statusClass,
        string group,
        string title,
        string status)
    {
        Assert.Equal(kind, row.Kind);
        Assert.Equal(groupClass, row.GroupClass);
        Assert.Equal(statusClass, row.StatusClass);
        Assert.Equal(group, row.GroupLabel);
        Assert.Equal(title, row.Title);
        Assert.Equal(status, row.StatusLabel);
        Assert.Equal(OccurredAt, row.OccurredAt);
        Assert.Equal(RecordedAt, row.RecordedAt);
        Assert.Equal(EntryOrigin.PaperFallback, row.EntryOrigin);
        Assert.Equal(ActorAccountId, row.ActorAccountId);
        Assert.Equal(SessionId, row.SessionId);
        Assert.Equal("audit reason", row.AuditReason);
        Assert.Equal("audit comment", row.AuditComment);
        Assert.NotEmpty(row.Facts);
        Assert.NotEmpty(row.Identifiers);
    }

    private static ClientHistorySourceRow CreateRow(ClientHistorySourceKind kind) => kind switch
    {
        ClientHistorySourceKind.MembershipIssued => CreateMembershipIssuedRow(),
        ClientHistorySourceKind.MembershipOpeningStateCreated => CreateMembershipOpeningRow(),
        ClientHistorySourceKind.VisitMarked => CreateVisitMarkedRow(),
        ClientHistorySourceKind.VisitCanceled => CreateVisitCanceledRow(),
        ClientHistorySourceKind.PaymentCreated => CreatePaymentCreatedRow(),
        ClientHistorySourceKind.PaymentCorrected => CreatePaymentCorrectedRow(),
        ClientHistorySourceKind.PaymentCanceled => CreatePaymentCanceledRow(),
        ClientHistorySourceKind.FreezeAdded => CreateFreezeAddedRow(),
        ClientHistorySourceKind.FreezeCanceled => CreateFreezeCanceledRow(),
        ClientHistorySourceKind.NonWorkingDayAdded => CreateNonWorkingDayAddedRow(),
        ClientHistorySourceKind.NonWorkingDayCorrected => CreateNonWorkingDayCorrectedRow(
            NonWorkingDayCorrectionMode.ReplaceRange),
        ClientHistorySourceKind.NonWorkingDayCanceled => CreateNonWorkingDayCanceledRow(),
        _ => throw new InvalidOperationException(),
    };

    private static ClientHistorySourceRow CreateMembershipIssuedRow()
    {
        var membership = new IssuedMembershipHistorySource(
            MembershipId,
            ClientId,
            MembershipTypeId,
            new IssuedMembershipSnapshot(
                "Standard",
                durationDays: 30,
                visitsLimit: 10,
                new Money(125.50m, "UAH")),
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30),
            OccurredAt,
            ActorAccountId,
            IssuedMembershipLifecycleStatus.Active,
            BatchId,
            "issued comment");
        var audit = Audit(MembershipId);
        var source = new ClientMembershipHistorySourceRow(
            ClientMembershipHistorySourceKind.IssuedMembership,
            ClientId,
            MembershipId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            membership,
            OpeningState: null,
            audit);
        return Root(
            ClientHistorySourceKind.MembershipIssued,
            audit,
            membership: source);
    }

    private static ClientHistorySourceRow CreateMembershipOpeningRow()
    {
        var opening = new MembershipOpeningStateHistorySource(
            Id(10),
            ClientId,
            MembershipId,
            MembershipOpeningState.FromStoredSource(
                new DateOnly(2026, 7, 1),
                declaredRemainingVisits: -2,
                declaredNegativeBalance: 2,
                new DateOnly(2026, 7, 30),
                knownExtensionDays: 3),
            "legacy-sheet-1",
            "opening reason",
            RecordedAt,
            ActorAccountId,
            SessionId,
            BatchId,
            MembershipOpeningStateSourceStatus.Active);
        var audit = Audit(opening.OpeningStateId);
        var source = new ClientMembershipHistorySourceRow(
            ClientMembershipHistorySourceKind.OpeningState,
            ClientId,
            MembershipId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            IssuedMembership: null,
            opening,
            audit);
        return Root(
            ClientHistorySourceKind.MembershipOpeningStateCreated,
            audit,
            membership: source);
    }

    private static ClientHistorySourceRow CreateVisitMarkedRow()
    {
        var visit = new MarkedVisitHistorySource(
            Id(20),
            ClientId,
            OccurredAt,
            RecordedAt,
            ActorAccountId,
            SessionId,
            VisitKind.Membership,
            BatchId,
            "visit comment",
            ClientVisitRowStatus.Active,
            new ClientVisitConsumption(
                Id(21),
                MembershipId,
                "Standard",
                ClientVisitConsumptionStatus.Active),
            CurrentCancellationId: null);
        var audit = Audit(visit.VisitId);
        var source = new ClientVisitHistorySourceRow(
            ClientVisitHistorySourceKind.MarkedVisit,
            ClientId,
            visit.VisitId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            visit,
            Cancellation: null,
            audit);
        return Root(ClientHistorySourceKind.VisitMarked, audit, visit: source);
    }

    private static ClientHistorySourceRow CreateVisitCanceledRow()
    {
        var cancellation = new VisitCancellationHistorySource(
            Id(22),
            Id(20),
            ClientId,
            "visit cancellation",
            OccurredAt,
            RecordedAt,
            ActorAccountId,
            SessionId,
            BatchId);
        var audit = Audit(cancellation.CancellationId);
        var source = new ClientVisitHistorySourceRow(
            ClientVisitHistorySourceKind.CanceledVisit,
            ClientId,
            cancellation.VisitId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            MarkedVisit: null,
            cancellation,
            audit);
        return Root(ClientHistorySourceKind.VisitCanceled, audit, visit: source);
    }

    private static ClientHistorySourceRow CreatePaymentCreatedRow()
    {
        var payment = Payment(
            Id(30),
            amount: 125.50m,
            ClientPaymentRowStatus.Active);
        var audit = Audit(payment.PaymentId);
        var source = new ClientPaymentHistorySourceRow(
            ClientPaymentHistorySourceKind.CreatedPayment,
            ClientId,
            payment.PaymentId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            payment,
            Correction: null,
            Cancellation: null,
            audit);
        return Root(ClientHistorySourceKind.PaymentCreated, audit, payment: source);
    }

    private static ClientHistorySourceRow CreatePaymentCorrectedRow()
    {
        var correctionId = Id(33);
        var original = Payment(
            Id(31),
            amount: 100m,
            ClientPaymentRowStatus.Replaced,
            outgoingCorrectionId: correctionId);
        var replacement = Payment(
            Id(32),
            amount: 125.50m,
            ClientPaymentRowStatus.Active,
            incomingCorrectionId: correctionId);
        var correction = new PaymentCorrectionHistorySource(
            correctionId,
            ClientId,
            original.PaymentId,
            replacement.PaymentId,
            ["amount", "occurred_at", "payment_context", "membership_id", "comment"],
            "payment correction",
            OccurredAt,
            RecordedAt,
            ActorAccountId,
            SessionId,
            EntryOrigin.PaperFallback,
            BatchId,
            original,
            replacement);
        var audit = Audit(correction.CorrectionId);
        var source = new ClientPaymentHistorySourceRow(
            ClientPaymentHistorySourceKind.CorrectedPayment,
            ClientId,
            original.PaymentId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            CreatedPayment: null,
            correction,
            Cancellation: null,
            audit);
        return Root(ClientHistorySourceKind.PaymentCorrected, audit, payment: source);
    }

    private static ClientHistorySourceRow CreatePaymentCanceledRow()
    {
        var cancellationId = Id(34);
        var payment = Payment(
            Id(30),
            amount: 125.50m,
            ClientPaymentRowStatus.Canceled,
            currentCancellationId: cancellationId);
        var cancellation = new PaymentCancellationHistorySource(
            cancellationId,
            ClientId,
            payment.PaymentId,
            "payment cancellation",
            OccurredAt,
            RecordedAt,
            ActorAccountId,
            SessionId,
            EntryOrigin.PaperFallback,
            BatchId,
            payment);
        var audit = Audit(cancellation.CancellationId);
        var source = new ClientPaymentHistorySourceRow(
            ClientPaymentHistorySourceKind.CanceledPayment,
            ClientId,
            payment.PaymentId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            CreatedPayment: null,
            Correction: null,
            cancellation,
            audit);
        return Root(ClientHistorySourceKind.PaymentCanceled, audit, payment: source);
    }

    private static PaymentHistorySource Payment(
        Guid paymentId,
        decimal amount,
        ClientPaymentRowStatus status,
        Guid? currentCancellationId = null,
        Guid? incomingCorrectionId = null,
        Guid? outgoingCorrectionId = null) => new(
            paymentId,
            ClientId,
            MembershipId,
            "Standard",
            new Money(amount, "UAH"),
            PaymentMethod.Cash,
            PaymentContext.MembershipSale,
            OccurredAt,
            RecordedAt,
            ActorAccountId,
            SessionId,
            EntryOrigin.PaperFallback,
            BatchId,
            "payment comment",
            status,
            currentCancellationId,
            incomingCorrectionId,
            outgoingCorrectionId);

    private static ClientHistorySourceRow CreateFreezeAddedRow()
    {
        var freeze = Freeze(
            Id(40),
            FreezeCancellationSourceStatus.Active,
            currentCancellationId: null);
        var audit = Audit(freeze.FreezeId);
        var source = new ClientFreezeHistorySourceRow(
            ClientFreezeHistorySourceKind.AddedFreeze,
            ClientId,
            freeze.FreezeId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            freeze,
            Cancellation: null,
            audit);
        return Root(ClientHistorySourceKind.FreezeAdded, audit, freeze: source);
    }

    private static ClientHistorySourceRow CreateFreezeCanceledRow()
    {
        var cancellationId = Id(41);
        var freeze = Freeze(
            Id(40),
            FreezeCancellationSourceStatus.Canceled,
            cancellationId);
        var cancellation = new FreezeCancellationHistorySource(
            cancellationId,
            freeze.FreezeId,
            ClientId,
            MembershipId,
            "freeze cancellation",
            OccurredAt,
            RecordedAt,
            ActorAccountId,
            SessionId,
            EntryOrigin.PaperFallback,
            BatchId,
            freeze);
        var audit = Audit(cancellation.CancellationId);
        var source = new ClientFreezeHistorySourceRow(
            ClientFreezeHistorySourceKind.CanceledFreeze,
            ClientId,
            freeze.FreezeId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            AddedFreeze: null,
            cancellation,
            audit);
        return Root(ClientHistorySourceKind.FreezeCanceled, audit, freeze: source);
    }

    private static FreezeHistorySource Freeze(
        Guid freezeId,
        FreezeCancellationSourceStatus status,
        Guid? currentCancellationId) => new(
            freezeId,
            ClientId,
            MembershipId,
            "Standard",
            Period,
            "freeze reason",
            OccurredAt,
            RecordedAt,
            ActorAccountId,
            SessionId,
            EntryOrigin.PaperFallback,
            BatchId,
            status,
            currentCancellationId);

    private static ClientHistorySourceRow CreateNonWorkingDayAddedRow()
    {
        var period = CreatePeriod(
            Id(50),
            NonWorkingDayCorrectionSourceStatus.Active,
            currentCancellationId: null,
            createdAt: OccurredAt);
        var audit = Audit(period.PeriodId);
        var source = new ClientNonWorkingDayHistorySourceRow(
            ClientNonWorkingDayHistorySourceKind.Added,
            ClientId,
            period.PeriodId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            period,
            Correction: null,
            audit);
        return Root(
            ClientHistorySourceKind.NonWorkingDayAdded,
            audit,
            nonWorkingDay: source);
    }

    private static ClientHistorySourceRow CreateNonWorkingDayCorrectedRow(
        NonWorkingDayCorrectionMode mode)
    {
        var original = CreatePeriod(
            Id(51),
            NonWorkingDayCorrectionSourceStatus.Corrected,
            currentCancellationId: null,
            createdAt: OccurredAt);
        var replacement = CreatePeriod(
            Id(52),
            NonWorkingDayCorrectionSourceStatus.Active,
            currentCancellationId: null,
            createdAt: RecordedAt);
        var correction = new NonWorkingDayCorrectionHistorySource(
            mode,
            original,
            replacement,
            cancellation: null,
            "period correction",
            "period correction comment",
            OccurredAt,
            RecordedAt,
            ActorAccountId,
            SessionId,
            EntryOrigin.PaperFallback,
            [MembershipId]);
        var audit = Audit(original.PeriodId);
        var source = new ClientNonWorkingDayHistorySourceRow(
            ClientNonWorkingDayHistorySourceKind.Corrected,
            ClientId,
            original.PeriodId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            AddedPeriod: null,
            correction,
            audit);
        return Root(
            ClientHistorySourceKind.NonWorkingDayCorrected,
            audit,
            nonWorkingDay: source);
    }

    private static ClientHistorySourceRow CreateNonWorkingDayCanceledRow()
    {
        var cancellationId = Id(53);
        var original = CreatePeriod(
            Id(51),
            NonWorkingDayCorrectionSourceStatus.Canceled,
            cancellationId,
            OccurredAt);
        var cancellation = new NonWorkingDayCancellationHistorySource(
            cancellationId,
            original.PeriodId,
            "period cancellation",
            RecordedAt,
            ActorAccountId,
            SessionId);
        var correction = new NonWorkingDayCorrectionHistorySource(
            NonWorkingDayCorrectionMode.Cancel,
            original,
            replacementPeriod: null,
            cancellation,
            "period cancellation",
            "period cancellation comment",
            OccurredAt,
            RecordedAt,
            ActorAccountId,
            SessionId,
            EntryOrigin.PaperFallback,
            [MembershipId]);
        var audit = Audit(original.PeriodId);
        var source = new ClientNonWorkingDayHistorySourceRow(
            ClientNonWorkingDayHistorySourceKind.Canceled,
            ClientId,
            original.PeriodId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            AddedPeriod: null,
            correction,
            audit);
        return Root(
            ClientHistorySourceKind.NonWorkingDayCanceled,
            audit,
            nonWorkingDay: source);
    }

    private static NonWorkingDayHistoryPeriodSource CreatePeriod(
        Guid periodId,
        NonWorkingDayCorrectionSourceStatus status,
        Guid? currentCancellationId,
        DateTimeOffset createdAt) => new(
            periodId,
            ClientId,
            Period,
            "HOLIDAY",
            "holiday comment",
            createdAt,
            ActorAccountId,
            SessionId,
            status,
            currentCancellationId,
            confirmedAffectedMembershipCount: 1,
            confirmedAffectedClientCount: 1,
            [
                new NonWorkingDayHistoryApplicationSource(
                    Id(periodId == Id(52) ? 55 : 54),
                    periodId,
                    MembershipId,
                    ClientId,
                    "Standard",
                    Period,
                    OccurredAt,
                    RecordedAt,
                    status),
            ]);

    private static ClientHistorySourceRow CreateUndefinedEnumRow(string scenario)
    {
        switch (scenario)
        {
            case "membership_status":
                {
                    var row = CreateMembershipIssuedRow();
                    return row with
                    {
                        MembershipSourceRow = row.MembershipSourceRow! with
                        {
                            IssuedMembership = row.MembershipSourceRow.IssuedMembership! with
                            {
                                Status = (IssuedMembershipLifecycleStatus)0,
                            },
                        },
                    };
                }
            case "opening_status":
                {
                    var row = CreateMembershipOpeningRow();
                    return row with
                    {
                        MembershipSourceRow = row.MembershipSourceRow! with
                        {
                            OpeningState = row.MembershipSourceRow.OpeningState! with
                            {
                                Status = (MembershipOpeningStateSourceStatus)0,
                            },
                        },
                    };
                }
            case "visit_status":
                {
                    var row = CreateVisitMarkedRow();
                    return row with
                    {
                        VisitSourceRow = row.VisitSourceRow! with
                        {
                            MarkedVisit = row.VisitSourceRow.MarkedVisit! with
                            {
                                CurrentStatus = (ClientVisitRowStatus)0,
                            },
                        },
                    };
                }
            case "consumption_status":
                {
                    var row = CreateVisitMarkedRow();
                    var visit = row.VisitSourceRow!.MarkedVisit!;
                    return row with
                    {
                        VisitSourceRow = row.VisitSourceRow with
                        {
                            MarkedVisit = visit with
                            {
                                CurrentConsumption = visit.CurrentConsumption! with
                                {
                                    Status = (ClientVisitConsumptionStatus)0,
                                },
                            },
                        },
                    };
                }
            case "visit_kind":
                {
                    var row = CreateVisitMarkedRow();
                    return row with
                    {
                        VisitSourceRow = row.VisitSourceRow! with
                        {
                            MarkedVisit = row.VisitSourceRow.MarkedVisit! with
                            {
                                VisitKind = (VisitKind)0,
                            },
                        },
                    };
                }
            case "payment_status":
                {
                    var row = CreatePaymentCreatedRow();
                    return row with
                    {
                        PaymentSourceRow = row.PaymentSourceRow! with
                        {
                            CreatedPayment = row.PaymentSourceRow.CreatedPayment! with
                            {
                                CurrentStatus = (ClientPaymentRowStatus)0,
                            },
                        },
                    };
                }
            case "payment_method":
                {
                    var row = CreatePaymentCreatedRow();
                    return row with
                    {
                        PaymentSourceRow = row.PaymentSourceRow! with
                        {
                            CreatedPayment = row.PaymentSourceRow.CreatedPayment! with
                            {
                                Method = (PaymentMethod)0,
                            },
                        },
                    };
                }
            case "payment_context":
                {
                    var row = CreatePaymentCreatedRow();
                    return row with
                    {
                        PaymentSourceRow = row.PaymentSourceRow! with
                        {
                            CreatedPayment = row.PaymentSourceRow.CreatedPayment! with
                            {
                                PaymentContext = (PaymentContext)0,
                            },
                        },
                    };
                }
            case "freeze_status":
                {
                    var row = CreateFreezeAddedRow();
                    return row with
                    {
                        FreezeSourceRow = row.FreezeSourceRow! with
                        {
                            AddedFreeze = row.FreezeSourceRow.AddedFreeze! with
                            {
                                CurrentStatus = (FreezeCancellationSourceStatus)0,
                            },
                        },
                    };
                }
            default:
                throw new InvalidOperationException();
        }
    }

    private static ClientHistorySourceRow Root(
        ClientHistorySourceKind kind,
        ClientAuditEntry audit,
        ClientMembershipHistorySourceRow? membership = null,
        ClientVisitHistorySourceRow? visit = null,
        ClientPaymentHistorySourceRow? payment = null,
        ClientFreezeHistorySourceRow? freeze = null,
        ClientNonWorkingDayHistorySourceRow? nonWorkingDay = null) => new(
            kind,
            ClientId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            membership,
            visit,
            payment,
            freeze,
            nonWorkingDay,
            audit);

    private static ClientAuditEntry Audit(Guid entityId) => new(
        new AuditEntryId(Id(90)),
        "test.action",
        ClientAuditEntityFilter.Client,
        entityId,
        ActorAccountId,
        AccountKind.Owner,
        ActorRole.Owner,
        SessionId,
        DeviceLabel: null,
        OccurredAt,
        RecordedAt,
        EntryOrigin.PaperFallback,
        "audit reason",
        "audit comment",
        "{}",
        "{}",
        "{}",
        new RequestCorrelationId("history-test"),
        IdempotencyKey: null,
        ChangedAfterClose: true);

    private static object[] RowExpectation(
        ClientHistorySourceKind kind,
        string groupClass,
        string statusClass,
        string englishGroup,
        string englishTitle,
        string englishStatus,
        string ukrainianGroup,
        string ukrainianTitle,
        string ukrainianStatus) =>
        [
            kind,
            groupClass,
            statusClass,
            englishGroup,
            englishTitle,
            englishStatus,
            ukrainianGroup,
            ukrainianTitle,
            ukrainianStatus,
        ];

    private static Guid Id(int value) => Guid.Parse(
        $"00000000-0000-0000-0000-{value:000000000000}");

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string culture)
        {
            var selected = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = selected;
            CultureInfo.CurrentUICulture = selected;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
