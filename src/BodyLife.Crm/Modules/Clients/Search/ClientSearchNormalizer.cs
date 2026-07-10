using System.Text;

namespace BodyLife.Crm.Modules.Clients.Search;

public static class ClientSearchNormalizer
{
    public static string NormalizeCardNumber(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            throw new ArgumentException("Card number is required.", nameof(cardNumber));
        }

        var normalizedInput = cardNumber.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalizedInput.Length);

        foreach (var character in normalizedInput)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            if (char.IsControl(character))
            {
                throw new ArgumentException(
                    "Card number contains an unsupported control character.",
                    nameof(cardNumber));
            }

            builder.Append(character);
        }

        if (builder.Length == 0)
        {
            throw new ArgumentException("Card number is required.", nameof(cardNumber));
        }

        return builder.ToString().ToUpperInvariant();
    }

    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            throw new ArgumentException("Phone is required.", nameof(phone));
        }

        var normalizedInput = phone.Normalize(NormalizationForm.FormKC);
        var digits = new StringBuilder(normalizedInput.Length);
        var plusSeen = false;

        foreach (var character in normalizedInput)
        {
            if (IsAsciiDigit(character))
            {
                digits.Append(character);
                continue;
            }

            if (char.IsWhiteSpace(character) || IsPhoneFormattingCharacter(character))
            {
                continue;
            }

            if (character == '+' && !plusSeen && digits.Length == 0)
            {
                plusSeen = true;
                continue;
            }

            throw new ArgumentException(
                "Phone contains an unsupported character or misplaced plus sign.",
                nameof(phone));
        }

        if (digits.Length < 4)
        {
            throw new ArgumentException("Phone must contain at least four digits.", nameof(phone));
        }

        return digits.ToString();
    }

    public static string ExtractPhoneLastFour(string? normalizedPhone)
    {
        if (string.IsNullOrWhiteSpace(normalizedPhone)
            || normalizedPhone.Length < 4
            || normalizedPhone.Any(character => !IsAsciiDigit(character)))
        {
            throw new ArgumentException(
                "Normalized phone must contain at least four ASCII digits only.",
                nameof(normalizedPhone));
        }

        return normalizedPhone[^4..];
    }

    public static string NormalizeNamePart(string? namePart)
    {
        return NormalizeNamePart(namePart, nameof(namePart));
    }

    public static string NormalizeFullName(
        string? surname,
        string? name,
        string? patronymic)
    {
        var normalizedParts = new List<string>
        {
            NormalizeNamePart(surname, nameof(surname)),
            NormalizeNamePart(name, nameof(name)),
        };

        if (!string.IsNullOrWhiteSpace(patronymic))
        {
            normalizedParts.Add(NormalizeNamePart(patronymic, nameof(patronymic)));
        }

        return string.Join(' ', normalizedParts);
    }

    private static string NormalizeNamePart(string? namePart, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(namePart))
        {
            throw new ArgumentException("Name part is required.", parameterName);
        }

        var normalizedInput = namePart.Normalize(NormalizationForm.FormC);
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
                    "Name part contains an unsupported control character.",
                    parameterName);
            }

            if (whitespacePending)
            {
                builder.Append(' ');
                whitespacePending = false;
            }

            builder.Append(CanonicalizeNamePunctuation(character));
        }

        if (builder.Length == 0)
        {
            throw new ArgumentException("Name part is required.", parameterName);
        }

        return builder.ToString().ToUpperInvariant();
    }

    private static bool IsAsciiDigit(char character)
    {
        return character is >= '0' and <= '9';
    }

    private static bool IsPhoneFormattingCharacter(char character)
    {
        return character is '(' or ')' or '-' or '.';
    }

    private static char CanonicalizeNamePunctuation(char character)
    {
        return character switch
        {
            '\u02bc' or '\u2018' or '\u2019' or '\uff07' => '\'',
            '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2212' => '-',
            _ => character,
        };
    }
}
