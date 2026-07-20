using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record ClientNonWorkingDayHistorySourceRow(
    ClientNonWorkingDayHistorySourceKind Kind,
    Guid ClientId,
    Guid PeriodId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    EntryOrigin EntryOrigin,
    NonWorkingDayHistoryPeriodSource? AddedPeriod,
    NonWorkingDayCorrectionHistorySource? Correction,
    ClientAuditEntry AuditEntry);
