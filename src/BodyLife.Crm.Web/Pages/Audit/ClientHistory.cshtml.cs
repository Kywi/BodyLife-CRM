using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Audit;

public sealed class ClientHistoryModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<GetClientHistoryQuery, GetClientHistoryResult>
        getClientHistory,
    IBodyLifeQueryHandler<GetClientProfileQuery, GetClientProfileResult>
        getClientProfile,
    AuditPresentation presentation,
    ClientHistoryRowPresenter rowPresenter)
    : PageModel
{
    public AuditPresentation Presentation { get; } = presentation;
    public const int PageSize = 10;

    public static IReadOnlyList<ClientHistoryEntityOption> EntityOptions { get; } =
    [
        new(ClientHistoryEntityFilter.Membership, "HistoryEntity.Membership"),
        new(ClientHistoryEntityFilter.MembershipOpeningState, "HistoryEntity.MembershipOpeningState"),
        new(ClientHistoryEntityFilter.Visit, "HistoryEntity.Visit"),
        new(ClientHistoryEntityFilter.Payment, "HistoryEntity.Payment"),
        new(ClientHistoryEntityFilter.Freeze, "HistoryEntity.Freeze"),
        new(ClientHistoryEntityFilter.NonWorkingDay, "HistoryEntity.NonWorkingDay"),
    ];

    [BindProperty(SupportsGet = true, Name = "clientId")]
    public Guid? ClientId { get; set; }

    [BindProperty(SupportsGet = true, Name = "entity")]
    public ClientHistoryEntityFilter? EntityFilter { get; set; }

    [BindProperty(SupportsGet = true, Name = "from")]
    public DateOnly? OccurredFromDate { get; set; }

    [BindProperty(SupportsGet = true, Name = "to")]
    public DateOnly? OccurredToDate { get; set; }

    [BindProperty(SupportsGet = true, Name = "offset")]
    public int? Offset { get; set; }

    public GetClientHistoryResult? Result { get; private set; }

    public ClientHistoryPage? HistoryPage =>
        Result is { Status: GetClientHistoryStatus.Success }
            ? Result.Page
            : null;

    public ClientProfile? ClientProfile { get; private set; }

    public IReadOnlyList<ClientHistoryRowViewModel> Rows { get; private set; } = [];

    public int? PreviousOffset => HistoryPage is { Offset: > 0 } page
        ? Math.Max(0, page.Offset - PageSize)
        : null;

    public int CurrentPageNumber => ((HistoryPage?.Offset ?? 0) / PageSize) + 1;

    public string? LoadError { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            LoadError = Presentation.Text("Error.InvalidFilter");
            return;
        }

        Offset ??= 0;
        if (ClientId is null)
        {
            return;
        }

        if (ClientId == Guid.Empty)
        {
            LoadError = Presentation.Text("Error.InvalidClient");
            return;
        }

        if (!TryBuildOccurredRange(
                OccurredFromDate,
                OccurredToDate,
                out var occurredFromInclusive,
                out var occurredBeforeExclusive))
        {
            LoadError = Presentation.Text("Error.InvalidRange");
            return;
        }

        var actor = requestContextResolver.Require().Actor;
        Result = await getClientHistory.ExecuteAsync(
            new GetClientHistoryQuery(
                actor,
                ClientId.Value,
                occurredFromInclusive,
                occurredBeforeExclusive,
                EntityFilter is { } entity
                    ? [entity]
                    : null,
                PageSize,
                Offset.Value),
            cancellationToken);
        if (HistoryPage is not { } historyPage)
        {
            return;
        }

        try
        {
            Rows = Array.AsReadOnly(historyPage.Items
                .Select(rowPresenter.Present)
                .ToArray());
        }
        catch (InvalidOperationException)
        {
            Rows = [];
            LoadError = Presentation.Text("Error.Unavailable");
            return;
        }

        var profileResult = await getClientProfile.ExecuteAsync(
            new GetClientProfileQuery(
                actor,
                ClientId.Value,
                IncludeHistory: false,
                IncludeDrillDowns: false),
            cancellationToken);
        if (profileResult.Status == GetClientProfileStatus.Success)
        {
            ClientProfile = profileResult.Profile;
        }
    }

    private static bool TryBuildOccurredRange(
        DateOnly? fromDate,
        DateOnly? toDate,
        out DateTimeOffset? occurredFromInclusive,
        out DateTimeOffset? occurredBeforeExclusive)
    {
        occurredFromInclusive = fromDate is { } from
            ? ToUtcStartOfDay(from)
            : null;
        if (toDate is null)
        {
            occurredBeforeExclusive = null;
            return true;
        }

        if (toDate == DateOnly.MaxValue
            || fromDate is { } selectedFrom && toDate < selectedFrom)
        {
            occurredBeforeExclusive = null;
            return false;
        }

        occurredBeforeExclusive = ToUtcStartOfDay(toDate.Value.AddDays(1));
        return true;
    }

    private static DateTimeOffset ToUtcStartOfDay(DateOnly date)
    {
        return new DateTimeOffset(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }
}

public sealed record ClientHistoryEntityOption(
    ClientHistoryEntityFilter Value,
    string Label);
