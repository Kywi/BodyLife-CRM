using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Localization;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Audit;

public sealed class TimelineModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<GetAuditTimelineQuery, GetAuditTimelineResult> getAuditTimeline,
    AuditPresentation presentation,
    AuditEntryExplanationPresenter explanationPresenter)
    : PageModel
{
    public const int PageSize = 10;

    public AuditPresentation Presentation { get; } = presentation;

    public AuditEntryExplanationPresenter ExplanationPresenter { get; } = explanationPresenter;

    public static IReadOnlyList<AuditTimelineEntityOption> EntityOptions { get; } =
    [
        new(AuditTimelineEntityType.Client, "Entity.Client"),
        new(AuditTimelineEntityType.MembershipType, "Entity.MembershipType"),
        new(AuditTimelineEntityType.Membership, "Entity.Membership"),
        new(AuditTimelineEntityType.MembershipOpeningState, "Entity.MembershipOpeningState"),
        new(AuditTimelineEntityType.Visit, "Entity.Visit"),
        new(AuditTimelineEntityType.Payment, "Entity.Payment"),
        new(AuditTimelineEntityType.Freeze, "Entity.Freeze"),
        new(AuditTimelineEntityType.NonWorkingPeriod, "Entity.NonWorkingPeriod"),
        new(AuditTimelineEntityType.StaffAccount, "Entity.StaffAccount"),
    ];

    public static IReadOnlyList<AuditTimelineActionOption> ActionOptions { get; } =
    [
        new("client.created", "Action.client.created"), new("client.updated", "Action.client.updated"), new("card.assigned", "Action.card.assigned"), new("card.changed", "Action.card.changed"), new("card.cleared", "Action.card.cleared"),
        new("membership_type.created", "Action.membership_type.created"), new("membership_type.edited", "Action.membership_type.edited"), new("membership_type.deactivated", "Action.membership_type.deactivated"), new("membership.issued", "Action.membership.issued"), new("membership_opening_state.created", "Action.membership_opening_state.created"),
        new("visit.marked", "Action.visit.marked"), new("visit.canceled", "Action.visit.canceled"), new("payment.created", "Action.payment.created"), new("payment.corrected", "Action.payment.corrected"), new("payment.canceled", "Action.payment.canceled"), new("freeze.added", "Action.freeze.added"), new("freeze.canceled", "Action.freeze.canceled"),
        new("non_working_day.added", "Action.non_working_day.added"), new("non_working_day.corrected", "Action.non_working_day.corrected"), new("non_working_day.canceled", "Action.non_working_day.canceled"),
        new("staff_account.created", "Action.staff_account.created"), new("staff_account.display_name_updated", "Action.staff_account.display_name_updated"), new("staff_account.activated", "Action.staff_account.activated"), new("staff_account.deactivated", "Action.staff_account.deactivated"), new("staff_credentials.configured", "Action.staff_credentials.configured"), new("staff_credentials.reset", "Action.staff_credentials.reset"),
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
            LoadError = Presentation.Text("Error.InvalidFilter");
            return;
        }

        ActionType = NormalizeOptional(ActionType);
        Offset ??= 0;
        if (ActionType is not null
            && !ActionOptions.Any(option => option.Value == ActionType))
        {
            LoadError = Presentation.Text("Error.UnsupportedAction");
            return;
        }

        if (!TryBuildRecordedRange(
                RecordedFromDate,
                RecordedToDate,
                out var recordedFromInclusive,
                out var recordedBeforeExclusive))
        {
            LoadError = Presentation.Text("Error.InvalidRange");
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
            LoadError = Presentation.Text("Error.Unavailable");
        }
    }

    private static bool TryBuildRecordedRange(
        DateOnly? fromDate,
        DateOnly? toDate,
        out DateTimeOffset? recordedFromInclusive,
        out DateTimeOffset? recordedBeforeExclusive)
    {
        recordedFromInclusive = null;
        recordedBeforeExclusive = null;
        if (!IsSupportedBusinessDate(fromDate)
            || !IsSupportedBusinessDate(toDate)
            || fromDate is { } selectedFrom
                && toDate is { } selectedTo
                && selectedTo < selectedFrom)
        {
            return false;
        }

        if (fromDate is { } from)
        {
            recordedFromInclusive = BusinessTimeZone.GetUtcDayRange(from).FromInclusive;
        }

        if (toDate is { } to)
        {
            recordedBeforeExclusive = BusinessTimeZone.GetUtcDayRange(to).ToExclusive;
        }

        return true;
    }

    private static bool IsSupportedBusinessDate(DateOnly? value) => !value.HasValue
        || value.Value != default && value.Value != DateOnly.MaxValue;

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    // Kept temporarily for out-of-stage explanation and history-row factories.
    public static string TimestampLabel(DateTimeOffset timestamp) => ReceptionDisplayFormatter.DateTime(timestamp);
    public static string ShortId(Guid value) => value.ToString("N")[..8];
}

public sealed record AuditTimelineEntityOption(
    AuditTimelineEntityType Value,
    string Label);

public sealed record AuditTimelineActionOption(
    string Value,
    string Label);
