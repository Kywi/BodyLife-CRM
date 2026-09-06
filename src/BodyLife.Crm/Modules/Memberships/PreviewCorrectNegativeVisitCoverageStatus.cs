namespace BodyLife.Crm.Modules.Memberships;

public enum PreviewCorrectNegativeVisitCoverageStatus
{
    Success = 1,
    PermissionDenied,
    NotFound,
    ValidationFailed,
    ReasonRequired,
    MembershipTypeInactive,
    MembershipNotEligible,
    StaleState,
    AlreadyCanceled,
    RecalculationFailed,
    CanonicalStateInvalid,
    LifecycleDependency,
}
