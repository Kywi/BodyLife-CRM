namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record ClientSearchMembershipSummary(
    Guid MembershipId,
    string Status,
    int RemainingVisits,
    DateOnly EffectiveEndDate);
