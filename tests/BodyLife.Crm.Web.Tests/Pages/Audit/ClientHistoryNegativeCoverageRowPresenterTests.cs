using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Localization;

namespace BodyLife.Crm.Web.Tests.Pages.Audit;

public sealed partial class ClientHistoryRowPresenterTests
{
    [Fact]
    public void NegativeCoverageRowsUseThePaperEventThatOwnsEachFact()
    {
        var cases = new[]
        {
            (CreateNegativeCoverageRow(
                ClientHistorySourceKind.NegativeCoverageCreated),
                "Negative Visit coverage",
                "Погашення мінусових відвідувань"),
            (CreateInitialNewMembershipCoverageRow(),
                "Membership sale",
                "Продаж абонемента"),
            (CreateCorrectionCreatedNegativeCoverageRow(
                NegativeVisitCoverageClosureMethod.OneOff),
                "Correction or cancellation",
                "Виправлення або скасування"),
            (CreateCorrectionCreatedNegativeCoverageRow(
                NegativeVisitCoverageClosureMethod.NewMembership),
                "Correction or cancellation",
                "Виправлення або скасування"),
            (CreateNegativeCoverageRow(
                ClientHistorySourceKind.NegativeCoverageCanceled),
                "Correction or cancellation",
                "Виправлення або скасування"),
            (CreateNegativeCoverageRow(
                ClientHistorySourceKind.NegativeCoverageReplaced),
                "Correction or cancellation",
                "Виправлення або скасування"),
        };

        foreach (var (source, englishEvent, ukrainianEvent) in cases)
        {
            var english = Present(source, WebCultures.English);
            var ukrainian = Present(source, WebCultures.Ukrainian);

            Assert.Equal(englishEvent, FactValue(english.Facts, "Event type"));
            Assert.Equal(ukrainianEvent, FactValue(ukrainian.Facts, "Тип події"));
            Assert.NotEmpty(FactValue(
                english.Identifiers,
                "Entry batch row ID"));
        }
    }

    [Fact]
    public void NegativeCoverageRowsExposeCanonicalFactsAndLifecycleIdentifiers()
    {
        var replaced = Present(
            CreateNegativeCoverageRow(
                ClientHistorySourceKind.NegativeCoverageReplaced),
            WebCultures.English);
        var newMembership = Present(
            CreateInitialNewMembershipCoverageRow(),
            WebCultures.English);

        Assert.Equal("One-off closure", FactValue(
            replaced.Facts,
            "Coverage method"));
        Assert.NotEmpty(FactValue(replaced.Facts, "Covered visits"));
        Assert.NotEmpty(FactValue(replaced.Facts, "Exact cash Payment"));
        Assert.NotEmpty(FactValue(
            replaced.Facts,
            "Replacement coverage"));
        Assert.NotNull(replaced.Change);
        Assert.Equal("Correction", replaced.Change.Label);
        Assert.NotEmpty(FactValue(
            replaced.Identifiers,
            "Negative closure ID"));
        Assert.NotEmpty(FactValue(
            replaced.Identifiers,
            "Replacement closure ID"));
        Assert.NotEmpty(FactValue(replaced.Identifiers, "Correction ID"));

        Assert.Equal("Covering Membership", FactValue(
            newMembership.Facts,
            "Coverage method"));
        Assert.Equal("Recovery", FactValue(
            newMembership.Facts,
            "Covering Membership"));
        Assert.Equal("Not applicable", FactValue(
            newMembership.Facts,
            "Exact cash Payment"));
        Assert.NotEmpty(FactValue(
            newMembership.Identifiers,
            "Membership ID"));
    }

