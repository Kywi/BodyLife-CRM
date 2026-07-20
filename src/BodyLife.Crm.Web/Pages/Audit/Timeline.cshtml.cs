using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Audit;

public sealed class TimelineModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<GetAuditTimelineQuery, GetAuditTimelineResult>
        getAuditTimeline)
    : PageModel
{
    public const int PageSize = 10;

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
    };

    public static IReadOnlyList<AuditTimelineEntityOption> EntityOptions { get; } =
    [
        new(AuditTimelineEntityType.Client, "Client"),
        new(AuditTimelineEntityType.MembershipType, "Membership type"),
        new(AuditTimelineEntityType.Membership, "Membership"),
        new(AuditTimelineEntityType.MembershipOpeningState, "Opening state"),
        new(AuditTimelineEntityType.Visit, "Visit"),
        new(AuditTimelineEntityType.Payment, "Payment"),
        new(AuditTimelineEntityType.Freeze, "Freeze"),
        new(AuditTimelineEntityType.NonWorkingPeriod, "Non-working period"),
        new(AuditTimelineEntityType.StaffAccount, "Staff account"),
    ];

    public static IReadOnlyList<AuditTimelineActionOption> ActionOptions { get; } =
    [
        new("client.created", "Client created"),
        new("client.updated", "Client updated"),
        new("card.assigned", "Card assigned"),
        new("card.changed", "Card changed"),
        new("card.cleared", "Card cleared"),
        new("membership_type.created", "Membership type created"),
        new("membership_type.edited", "Membership type edited"),
        new("membership_type.deactivated", "Membership type deactivated"),
        new("membership.issued", "Membership issued"),
        new("membership_opening_state.created", "Opening state created"),
        new("visit.marked", "Visit marked"),
        new("visit.canceled", "Visit canceled"),
        new("payment.created", "Payment created"),
        new("payment.corrected", "Payment corrected"),
        new("payment.canceled", "Payment canceled"),
        new("freeze.added", "Freeze added"),
        new("freeze.canceled", "Freeze canceled"),
        new("non_working_day.added", "Non-working day added"),
        new("non_working_day.corrected", "Non-working day corrected"),
        new("non_working_day.canceled", "Non-working day canceled"),
        new("staff_account.created", "Staff account created"),
        new("staff_account.display_name_updated", "Staff name updated"),
        new("staff_account.activated", "Staff account activated"),
        new("staff_account.deactivated", "Staff account deactivated"),
        new("staff_credentials.configured", "Staff credentials configured"),
        new("staff_credentials.reset", "Staff credentials reset"),
    ];

    [BindProperty(SupportsGet = true, Name = "clientId")]
    public Guid? ClientId { get; set; }

    [BindProperty(SupportsGet = true, Name = "entity")]
    public AuditTimelineEntityType? EntityType { get; set; }

    [BindProperty(SupportsGet = true, Name = "entityId")]
    public Guid? EntityId { get; set; }

    [BindProperty(SupportsGet = true, Name = "from")]
    public DateOnly? RecordedFromDate { get; set; }

    [BindProperty(SupportsGet = true, Name = "to")]
    public DateOnly? RecordedToDate { get; set; }

    [BindProperty(SupportsGet = true, Name = "action")]
    public string? ActionType { get; set; }

    [BindProperty(SupportsGet = true, Name = "offset")]
    public int? Offset { get; set; }

    public GetAuditTimelineResult? Result { get; private set; }

    public AuditTimelinePage? TimelinePage =>
        Result is { Status: GetAuditTimelineStatus.Success }
            ? Result.Page
            : null;

    public int? PreviousOffset => TimelinePage is { Offset: > 0 } page
        ? Math.Max(0, page.Offset - PageSize)
        : null;

    public int CurrentPageNumber =>
        ((TimelinePage?.Offset ?? 0) / PageSize) + 1;

    public string? LoadError { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            LoadError = "Enter valid Client, entity, date and page filter values.";
            return;
        }

        ActionType = NormalizeOptional(ActionType);
        Offset ??= 0;
        if (ActionType is not null
            && !ActionOptions.Any(option => option.Value == ActionType))
        {
            LoadError = "Select a supported business action.";
            return;
        }

        if (!TryBuildRecordedRange(
                RecordedFromDate,
                RecordedToDate,
                out var recordedFromInclusive,
                out var recordedBeforeExclusive))
        {
            LoadError = "Recorded-through date is outside the supported range.";
            return;
        }

        Result = await getAuditTimeline.ExecuteAsync(
            new GetAuditTimelineQuery(
                requestContextResolver.Require().Actor,
                ClientId,
                EntityType,
                EntityId,
                recordedFromInclusive,
                recordedBeforeExclusive,
                ActionType is null ? null : [ActionType],
                Limit: PageSize,
                Offset: Offset.Value),
            cancellationToken);

        if (Result is { Status: GetAuditTimelineStatus.Success, Page: null })
        {
            LoadError = "Audit timeline returned no canonical page.";
        }
    }

    public static string ActionLabel(string actionType)
    {
        return ActionOptions.FirstOrDefault(option => option.Value == actionType)?.Label
            ?? actionType;
    }

    public static string EntityLabel(AuditTimelineEntityType entityType)
    {
        return EntityOptions.First(option => option.Value == entityType).Label;
    }

    public static string AccountKindLabel(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.Owner => "Owner account",
            AccountKind.NamedAdmin => "Named Admin",
            AccountKind.SharedReceptionAdmin => "Shared Reception/Admin",
            _ => "Account",
        };
    }

    public static string RoleLabel(ActorRole role)
    {
        return role switch
        {
            ActorRole.Owner => "Owner",
            ActorRole.Admin => "Admin",
            _ => "Role",
        };
    }

    public static string EntryOriginLabel(EntryOrigin entryOrigin)
    {
        return entryOrigin switch
        {
            EntryOrigin.Normal => "Normal entry",
            EntryOrigin.ManualBackfill => "Manual backfill",
            EntryOrigin.PaperFallback => "Paper fallback",
            EntryOrigin.FutureImport => "Future import",
            _ => "Entry origin",
        };
    }

    public static string EntryOriginClass(EntryOrigin entryOrigin)
    {
        return entryOrigin switch
        {
            EntryOrigin.Normal => "audit-origin-normal",
            EntryOrigin.ManualBackfill => "audit-origin-backfill",
            EntryOrigin.PaperFallback => "audit-origin-fallback",
            EntryOrigin.FutureImport => "audit-origin-import",
            _ => "audit-origin-normal",
        };
    }

    public static string EntryClass(AuditTimelineEntry entry)
    {
        if (entry.ChangedAfterClose)
        {
            return "audit-entry-changed-after-close";
        }

        return entry.EntryOrigin switch
        {
            EntryOrigin.PaperFallback or EntryOrigin.ManualBackfill
                => "audit-entry-backdated",
            _ => string.Empty,
        };
    }

    public static string TimestampLabel(DateTimeOffset timestamp)
    {
        return $"{timestamp.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC";
    }

    public static string ShortId(Guid value)
    {
        return value.ToString("N")[..8];
    }

    public static string FormatJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, PrettyJsonOptions);
        }
        catch (JsonException)
        {
            return "Stored audit summary is unavailable.";
        }
    }

    private static bool TryBuildRecordedRange(
        DateOnly? fromDate,
        DateOnly? toDate,
        out DateTimeOffset? recordedFromInclusive,
        out DateTimeOffset? recordedBeforeExclusive)
    {
        recordedFromInclusive = fromDate is { } from
            ? ToUtcStartOfDay(from)
            : null;
        if (toDate is null)
        {
            recordedBeforeExclusive = null;
            return true;
        }

        if (toDate == DateOnly.MaxValue)
        {
            recordedBeforeExclusive = null;
            return false;
        }

        recordedBeforeExclusive = ToUtcStartOfDay(toDate.Value.AddDays(1));
        return true;
    }

    private static DateTimeOffset ToUtcStartOfDay(DateOnly date)
    {
        return new DateTimeOffset(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

public sealed record AuditTimelineEntityOption(
    AuditTimelineEntityType Value,
    string Label);

public sealed record AuditTimelineActionOption(
    string Value,
    string Label);
