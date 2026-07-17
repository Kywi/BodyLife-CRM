using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class NonWorkingDayPreviewTokenOptions
{
    public const string SectionName = "BodyLife:NonWorkingDayPreviewToken";
    public const string SigningKeyName = "SigningKey";
    public const string LifetimeName = "Lifetime";
    public const int MinimumSigningKeyBytes = 32;
    public const int MaximumSigningKeyBytes = 64;
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinimumLifetime = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(30);

    private readonly byte[] signingKey;

    public NonWorkingDayPreviewTokenOptions(
        string signingKeyBase64,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(signingKeyBase64);
        var canonicalKey = signingKeyBase64.Trim();
        if (canonicalKey.Length == 0 || canonicalKey != signingKeyBase64)
        {
            throw new ArgumentException(
                "Preview token signing key must be a canonical Base64 value.",
                nameof(signingKeyBase64));
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(canonicalKey);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "Preview token signing key must be valid Base64.",
                nameof(signingKeyBase64),
                exception);
        }

        if (!string.Equals(
                Convert.ToBase64String(key),
                canonicalKey,
                StringComparison.Ordinal)
            || key.Length is < MinimumSigningKeyBytes or > MaximumSigningKeyBytes)
        {
            throw new ArgumentException(
                $"Preview token signing key must contain {MinimumSigningKeyBytes} to "
                + $"{MaximumSigningKeyBytes} bytes in canonical Base64 form.",
                nameof(signingKeyBase64));
        }

        if (lifetime < MinimumLifetime
            || lifetime > MaximumLifetime
            || lifetime.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                lifetime,
                $"Preview token lifetime must be a whole number of seconds between "
                + $"{MinimumLifetime} and {MaximumLifetime}.");
        }

        signingKey = key;
        Lifetime = lifetime;
    }

    public TimeSpan Lifetime { get; }

    public static NonWorkingDayPreviewTokenOptions FromConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var signingKeyPath = $"{SectionName}:{SigningKeyName}";
        var lifetimePath = $"{SectionName}:{LifetimeName}";
        var signingKey = configuration[signingKeyPath];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                $"{signingKeyPath} must be configured with a secret Base64 key.");
        }

        var lifetime = DefaultLifetime;
        var lifetimeValue = configuration[lifetimePath];
        if (!string.IsNullOrWhiteSpace(lifetimeValue)
            && !TimeSpan.TryParse(
                lifetimeValue,
                CultureInfo.InvariantCulture,
                out lifetime))
        {
            throw new InvalidOperationException(
                $"{lifetimePath} must be a valid invariant TimeSpan value.");
        }

        try
        {
            return new NonWorkingDayPreviewTokenOptions(signingKey, lifetime);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{SectionName} configuration is invalid: {exception.Message}",
                exception);
        }
    }

    internal byte[] CopySigningKey() => (byte[])signingKey.Clone();
}
