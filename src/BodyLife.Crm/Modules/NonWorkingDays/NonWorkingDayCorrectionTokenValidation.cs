namespace BodyLife.Crm.Modules.NonWorkingDays;

public enum NonWorkingDayCorrectionTokenValidationStatus
{
    Valid,
    Expired,
    InvalidToken,
    ConfirmationMaterialMismatch,
}

public sealed class NonWorkingDayCorrectionTokenValidation
{
    private NonWorkingDayCorrectionTokenValidation(
        NonWorkingDayCorrectionTokenValidationStatus status,
        string? confirmationFingerprint,
        DateTimeOffset? issuedAt,
        DateTimeOffset? expiresAt)
    {
        Status = status;
        ConfirmationFingerprint = confirmationFingerprint;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public NonWorkingDayCorrectionTokenValidationStatus Status { get; }

    public bool IsValid =>
        Status == NonWorkingDayCorrectionTokenValidationStatus.Valid;

    public string? ConfirmationFingerprint { get; }

    public DateTimeOffset? IssuedAt { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public static NonWorkingDayCorrectionTokenValidation InvalidToken()
    {
        return new NonWorkingDayCorrectionTokenValidation(
            NonWorkingDayCorrectionTokenValidationStatus.InvalidToken,
            confirmationFingerprint: null,
            issuedAt: null,
            expiresAt: null);
    }

    public static NonWorkingDayCorrectionTokenValidation FromAuthenticatedToken(
        NonWorkingDayCorrectionTokenValidationStatus status,
        string confirmationFingerprint,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        if (status is NonWorkingDayCorrectionTokenValidationStatus.InvalidToken
            || !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Authenticated correction token status is not supported.");
        }

        NonWorkingDayConfirmationTokenContract.EnsureFingerprint(
            confirmationFingerprint);
        NonWorkingDayConfirmationTokenContract.EnsureUtcWindow(issuedAt, expiresAt);
        return new NonWorkingDayCorrectionTokenValidation(
            status,
            confirmationFingerprint,
            issuedAt,
            expiresAt);
    }
}
