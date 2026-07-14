namespace BodyLife.Crm.Modules.Memberships;

public enum GetClientMembershipStatesStatus
{
    Success = 1,
    PermissionDenied,
    NotFound,
    ValidationFailed,
    RecalculationFailed
}
