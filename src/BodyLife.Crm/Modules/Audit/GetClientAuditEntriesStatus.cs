namespace BodyLife.Crm.Modules.Audit;

public enum GetClientAuditEntriesStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    SourceInconsistent,
}
