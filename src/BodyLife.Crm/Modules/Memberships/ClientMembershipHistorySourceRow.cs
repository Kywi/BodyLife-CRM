using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record ClientMembershipHistorySourceRow(
    ClientMembershipHistorySourceKind Kind,
    Guid ClientId,
    Guid MembershipId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    EntryOrigin EntryOrigin,
    IssuedMembershipHistorySource? IssuedMembership,
    MembershipOpeningStateHistorySource? OpeningState,
    ClientAuditEntry AuditEntry);
