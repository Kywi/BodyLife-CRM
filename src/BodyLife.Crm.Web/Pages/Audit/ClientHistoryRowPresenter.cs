using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Web.Pages.Audit;

/// <summary>
/// Culture-aware, source-faithful presentation for canonical Client history rows.
/// </summary>
public sealed class ClientHistoryRowPresenter(AuditPresentation presentation)
{
    public ClientHistoryRowViewModel Present(ClientHistorySourceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Kind switch
        {
            ClientHistorySourceKind.MembershipIssued => Issued(
                row,
                Require(row.MembershipSourceRow?.IssuedMembership)),
            ClientHistorySourceKind.MembershipOpeningStateCreated => Opening(
                row,
                Require(row.MembershipSourceRow?.OpeningState)),
            ClientHistorySourceKind.VisitMarked => MarkedVisit(
                row,
                Require(row.VisitSourceRow?.MarkedVisit)),
            ClientHistorySourceKind.VisitCanceled => CanceledVisit(
                row,
                Require(row.VisitSourceRow?.Cancellation)),
            ClientHistorySourceKind.PaymentCreated => Payment(
                row,
                Require(row.PaymentSourceRow?.CreatedPayment)),
            ClientHistorySourceKind.PaymentCorrected => CorrectedPayment(
                row,
                Require(row.PaymentSourceRow?.Correction)),
            ClientHistorySourceKind.PaymentCanceled => CanceledPayment(
                row,
                Require(row.PaymentSourceRow?.Cancellation)),
            ClientHistorySourceKind.FreezeAdded => Freeze(
                row,
                Require(row.FreezeSourceRow?.AddedFreeze)),
            ClientHistorySourceKind.FreezeCanceled => CanceledFreeze(
                row,
                Require(row.FreezeSourceRow?.Cancellation)),
            ClientHistorySourceKind.NonWorkingDayAdded => NonWorkingDay(
                row,
                Require(row.NonWorkingDaySourceRow?.AddedPeriod)),
            ClientHistorySourceKind.NonWorkingDayCorrected => CorrectedNonWorkingDay(
                row,
                Require(row.NonWorkingDaySourceRow?.Correction)),
            ClientHistorySourceKind.NonWorkingDayCanceled => CanceledNonWorkingDay(
                row,
                Require(row.NonWorkingDaySourceRow?.Correction)),
            _ => throw Unsupported("source kind", row.Kind),
        };
    }

    private ClientHistoryRowViewModel Issued(
        ClientHistorySourceRow row,
        IssuedMembershipHistorySource source)
    {
        var status = MembershipStatus(source.Status);

        return Row(
            row,
            "Membership",
            "history-group-membership",
            "MembershipIssued",
            status,
            Facts(
                ("MembershipType", source.Snapshot.TypeName),
                ("StartDate", presentation.Date(source.StartDate)),
                ("BaseEndDate", presentation.Date(source.BaseEndDate)),
                ("DurationSnapshot", presentation.Days(source.Snapshot.DurationDays)),
                ("VisitsSnapshot", presentation.Visits(source.Snapshot.VisitsLimit)),
                ("PriceSnapshot", presentation.Money(source.Snapshot.Price))),
            change: null,
            source.Comment,
            Ids(
                ("Membership", source.MembershipId),
                ("MembershipType", source.MembershipTypeId),
                ("EntryBatch", source.EntryBatchId)));
    }

    private ClientHistoryRowViewModel Opening(
        ClientHistorySourceRow row,
        MembershipOpeningStateHistorySource source)
    {
        var status = OpeningStatus(source.Status);

        return Row(
            row,
            "OpeningState",
            "history-group-opening",
            "OpeningStateRecorded",
            status,
            Facts(
                ("OpeningAsOf", presentation.Date(source.OpeningAsOfDate)),
                ("DeclaredRemainingVisits", presentation.Number(source.DeclaredRemainingVisits)),
                ("DeclaredNegativeBalance", presentation.Number(source.DeclaredNegativeBalance)),
                ("KnownEffectiveEnd", source.KnownEffectiveEndDate is { } end
                    ? presentation.Date(end)
                    : presentation.Value("NotDeclared")),
                ("KnownExtension", source.KnownExtensionDays is { } days
                    ? presentation.Days(days)
                    : presentation.Value("NotDeclared")),
                ("SourceReference", source.SourceReference)),
            new ClientHistoryChangeViewModel(
                presentation.HistoryChange("BackfilledDeclaration"),
                source.Reason,
                Comment: null,
                Details: []),
            narrative: null,
            Ids(
                ("OpeningState", source.OpeningStateId),
                ("Membership", source.MembershipId),
                ("EntryBatch", source.EntryBatchId)));
    }

