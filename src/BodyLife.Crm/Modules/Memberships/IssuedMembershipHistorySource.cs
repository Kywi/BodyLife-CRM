using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record IssuedMembershipHistorySource(
    Guid MembershipId,
    Guid ClientId,
    Guid MembershipTypeId,
    IssuedMembershipSnapshot Snapshot,
    DateOnly StartDate,
    DateOnly BaseEndDate,
    DateTimeOffset IssuedAt,
    AccountId IssuedByAccountId,
    IssuedMembershipLifecycleStatus Status,
    Guid? EntryBatchId,
    string? Comment);
