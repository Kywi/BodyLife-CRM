using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

/// <summary>Authenticates the exact server-derived allocation shown in an Issue preview.</summary>
public sealed class HmacMembershipIssuePreviewTokenService(
    NonWorkingDayPreviewTokenOptions options,
    TimeProvider timeProvider)
    : IMembershipIssuePreviewTokenService
{
    private const string TokenPrefix = "bodylife-membership-issue-preview-v2";
    private const string Schema = "bodylife.membership-issue-preview.v2";
    private const int MaxTokenLength = 2048;
    private readonly HmacNonWorkingDayTokenCodec codec = new(
        options, timeProvider, TokenPrefix, MaxTokenLength);

    public MembershipIssuePreviewToken Issue(MembershipIssuePreviewTokenMaterial material)
    {
        var issued = codec.Issue(CreateFingerprint(material));
        return new MembershipIssuePreviewToken(
            issued.ConfirmationToken, issued.IssuedAt, issued.ExpiresAt);
    }

    public MembershipIssuePreviewTokenValidation Validate(
        string? token,
        MembershipIssuePreviewTokenMaterial currentMaterial)
    {
        var result = codec.Validate(token, () => CreateFingerprint(currentMaterial));
        return new MembershipIssuePreviewTokenValidation(result.Status switch
        {
            HmacNonWorkingDayTokenValidationStatus.Valid => MembershipIssuePreviewTokenValidationStatus.Valid,
            HmacNonWorkingDayTokenValidationStatus.Expired => MembershipIssuePreviewTokenValidationStatus.Expired,
            HmacNonWorkingDayTokenValidationStatus.FingerprintMismatch => MembershipIssuePreviewTokenValidationStatus.PreviewMismatch,
            _ => MembershipIssuePreviewTokenValidationStatus.InvalidToken,
        });
    }

    private static byte[] CreateFingerprint(MembershipIssuePreviewTokenMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteString("clientId", Format(material.ClientId));
            writer.WriteString("membershipTypeId", Format(material.MembershipTypeId));
            writer.WriteNumber("membershipTypeUpdatedAtUtcTicks", material.MembershipTypeUpdatedAt.UtcDateTime.Ticks);
            writer.WriteString("proposedStartDate", Format(material.ProposedStartDate));
            writer.WriteNumber("totalNegativeBalance", material.TotalNegativeBalance);
            writer.WriteNumber("unknownNegativeBalance", material.UnknownNegativeBalance);
            writer.WriteNumber("coveredNegativeVisitCount", material.CoveredNegativeVisitCount);
            writer.WriteString("activePredecessorId", material.ActivePredecessorId?.ToString("D", CultureInfo.InvariantCulture));
            writer.WriteString("activePredecessorStatus", material.ActivePredecessorStatus?.ToString());
            writer.WriteString("activePredecessorStateVersion", material.ActivePredecessorStateVersion);
            if (material.ActivePredecessorRemainingVisits is { } remaining)
            {
                writer.WriteNumber("activePredecessorRemainingVisits", remaining);
            }
            else
            {
                writer.WriteNull("activePredecessorRemainingVisits");
            }
            writer.WriteStartArray("candidates");
            foreach (var visit in material.CandidateVisits)
            {
                writer.WriteStartObject();
                writer.WriteString("visitId", Format(visit.VisitId));
                writer.WriteString("sourceMembershipId", Format(visit.SourceMembershipId));
                writer.WriteString("oldConsumptionId", Format(visit.OldConsumptionId));
                writer.WriteString("occurredAt", visit.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                writer.WriteString("consumptionRecordedAt", visit.ConsumptionRecordedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                writer.WriteString("businessDate", Format(visit.BusinessDate));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return SHA256.HashData(stream.ToArray());
    }

    private static string Format(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
    private static string Format(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
