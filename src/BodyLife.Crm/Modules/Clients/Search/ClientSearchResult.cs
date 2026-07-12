namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record ClientSearchResult(
    Guid ClientId,
    string DisplayName,
    string? Phone,
    string? CurrentCardNumber,
    ClientOperationalStatus OperationalStatus,
    ClientSearchMatchType MatchType,
    int MatchPriority,
    ClientMembershipSummary? CurrentMembership,
    IReadOnlyList<ClientWarning> Warnings);
