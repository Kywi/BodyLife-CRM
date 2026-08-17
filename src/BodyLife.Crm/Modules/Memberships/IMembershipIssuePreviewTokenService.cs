namespace BodyLife.Crm.Modules.Memberships;

public interface IMembershipIssuePreviewTokenService
{
    MembershipIssuePreviewToken Issue(MembershipIssuePreviewTokenMaterial material);

    MembershipIssuePreviewTokenValidation Validate(
        string? token,
        MembershipIssuePreviewTokenMaterial currentMaterial);
}

public sealed record MembershipIssuePreviewToken(
    string Value,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

public enum MembershipIssuePreviewTokenValidationStatus
{
    Valid,
    Expired,
    InvalidToken,
    PreviewMismatch,
}

public sealed record MembershipIssuePreviewTokenValidation(
    MembershipIssuePreviewTokenValidationStatus Status)
{
    public bool IsValid => Status == MembershipIssuePreviewTokenValidationStatus.Valid;
}
