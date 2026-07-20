namespace BodyLife.Crm.Modules.Reports;

public enum ClientHistorySourceKind
{
    MembershipIssued = 1,
    MembershipOpeningStateCreated,
    VisitMarked,
    VisitCanceled,
    PaymentCreated,
    PaymentCorrected,
    PaymentCanceled,
    FreezeAdded,
    FreezeCanceled,
    NonWorkingDayAdded,
    NonWorkingDayCorrected,
    NonWorkingDayCanceled,
}
