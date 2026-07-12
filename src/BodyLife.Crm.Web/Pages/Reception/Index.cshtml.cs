using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed class IndexModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<SearchClientsQuery, SearchClientsResult> searchClients,
    IBodyLifeQueryHandler<GetClientProfileQuery, GetClientProfileResult> getClientProfile)
    : PageModel
{
    private const int SearchPageSize = 20;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "mode")]
    public ClientSearchMode Mode { get; set; } = ClientSearchMode.Auto;

    [BindProperty(SupportsGet = true, Name = "includeInactive")]
    public bool IncludeInactive { get; set; }

    [BindProperty(SupportsGet = true, Name = "pageCursor")]
    public string? PageCursor { get; set; }

    [BindProperty(SupportsGet = true, Name = "clientId")]
    public Guid? ClientId { get; set; }

    public ReceptionWorkspaceViewModel Workspace { get; private set; }
        = ReceptionWorkspaceViewModel.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Workspace = await BuildWorkspaceAsync(cancellationToken);
    }

    public async Task<IActionResult> OnGetSearchAsync(CancellationToken cancellationToken)
    {
        if (!IsHtmxRequest())
        {
            return RedirectToCanonicalPage(clientId: null);
        }

        ClientId = null;
        Workspace = await BuildWorkspaceAsync(cancellationToken);
        var selectedClientId = Workspace.ProfileResult?.Profile?.ClientId;
        SetHtmxPushUrl(selectedClientId);

        return Partial("_ReceptionWorkspace", Workspace);
    }

    public async Task<IActionResult> OnGetProfileAsync(CancellationToken cancellationToken)
    {
        if (!IsHtmxRequest())
        {
            return RedirectToCanonicalPage(ClientId);
        }

        var actor = requestContextResolver.Require().Actor;
        var result = await getClientProfile.ExecuteAsync(
            new GetClientProfileQuery(actor, ClientId ?? Guid.Empty),
            cancellationToken);
        SetHtmxPushUrl(result.Profile?.ClientId ?? ClientId);

        return Partial("_ClientProfile", result);
    }

    private async Task<ReceptionWorkspaceViewModel> BuildWorkspaceAsync(
        CancellationToken cancellationToken)
    {
        var actor = requestContextResolver.Require().Actor;
        SearchClientsResult? searchResult = null;

        if (!string.IsNullOrWhiteSpace(Query))
        {
            searchResult = await searchClients.ExecuteAsync(
                new SearchClientsQuery(
                    actor,
                    Query,
                    Mode,
                    IncludeInactive,
                    SearchPageSize,
                    PageCursor),
                cancellationToken);
        }

        var profileClientId = ClientId;

        if (!profileClientId.HasValue
            && searchResult is { Status: SearchClientsStatus.Success, AutoOpenClientId: not null })
        {
            profileClientId = searchResult.AutoOpenClientId;
        }

        GetClientProfileResult? profileResult = null;

        if (profileClientId.HasValue)
        {
            profileResult = await getClientProfile.ExecuteAsync(
                new GetClientProfileQuery(actor, profileClientId.Value),
                cancellationToken);
        }

        return new ReceptionWorkspaceViewModel(
            Query?.Trim(),
            Mode,
            IncludeInactive,
            PageCursor,
            searchResult,
            profileResult);
    }

    private bool IsHtmxRequest()
    {
        return string.Equals(
            Request.Headers["HX-Request"],
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private IActionResult RedirectToCanonicalPage(Guid? clientId)
    {
        return RedirectToPage(new
        {
            q = Query,
            mode = Mode == ClientSearchMode.Auto ? (ClientSearchMode?)null : Mode,
            includeInactive = IncludeInactive ? true : (bool?)null,
            pageCursor = PageCursor,
            clientId,
        });
    }

    private void SetHtmxPushUrl(Guid? clientId)
    {
        var url = Url.Page(
            "/Reception/Index",
            values: new
            {
                q = Query?.Trim(),
                mode = Mode == ClientSearchMode.Auto ? (ClientSearchMode?)null : Mode,
                includeInactive = IncludeInactive ? true : (bool?)null,
                pageCursor = PageCursor,
                clientId,
            })
            ?? throw new InvalidOperationException("Could not generate the reception dashboard URL.");

        Response.Headers["HX-Push-Url"] = url;
    }
}
