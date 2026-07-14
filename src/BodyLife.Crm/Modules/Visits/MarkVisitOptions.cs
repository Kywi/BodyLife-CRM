namespace BodyLife.Crm.Modules.Visits;

public sealed record MarkVisitOptions(
    Guid ClientId,
    DateTimeOffset OccurredAt,
    DateOnly VisitDate,
    IReadOnlyList<MarkVisitMembershipOption> MembershipOptions,
    Guid? SuggestedMembershipId);
