using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Reports;

public interface IReceptionActivityCursorProtector
{
    string Encode(DateOnly date, DateTimeOffset recordedAt, Guid auditId);

    bool TryDecode(string? value, DateOnly requestedDate, out ReceptionActivityCursor? cursor);
}

public sealed record ReceptionActivityCursor(DateTimeOffset RecordedAt, Guid AuditId);

internal sealed class ReceptionActivityCursorCodec : IReceptionActivityCursorProtector
{
    private const string Prefix = "bodylife-reception-activity-cursor-v1";
    private const string Sort = "recorded_at_desc_id_desc";
    private const int MaxLength = 384;
    private readonly byte[] signingKey;

    internal ReceptionActivityCursorCodec(NonWorkingDayPreviewTokenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        signingKey = options.CopySigningKey();
    }

    public string Encode(DateOnly date, DateTimeOffset recordedAt, Guid auditId)
    {
        if (auditId == Guid.Empty || !BusinessTimeZone.TryNormalizeUtcInstant(recordedAt, out var utc))
            throw new ArgumentException("Cursor key is invalid.");
        var payload = string.Create(CultureInfo.InvariantCulture, $"1|{date:yyyy-MM-dd}|{Sort}|{utc.UtcTicks}|{auditId:D}");
        var payloadSegment = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        var signature = HMACSHA256.HashData(signingKey, Encoding.ASCII.GetBytes($"{Prefix}.{payloadSegment}"));
        return $"{Prefix}.{payloadSegment}.{Base64UrlEncode(signature)}";
    }

    public bool TryDecode(string? value, DateOnly requestedDate, out ReceptionActivityCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (value != value.Trim() || value.Length > MaxLength) return false;
        var segments = value.Split('.', StringSplitOptions.None);
        if (segments.Length != 3 || segments[0] != Prefix
            || !TryBase64UrlDecode(segments[1], out var payloadBytes)
            || !TryBase64UrlDecode(segments[2], out var suppliedSignature)
            || suppliedSignature.Length != HMACSHA256.HashSizeInBytes)
            return false;
        if (Base64UrlEncode(payloadBytes) != segments[1] || Base64UrlEncode(suppliedSignature) != segments[2]) return false;
        var expectedSignature = HMACSHA256.HashData(signingKey, Encoding.ASCII.GetBytes($"{Prefix}.{segments[1]}"));
        if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature)) return false;
        var payload = Encoding.UTF8.GetString(payloadBytes);
        var parts = payload.Split('|', StringSplitOptions.None);
        if (parts.Length != 5 || parts[0] != "1" || parts[1] != requestedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) || parts[2] != Sort
            || !long.TryParse(parts[3], CultureInfo.InvariantCulture, out var ticks) || !Guid.TryParseExact(parts[4], "D", out var auditId) || auditId == Guid.Empty)
            return false;
        try
        {
            var recordedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            if (!BusinessTimeZone.TryNormalizeUtcInstant(recordedAt, out var utc)) return false;
            var range = BusinessTimeZone.GetUtcDayRange(requestedDate);
            if (utc < range.FromInclusive || utc >= range.ToExclusive) return false;
            if (Encode(requestedDate, utc, auditId) != value) return false;
            cursor = new ReceptionActivityCursor(utc, auditId);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool TryBase64UrlDecode(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length == 0 || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))) return false;
        try { bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4)); return true; }
        catch (FormatException) { return false; }
    }
}
