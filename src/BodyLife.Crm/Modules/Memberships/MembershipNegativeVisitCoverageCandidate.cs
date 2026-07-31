namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipNegativeVisitCoverageCandidate(
    Guid VisitId,
    Guid SourceMembershipId,
    Guid OldConsumptionId,
    DateTimeOffset OccurredAt,
    DateTimeOffset ConsumptionRecordedAt,
    DateOnly BusinessDate);
