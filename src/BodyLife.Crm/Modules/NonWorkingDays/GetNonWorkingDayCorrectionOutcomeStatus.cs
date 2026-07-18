namespace BodyLife.Crm.Modules.NonWorkingDays;

public enum GetNonWorkingDayCorrectionOutcomeStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    SourceInconsistent,
}
