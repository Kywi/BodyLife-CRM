namespace BodyLife.Crm.Modules.Memberships;

public enum GetEndingSoonMembershipStateRowsStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    RecalculationFailed,
    SourceInconsistent,
}
