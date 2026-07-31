using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record CorrectNegativeVisitCoverageCommand(
    CommandEnvelope Envelope,
    Guid OriginalNegativeClosureId,
    NegativeVisitCoverageCorrectionMode Mode,
    IReadOnlyList<NegativeVisitClosureLineSelection>? ReplacementOneOffLines = null,
    int? ReplacementNewMembershipCoverageCount = null,
    Guid? ExpectedOldestOpenNegativeVisitId = null,
    Guid? EntryBatchId = null)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "membership_negative_closure_correction";
    public const string ClosureEntityType = "membership_negative_closure";
    public const string CanonicalRereadEntityType = "client";
}
