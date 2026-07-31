namespace BodyLife.Crm.Modules.Memberships;

public enum GetClientNegativeVisitCoverageStatus
{
    Success = 1,
    PermissionDenied,
    NotFound,
    ValidationFailed,
    RecalculationFailed,
    CanonicalStateInvalid,
}
