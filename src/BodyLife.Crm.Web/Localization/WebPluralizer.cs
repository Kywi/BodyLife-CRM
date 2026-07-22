using System.Globalization;
using Microsoft.Extensions.Localization;

namespace BodyLife.Crm.Web.Localization;

public static class WebPluralizer
{
    public static string Visits(IStringLocalizer<Shared> localizer, int count) =>
        Format(localizer, count, "Visits.One", "Visits.Few", "Visits.Many", "Visits.Other");

    public static string Days(IStringLocalizer<Shared> localizer, int count) =>
        Format(localizer, count, "Days.One", "Days.Few", "Days.Many", "Days.Other");

    public static string Clients(IStringLocalizer<Shared> localizer, int count) =>
        Format(localizer, count, "Clients.One", "Clients.Few", "Clients.Many", "Clients.Other");

    public static string Memberships(IStringLocalizer<Shared> localizer, int count) =>
        Format(localizer, count, "Memberships.One", "Memberships.Few", "Memberships.Many", "Memberships.Other");

    public static string Entries(IStringLocalizer<Shared> localizer, int count) =>
        Format(localizer, count, "Entries.One", "Entries.Few", "Entries.Many", "Entries.Other");

    public static string Rows(IStringLocalizer<Shared> localizer, int count) =>
        Format(localizer, count, "Rows.One", "Rows.Few", "Rows.Many", "Rows.Other");

    public static string UniqueDays(IStringLocalizer<Shared> localizer, int count) =>
        Format(localizer, count, "UniqueDays.One", "UniqueDays.Few", "UniqueDays.Many", "UniqueDays.Other");

    private static string Format(IStringLocalizer<Shared> localizer, int count, string one, string few, string many, string other)
    {
        var key = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "uk"
            ? UkrainianForm(count, one, few, many)
            : count == 1 ? one : other;
        return string.Format(CultureInfo.CurrentCulture, localizer[key], count);
    }

    private static string UkrainianForm(int count, string one, string few, string many)
    {
        var mod10 = Math.Abs(count) % 10;
        var mod100 = Math.Abs(count) % 100;
        return mod10 == 1 && mod100 != 11 ? one :
            mod10 is >= 2 and <= 4 && (mod100 < 12 || mod100 > 14) ? few : many;
    }
}
