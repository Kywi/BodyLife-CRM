using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record PreviewCorrectNegativeVisitCoverageQuery(
    ActorContext Actor,
    Guid OriginalNegativeClosureId,
    NegativeVisitCoverageCorrectionMode Mode,
    string? Reason,
    IReadOnlyList<NegativeVisitClosureLineSelection>? ReplacementOneOffLines = null,
    int? ReplacementNewMembershipCoverageCount = null,
    Guid? ExpectedOldestOpenNegativeVisitId = null)
    : IBodyLifeQuery<PreviewCorrectNegativeVisitCoverageResult>;
