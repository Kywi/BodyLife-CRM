namespace BodyLife.Crm.Modules.Memberships;

public enum PreviewIssueMembershipStatus
{
    Success = 1,
    PermissionDenied,
    NotFound,
    MembershipTypeInactive,
    ValidationFailed,
    RecalculationFailed
}
