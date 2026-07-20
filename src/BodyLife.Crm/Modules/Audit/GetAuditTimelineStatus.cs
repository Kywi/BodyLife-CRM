namespace BodyLife.Crm.Modules.Audit;

public enum GetAuditTimelineStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    SourceInconsistent,
}
