using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
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
    private readonly HmacNonWorkingDayTokenCodec tokenCodec = new(
        options,
        timeProvider,
        TokenPrefix,
        NonWorkingDayPreviewConfirmation.MaxTokenLength);

    public NonWorkingDayPreviewConfirmation Issue(
        NonWorkingDayPreviewInput input,
        MembershipNonWorkingDayAffectedScope scope)
    {
        var issue = tokenCodec.Issue(CreateFingerprint(input, scope));

        return new NonWorkingDayPreviewConfirmation(
            issue.ConfirmationToken,
            issue.Fingerprint,
            issue.IssuedAt,
            issue.ExpiresAt);
    }

    public NonWorkingDayPreviewTokenValidation Validate(
        string? confirmationToken,
        NonWorkingDayPreviewInput input,
        MembershipNonWorkingDayAffectedScope currentScope)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(currentScope);

        var validation = tokenCodec.Validate(
            confirmationToken,
            () => CreateFingerprint(input, currentScope));
        if (validation.Status == HmacNonWorkingDayTokenValidationStatus.InvalidToken)
        {
            return NonWorkingDayPreviewTokenValidation.InvalidToken();
        }

        var status = validation.Status switch
        {
            HmacNonWorkingDayTokenValidationStatus.Valid =>
                NonWorkingDayPreviewTokenValidationStatus.Valid,
            HmacNonWorkingDayTokenValidationStatus.Expired =>
                NonWorkingDayPreviewTokenValidationStatus.Expired,
            HmacNonWorkingDayTokenValidationStatus.FingerprintMismatch =>
                NonWorkingDayPreviewTokenValidationStatus.InputOrScopeMismatch,
            _ => throw new InvalidOperationException(
                "Authenticated preview token has an unsupported status."),
        };
        return NonWorkingDayPreviewTokenValidation.FromAuthenticatedToken(
            status,
            validation.Fingerprint!,
            validation.IssuedAt!.Value,
            validation.ExpiresAt!.Value);
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

    private static string Format(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string Format(Guid identifier)
    {
        return identifier.ToString("D", CultureInfo.InvariantCulture);
    }

}
