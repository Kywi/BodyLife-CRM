namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record ClientMembershipSummary(
    Guid MembershipId,
    string Status,
    int RemainingVisits,
    DateOnly EffectiveEndDate);
