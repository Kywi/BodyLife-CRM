using System.Text;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.MembershipTypes;

public static class MembershipTypeCatalogRules
{
    public static MembershipTypeCatalogValues NormalizeAndValidate(
        string? name,
        int durationDays,
        int visitsLimit,
        Money price,
        string? comment)
    {
        if (durationDays <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationDays),
                "Duration days must be greater than zero.");
        }

        if (visitsLimit < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(visitsLimit),
                "Visits limit cannot be negative.");
        }

        var normalizedPrice = new Money(price.Amount, price.Currency);

        return new MembershipTypeCatalogValues(
            NormalizeName(name),
            durationDays,
            visitsLimit,
            normalizedPrice,
            NormalizeOptional(comment));
    }

    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Membership type name is required.", nameof(name));
        }

        var normalizedInput = name.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalizedInput.Length);
        var whitespacePending = false;

        foreach (var character in normalizedInput)
        {
            if (char.IsWhiteSpace(character))
            {
                whitespacePending = builder.Length > 0;
                continue;
            }

            if (char.IsControl(character))
            {
                throw new ArgumentException(
                    "Membership type name contains an unsupported control character.",
                    nameof(name));
            }

            if (whitespacePending)
            {
                builder.Append(' ');
                whitespacePending = false;
            }

            builder.Append(character);
        }

        if (builder.Length == 0)
        {
            throw new ArgumentException("Membership type name is required.", nameof(name));
        }

        return builder.ToString();
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
