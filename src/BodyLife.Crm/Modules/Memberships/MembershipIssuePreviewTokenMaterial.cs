namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipIssuePreviewTokenMaterial(
    Guid ClientId,
    Guid MembershipTypeId,
    DateTimeOffset MembershipTypeUpdatedAt,
    DateOnly ProposedStartDate,
    int TotalNegativeBalance,
    int UnknownNegativeBalance,
    IReadOnlyList<MembershipNegativeVisitCoverageCandidate> CandidateVisits,
    int CoveredNegativeVisitCount);