    private ClientHistoryRowViewModel MarkedVisit(
        ClientHistorySourceRow row,
        MarkedVisitHistorySource source)
    {
        var status = VisitStatus(source.CurrentStatus);
        var consumption = source.CurrentConsumption is { } currentConsumption
            ? presentation.Consumption(ConsumptionStatus(currentConsumption.Status))
            : presentation.Value("NoMembershipConsumption");

        return Row(
            row,
            "Visit",
            "history-group-visit",
            "VisitMarked",
            status,
            Facts(
                ("VisitType", presentation.VisitKind(source.VisitKind)),
                ("Membership", source.CurrentConsumption?.MembershipTypeNameSnapshot),
                ("Consumption", consumption)),
            change: null,
            source.Comment,
            Ids(
                ("Visit", source.VisitId),
                ("Consumption", source.CurrentConsumption?.ConsumptionId),
                ("Membership", source.CurrentConsumption?.MembershipId),
                ("Cancellation", source.CurrentCancellationId),
                ("EntryBatch", source.EntryBatchId)));
    }

    private ClientHistoryRowViewModel CanceledVisit(
        ClientHistorySourceRow row,
        VisitCancellationHistorySource source) => Row(
            row,
            "Visit",
            "history-group-visit",
            "VisitCanceled",
            new PresentedStatus(presentation.Status("Canceled"), "status-canceled"),
            Facts(("OriginalVisit", presentation.Value("PreservedSeparateSourceFact"))),
            new ClientHistoryChangeViewModel(
                presentation.HistoryChange("Cancellation"),
                source.Reason,
                Comment: null,
                Facts(
                    ("CanceledAt", presentation.Timestamp(source.OccurredAt)),
                    ("RecordedAt", presentation.Timestamp(source.RecordedAt)))),
            narrative: null,
            Ids(
                ("Visit", source.VisitId),
                ("Cancellation", source.CancellationId),
                ("EntryBatch", source.EntryBatchId)));

    private ClientHistoryRowViewModel Payment(
        ClientHistorySourceRow row,
        PaymentHistorySource source) => Row(
            row,
            "Payment",
            "history-group-payment",
            "PaymentRecorded",
            PaymentStatus(source.CurrentStatus),
            PaymentFacts(source),
            change: null,
            source.Comment,
            PaymentIds(source));

    private ClientHistoryRowViewModel CorrectedPayment(
        ClientHistorySourceRow row,
        PaymentCorrectionHistorySource source) => Row(
            row,
            "Payment",
            "history-group-payment",
            "PaymentCorrected",
            new PresentedStatus(presentation.Status("ReplacementActive"), "status-warning"),
            Facts(
                ("OriginalAmount", presentation.Money(source.OriginalPayment.Amount)),
                ("ReplacementAmount", presentation.Money(source.ReplacementPayment.Amount)),
                ("OriginalOccurred", presentation.Timestamp(source.OriginalPayment.OccurredAt)),
                ("ReplacementOccurred", presentation.Timestamp(source.ReplacementPayment.OccurredAt)),
                ("PaymentContext", presentation.PaymentContext(source.ReplacementPayment.PaymentContext)),
                ("Membership", source.ReplacementPayment.MembershipTypeNameSnapshot)),
            new ClientHistoryChangeViewModel(
                presentation.HistoryChange("Correction"),
                source.Reason,
                source.ReplacementPayment.Comment,
                Facts((
                    "ChangedFields",
                    string.Join(", ", source.ChangedFields.Select(presentation.ChangedField))))),
            narrative: null,
            Ids(
                ("OriginalPayment", source.OriginalPaymentId),
                ("ReplacementPayment", source.ReplacementPaymentId),
                ("Correction", source.CorrectionId),
                ("EntryBatch", source.EntryBatchId)));

    private ClientHistoryRowViewModel CanceledPayment(
        ClientHistorySourceRow row,
        PaymentCancellationHistorySource source) => Row(
            row,
            "Payment",
            "history-group-payment",
            "PaymentCanceled",
            new PresentedStatus(presentation.Status("Canceled"), "status-canceled"),
            PaymentFacts(source.Payment),
            new ClientHistoryChangeViewModel(
                presentation.HistoryChange("Cancellation"),
                source.Reason,
                Comment: null,
                Details: []),
            source.Payment.Comment,
            Ids(
                ("Payment", source.PaymentId),
                ("Cancellation", source.CancellationId),
                ("Membership", source.Payment.MembershipId),
                ("EntryBatch", source.EntryBatchId)));

