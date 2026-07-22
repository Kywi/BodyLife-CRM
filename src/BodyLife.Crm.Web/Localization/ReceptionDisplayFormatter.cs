using System.Globalization;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Web.Localization;

/// <summary>Culture-aware display formatting for Web UI; machine form values stay invariant.</summary>
public static class ReceptionDisplayFormatter
{
    public static string Date(DateOnly value) => value.ToString("d", CultureInfo.CurrentCulture);
    public static string DateTime(DateTimeOffset value) =>
        BusinessTimeZone.ConvertInstantToLocal(value).ToString("g", CultureInfo.CurrentCulture);
    public static string Time(DateTimeOffset value) =>
        BusinessTimeZone.ConvertInstantToLocal(value).ToString("t", CultureInfo.CurrentCulture);
    public static string Money(Money value) => string.Format(CultureInfo.CurrentCulture, "{0:N2} {1}", value.Amount, value.Currency);
    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);
}
