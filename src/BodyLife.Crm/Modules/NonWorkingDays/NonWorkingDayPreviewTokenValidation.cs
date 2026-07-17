namespace BodyLife.Crm.Modules.NonWorkingDays;

public enum NonWorkingDayPreviewTokenValidationStatus
{
    Valid,
    Expired,
    InvalidToken,
    InputOrScopeMismatch,
}

public sealed class NonWorkingDayPreviewTokenValidation
{
    private NonWorkingDayPreviewTokenValidation(
        NonWorkingDayPreviewTokenValidationStatus status,
        string? scopeFingerprint,
        DateTimeOffset? issuedAt,
        DateTimeOffset? expiresAt)
    {
        Status = status;
        ScopeFingerprint = scopeFingerprint;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public NonWorkingDayPreviewTokenValidationStatus Status { get; }

    public bool IsValid => Status == NonWorkingDayPreviewTokenValidationStatus.Valid;

    public string? ScopeFingerprint { get; }

    public DateTimeOffset? IssuedAt { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public static NonWorkingDayPreviewTokenValidation InvalidToken()
    {
        return new NonWorkingDayPreviewTokenValidation(
            NonWorkingDayPreviewTokenValidationStatus.InvalidToken,
            scopeFingerprint: null,
            issuedAt: null,
            expiresAt: null);
    }

    public static NonWorkingDayPreviewTokenValidation FromAuthenticatedToken(
        NonWorkingDayPreviewTokenValidationStatus status,
        string scopeFingerprint,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        if (status is NonWorkingDayPreviewTokenValidationStatus.InvalidToken
            || !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Authenticated token status is not supported.");
        }

        NonWorkingDayPreviewTokenContract.EnsureFingerprint(scopeFingerprint);
        NonWorkingDayPreviewTokenContract.EnsureUtcWindow(issuedAt, expiresAt);
        return new NonWorkingDayPreviewTokenValidation(
            status,
            scopeFingerprint,
            issuedAt,
            expiresAt);
    }
}
