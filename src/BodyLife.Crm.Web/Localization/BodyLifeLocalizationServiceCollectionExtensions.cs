using Microsoft.AspNetCore.Localization;

namespace BodyLife.Crm.Web.Localization;

public static class BodyLifeLocalizationServiceCollectionExtensions
{
    public static IServiceCollection AddBodyLifeLocalization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddScoped<Pages.Audit.AuditPresentation>();
        services.AddScoped<Pages.Audit.ClientAuditExplanationFactory>();
        services.AddScoped<Pages.Audit.StaffAccountAuditExplanationFactory>();
        services.AddScoped<Pages.Audit.NonWorkingDayAuditExplanationFactory>();
        services.AddScoped<Pages.Audit.AuditEntryExplanationPresenter>();
        services.AddScoped<Pages.Audit.ClientHistoryRowPresenter>();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(WebCultures.Default);
            options.SupportedCultures = WebCultures.Supported;
            options.SupportedUICultures = WebCultures.Supported;
            options.FallBackToParentCultures = false;
            options.FallBackToParentUICultures = false;
            options.RequestCultureProviders =
            [
                new CookieRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
        });

        return services;
    }
}
