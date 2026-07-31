using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record NegativeVisitCoverageStaleSelectors(
    Guid? CurrentOldestOpenNegativeVisitId,
    IReadOnlyList<OneOffNegativeClosureSelectorReadModel> ActiveOneOffTypes);

public sealed record OneOffNegativeClosureSelectorReadModel(
    Guid MembershipTypeId,
    string Name,
    int DurationDays,
    int VisitsLimit,
    Money UnitPrice,
    DateTimeOffset CurrentUpdatedAt);

public sealed record OneOffNegativeClosurePreviewLine(
    Guid MembershipTypeId,
    string TypeName,
    int Quantity,
    Money UnitPrice,
    Money LineTotal,
    DateTimeOffset ExpectedMembershipTypeUpdatedAt,
    int Sequence);

public sealed record OneOffNegativeClosurePreview(
    Guid ClientId,
    Guid ExpectedOldestOpenNegativeVisitId,
    IReadOnlyList<OneOffNegativeClosurePreviewLine> Lines,
    Money ExactPaymentTotal,
    IReadOnlyList<NegativeVisitCoverageCandidateReadModel> CoveredVisits,
    int RemainingTotalNegativeBalance,
    int RemainingUnknownNegativeBalance,
    NegativeVisitCoverageStaleSelectors CurrentSelectors);

public sealed record NegativeVisitCoveragePaymentContextReadModel(
    Guid? PaymentId,
    Money Amount,
    string Method,
    string PaymentContext,
    DateTimeOffset? OccurredAt,
    DateTimeOffset? RecordedAt,
    string Status);

public sealed record NegativeVisitCoverageCoveringMembershipPreview(
    IssuedMembershipCoverageSnapshotReadModel Membership,
    int CurrentRemainingVisits,
    int RestoredRemainingVisits,
    int? ReplacementRemainingVisits,
    int CurrentNegativeBalance,
    DateOnly CurrentEffectiveEndDate);

public sealed record NegativeVisitCoverageCorrectionPreview(
    Guid OriginalNegativeClosureId,
    string CoverageMethod,
    NegativeVisitCoverageCorrectionMode Mode,
    string Reason,
    int OriginalVisitsCount,
    IReadOnlyList<NegativeVisitCoverageLineReadModel> OriginalLines,
    IReadOnlyList<NegativeVisitCoverageCandidateReadModel> RestoredVisits,
    IReadOnlyList<OneOffNegativeClosurePreviewLine> ReplacementOneOffLines,
    IReadOnlyList<NegativeVisitCoverageCandidateReadModel> ReplacementCoveredVisits,
    NegativeVisitCoveragePaymentContextReadModel? OriginalPayment,
    NegativeVisitCoveragePaymentContextReadModel? ReplacementPayment,
    NegativeVisitCoverageCoveringMembershipPreview? CoveringMembership,
    int RestoredTotalNegativeBalance,
    int RestoredUnknownNegativeBalance,
    int ResultingRemainingNegativeBalance,
    int ResultingRemainingUnknownNegativeBalance,
    NegativeVisitCoverageStaleSelectors CurrentSelectors);
