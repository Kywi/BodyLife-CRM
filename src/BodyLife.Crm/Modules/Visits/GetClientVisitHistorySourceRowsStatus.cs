namespace BodyLife.Crm.Modules.Visits;

public enum GetClientVisitHistorySourceRowsStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    SourceInconsistent,
}