    private ClientHistoryRowViewModel Freeze(
        ClientHistorySourceRow row,
        FreezeHistorySource source) => Row(
            row,
            "Freeze",
            "history-group-freeze",
            "FreezeAdded",
            FreezeStatus(source.CurrentStatus),
            FreezeFacts(source),
            change: null,
            source.Reason,
            FreezeIds(source));

    private ClientHistoryRowViewModel CanceledFreeze(
        ClientHistorySourceRow row,
        FreezeCancellationHistorySource source) => Row(
            row,
            "Freeze",
            "history-group-freeze",
            "FreezeCanceled",
            new PresentedStatus(presentation.Status("Canceled"), "status-canceled"),
            FreezeFacts(source.Freeze),
            new ClientHistoryChangeViewModel(
                presentation.HistoryChange("Cancellation"),
                source.Reason,
                Comment: null,
                Details: []),
            source.Freeze.Reason,
            Ids(
                ("Freeze", source.FreezeId),
                ("Membership", source.MembershipId),
                ("Cancellation", source.CancellationId),
                ("EntryBatch", source.EntryBatchId)));

    private ClientHistoryRowViewModel NonWorkingDay(
        ClientHistorySourceRow row,
        NonWorkingDayHistoryPeriodSource source) => Row(
            row,
            "NonWorkingDay",
            "history-group-non-working",
            "NonWorkingDayApplied",
            NonWorkingStatus(source.CurrentStatus),
            NonWorkingFacts(source),
            change: null,
            source.ReasonComment,
            NonWorkingIds(source));

    private ClientHistoryRowViewModel CorrectedNonWorkingDay(
        ClientHistorySourceRow row,
        NonWorkingDayCorrectionHistorySource source)
    {
        var changeKey = source.Mode switch
        {
            NonWorkingDayCorrectionMode.ReplaceRange => "RangeCorrection",
            NonWorkingDayCorrectionMode.ReplaceReason => "ReasonCorrection",
            NonWorkingDayCorrectionMode.Cancel => throw new InvalidOperationException(
                "Client history NonWorkingDay corrected row contains a cancellation source."),
            _ => throw Unsupported("NonWorkingDay correction mode", source.Mode),
        };
        var replacement = Require(source.ReplacementPeriod);

        return Row(
            row,
            "NonWorkingDay",
            "history-group-non-working",
            "NonWorkingDayCorrected",
            new PresentedStatus(presentation.Status("ReplacementActive"), "status-warning"),
            Facts(
                ("OriginalPeriod", Range(source.OriginalPeriod.Period)),
                ("ReplacementPeriod", Range(replacement.Period)),
                ("OriginalReason", source.OriginalPeriod.ReasonCode),
                ("ReplacementReason", replacement.ReasonCode),
                ("AffectedMemberships", presentation.Number(source.AffectedMembershipIds.Count))),
            new ClientHistoryChangeViewModel(
                presentation.HistoryChange(changeKey),
                source.CorrectionReason,
                source.CorrectionComment,
                Facts(
                    ("OriginalClientApplications", presentation.Number(source.OriginalPeriod.ClientApplications.Count)),
                    ("ReplacementClientApplications", presentation.Number(replacement.ClientApplications.Count)))),
            narrative: null,
            Ids(
                ("OriginalPeriod", source.OriginalPeriod.PeriodId),
                ("ReplacementPeriod", replacement.PeriodId)));
    }

    private ClientHistoryRowViewModel CanceledNonWorkingDay(
        ClientHistorySourceRow row,
        NonWorkingDayCorrectionHistorySource source)
    {
        if (source.Mode != NonWorkingDayCorrectionMode.Cancel)
        {
            throw new InvalidOperationException(
                "Client history NonWorkingDay canceled row contains a replacement source.");
        }

        var cancellation = Require(source.Cancellation);
        return Row(
            row,
            "NonWorkingDay",
            "history-group-non-working",
            "NonWorkingDayCanceled",
            new PresentedStatus(presentation.Status("Canceled"), "status-canceled"),
            NonWorkingFacts(source.OriginalPeriod),
            new ClientHistoryChangeViewModel(
                presentation.HistoryChange("Cancellation"),
                source.CorrectionReason,
                source.CorrectionComment,
                Facts(("AffectedMemberships", presentation.Number(source.AffectedMembershipIds.Count)))),
            narrative: null,
            Ids(
                ("Period", source.OriginalPeriod.PeriodId),
                ("Cancellation", cancellation.CancellationId)));
    }

