using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Freezes;

public sealed record FreezeCancellationHistorySource(
    Guid CancellationId,
    Guid FreezeId,
    Guid ClientId,
    Guid MembershipId,
    string Reason,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    AccountId RecordedByAccountId,
    SessionId RecordedSessionId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId,
    FreezeHistorySource Freeze);
