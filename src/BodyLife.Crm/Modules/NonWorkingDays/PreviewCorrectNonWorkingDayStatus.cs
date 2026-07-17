namespace BodyLife.Crm.Modules.NonWorkingDays;

public enum PreviewCorrectNonWorkingDayStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    AlreadyCanceled,
    StaleState,
    SourceInconsistent,
    RecalculationFailed,
}