    [Fact]
    public void PaperNegativeCoverageWithoutCanonicalRowReferenceFailsClosed()
    {
        var row = CreateNegativeCoverageRow(
            ClientHistorySourceKind.NegativeCoverageCreated);
        row = row with
        {
            NegativeCoverageSourceRow = row.NegativeCoverageSourceRow! with
            {
                PaperReference = null,
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => Present(row, WebCultures.English));

        Assert.Equal(
            "Paper negative-visit coverage history provenance is inconsistent.",
            exception.Message);
    }

    [Fact]
    public void PaperNegativeCoverageWithWrongEventTypeFailsClosed()
    {
        var row = CreateNegativeCoverageRow(
            ClientHistorySourceKind.NegativeCoverageCreated);
        var source = row.NegativeCoverageSourceRow!;
        row = row with
        {
            NegativeCoverageSourceRow = source with
            {
                PaperReference = source.PaperReference! with
                {
                    EventType = PaperFallbackEventType.Payment,
                },
            },
        };

        Assert.Throws<InvalidOperationException>(
            () => Present(row, WebCultures.English));
    }

    [Fact]
    public void CorrectionCreatedNegativeClosurePaymentUsesCorrectionPaperEvent()
    {
        var row = CreatePaymentCreatedRow();
        var source = row.PaymentSourceRow!;
        var payment = source.CreatedPayment!;
        row = row with
        {
            PaymentSourceRow = source with
            {
                CreatedPayment = payment with
                {
                    PaymentContext = PaymentContext.NegativeClosure,
                    PaperReference = payment.PaperReference! with
                    {
                        PaperSheetNumber = "NEGATIVE-CORRECTION-001",
                        EventType = PaperFallbackEventType.CorrectionOrCancellation,
                    },
                },
            },
        };

        var english = Present(row, WebCultures.English);
        var ukrainian = Present(row, WebCultures.Ukrainian);

        Assert.Equal("Correction or cancellation", FactValue(
            english.Facts,
            "Event type"));
        Assert.Equal("Виправлення або скасування", FactValue(
            ukrainian.Facts,
            "Тип події"));
    }

    [Fact]
    public void NegativeClosurePaymentRejectsUnrelatedPaperEvent()
    {
        var row = CreatePaymentCreatedRow();
        var source = row.PaymentSourceRow!;
        var payment = source.CreatedPayment!;
        row = row with
        {
            PaymentSourceRow = source with
            {
                CreatedPayment = payment with
                {
                    PaymentContext = PaymentContext.NegativeClosure,
                    PaperReference = payment.PaperReference! with
                    {
                        EventType = PaperFallbackEventType.Payment,
                    },
                },
            },
        };

        Assert.Throws<InvalidOperationException>(
            () => Present(row, WebCultures.English));
    }

    private static ClientHistorySourceRow CreateNegativeCoverageRow(
        ClientHistorySourceKind kind)
    {
        var sourceKind = kind switch
        {
            ClientHistorySourceKind.NegativeCoverageCreated
                => ClientNegativeVisitCoverageHistorySourceKind.Created,
            ClientHistorySourceKind.NegativeCoverageCanceled
                => ClientNegativeVisitCoverageHistorySourceKind.Canceled,
            ClientHistorySourceKind.NegativeCoverageReplaced
                => ClientNegativeVisitCoverageHistorySourceKind.Replaced,
            _ => throw new InvalidOperationException(),
        };
        var status = sourceKind switch
        {
            ClientNegativeVisitCoverageHistorySourceKind.Created
                => NegativeVisitCoverageClosureHistoryStatus.Active,
            ClientNegativeVisitCoverageHistorySourceKind.Canceled
                => NegativeVisitCoverageClosureHistoryStatus.Canceled,
            ClientNegativeVisitCoverageHistorySourceKind.Replaced
                => NegativeVisitCoverageClosureHistoryStatus.Replaced,
            _ => throw new InvalidOperationException(),
        };
        var closure = CoverageClosure(Id(210), status);
        var replacement = sourceKind
            == ClientNegativeVisitCoverageHistorySourceKind.Replaced
            ? CoverageClosure(
                Id(211),
                NegativeVisitCoverageClosureHistoryStatus.Active)
            : null;
        var correction = sourceKind switch
        {
            ClientNegativeVisitCoverageHistorySourceKind.Canceled =>
                CoverageCorrection(
                    Id(220),
                    closure.ClosureId,
                    replacementClosureId: null,
                    NegativeVisitCoverageCorrectionHistoryMode.Cancel),
            ClientNegativeVisitCoverageHistorySourceKind.Replaced =>
                CoverageCorrection(
                    Id(221),
                    closure.ClosureId,
                    replacement!.ClosureId,
                    NegativeVisitCoverageCorrectionHistoryMode.Replace),
            _ => null,
        };
        var eventType = sourceKind
            == ClientNegativeVisitCoverageHistorySourceKind.Created
            ? PaperFallbackEventType.NegativeCoverage
            : PaperFallbackEventType.CorrectionOrCancellation;
        var audit = CoverageAudit(closure.ClosureId, sourceKind);
        var source = new ClientNegativeVisitCoverageHistorySourceRow(
            sourceKind,
            ClientId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            closure,
            replacement,
            correction,
            PaperReference(Id(230 + (int)sourceKind), eventType),
            audit);
        return Root(kind, audit, negativeCoverage: source);
    }

    private static ClientHistorySourceRow CreateInitialNewMembershipCoverageRow()
    {
        var row = CreateNegativeCoverageRow(
            ClientHistorySourceKind.NegativeCoverageCreated);
        var source = row.NegativeCoverageSourceRow!;
        return row with
        {
            NegativeCoverageSourceRow = source with
            {
                Closure = NewMembershipCoverageClosure(
                    source.Closure.ClosureId,
                    NegativeVisitCoverageClosureHistoryStatus.Active),
                PaperReference = source.PaperReference! with
                {
                    EventType = PaperFallbackEventType.MembershipSale,
                    PaperSheetNumber = "MEMBERSHIP-SALE-NEGATIVE-001",
                },
            },
        };
    }

    private static ClientHistorySourceRow CreateCorrectionCreatedNegativeCoverageRow(
        NegativeVisitCoverageClosureMethod method)
    {
        var closureId = method == NegativeVisitCoverageClosureMethod.OneOff
            ? Id(240)
            : Id(241);
        var closure = method == NegativeVisitCoverageClosureMethod.OneOff
            ? CoverageClosure(
                closureId,
                NegativeVisitCoverageClosureHistoryStatus.Active)
            : NewMembershipCoverageClosure(
                closureId,
                NegativeVisitCoverageClosureHistoryStatus.Active);
        var correction = CoverageCorrection(
            Id(242 + (int)method),
            Id(239),
            closureId,
            NegativeVisitCoverageCorrectionHistoryMode.Replace);
        var audit = CoverageAudit(
            closureId,
            ClientNegativeVisitCoverageHistorySourceKind.Created);
        var source = new ClientNegativeVisitCoverageHistorySourceRow(
            ClientNegativeVisitCoverageHistorySourceKind.Created,
            ClientId,
            OccurredAt,
            RecordedAt,
            EntryOrigin.PaperFallback,
            closure,
            ReplacementClosure: null,
            correction,
            PaperReference(
                Id(245 + (int)method),
                PaperFallbackEventType.CorrectionOrCancellation),
            audit);
        return Root(
            ClientHistorySourceKind.NegativeCoverageCreated,
            audit,
            negativeCoverage: source);
    }

    private static NegativeVisitCoverageClosureHistorySnapshot CoverageClosure(
        Guid closureId,
        NegativeVisitCoverageClosureHistoryStatus status) => new(
            closureId,
            ClientId,
            NegativeVisitCoverageClosureMethod.OneOff,
            Id(204),
            2,
            "coverage comment",
            OccurredAt,
            RecordedAt,
            ActorAccountId,
            SessionId,
            EntryOrigin.PaperFallback,
            BatchId,
            status,
            Lines: [],
            Items: [],
            new NegativeVisitCoveragePaymentHistorySnapshot(
                Id(closureId == Id(211) ? 213 : 212),
                new Money(80m, "UAH"),
                OccurredAt,
                RecordedAt,
                ActorAccountId,
                SessionId,
                EntryOrigin.PaperFallback,
                BatchId,
                status),
            CoveringMembership: null);

    private static NegativeVisitCoverageClosureHistorySnapshot
        NewMembershipCoverageClosure(
            Guid closureId,
            NegativeVisitCoverageClosureHistoryStatus status) => new(
                closureId,
                ClientId,
                NegativeVisitCoverageClosureMethod.NewMembership,
                Id(204),
                2,
                "coverage membership comment",
                OccurredAt,
                RecordedAt,
                ActorAccountId,
                SessionId,
                EntryOrigin.PaperFallback,
                BatchId,
                status,
                Lines: [],
                Items: [],
                Payment: null,
                new NegativeVisitCoverageCoveringMembershipHistorySnapshot(
                    Id(214),
                    MembershipTypeId,
                    new IssuedMembershipSnapshot(
                        "Recovery",
                        durationDays: 30,
                        visitsLimit: 8,
                        new Money(200m, "UAH")),
                    new DateOnly(2026, 7, 10),
                    new DateOnly(2026, 8, 8),
                    OccurredAt,
                    ActorAccountId,
                    EntryOrigin.PaperFallback,
                    BatchId,
                    IssuedMembershipLifecycleStatus.Active));

    private static NegativeVisitCoverageCorrectionHistorySnapshot CoverageCorrection(
        Guid correctionId,
        Guid originalClosureId,
        Guid? replacementClosureId,
        NegativeVisitCoverageCorrectionHistoryMode mode) => new(
            correctionId,
            originalClosureId,
            replacementClosureId,
            mode,
            "coverage correction reason",
            OccurredAt,
            RecordedAt,
            ActorAccountId,
            SessionId,
            EntryOrigin.PaperFallback,
            BatchId);

    private static PaperFallbackEntryRowReference PaperReference(
        Guid rowId,
        PaperFallbackEventType eventType) => new(
            BatchId,
            rowId,
            eventType == PaperFallbackEventType.CorrectionOrCancellation
                ? "NEGATIVE-CORRECTION-001"
                : "NEGATIVE-COVERAGE-001",
            41,
            eventType,
            OccurredAt,
            "Recovered paper negative Visit coverage");

    private static ClientAuditEntry CoverageAudit(
        Guid entityId,
        ClientNegativeVisitCoverageHistorySourceKind kind) =>
        Audit(entityId) with
        {
            ActionType = kind switch
            {
                ClientNegativeVisitCoverageHistorySourceKind.Created
                    => "membership_negative_closure.created",
                ClientNegativeVisitCoverageHistorySourceKind.Canceled
                    => "membership_negative_closure.canceled",
                ClientNegativeVisitCoverageHistorySourceKind.Replaced
                    => "membership_negative_closure.replaced",
                _ => throw new InvalidOperationException(),
            },
            EntityType = ClientAuditEntityFilter.MembershipNegativeClosure,
        };
}
