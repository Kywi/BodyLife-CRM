using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class HmacNonWorkingDayPreviewTokenService(
    NonWorkingDayPreviewTokenOptions options,
    TimeProvider timeProvider)
    : INonWorkingDayPreviewTokenService
{
    private const string TokenPrefix = "bodylife-nwd-preview-v1";
    private const string FingerprintSchema = "bodylife.nonworking-day-preview.v1";
    private const int TokenVersion = 1;
    private const int MaximumPayloadBytes = 512;
    private static readonly JsonSerializerOptions TokenJsonOptions = new()
    {
        MaxDepth = 4,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly TimeSpan lifetime = options?.Lifetime
        ?? throw new ArgumentNullException(nameof(options));
    private readonly byte[] signingKey = options.CopySigningKey();
    private readonly TimeProvider timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    public NonWorkingDayPreviewConfirmation Issue(
        NonWorkingDayPreviewInput input,
        MembershipNonWorkingDayAffectedScope scope)
    {
        var fingerprintBytes = CreateFingerprint(input, scope);
        var fingerprint = Convert.ToHexString(fingerprintBytes);
        var issuedAt = NormalizeTimestamp(timeProvider.GetUtcNow());
        var expiresAt = issuedAt.Add(lifetime);
        var payload = new PreviewTokenPayload(
            TokenVersion,
            fingerprint,
            issuedAt.ToUnixTimeMilliseconds(),
            expiresAt.ToUnixTimeMilliseconds());
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(
            payload,
            TokenJsonOptions);
        var payloadSegment = Convert.ToBase64String(payloadBytes);
        var signingInput = Encoding.ASCII.GetBytes($"{TokenPrefix}.{payloadSegment}");
        var signature = HMACSHA256.HashData(signingKey, signingInput);
        var token = $"{TokenPrefix}.{payloadSegment}.{Convert.ToHexString(signature)}";

        return new NonWorkingDayPreviewConfirmation(
            token,
            fingerprint,
            issuedAt,
            expiresAt);
    }

    public NonWorkingDayPreviewTokenValidation Validate(
        string? confirmationToken,
        NonWorkingDayPreviewInput input,
        MembershipNonWorkingDayAffectedScope currentScope)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(currentScope);

        if (!TryReadAuthenticatedToken(
                confirmationToken,
                out var payload,
                out var issuedAt,
                out var expiresAt,
                out var tokenFingerprintBytes))
        {
            return NonWorkingDayPreviewTokenValidation.InvalidToken();
        }

        var now = NormalizeTimestamp(timeProvider.GetUtcNow());
        if (issuedAt > now)
        {
            return NonWorkingDayPreviewTokenValidation.InvalidToken();
        }

        if (now >= expiresAt)
        {
            return NonWorkingDayPreviewTokenValidation.FromAuthenticatedToken(
                NonWorkingDayPreviewTokenValidationStatus.Expired,
                payload.Fingerprint,
                issuedAt,
                expiresAt);
        }

        var expectedFingerprintBytes = CreateFingerprint(input, currentScope);
        var status = CryptographicOperations.FixedTimeEquals(
            tokenFingerprintBytes,
            expectedFingerprintBytes)
            ? NonWorkingDayPreviewTokenValidationStatus.Valid
            : NonWorkingDayPreviewTokenValidationStatus.InputOrScopeMismatch;
        return NonWorkingDayPreviewTokenValidation.FromAuthenticatedToken(
            status,
            payload.Fingerprint,
            issuedAt,
            expiresAt);
    }

    private bool TryReadAuthenticatedToken(
        string? confirmationToken,
        out PreviewTokenPayload payload,
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
            || confirmationToken.Length > NonWorkingDayPreviewConfirmation.MaxTokenLength)
        {
            return false;
        }

        var segments = confirmationToken.Split('.', StringSplitOptions.None);
        if (segments.Length != 3
            || !string.Equals(segments[0], TokenPrefix, StringComparison.Ordinal))
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

        var signingInput = Encoding.ASCII.GetBytes($"{TokenPrefix}.{segments[1]}");
        var expectedSignature = HMACSHA256.HashData(signingKey, signingInput);
        if (!CryptographicOperations.FixedTimeEquals(
                suppliedSignature,
                expectedSignature))
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<PreviewTokenPayload>(
                    payloadBytes,
                    TokenJsonOptions)
                ?? throw new JsonException("Preview token payload is missing.");
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

    private static byte[] CreateFingerprint(
        NonWorkingDayPreviewInput input,
        MembershipNonWorkingDayAffectedScope scope)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Period != input.Period)
        {
            throw new ArgumentException(
                "Affected Membership scope period must match preview input.",
                nameof(scope));
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", FingerprintSchema);
            writer.WriteString("periodStart", Format(input.Period.StartDate));
            writer.WriteString("periodEnd", Format(input.Period.EndDate));
            writer.WriteString("reasonCode", input.ReasonCode);
            if (input.ReasonComment is null)
            {
                writer.WriteNull("reasonComment");
            }
            else
            {
                writer.WriteString("reasonComment", input.ReasonComment);
            }

            writer.WriteStartArray("scope");
            foreach (var item in scope.AffectedMemberships)
            {
                writer.WriteStartObject();
                writer.WriteString("membershipId", Format(item.MembershipId));
                writer.WriteString("clientId", Format(item.ClientId));
                writer.WriteString("appliedStart", Format(item.AppliedRange.StartDate));
                writer.WriteString("appliedEnd", Format(item.AppliedRange.EndDate));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return SHA256.HashData(stream.ToArray());
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset timestamp)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(
            timestamp.ToUnixTimeMilliseconds());
    }

    private static string Format(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string Format(Guid identifier)
    {
        return identifier.ToString("D", CultureInfo.InvariantCulture);
    }

    private sealed record PreviewTokenPayload(
        int Version,
        string Fingerprint,
        long IssuedAtUnixMilliseconds,
        long ExpiresAtUnixMilliseconds);
}
