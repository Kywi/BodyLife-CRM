namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class NonWorkingDayPreviewConfirmation
{
    public const int FingerprintLength = 64;
    public const int MaxTokenLength = 4096;

    public NonWorkingDayPreviewConfirmation(
        string confirmationToken,
        string scopeFingerprint,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmationToken);
        if (confirmationToken != confirmationToken.Trim()
            || confirmationToken.Length > MaxTokenLength)
        {
            throw new ArgumentException(
                "Confirmation token has an invalid format or length.",
                nameof(confirmationToken));
        }

        NonWorkingDayConfirmationTokenContract.EnsureFingerprint(scopeFingerprint);
        NonWorkingDayConfirmationTokenContract.EnsureUtcWindow(issuedAt, expiresAt);

        ConfirmationToken = confirmationToken;
        ScopeFingerprint = scopeFingerprint;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public string ConfirmationToken { get; }

    public string ScopeFingerprint { get; }

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset ExpiresAt { get; }
}

internal static class NonWorkingDayConfirmationTokenContract
{
    internal static void EnsureFingerprint(string scopeFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeFingerprint);
        if (scopeFingerprint.Length != NonWorkingDayPreviewConfirmation.FingerprintLength
            || scopeFingerprint.Any(character =>
                !char.IsAsciiDigit(character)
                && character is not (>= 'A' and <= 'F')))
        {
            throw new ArgumentException(
                "Scope fingerprint must be an uppercase SHA-256 hexadecimal value.",
                nameof(scopeFingerprint));
        }
    }

    internal static void EnsureUtcWindow(
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        if (issuedAt.Offset != TimeSpan.Zero || expiresAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Confirmation token timestamps must use UTC.",
                nameof(issuedAt));
        }

        if (expiresAt <= issuedAt)
        {
            throw new ArgumentException(
                "Confirmation token expiry must be after issue time.",
                nameof(expiresAt));
        }
    }
}
