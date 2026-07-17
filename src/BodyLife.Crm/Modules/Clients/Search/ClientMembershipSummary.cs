using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record ClientMembershipSummary(
    Guid MembershipId,
    string TypeNameSnapshot,
    string Status,
    int RemainingVisits,
    DateOnly EffectiveEndDate,
    IReadOnlyList<MembershipExtensionExplanation> ExtensionExplanations);
