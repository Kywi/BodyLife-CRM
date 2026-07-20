namespace BodyLife.Crm.Modules.Payments;

public enum GetClientPaymentHistorySourceRowsStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    SourceInconsistent,
}
