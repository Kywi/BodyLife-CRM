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

namespace BodyLife.Crm.Web.Pages.Audit;

public sealed record ClientHistoryRowViewModel(
    AuditEntryId AuditEntryId,
    Guid SourceEntityId,
    ClientHistorySourceKind Kind,
    string GroupLabel,
    string GroupClass,
    string Title,
    string StatusLabel,
    string StatusClass,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    EntryOrigin EntryOrigin,
    AccountId ActorAccountId,
    AccountKind ActorAccountKind,
    ActorRole ActorRole,
    SessionId SessionId,
    string DeviceLabel,
    bool ChangedAfterClose,
    string? AuditReason,
    string? AuditComment,
    IReadOnlyList<ClientHistoryFactViewModel> Facts,
    ClientHistoryChangeViewModel? Change,
    string? Narrative,
    IReadOnlyList<ClientHistoryFactViewModel> Identifiers)
{
    public static ClientHistoryRowViewModel Create(ClientHistorySourceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Kind switch
        {
            ClientHistorySourceKind.MembershipIssued
                => CreateIssuedMembership(row, Require(row.MembershipSourceRow?.IssuedMembership)),
            ClientHistorySourceKind.MembershipOpeningStateCreated
                => CreateOpeningState(row, Require(row.MembershipSourceRow?.OpeningState)),
            ClientHistorySourceKind.VisitMarked
                => CreateMarkedVisit(row, Require(row.VisitSourceRow?.MarkedVisit)),
            ClientHistorySourceKind.VisitCanceled
                => CreateCanceledVisit(row, Require(row.VisitSourceRow?.Cancellation)),
            ClientHistorySourceKind.PaymentCreated
                => CreatePayment(row, Require(row.PaymentSourceRow?.CreatedPayment)),
            ClientHistorySourceKind.PaymentCorrected
                => CreateCorrectedPayment(row, Require(row.PaymentSourceRow?.Correction)),
            ClientHistorySourceKind.PaymentCanceled
                => CreateCanceledPayment(row, Require(row.PaymentSourceRow?.Cancellation)),
            ClientHistorySourceKind.FreezeAdded
                => CreateFreeze(row, Require(row.FreezeSourceRow?.AddedFreeze)),
            ClientHistorySourceKind.FreezeCanceled
                => CreateCanceledFreeze(row, Require(row.FreezeSourceRow?.Cancellation)),
            ClientHistorySourceKind.NonWorkingDayAdded
                => CreateNonWorkingDay(row, Require(row.NonWorkingDaySourceRow?.AddedPeriod)),
            ClientHistorySourceKind.NonWorkingDayCorrected
                => CreateCorrectedNonWorkingDay(
                    row,
                    Require(row.NonWorkingDaySourceRow?.Correction)),
            ClientHistorySourceKind.NonWorkingDayCanceled
                => CreateCanceledNonWorkingDay(
                    row,
                    Require(row.NonWorkingDaySourceRow?.Correction)),
            _ => throw new InvalidOperationException(
                $"Unsupported Client history source kind '{row.Kind}'."),
        };
    }

    private static ClientHistoryRowViewModel CreateIssuedMembership(
        ClientHistorySourceRow row,
        IssuedMembershipHistorySource source)
    {
        return CreateRow(
            row,
            "Membership",
            "history-group-membership",
            "Membership issued",
            LifecycleStatusLabel(source.Status),
            LifecycleStatusClass(source.Status),
            BuildFacts(
                ("Membership type", source.Snapshot.TypeName),
                ("Start date", DateLabel(source.StartDate)),
                ("Base end date", DateLabel(source.BaseEndDate)),
                ("Duration snapshot", $"{source.Snapshot.DurationDays} days"),
                ("Visits snapshot", source.Snapshot.VisitsLimit.ToString(CultureInfo.InvariantCulture)),
                ("Price snapshot", MoneyLabel(source.Snapshot.Price))),
            change: null,
            source.Comment,
            BuildIdentifiers(
                ("Membership ID", source.MembershipId),
                ("Membership type ID", source.MembershipTypeId),
                ("Entry batch ID", source.EntryBatchId)));
    }

    private static ClientHistoryRowViewModel CreateOpeningState(
        ClientHistorySourceRow row,
        MembershipOpeningStateHistorySource source)
    {
        return CreateRow(
            row,
            "Opening state",
            "history-group-opening",
            "Opening state recorded",
            OpeningStatusLabel(source.Status),
            source.Status == MembershipOpeningStateSourceStatus.Active
                ? "status-warning"
                : "status-canceled",
            BuildFacts(
                ("Opening as of", DateLabel(source.OpeningAsOfDate)),
                ("Declared remaining visits", source.DeclaredRemainingVisits.ToString(CultureInfo.InvariantCulture)),
                ("Declared negative balance", source.DeclaredNegativeBalance.ToString(CultureInfo.InvariantCulture)),
                ("Known effective end", OptionalDateLabel(source.KnownEffectiveEndDate)),
                ("Known extension", source.KnownExtensionDays is { } extension
                    ? $"{extension} days"
                    : "Not declared"),
                ("Source reference", source.SourceReference)),
            new ClientHistoryChangeViewModel(
                "Backfilled declaration",
                source.Reason,
                Comment: null,
                Details: []),
            narrative: null,
            BuildIdentifiers(
                ("Opening state ID", source.OpeningStateId),
                ("Membership ID", source.MembershipId),
                ("Entry batch ID", source.EntryBatchId)));
    }

    private static ClientHistoryRowViewModel CreateMarkedVisit(
        ClientHistorySourceRow row,
        MarkedVisitHistorySource source)
    {
        return CreateRow(
            row,
            "Visit",
            "history-group-visit",
            "Visit marked",
            VisitStatusLabel(source.CurrentStatus),
            source.CurrentStatus == ClientVisitRowStatus.Active
                ? "status-active"
                : "status-canceled",
            BuildFacts(
                ("Visit type", VisitKindLabel(source.VisitKind)),
                ("Membership", source.CurrentConsumption?.MembershipTypeNameSnapshot),
                ("Consumption", source.CurrentConsumption is { } consumption
                    ? ConsumptionStatusLabel(consumption.Status)
                    : "No membership consumption")),
            change: null,
            source.Comment,
            BuildIdentifiers(
                ("Visit ID", source.VisitId),
                ("Consumption ID", source.CurrentConsumption?.ConsumptionId),
                ("Membership ID", source.CurrentConsumption?.MembershipId),
                ("Cancellation ID", source.CurrentCancellationId),
                ("Entry batch ID", source.EntryBatchId)));
    }

    private static ClientHistoryRowViewModel CreateCanceledVisit(
        ClientHistorySourceRow row,
        VisitCancellationHistorySource source)
    {
        return CreateRow(
            row,
            "Visit",
            "history-group-visit",
            "Visit canceled",
            "Canceled",
            "status-canceled",
            BuildFacts(("Original visit", "Preserved as a separate source fact")),
            new ClientHistoryChangeViewModel(
                "Cancellation",
                source.Reason,
                Comment: null,
                BuildFacts(
                    ("Canceled at", TimelineModel.TimestampLabel(source.OccurredAt)),
                    ("Recorded at", TimelineModel.TimestampLabel(source.RecordedAt)))),
            narrative: null,
            BuildIdentifiers(
                ("Visit ID", source.VisitId),
                ("Cancellation ID", source.CancellationId),
                ("Entry batch ID", source.EntryBatchId)));
    }

    private static ClientHistoryRowViewModel CreatePayment(
        ClientHistorySourceRow row,
        PaymentHistorySource source)
    {
        return CreateRow(
            row,
            "Payment",
            "history-group-payment",
            "Payment recorded",
            PaymentStatusLabel(source.CurrentStatus),
            PaymentStatusClass(source.CurrentStatus),
            PaymentFacts(source),
            change: null,
            source.Comment,
            PaymentIdentifiers(source));
    }

    private static ClientHistoryRowViewModel CreateCorrectedPayment(
        ClientHistorySourceRow row,
        PaymentCorrectionHistorySource source)
    {
        return CreateRow(
            row,
            "Payment",
            "history-group-payment",
            "Payment corrected",
            "Replacement active",
            "status-warning",
            BuildFacts(
                ("Original amount", MoneyLabel(source.OriginalPayment.Amount)),
                ("Replacement amount", MoneyLabel(source.ReplacementPayment.Amount)),
                ("Original occurred", TimelineModel.TimestampLabel(source.OriginalPayment.OccurredAt)),
                ("Replacement occurred", TimelineModel.TimestampLabel(source.ReplacementPayment.OccurredAt)),
                ("Payment context", PaymentContextLabel(source.ReplacementPayment.PaymentContext)),
                ("Membership", source.ReplacementPayment.MembershipTypeNameSnapshot)),
            new ClientHistoryChangeViewModel(
                "Correction",
                source.Reason,
                source.ReplacementPayment.Comment,
                BuildFacts(("Changed fields", string.Join(", ", source.ChangedFields)))),
            narrative: null,
            BuildIdentifiers(
                ("Original payment ID", source.OriginalPaymentId),
                ("Replacement payment ID", source.ReplacementPaymentId),
                ("Correction ID", source.CorrectionId),
                ("Entry batch ID", source.EntryBatchId)));
    }

    private static ClientHistoryRowViewModel CreateCanceledPayment(
        ClientHistorySourceRow row,
        PaymentCancellationHistorySource source)
    {
        return CreateRow(
            row,
            "Payment",
            "history-group-payment",
            "Payment canceled",
            "Canceled",
            "status-canceled",
            PaymentFacts(source.Payment),
            new ClientHistoryChangeViewModel(
                "Cancellation",
                source.Reason,
                Comment: null,
                Details: []),
            source.Payment.Comment,
            BuildIdentifiers(
                ("Payment ID", source.PaymentId),
                ("Cancellation ID", source.CancellationId),
                ("Membership ID", source.Payment.MembershipId),
                ("Entry batch ID", source.EntryBatchId)));
    }

    private static ClientHistoryRowViewModel CreateFreeze(
        ClientHistorySourceRow row,
        FreezeHistorySource source)
    {
        return CreateRow(
            row,
            "Freeze",
            "history-group-freeze",
            "Freeze added",
            FreezeStatusLabel(source.CurrentStatus),
            source.CurrentStatus == FreezeCancellationSourceStatus.Active
                ? "status-active"
                : "status-canceled",
            FreezeFacts(source),
            change: null,
            source.Reason,
            FreezeIdentifiers(source));
    }

    private static ClientHistoryRowViewModel CreateCanceledFreeze(
        ClientHistorySourceRow row,
        FreezeCancellationHistorySource source)
    {
        return CreateRow(
            row,
            "Freeze",
            "history-group-freeze",
            "Freeze canceled",
            "Canceled",
            "status-canceled",
            FreezeFacts(source.Freeze),
            new ClientHistoryChangeViewModel(
                "Cancellation",
                source.Reason,
                Comment: null,
                Details: []),
            source.Freeze.Reason,
            BuildIdentifiers(
                ("Freeze ID", source.FreezeId),
                ("Membership ID", source.MembershipId),
                ("Cancellation ID", source.CancellationId),
                ("Entry batch ID", source.EntryBatchId)));
    }

    private static ClientHistoryRowViewModel CreateNonWorkingDay(
        ClientHistorySourceRow row,
        NonWorkingDayHistoryPeriodSource source)
    {
        return CreateRow(
            row,
            "Non-working day",
            "history-group-non-working",
            "Non-working period applied",
            NonWorkingStatusLabel(source.CurrentStatus),
            source.CurrentStatus == NonWorkingDayCorrectionSourceStatus.Active
                ? "status-active"
                : "status-warning",
            NonWorkingDayFacts(source),
            change: null,
            source.ReasonComment,
            NonWorkingDayIdentifiers(source));
    }

    private static ClientHistoryRowViewModel CreateCorrectedNonWorkingDay(
        ClientHistorySourceRow row,
        NonWorkingDayCorrectionHistorySource source)
    {
        var replacement = Require(source.ReplacementPeriod);
        return CreateRow(
            row,
            "Non-working day",
            "history-group-non-working",
            "Non-working period corrected",
            "Replacement active",
            "status-warning",
            BuildFacts(
                ("Original period", DateRangeLabel(source.OriginalPeriod.Period)),
                ("Replacement period", DateRangeLabel(replacement.Period)),
                ("Original reason", source.OriginalPeriod.ReasonCode),
                ("Replacement reason", replacement.ReasonCode),
                ("Affected memberships", source.AffectedMembershipIds.Count.ToString(CultureInfo.InvariantCulture))),
            new ClientHistoryChangeViewModel(
                NonWorkingModeLabel(source.Mode),
                source.CorrectionReason,
                source.CorrectionComment,
                BuildFacts(
                    ("Original client applications", source.OriginalPeriod.ClientApplications.Count.ToString(CultureInfo.InvariantCulture)),
                    ("Replacement client applications", replacement.ClientApplications.Count.ToString(CultureInfo.InvariantCulture)))),
            narrative: null,
            BuildIdentifiers(
                ("Original period ID", source.OriginalPeriod.PeriodId),
                ("Replacement period ID", replacement.PeriodId)));
    }

    private static ClientHistoryRowViewModel CreateCanceledNonWorkingDay(
        ClientHistorySourceRow row,
        NonWorkingDayCorrectionHistorySource source)
    {
        return CreateRow(
            row,
            "Non-working day",
            "history-group-non-working",
            "Non-working period canceled",
            "Canceled",
            "status-canceled",
            NonWorkingDayFacts(source.OriginalPeriod),
            new ClientHistoryChangeViewModel(
                "Cancellation",
                source.CorrectionReason,
                source.CorrectionComment,
                BuildFacts(("Affected memberships", source.AffectedMembershipIds.Count.ToString(CultureInfo.InvariantCulture)))),
            narrative: null,
            BuildIdentifiers(
                ("Period ID", source.OriginalPeriod.PeriodId),
                ("Cancellation ID", source.Cancellation?.CancellationId)));
    }

    private static ClientHistoryRowViewModel CreateRow(
        ClientHistorySourceRow row,
        string groupLabel,
        string groupClass,
        string title,
        string statusLabel,
        string statusClass,
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
            groupLabel,
            groupClass,
            title,
            statusLabel,
            statusClass,
            row.OccurredAt,
            row.RecordedAt,
            row.EntryOrigin,
            audit.ActorAccountId,
            audit.ActorAccountKind,
            audit.ActorRole,
            audit.SessionId,
            audit.DeviceLabel ?? "Not recorded",
            audit.ChangedAfterClose,
            audit.Reason,
            audit.Comment,
            facts,
            change,
            narrative,
            identifiers);
    }

    private static IReadOnlyList<ClientHistoryFactViewModel> PaymentFacts(
        PaymentHistorySource source)
    {
        return BuildFacts(
            ("Amount", MoneyLabel(source.Amount)),
            ("Method", source.Method == PaymentMethod.Cash ? "Cash" : "Payment"),
            ("Context", PaymentContextLabel(source.PaymentContext)),
            ("Membership", source.MembershipTypeNameSnapshot),
            ("Source status", PaymentStatusLabel(source.CurrentStatus)));
    }

    private static IReadOnlyList<ClientHistoryFactViewModel> PaymentIdentifiers(
        PaymentHistorySource source)
    {
        return BuildIdentifiers(
            ("Payment ID", source.PaymentId),
            ("Membership ID", source.MembershipId),
            ("Cancellation ID", source.CurrentCancellationId),
            ("Incoming correction ID", source.IncomingCorrectionId),
            ("Outgoing correction ID", source.OutgoingCorrectionId),
            ("Entry batch ID", source.EntryBatchId));
    }

    private static IReadOnlyList<ClientHistoryFactViewModel> FreezeFacts(
        FreezeHistorySource source)
    {
        return BuildFacts(
            ("Membership", source.MembershipTypeNameSnapshot),
            ("Freeze range", DateRangeLabel(source.Range)),
            ("Freeze reason", source.Reason),
            ("Source status", FreezeStatusLabel(source.CurrentStatus)));
    }

    private static IReadOnlyList<ClientHistoryFactViewModel> FreezeIdentifiers(
        FreezeHistorySource source)
    {
        return BuildIdentifiers(
            ("Freeze ID", source.FreezeId),
            ("Membership ID", source.MembershipId),
            ("Cancellation ID", source.CurrentCancellationId),
            ("Entry batch ID", source.EntryBatchId));
    }

    private static IReadOnlyList<ClientHistoryFactViewModel> NonWorkingDayFacts(
        NonWorkingDayHistoryPeriodSource source)
    {
        return BuildFacts(
            ("Applied period", DateRangeLabel(source.Period)),
            ("Reason code", source.ReasonCode),
            ("Reason comment", source.ReasonComment),
            ("This client memberships", source.ClientApplications.Count.ToString(CultureInfo.InvariantCulture)),
            ("Confirmed membership scope", source.ConfirmedAffectedMembershipCount.ToString(CultureInfo.InvariantCulture)),
            ("Confirmed client scope", source.ConfirmedAffectedClientCount.ToString(CultureInfo.InvariantCulture)));
    }

    private static IReadOnlyList<ClientHistoryFactViewModel> NonWorkingDayIdentifiers(
        NonWorkingDayHistoryPeriodSource source)
    {
        var identifiers = new List<ClientHistoryFactViewModel>
        {
            new("Period ID", source.PeriodId.ToString()),
        };
        identifiers.AddRange(source.ClientApplications.Select(application =>
            new ClientHistoryFactViewModel(
                "Application / membership",
                $"{application.ApplicationId} / {application.MembershipId}")));
        if (source.CurrentCancellationId is { } cancellationId)
        {
            identifiers.Add(new ClientHistoryFactViewModel(
                "Cancellation ID",
                cancellationId.ToString()));
        }

        return identifiers.AsReadOnly();
    }

    private static IReadOnlyList<ClientHistoryFactViewModel> BuildFacts(
        params (string Label, string? Value)[] values)
    {
        return Array.AsReadOnly(values
            .Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .Select(value => new ClientHistoryFactViewModel(
                value.Label,
                value.Value!))
            .ToArray());
    }

    private static IReadOnlyList<ClientHistoryFactViewModel> BuildIdentifiers(
        params (string Label, Guid? Value)[] values)
    {
        return Array.AsReadOnly(values
            .Where(value => value.Value is not null)
            .Select(value => new ClientHistoryFactViewModel(
                value.Label,
                value.Value!.Value.ToString()))
            .ToArray());
    }

    private static T Require<T>(T? source)
        where T : class
    {
        return source ?? throw new InvalidOperationException(
            "Client history source projection is incomplete.");
    }

    private static string DateLabel(DateOnly date) => date.ToString("yyyy-MM-dd");

    private static string OptionalDateLabel(DateOnly? date) =>
        date is { } value ? DateLabel(value) : "Not declared";

    private static string DateRangeLabel(DateRange range) =>
        $"{DateLabel(range.StartDate)} to {DateLabel(range.EndDate)}";

    private static string MoneyLabel(Money money) =>
        $"{money.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {money.Currency}";

    private static string LifecycleStatusLabel(IssuedMembershipLifecycleStatus status) =>
        status switch
        {
            IssuedMembershipLifecycleStatus.Active => "Active source",
            IssuedMembershipLifecycleStatus.Canceled => "Canceled",
            IssuedMembershipLifecycleStatus.Corrected => "Corrected",
            _ => "Membership source",
        };

    private static string LifecycleStatusClass(IssuedMembershipLifecycleStatus status) =>
        status == IssuedMembershipLifecycleStatus.Active
            ? "status-active"
            : "status-canceled";

    private static string OpeningStatusLabel(MembershipOpeningStateSourceStatus status) =>
        status switch
        {
            MembershipOpeningStateSourceStatus.Active => "Opening declaration",
            MembershipOpeningStateSourceStatus.Canceled => "Canceled",
            MembershipOpeningStateSourceStatus.Corrected => "Corrected",
            _ => "Opening state",
        };

    private static string VisitKindLabel(VisitKind kind) => kind switch
    {
        VisitKind.Membership => "Membership visit",
        VisitKind.OneOff => "One-off visit",
        VisitKind.Trial => "Trial visit",
        _ => "Visit",
    };

    private static string VisitStatusLabel(ClientVisitRowStatus status) =>
        status == ClientVisitRowStatus.Active ? "Active source" : "Canceled";

    private static string ConsumptionStatusLabel(ClientVisitConsumptionStatus status) =>
        status == ClientVisitConsumptionStatus.Active
            ? "Counted membership consumption"
            : "Canceled membership consumption";

    private static string PaymentContextLabel(PaymentContext context) => context switch
    {
        PaymentContext.MembershipSale => "Membership sale",
        PaymentContext.OneOff => "One-off visit",
        PaymentContext.Trial => "Trial visit",
        PaymentContext.NegativeClosure => "Negative closure",
        PaymentContext.Other => "Other",
        _ => "Payment",
    };

    private static string PaymentStatusLabel(ClientPaymentRowStatus status) => status switch
    {
        ClientPaymentRowStatus.Active => "Active source",
        ClientPaymentRowStatus.Canceled => "Canceled",
        ClientPaymentRowStatus.Replaced => "Replaced",
        _ => "Payment source",
    };

    private static string PaymentStatusClass(ClientPaymentRowStatus status) => status switch
    {
        ClientPaymentRowStatus.Active => "status-active",
        ClientPaymentRowStatus.Canceled => "status-canceled",
        ClientPaymentRowStatus.Replaced => "status-warning",
        _ => "status-warning",
    };

    private static string FreezeStatusLabel(FreezeCancellationSourceStatus status) =>
        status == FreezeCancellationSourceStatus.Active ? "Active source" : "Canceled";

    private static string NonWorkingStatusLabel(
        NonWorkingDayCorrectionSourceStatus status) => status switch
        {
            NonWorkingDayCorrectionSourceStatus.Active => "Active application",
            NonWorkingDayCorrectionSourceStatus.Canceled => "Canceled",
            NonWorkingDayCorrectionSourceStatus.Corrected => "Corrected",
            _ => "Non-working source",
        };

    private static string NonWorkingModeLabel(NonWorkingDayCorrectionMode mode) => mode switch
    {
        NonWorkingDayCorrectionMode.ReplaceRange => "Range correction",
        NonWorkingDayCorrectionMode.ReplaceReason => "Reason correction",
        NonWorkingDayCorrectionMode.Cancel => "Cancellation",
        _ => "Correction",
    };
}

public sealed record ClientHistoryFactViewModel(string Label, string Value);

public sealed record ClientHistoryChangeViewModel(
    string Label,
    string Reason,
    string? Comment,
    IReadOnlyList<ClientHistoryFactViewModel> Details);
