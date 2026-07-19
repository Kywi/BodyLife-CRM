namespace BodyLife.Crm.Modules.Memberships;

public enum GetClientMembershipReportStatesStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    RecalculationFailed,
    SourceInconsistent,
}
