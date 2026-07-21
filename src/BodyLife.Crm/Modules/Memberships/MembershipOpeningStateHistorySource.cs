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
    MembershipOpeningStateSourceStatus Status)
{
    public DateOnly OpeningAsOfDate => Declaration.OpeningAsOfDate;

    public int DeclaredRemainingVisits => Declaration.DeclaredRemainingVisits;

    public int DeclaredNegativeBalance => Declaration.DeclaredNegativeBalance;

    public DateOnly? KnownEffectiveEndDate => Declaration.KnownEffectiveEndDate;

    public int? KnownExtensionDays => Declaration.KnownExtensionDays;
}
