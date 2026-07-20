namespace BodyLife.Crm.Modules.Freezes;

public enum GetClientFreezeHistorySourceRowsStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    SourceInconsistent,
}
