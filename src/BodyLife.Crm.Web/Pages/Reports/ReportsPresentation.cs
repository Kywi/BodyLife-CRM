using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Localization;
using Microsoft.Extensions.Localization;

namespace BodyLife.Crm.Web.Pages.Reports;

/// <summary>Presentation-only localization shared by report Razor pages.</summary>
public static class ReportsPresentation
{
    public static string Date(DateOnly value) => ReceptionDisplayFormatter.Date(value);
    public static string Timestamp(DateTimeOffset value) => ReceptionDisplayFormatter.DateTime(value);
    public static string Money(Money value) => ReceptionDisplayFormatter.Money(value);
    public static string WarningMessage(
        IStringLocalizer<BodyLife.Crm.Web.Localization.Reports> localizer,
        string code) => code switch
        {
            MembershipWarningCodes.NegativeBalance => localizer["Warning.NegativeBalance"],
            MembershipWarningCodes.ExpiredByDate => localizer["Warning.ExpiredByDate"],
            MembershipWarningCodes.ZeroRemaining => localizer["Warning.ZeroRemaining"],
            MembershipWarningCodes.EndingSoon => localizer["Warning.EndingSoon"],
            MembershipWarningCodes.LowRemaining => localizer["Warning.LowRemaining"],
            _ => localizer["Warning.Generic"],
        };
    public static string Error(IStringLocalizer<BodyLife.Crm.Web.Localization.Reports> localizer, object? status) =>
        status?.ToString() switch
        {
            "PermissionDenied" => localizer["Error.PermissionDenied"],
            "InvalidDate" or "InvalidInput" or "ValidationFailed" =>
                localizer["Error.InvalidInput"],
            _ => localizer["Error.Unavailable"],
        };
}
