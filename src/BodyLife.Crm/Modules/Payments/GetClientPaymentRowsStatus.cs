namespace BodyLife.Crm.Modules.Payments;

public enum GetClientPaymentRowsStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    SourceInconsistent,
}
