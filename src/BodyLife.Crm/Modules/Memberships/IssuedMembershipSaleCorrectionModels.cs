using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public enum PreviewIssuedMembershipSaleCorrectionStatus
{
    Success,
    PermissionDenied,
    NotFound,
    ValidationFailed,
    CanonicalStateInvalid,
}

public sealed record IssuedMembershipSaleDependency(
    string DependencyType,
    Guid DependencyId,
    DateOnly? RelevantDate,
    string Context);

public sealed record IssuedMembershipSaleDetails(
    Guid MembershipId,
    Guid PaymentId,
    string TypeNameSnapshot,
    int DurationDaysSnapshot,
    int VisitsLimitSnapshot,
    Money PriceSnapshot,
    DateOnly StartDate,
    DateOnly BaseEndDate,
    DateTimeOffset IssuedAt);

public sealed record IssuedMembershipSaleReplacementTerms(
    Guid MembershipTypeId,
    DateTimeOffset ExpectedMembershipTypeUpdatedAt,
    string TypeNameSnapshot,
    int DurationDaysSnapshot,
    int VisitsLimitSnapshot,
    Money PriceSnapshot,
    DateOnly StartDate,
    DateOnly BaseEndDate);

public sealed record IssuedMembershipSaleCorrectionPreview(
    IssuedMembershipSaleDetails OriginalSale,
    IReadOnlyList<IssuedMembershipSaleDependency> Dependencies,
    string DependencyToken,
    IssuedMembershipSaleReplacementTerms? Replacement);

public sealed record PreviewIssuedMembershipSaleCorrectionResult(
    PreviewIssuedMembershipSaleCorrectionStatus Status,
    IssuedMembershipSaleCorrectionPreview? Preview,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static PreviewIssuedMembershipSaleCorrectionResult Succeeded(
        IssuedMembershipSaleCorrectionPreview preview) =>
        new(PreviewIssuedMembershipSaleCorrectionStatus.Success, preview, null, null, null);

    public static PreviewIssuedMembershipSaleCorrectionResult Failure(
        PreviewIssuedMembershipSaleCorrectionStatus status,
        string code,
        string message,
        string? field = null) => new(status, null, code, message, field);
}
