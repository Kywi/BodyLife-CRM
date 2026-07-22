using System.Globalization;

namespace BodyLife.Crm.Web.Localization;

public static class WebCultures
{
    public const string Ukrainian = "uk-UA";
    public const string English = "en-US";
    public const string Default = Ukrainian;

    public static readonly CultureInfo[] Supported =
    [
        CultureInfo.GetCultureInfo(Ukrainian),
        CultureInfo.GetCultureInfo(English),
    ];

    public static bool IsSupported(string? culture) =>
        string.Equals(culture, Ukrainian, StringComparison.Ordinal) ||
        string.Equals(culture, English, StringComparison.Ordinal);
}
