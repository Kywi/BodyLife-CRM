using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BodyLife.Crm.Web.Operations;

public sealed class BodyLifeCookieAuthenticationEvents(
    AccountSessionValidationService sessionValidationService,
    IBodyLifeAuthTechnicalLogger authTechnicalLogger) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal is null
            || !BodyLifeClaimsPrincipalReader.TryReadActorContext(context.Principal, out var actor))
        {
            await RejectAsync(context, AccountSessionValidationStatus.InvalidClaims);
            return;
        }

        var validationStatus = await sessionValidationService.ValidateAsync(
            actor,
            context.HttpContext.RequestAborted);

        if (validationStatus != AccountSessionValidationStatus.Active)
        {
            await RejectAsync(context, validationStatus);
        }
    }

    private async Task RejectAsync(
        CookieValidatePrincipalContext context,
        AccountSessionValidationStatus validationStatus)
    {
        authTechnicalLogger.SessionRejected(context.HttpContext, validationStatus);
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