    private ClientHistoryRowViewModel Row(
        ClientHistorySourceRow row,
        string groupKey,
        string groupClass,
        string titleKey,
        PresentedStatus status,
        IReadOnlyList<ClientHistoryFactViewModel> facts,
        ClientHistoryChangeViewModel? change,
        string? narrative,
        IReadOnlyList<ClientHistoryFactViewModel> identifiers)
    {
        var audit = row.AuditEntry;
        return new ClientHistoryRowViewModel(
            audit.AuditEntryId,
            audit.EntityId,
            row.Kind,
            presentation.HistoryGroup(groupKey),
            groupClass,
            presentation.HistoryTitle(titleKey),
            status.Label,
            status.CssClass,
            row.OccurredAt,
            row.RecordedAt,
            row.EntryOrigin,
            audit.ActorAccountId,
            audit.ActorAccountKind,
            audit.ActorRole,
            audit.SessionId,
            audit.DeviceLabel ?? presentation.HistoryDeviceNotRecorded(),
            audit.ChangedAfterClose,
            audit.Reason,
            audit.Comment,
            facts,
            change,
            narrative,
            identifiers);
    }

    private IReadOnlyList<ClientHistoryFactViewModel> PaymentFacts(
        PaymentHistorySource source) => Facts(
            ("Amount", presentation.Money(source.Amount)),
            ("Method", presentation.PaymentMethod(source.Method)),
            ("Context", presentation.PaymentContext(source.PaymentContext)),
            ("Membership", source.MembershipTypeNameSnapshot),
            ("SourceStatus", PaymentStatus(source.CurrentStatus).Label));

    private IReadOnlyList<ClientHistoryFactViewModel> PaymentIds(
        PaymentHistorySource source) => Ids(
            ("Payment", source.PaymentId),
            ("Membership", source.MembershipId),
            ("Cancellation", source.CurrentCancellationId),
            ("IncomingCorrection", source.IncomingCorrectionId),
            ("OutgoingCorrection", source.OutgoingCorrectionId),
            ("EntryBatch", source.EntryBatchId));

    private IReadOnlyList<ClientHistoryFactViewModel> FreezeFacts(
        FreezeHistorySource source) => Facts(
            ("Membership", source.MembershipTypeNameSnapshot),
            ("FreezeRange", Range(source.Range)),
            ("FreezeReason", source.Reason),
            ("SourceStatus", FreezeStatus(source.CurrentStatus).Label));

    private IReadOnlyList<ClientHistoryFactViewModel> FreezeIds(
        FreezeHistorySource source) => Ids(
            ("Freeze", source.FreezeId),
            ("Membership", source.MembershipId),
            ("Cancellation", source.CurrentCancellationId),
            ("EntryBatch", source.EntryBatchId));

    private IReadOnlyList<ClientHistoryFactViewModel> NonWorkingFacts(
        NonWorkingDayHistoryPeriodSource source) => Facts(
            ("AppliedPeriod", Range(source.Period)),
            ("ReasonCode", source.ReasonCode),
            ("ReasonComment", source.ReasonComment),
            ("ThisClientMemberships", presentation.Number(source.ClientApplications.Count)),
            ("ConfirmedMembershipScope", presentation.Number(source.ConfirmedAffectedMembershipCount)),
            ("ConfirmedClientScope", presentation.Number(source.ConfirmedAffectedClientCount)));

    private IReadOnlyList<ClientHistoryFactViewModel> NonWorkingIds(
        NonWorkingDayHistoryPeriodSource source)
    {
        var result = new List<ClientHistoryFactViewModel>
        {
            new(presentation.Identifier("Period"), source.PeriodId.ToString()),
        };
        result.AddRange(source.ClientApplications.Select(application =>
            new ClientHistoryFactViewModel(
                presentation.Identifier("ApplicationMembership"),
                $"{application.ApplicationId} / {application.MembershipId}")));
        if (source.CurrentCancellationId is { } cancellationId)
        {
            result.Add(new ClientHistoryFactViewModel(
                presentation.Identifier("Cancellation"),
                cancellationId.ToString()));
        }

        return result.AsReadOnly();
    }

