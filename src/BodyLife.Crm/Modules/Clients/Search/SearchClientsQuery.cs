using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record SearchClientsQuery(
    ActorContext Actor,
    string SearchText,
    ClientSearchMode Mode = ClientSearchMode.Auto,
    bool IncludeInactive = false,
    int Limit = 20,
    string? PageCursor = null)
    : IBodyLifeQuery<SearchClientsResult>;
