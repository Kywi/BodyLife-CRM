using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public enum ClientNegativeVisitCoverageHistorySourceKind
{
    Created = 1,
    Canceled,
    Replaced,
}

public enum NegativeVisitCoverageClosureHistoryStatus
{
    Active = 1,
    Canceled,
    Replaced,
}

public enum NegativeVisitCoverageClosureMethod
{
    OneOff = 1,
    NewMembership,
}

public enum NegativeVisitCoverageCorrectionHistoryMode
{
    Cancel = 1,
    Replace,
}

public sealed record ClientNegativeVisitCoverageHistorySourceRow(
    ClientNegativeVisitCoverageHistorySourceKind Kind,
    Guid ClientId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    EntryOrigin EntryOrigin,
    NegativeVisitCoverageClosureHistorySnapshot Closure,
    NegativeVisitCoverageClosureHistorySnapshot? ReplacementClosure,
    NegativeVisitCoverageCorrectionHistorySnapshot? Correction,
    PaperFallbackEntryRowReference? PaperReference,
    ClientAuditEntry AuditEntry);

public sealed record NegativeVisitCoverageClosureHistorySnapshot(
    Guid ClosureId,
    Guid ClientId,
    NegativeVisitCoverageClosureMethod Method,
    Guid OldestOpenNegativeVisitId,
    int VisitsCount,
    string? Comment,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    AccountId RecordedByAccountId,
    SessionId SessionId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId,
    NegativeVisitCoverageClosureHistoryStatus Status,
    IReadOnlyList<NegativeVisitCoverageLineHistorySnapshot> Lines,
    IReadOnlyList<NegativeVisitCoverageItemHistorySnapshot> Items,
    NegativeVisitCoveragePaymentHistorySnapshot? Payment,
    NegativeVisitCoverageCoveringMembershipHistorySnapshot? CoveringMembership);

public sealed record NegativeVisitCoverageLineHistorySnapshot(
    Guid LineId,
    Guid MembershipTypeId,
    string TypeName,
    int DurationDays,
    int VisitsLimit,
    int Quantity,
    Money UnitPrice,
    Money LineTotal,
    int Sequence);

public sealed record NegativeVisitCoverageItemHistorySnapshot(
    Guid ItemId,
    int Sequence,
    Guid VisitId,
    DateTimeOffset VisitOccurredAt,
    DateOnly VisitBusinessDate,
    Guid SourceMembershipId,
    Guid OldConsumptionId,
    Guid? CoveringMembershipId,
    Guid? NewConsumptionId,
    NegativeVisitCoverageClosureHistoryStatus Status);

public sealed record NegativeVisitCoveragePaymentHistorySnapshot(
    Guid PaymentId,
    Money Amount,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    AccountId RecordedByAccountId,
    SessionId SessionId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId,
    NegativeVisitCoverageClosureHistoryStatus Status);

public sealed record NegativeVisitCoverageCoveringMembershipHistorySnapshot(
    Guid MembershipId,
    Guid MembershipTypeId,
    IssuedMembershipSnapshot Snapshot,
    DateOnly StartDate,
    DateOnly BaseEndDate,
    DateTimeOffset IssuedAt,
    AccountId IssuedByAccountId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId,
    IssuedMembershipLifecycleStatus Status);

public sealed record NegativeVisitCoverageCorrectionHistorySnapshot(
    Guid CorrectionId,
    Guid OriginalClosureId,
    Guid? ReplacementClosureId,
    NegativeVisitCoverageCorrectionHistoryMode Mode,
    string Reason,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    AccountId RecordedByAccountId,
    SessionId SessionId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId);
