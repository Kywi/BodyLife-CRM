namespace BodyLife.Crm.Modules.Memberships;

public enum GetNegativeMembershipStateRowsStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    RecalculationFailed,
    SourceInconsistent,
}
