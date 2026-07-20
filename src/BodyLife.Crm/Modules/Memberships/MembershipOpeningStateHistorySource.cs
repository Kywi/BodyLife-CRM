using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipOpeningStateHistorySource(
    Guid OpeningStateId,
    Guid ClientId,
    Guid MembershipId,
    MembershipOpeningState Declaration,
    string SourceReference,
    string Reason,
    DateTimeOffset RecordedAt,
    AccountId RecordedByAccountId,
    SessionId RecordedSessionId,
    Guid? EntryBatchId,
    MembershipOpeningStateSourceStatus Status);
