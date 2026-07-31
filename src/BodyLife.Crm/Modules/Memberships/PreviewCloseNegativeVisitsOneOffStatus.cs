namespace BodyLife.Crm.Modules.Memberships;

public enum PreviewCloseNegativeVisitsOneOffStatus
{
    Success = 1,
    PermissionDenied,
    NotFound,
    ValidationFailed,
    MembershipTypeInactive,
    MembershipNotEligible,
    StaleState,
    RecalculationFailed,
    CanonicalStateInvalid,
}
