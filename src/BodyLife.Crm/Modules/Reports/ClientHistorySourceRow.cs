using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Visits;

namespace BodyLife.Crm.Modules.Reports;

public sealed record ClientHistorySourceRow(
    ClientHistorySourceKind Kind,
    Guid ClientId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    EntryOrigin EntryOrigin,
    ClientMembershipHistorySourceRow? MembershipSourceRow,
    ClientVisitHistorySourceRow? VisitSourceRow,
    ClientPaymentHistorySourceRow? PaymentSourceRow,
    ClientFreezeHistorySourceRow? FreezeSourceRow,
    ClientNonWorkingDayHistorySourceRow? NonWorkingDaySourceRow,
    ClientAuditEntry AuditEntry);
