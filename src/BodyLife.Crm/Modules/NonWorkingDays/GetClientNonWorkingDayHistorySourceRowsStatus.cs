namespace BodyLife.Crm.Modules.NonWorkingDays;

public enum GetClientNonWorkingDayHistorySourceRowsStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    SourceInconsistent,
}
