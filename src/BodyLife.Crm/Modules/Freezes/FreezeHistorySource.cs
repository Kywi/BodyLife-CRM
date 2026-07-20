using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Freezes;

public sealed record FreezeHistorySource(
    Guid FreezeId,
    Guid ClientId,
    Guid MembershipId,
    string MembershipTypeNameSnapshot,
    DateRange Range,
    string Reason,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    AccountId RecordedByAccountId,
    SessionId RecordedSessionId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId,
    FreezeCancellationSourceStatus CurrentStatus,
    Guid? CurrentCancellationId);
