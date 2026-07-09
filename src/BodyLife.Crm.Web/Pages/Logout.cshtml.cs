using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages;

public sealed class LogoutModel(AccountLoginService loginService) : PageModel
{
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var sessionId = User.FindFirst(BodyLifeClaimTypes.SessionId)?.Value;

        if (Guid.TryParse(sessionId, out var parsedSessionId))
        {
            await loginService.LogoutAsync(parsedSessionId, cancellationToken);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToPage("/Login");
    }
}
