namespace BodyLife.Crm.Modules.Visits;

public enum GetClientVisitRowsStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    SourceInconsistent,
}
