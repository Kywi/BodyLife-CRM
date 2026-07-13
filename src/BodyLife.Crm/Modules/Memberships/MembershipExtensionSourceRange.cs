using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipExtensionSourceRange
{
    public const int MaxSourceTypeLength = 64;
    public const int MaxSourceLabelLength = 500;

    public MembershipExtensionSourceRange(
        string? sourceType,
        Guid sourceId,
        string? sourceLabel,
        DateRange range,
        bool isActive)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Extension source id is required.", nameof(sourceId));
        }

        SourceType = NormalizeRequired(
            sourceType,
            MaxSourceTypeLength,
            "Extension source type is required.",
            "Extension source type is too long.",
            nameof(sourceType));
        SourceId = sourceId;
        SourceLabel = NormalizeRequired(
            sourceLabel,
            MaxSourceLabelLength,
            "Extension source label is required.",
            "Extension source label is too long.",
            nameof(sourceLabel));
        Range = range;
        IsActive = isActive;
    }

    public string SourceType { get; }

    public Guid SourceId { get; }

    public string SourceLabel { get; }

    public DateRange Range { get; }

    public bool IsActive { get; }

    private static string NormalizeRequired(
        string? value,
        int maxLength,
        string requiredMessage,
        string tooLongMessage,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException(requiredMessage, parameterName);
        }

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException(tooLongMessage, parameterName);
        }

        return normalized;
    }
}
