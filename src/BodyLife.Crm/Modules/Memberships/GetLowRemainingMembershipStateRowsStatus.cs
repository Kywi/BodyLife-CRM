namespace BodyLife.Crm.Modules.Memberships;

public enum GetLowRemainingMembershipStateRowsStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    RecalculationFailed,
    SourceInconsistent,
}
