namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record ClientDuplicateCandidate(
    Guid MatchedClientId,
    ClientDuplicateWarningType WarningType,
    string Surname,
    string Name,
    string? Patronymic,
    string? Phone,
    bool IsActive);
