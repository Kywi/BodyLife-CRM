namespace BodyLife.Crm.Modules.Memberships;

public enum GetClientMembershipHistorySourceRowsStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    NotFound,
    SourceInconsistent,
}
