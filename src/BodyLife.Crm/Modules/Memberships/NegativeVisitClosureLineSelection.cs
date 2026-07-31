namespace BodyLife.Crm.Modules.Memberships;

public sealed record NegativeVisitClosureLineSelection(
    Guid MembershipTypeId,
    DateTimeOffset ExpectedMembershipTypeUpdatedAt,
    int Quantity);