    private IReadOnlyList<ClientHistoryFactViewModel> Facts(
        params (string Key, string? Value)[] values) => Array.AsReadOnly(values
            .Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .Select(value => new ClientHistoryFactViewModel(
                presentation.Fact(value.Key),
                value.Value!))
            .ToArray());

    private IReadOnlyList<ClientHistoryFactViewModel> Ids(
        params (string Key, Guid? Value)[] values) => Array.AsReadOnly(values
            .Where(value => value.Value is not null)
            .Select(value => new ClientHistoryFactViewModel(
                presentation.Identifier(value.Key),
                value.Value!.Value.ToString()))
            .ToArray());

    private string Range(DateRange value) => presentation.Text(
        "Template.DateRange",
        presentation.Date(value.StartDate),
        presentation.Date(value.EndDate));

    private PresentedStatus MembershipStatus(
        IssuedMembershipLifecycleStatus value) => value switch
        {
            IssuedMembershipLifecycleStatus.Active => new(
                presentation.Status("ActiveSource"),
                "status-active"),
            IssuedMembershipLifecycleStatus.Canceled => new(
                presentation.Status("Canceled"),
                "status-canceled"),
            IssuedMembershipLifecycleStatus.Corrected => new(
                presentation.Status("Corrected"),
                "status-warning"),
            _ => throw Unsupported("Membership status", value),
        };

    private PresentedStatus OpeningStatus(
        MembershipOpeningStateSourceStatus value) => value switch
        {
            MembershipOpeningStateSourceStatus.Active => new(
                presentation.Status("OpeningDeclaration"),
                "status-warning"),
            MembershipOpeningStateSourceStatus.Canceled => new(
                presentation.Status("Canceled"),
                "status-canceled"),
            MembershipOpeningStateSourceStatus.Corrected => new(
                presentation.Status("Corrected"),
                "status-warning"),
            _ => throw Unsupported("opening-state status", value),
        };

    private PresentedStatus VisitStatus(ClientVisitRowStatus value) => value switch
    {
        ClientVisitRowStatus.Active => new(
            presentation.Status("ActiveSource"),
            "status-active"),
        ClientVisitRowStatus.Canceled => new(
            presentation.Status("Canceled"),
            "status-canceled"),
        _ => throw Unsupported("Visit status", value),
    };

    private static string ConsumptionStatus(
        ClientVisitConsumptionStatus value) => value switch
        {
            ClientVisitConsumptionStatus.Active => "Counted",
            ClientVisitConsumptionStatus.Canceled => "CanceledMembership",
            _ => throw Unsupported("Visit consumption status", value),
        };

    private PresentedStatus PaymentStatus(ClientPaymentRowStatus value) => value switch
    {
        ClientPaymentRowStatus.Active => new(
            presentation.Status("ActiveSource"),
            "status-active"),
        ClientPaymentRowStatus.Canceled => new(
            presentation.Status("Canceled"),
            "status-canceled"),
        ClientPaymentRowStatus.Replaced => new(
            presentation.Status("Replaced"),
            "status-warning"),
        _ => throw Unsupported("Payment status", value),
    };

    private PresentedStatus FreezeStatus(
        FreezeCancellationSourceStatus value) => value switch
        {
            FreezeCancellationSourceStatus.Active => new(
                presentation.Status("ActiveSource"),
                "status-active"),
            FreezeCancellationSourceStatus.Canceled => new(
                presentation.Status("Canceled"),
                "status-canceled"),
            _ => throw Unsupported("Freeze status", value),
        };

    private PresentedStatus NonWorkingStatus(
        NonWorkingDayCorrectionSourceStatus value) => value switch
        {
            NonWorkingDayCorrectionSourceStatus.Active => new(
                presentation.Status("ActiveApplication"),
                "status-active"),
            NonWorkingDayCorrectionSourceStatus.Canceled => new(
                presentation.Status("Canceled"),
                "status-canceled"),
            NonWorkingDayCorrectionSourceStatus.Corrected => new(
                presentation.Status("Corrected"),
                "status-warning"),
            _ => throw Unsupported("NonWorkingDay status", value),
        };

    private static T Require<T>(T? value)
        where T : class => value ?? throw new InvalidOperationException(
            "Client history source projection is incomplete.");

    private static InvalidOperationException Unsupported<T>(
        string scope,
        T value)
        where T : struct, Enum => new(
            $"Unsupported Client history {scope} '{value}'.");

    private readonly record struct PresentedStatus(string Label, string CssClass);
}
