using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal sealed class HmacNonWorkingDayTokenCodec
{
    private const int TokenVersion = 1;
    private const int MaximumPayloadBytes = 512;
    private static readonly JsonSerializerOptions TokenJsonOptions = new()
    {
        MaxDepth = 4,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly int maxTokenLength;
    private readonly byte[] signingKey;
    private readonly TimeProvider timeProvider;
    private readonly string tokenPrefix;
    private readonly TimeSpan lifetime;

    internal HmacNonWorkingDayTokenCodec(
        NonWorkingDayPreviewTokenOptions options,
        TimeProvider timeProvider,
        string tokenPrefix,
        int maxTokenLength)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenPrefix);
        if (tokenPrefix != tokenPrefix.Trim()
            || tokenPrefix.Contains('.', StringComparison.Ordinal)
            || !tokenPrefix.All(char.IsAscii))
        {
            throw new ArgumentException(
                "Token prefix must be canonical ASCII without separators.",
                nameof(tokenPrefix));
        }

        if (maxTokenLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokenLength));
        }

        lifetime = options.Lifetime;
        signingKey = options.CopySigningKey();
        this.timeProvider = timeProvider;
        this.tokenPrefix = tokenPrefix;
        this.maxTokenLength = maxTokenLength;
    }

    internal HmacNonWorkingDayTokenIssue Issue(byte[] fingerprintBytes)
    {
        EnsureFingerprint(fingerprintBytes);

        var fingerprint = Convert.ToHexString(fingerprintBytes);
        var issuedAt = NormalizeTimestamp(timeProvider.GetUtcNow());
        var expiresAt = issuedAt.Add(lifetime);
        var payload = new TokenPayload(
            TokenVersion,
            fingerprint,
            issuedAt.ToUnixTimeMilliseconds(),
            expiresAt.ToUnixTimeMilliseconds());
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(
            payload,
            TokenJsonOptions);
        var payloadSegment = Convert.ToBase64String(payloadBytes);
        var signingInput = Encoding.ASCII.GetBytes($"{tokenPrefix}.{payloadSegment}");
        var signature = HMACSHA256.HashData(signingKey, signingInput);
        var token = $"{tokenPrefix}.{payloadSegment}.{Convert.ToHexString(signature)}";

        return new HmacNonWorkingDayTokenIssue(
            token,
            fingerprint,
            issuedAt,
            expiresAt);
    }

    internal HmacNonWorkingDayTokenValidation Validate(
        string? confirmationToken,
        Func<byte[]> expectedFingerprintFactory)
    {
        ArgumentNullException.ThrowIfNull(expectedFingerprintFactory);

        if (!TryReadAuthenticatedToken(
                confirmationToken,
                out var payload,
                out var issuedAt,
                out var expiresAt,
                out var tokenFingerprintBytes))
        {
            return HmacNonWorkingDayTokenValidation.InvalidToken();
        }

        var now = NormalizeTimestamp(timeProvider.GetUtcNow());
        if (issuedAt > now)
        {
            return HmacNonWorkingDayTokenValidation.InvalidToken();
        }

        if (now >= expiresAt)
        {
            return HmacNonWorkingDayTokenValidation.Authenticated(
                HmacNonWorkingDayTokenValidationStatus.Expired,
                payload.Fingerprint,
                issuedAt,
                expiresAt);
        }

        var expectedFingerprintBytes = expectedFingerprintFactory();
        EnsureFingerprint(expectedFingerprintBytes);
        var status = CryptographicOperations.FixedTimeEquals(
            tokenFingerprintBytes,
            expectedFingerprintBytes)
            ? HmacNonWorkingDayTokenValidationStatus.Valid
            : HmacNonWorkingDayTokenValidationStatus.FingerprintMismatch;
        return HmacNonWorkingDayTokenValidation.Authenticated(
            status,
            payload.Fingerprint,
            issuedAt,
            expiresAt);
    }

    private bool TryReadAuthenticatedToken(
        string? confirmationToken,
        out TokenPayload payload,
        out DateTimeOffset issuedAt,
        out DateTimeOffset expiresAt,
        out byte[] fingerprintBytes)
    {
        payload = default!;
        issuedAt = default;
        expiresAt = default;
        fingerprintBytes = [];

        if (string.IsNullOrWhiteSpace(confirmationToken)
            || confirmationToken != confirmationToken.Trim()
            || confirmationToken.Length > maxTokenLength)
        {
            return false;
        }

        var segments = confirmationToken.Split('.', StringSplitOptions.None);
        if (segments.Length != 3
            || !string.Equals(segments[0], tokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] payloadBytes;
        byte[] suppliedSignature;
        try
        {
            payloadBytes = Convert.FromBase64String(segments[1]);
            suppliedSignature = Convert.FromHexString(segments[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (payloadBytes.Length is 0 or > MaximumPayloadBytes
            || !string.Equals(
                Convert.ToBase64String(payloadBytes),
                segments[1],
                StringComparison.Ordinal)
            || suppliedSignature.Length != HMACSHA256.HashSizeInBytes
            || !string.Equals(
                Convert.ToHexString(suppliedSignature),
                segments[2],
                StringComparison.Ordinal))
        {
            return false;
        }

        var signingInput = Encoding.ASCII.GetBytes($"{tokenPrefix}.{segments[1]}");
        var expectedSignature = HMACSHA256.HashData(signingKey, signingInput);
        if (!CryptographicOperations.FixedTimeEquals(
                suppliedSignature,
                expectedSignature))
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<TokenPayload>(
                    payloadBytes,
                    TokenJsonOptions)
                ?? throw new JsonException("Confirmation token payload is missing.");
            fingerprintBytes = Convert.FromHexString(payload.Fingerprint);
            issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(
                payload.IssuedAtUnixMilliseconds);
            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(
                payload.ExpiresAtUnixMilliseconds);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }

        return payload.Version == TokenVersion
            && fingerprintBytes.Length == SHA256.HashSizeInBytes
            && string.Equals(
                Convert.ToHexString(fingerprintBytes),
                payload.Fingerprint,
                StringComparison.Ordinal)
            && expiresAt > issuedAt
            && expiresAt - issuedAt == lifetime;
    }

    private static void EnsureFingerprint(byte[] fingerprintBytes)
    {
        ArgumentNullException.ThrowIfNull(fingerprintBytes);
        if (fingerprintBytes.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException(
                "Token fingerprint must be a SHA-256 digest.",
                nameof(fingerprintBytes));
        }
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset timestamp)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(
            timestamp.ToUnixTimeMilliseconds());
    }

    private sealed record TokenPayload(
        int Version,
        string Fingerprint,
        long IssuedAtUnixMilliseconds,
        long ExpiresAtUnixMilliseconds);
}

internal sealed record HmacNonWorkingDayTokenIssue(
    string ConfirmationToken,
    string Fingerprint,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

internal enum HmacNonWorkingDayTokenValidationStatus
{
    Valid,
    Expired,
    InvalidToken,
    FingerprintMismatch,
}

internal sealed class HmacNonWorkingDayTokenValidation
{
    private HmacNonWorkingDayTokenValidation(
        HmacNonWorkingDayTokenValidationStatus status,
        string? fingerprint,
        DateTimeOffset? issuedAt,
        DateTimeOffset? expiresAt)
    {
        Status = status;
        Fingerprint = fingerprint;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    internal HmacNonWorkingDayTokenValidationStatus Status { get; }

    internal string? Fingerprint { get; }

    internal DateTimeOffset? IssuedAt { get; }

    internal DateTimeOffset? ExpiresAt { get; }

    internal static HmacNonWorkingDayTokenValidation InvalidToken()
    {
        return new HmacNonWorkingDayTokenValidation(
            HmacNonWorkingDayTokenValidationStatus.InvalidToken,
            fingerprint: null,
            issuedAt: null,
            expiresAt: null);
    }

    internal static HmacNonWorkingDayTokenValidation Authenticated(
        HmacNonWorkingDayTokenValidationStatus status,
        string fingerprint,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        if (status is HmacNonWorkingDayTokenValidationStatus.InvalidToken
            || !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new HmacNonWorkingDayTokenValidation(
            status,
            fingerprint,
            issuedAt,
            expiresAt);
    }
}
