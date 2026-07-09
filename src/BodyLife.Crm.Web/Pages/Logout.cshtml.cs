using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages;

public sealed class LogoutModel(
    AccountLoginService loginService,
    IBodyLifeAuthTechnicalLogger authTechnicalLogger) : PageModel
{
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var sessionId = User.FindFirst(BodyLifeClaimTypes.SessionId)?.Value;
        Guid? parsedSessionId = null;
        var sessionEnded = false;

        if (Guid.TryParse(sessionId, out var resolvedSessionId))
        {
            parsedSessionId = resolvedSessionId;
            sessionEnded = await loginService.LogoutAsync(resolvedSessionId, cancellationToken);
        }

        authTechnicalLogger.Logout(HttpContext, parsedSessionId, sessionEnded);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToPage("/Login");
    }
}
