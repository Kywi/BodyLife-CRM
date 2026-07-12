using BodyLife.Crm.Modules.Clients.Search;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed record ReceptionWorkspaceViewModel(
    string? Query,
    ClientSearchMode Mode,
    bool IncludeInactive,
    string? PageCursor,
    SearchClientsResult? SearchResult,
    GetClientProfileResult? ProfileResult)
{
    public static ReceptionWorkspaceViewModel Empty { get; } = new(
        Query: null,
        ClientSearchMode.Auto,
        IncludeInactive: false,
        PageCursor: null,
        SearchResult: null,
        ProfileResult: null);
}
