namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class NonWorkingDayCorrectionConfirmation
{
    public const int FingerprintLength = NonWorkingDayPreviewConfirmation.FingerprintLength;
    public const int MaxTokenLength = NonWorkingDayPreviewConfirmation.MaxTokenLength;

    public NonWorkingDayCorrectionConfirmation(
        string confirmationToken,
        string confirmationFingerprint,
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

        NonWorkingDayConfirmationTokenContract.EnsureFingerprint(
            confirmationFingerprint);
        NonWorkingDayConfirmationTokenContract.EnsureUtcWindow(issuedAt, expiresAt);

        ConfirmationToken = confirmationToken;
        ConfirmationFingerprint = confirmationFingerprint;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public string ConfirmationToken { get; }

    public string ConfirmationFingerprint { get; }

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset ExpiresAt { get; }
}
