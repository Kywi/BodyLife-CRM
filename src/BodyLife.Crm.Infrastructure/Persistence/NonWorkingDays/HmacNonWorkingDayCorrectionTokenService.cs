using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Modules.NonWorkingDays;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

public sealed class HmacNonWorkingDayCorrectionTokenService(
    NonWorkingDayPreviewTokenOptions options,
    TimeProvider timeProvider)
    : INonWorkingDayCorrectionTokenService
{
    private const string TokenPrefix = "bodylife-nwd-correction-v1";
    private const string FingerprintSchema = "bodylife.nonworking-day-correction.v1";
    private readonly HmacNonWorkingDayTokenCodec tokenCodec = new(
        options,
        timeProvider,
        TokenPrefix,
        NonWorkingDayCorrectionConfirmation.MaxTokenLength);

    public NonWorkingDayCorrectionConfirmation Issue(
        NonWorkingDayCorrectionConfirmationMaterial material)
    {
        var issue = tokenCodec.Issue(CreateFingerprint(material));
        return new NonWorkingDayCorrectionConfirmation(
            issue.ConfirmationToken,
            issue.Fingerprint,
            issue.IssuedAt,
            issue.ExpiresAt);
    }

    public NonWorkingDayCorrectionTokenValidation Validate(
        string? confirmationToken,
        NonWorkingDayCorrectionConfirmationMaterial currentMaterial)
    {
        ArgumentNullException.ThrowIfNull(currentMaterial);

        var validation = tokenCodec.Validate(
            confirmationToken,
            () => CreateFingerprint(currentMaterial));
        if (validation.Status == HmacNonWorkingDayTokenValidationStatus.InvalidToken)
        {
            return NonWorkingDayCorrectionTokenValidation.InvalidToken();
        }

        var status = validation.Status switch
        {
            HmacNonWorkingDayTokenValidationStatus.Valid =>
                NonWorkingDayCorrectionTokenValidationStatus.Valid,
            HmacNonWorkingDayTokenValidationStatus.Expired =>
                NonWorkingDayCorrectionTokenValidationStatus.Expired,
            HmacNonWorkingDayTokenValidationStatus.FingerprintMismatch =>
                NonWorkingDayCorrectionTokenValidationStatus
                    .ConfirmationMaterialMismatch,
            _ => throw new InvalidOperationException(
                "Authenticated correction token has an unsupported status."),
        };
        return NonWorkingDayCorrectionTokenValidation.FromAuthenticatedToken(
            status,
            validation.Fingerprint!,
            validation.IssuedAt!.Value,
            validation.ExpiresAt!.Value);
    }

    private static byte[] CreateFingerprint(
        NonWorkingDayCorrectionConfirmationMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", FingerprintSchema);
            writer.WriteString("periodId", Format(material.PeriodId));
            writer.WriteNumber("mode", (int)material.Mode);
            writer.WriteNumber("scopeBehavior", (int)material.ScopeBehavior);
            WriteOriginalSource(writer, material.OriginalSource);

            writer.WriteStartArray("originalApplicationIds");
            foreach (var applicationId in material.OriginalApplicationIds)
            {
                writer.WriteStringValue(Format(applicationId));
            }

            writer.WriteEndArray();
            WriteReplacementInput(writer, material.ReplacementInput);
            WriteConfirmedScope(writer, material.ConfirmedScope);
            writer.WriteEndObject();
        }

        return SHA256.HashData(stream.ToArray());
    }

    private static void WriteOriginalSource(
        Utf8JsonWriter writer,
        NonWorkingDayCorrectionSource source)
    {
        writer.WriteStartObject("originalSource");
        writer.WriteString("periodId", Format(source.PeriodId));
        writer.WriteString("periodStart", Format(source.Period.StartDate));
        writer.WriteString("periodEnd", Format(source.Period.EndDate));
        writer.WriteString("reasonCode", source.ReasonCode);
        WriteOptionalString(writer, "reasonComment", source.ReasonComment);
        writer.WriteNumber("createdAtUtcTicks", source.CreatedAt.UtcDateTime.Ticks);
        writer.WriteString("createdByAccountId", Format(source.CreatedByAccountId));
        writer.WriteString("sessionId", Format(source.SessionId));
        writer.WriteNumber("status", (int)source.Status);
        WriteOptionalId(writer, "existingCancellationId", source.ExistingCancellationId);

        writer.WriteStartArray("applications");
        foreach (var application in source.Applications)
        {
            writer.WriteStartObject();
            writer.WriteString("applicationId", Format(application.ApplicationId));
            writer.WriteString("membershipId", Format(application.MembershipId));
            writer.WriteString("clientId", Format(application.ClientId));
            writer.WriteString(
                "appliedStart",
                Format(application.AppliedRange.StartDate));
            writer.WriteString("appliedEnd", Format(application.AppliedRange.EndDate));
            writer.WriteNumber(
                "previewedAtUtcTicks",
                application.PreviewedAt.UtcDateTime.Ticks);
            writer.WriteNumber(
                "confirmedAtUtcTicks",
                application.ConfirmedAt.UtcDateTime.Ticks);
            writer.WriteNumber("status", (int)application.Status);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteReplacementInput(
        Utf8JsonWriter writer,
        NonWorkingDayPreviewInput? replacementInput)
    {
        if (replacementInput is null)
        {
            writer.WriteNull("replacementInput");
            return;
        }

        writer.WriteStartObject("replacementInput");
        writer.WriteString("periodStart", Format(replacementInput.Period.StartDate));
        writer.WriteString("periodEnd", Format(replacementInput.Period.EndDate));
        writer.WriteString("reasonCode", replacementInput.ReasonCode);
        WriteOptionalString(
            writer,
            "reasonComment",
            replacementInput.ReasonComment);
        writer.WriteEndObject();
    }

    private static void WriteConfirmedScope(
        Utf8JsonWriter writer,
        Modules.Memberships.MembershipNonWorkingDayAffectedScope? confirmedScope)
    {
        if (confirmedScope is null)
        {
            writer.WriteNull("confirmedScope");
            return;
        }

        writer.WriteStartObject("confirmedScope");
        writer.WriteString("periodStart", Format(confirmedScope.Period.StartDate));
        writer.WriteString("periodEnd", Format(confirmedScope.Period.EndDate));
        writer.WriteStartArray("memberships");
        foreach (var item in confirmedScope.AffectedMemberships)
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

    private static void WriteOptionalString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteOptionalId(
        Utf8JsonWriter writer,
        string propertyName,
        Guid? identifier)
    {
        if (identifier is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, Format(identifier.Value));
        }
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
