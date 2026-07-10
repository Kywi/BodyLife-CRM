using BodyLife.Crm.Application.Queries;

namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record FindClientDuplicateCandidatesQuery(
    string Surname,
    string Name,
    string? Patronymic,
    string? Phone,
    Guid? ExcludedClientId = null)
    : IBodyLifeQuery<IReadOnlyList<ClientDuplicateCandidate>>;
