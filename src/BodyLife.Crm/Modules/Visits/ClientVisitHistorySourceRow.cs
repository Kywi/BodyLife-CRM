using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;

namespace BodyLife.Crm.Modules.Visits;

public sealed record ClientVisitHistorySourceRow(
    ClientVisitHistorySourceKind Kind,
    Guid ClientId,
    Guid VisitId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    EntryOrigin EntryOrigin,
    MarkedVisitHistorySource? MarkedVisit,
    VisitCancellationHistorySource? Cancellation,
    ClientAuditEntry AuditEntry);
