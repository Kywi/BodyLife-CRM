using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;

namespace BodyLife.Crm.Modules.Freezes;

public sealed record ClientFreezeHistorySourceRow(
    ClientFreezeHistorySourceKind Kind,
    Guid ClientId,
    Guid FreezeId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    EntryOrigin EntryOrigin,
    FreezeHistorySource? AddedFreeze,
    FreezeCancellationHistorySource? Cancellation,
    ClientAuditEntry AuditEntry);
