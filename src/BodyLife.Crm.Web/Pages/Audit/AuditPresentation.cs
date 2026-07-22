using System.Globalization;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Localization;
using Microsoft.Extensions.Localization;

namespace BodyLife.Crm.Web.Pages.Audit;

/// <summary>Culture-aware, presentation-only labels for the Audit pages.</summary>
public sealed class AuditPresentation(
    IStringLocalizer<Localization.Audit> audit,
    IStringLocalizer<BodyLife.Crm.Web.Localization.Shared> shared,
    ILogger<AuditPresentation> logger)
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

    public string Text(string key, params object[] arguments)
    {
        var localized = audit[key, arguments];
        if (!localized.ResourceNotFound)
        {
            return localized.Value;
        }

        logger.LogError(
            "Missing Audit UI localization resource {ResourceKey} for culture {Culture}",
            key,
            CultureInfo.CurrentUICulture.Name);
        var fallback = audit["Value.Unavailable"];
        return fallback.ResourceNotFound
            ? throw new InvalidOperationException(
                "The Audit localization fallback resource is unavailable.")
            : fallback.Value;
    }
    public string SharedText(string key, params object[] arguments) => shared[key, arguments];
    public string Action(string value) => Text($"Action.{value}");
    public string Entity(AuditTimelineEntityType value) => Text($"Entity.{value}");
    public string HistoryEntity(ClientHistoryEntityFilter value) => Text($"HistoryEntity.{value}");
    public string AccountKind(AccountKind value) => Text($"AccountKind.{value}");
    public string Role(ActorRole value) => Text($"Role.{value}");
    public string EntryOrigin(EntryOrigin value) => Text($"EntryOrigin.{value}");
    public string Date(DateOnly value) => ReceptionDisplayFormatter.Date(value);
    public string Timestamp(DateTimeOffset value) => ReceptionDisplayFormatter.DateTime(value);
    public string Money(Money value) => ReceptionDisplayFormatter.Money(value);
    public string Days(int value) => WebPluralizer.Days(shared, value);
    public string Visits(int value) => WebPluralizer.Visits(shared, value);
    public string Entries(int value) => WebPluralizer.Entries(shared, value);
    public string Rows(int value) => WebPluralizer.Rows(shared, value);
    public string Number(int value) => value.ToString(CultureInfo.CurrentCulture);
    public string HistoryGroup(string value) => Text($"History.Group.{value}");
    public string HistoryTitle(string value) => Text($"History.{value}.Title");
    public string Status(string value) => Text($"Status.{value}");
    public string Fact(string value) => Text($"Fact.{value}");
    public string Identifier(string value) => Text($"Identifier.{value}");
    public string HistoryChange(string value) => Text($"HistoryChange.{value}");
    public string Value(string value) => Text($"Value.{value}");
    public string VisitKind(VisitKind value) => value switch
    {
        BodyLife.Crm.Modules.Visits.VisitKind.Membership => Text("VisitKind.Membership"),
        BodyLife.Crm.Modules.Visits.VisitKind.OneOff => Text("VisitKind.OneOff"),
        BodyLife.Crm.Modules.Visits.VisitKind.Trial => Text("VisitKind.Trial"),
        _ => throw Unsupported(nameof(VisitKind), value),
    };
    public string Consumption(string value) => Text($"Consumption.{value}");
    public string Explanation(string key, params object[] arguments) =>
        Text($"Explanation.{key}", arguments);
    public string PaymentMethod(PaymentMethod value) => value switch
    {
        BodyLife.Crm.Modules.Payments.PaymentMethod.Cash => Text("PaymentMethod.Cash"),
        _ => throw Unsupported(nameof(PaymentMethod), value),
    };
    public string PaymentContext(PaymentContext value) => value switch
    {
        BodyLife.Crm.Modules.Payments.PaymentContext.MembershipSale => Text("PaymentContext.MembershipSale"),
        BodyLife.Crm.Modules.Payments.PaymentContext.OneOff => Text("PaymentContext.OneOff"),
        BodyLife.Crm.Modules.Payments.PaymentContext.Trial => Text("PaymentContext.Trial"),
        BodyLife.Crm.Modules.Payments.PaymentContext.NegativeClosure => Text("PaymentContext.NegativeClosure"),
        BodyLife.Crm.Modules.Payments.PaymentContext.Other => Text("PaymentContext.Other"),
        _ => throw Unsupported(nameof(PaymentContext), value),
    };
    public string Changed(string value) => Text($"Changed.{value}");
    public string ChangedField(string code) => code switch
    {
        "amount" => Text("Changed.Amount"),
        "occurred_at" => Text("Changed.OccurredTime"),
        "payment_context" => Text("Changed.PaymentContext"),
        "membership_id" => Text("Changed.Membership"),
        "comment" => Text("Changed.Comment"),
        _ => throw new InvalidOperationException(
            $"Unsupported Client history changed-field code '{code}'."),
    };
    public string HistoryDeviceNotRecorded() => Text("History.Device.NotRecorded");
    public string ShortId(Guid value) => value.ToString("N")[..8];
    public string EntryOriginClass(EntryOrigin value) => value switch
    {
        BodyLife.Crm.Application.Commands.EntryOrigin.Normal => "audit-origin-normal",
        BodyLife.Crm.Application.Commands.EntryOrigin.ManualBackfill => "audit-origin-backfill",
        BodyLife.Crm.Application.Commands.EntryOrigin.PaperFallback => "audit-origin-fallback",
        BodyLife.Crm.Application.Commands.EntryOrigin.FutureImport => "audit-origin-import",
        _ => "audit-origin-normal",
    };
    public string EntryClass(bool changedAfterClose, EntryOrigin origin) => changedAfterClose
        ? "audit-entry-changed-after-close"
        : origin is BodyLife.Crm.Application.Commands.EntryOrigin.PaperFallback or BodyLife.Crm.Application.Commands.EntryOrigin.ManualBackfill ? "audit-entry-backdated" : string.Empty;
    public string TimelineError(GetAuditTimelineStatus? status) => status switch
    {
        GetAuditTimelineStatus.PermissionDenied => Text("Error.PermissionDenied"),
        GetAuditTimelineStatus.NotFound => Text("Error.NotFound"),
        GetAuditTimelineStatus.ValidationFailed => Text("Error.InvalidFilter"),
        GetAuditTimelineStatus.SourceInconsistent => Text("Error.Unavailable"),
        _ => Text("Error.Unavailable"),
    };
    public string HistoryError(GetClientHistoryStatus? status) => status switch
    {
        GetClientHistoryStatus.PermissionDenied => Text("Error.PermissionDenied"),
        GetClientHistoryStatus.NotFound => Text("Error.NotFound"),
        GetClientHistoryStatus.ValidationFailed => Text("Error.InvalidFilter"),
        GetClientHistoryStatus.SourceInconsistent => Text("Error.Unavailable"),
        _ => Text("Error.Unavailable"),
    };
    public string Json(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, PrettyJsonOptions);
        }
        catch (JsonException) { return Text("Json.Unavailable"); }
    }
    public string DateRange(DateOnly? from, DateOnly? to) => (from, to) switch
    {
        ({ } start, { } end) => Text("Range.Between", Date(start), Date(end)),
        ({ } start, null) => Text("Range.From", Date(start)),
        (null, { } end) => Text("Range.Through", Date(end)),
        _ => Text("Range.All"),
    };

    private static InvalidOperationException Unsupported<T>(string name, T value)
        where T : struct, Enum => new(
            $"Unsupported Client history {name} value '{value}'.");
}
