using System.Security.Claims;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages;

public sealed class LoginModel(AccountLoginService loginService) : PageModel
{
    [BindProperty]
    public string? LoginName { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? DeviceLabel { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var loginResult = await loginService.LoginAsync(LoginName, Password, DeviceLabel, cancellationToken);

        if (loginResult is not { Status: AccountLoginStatus.Success, Session: not null })
        {
            ModelState.AddModelError(string.Empty, "Login failed.");
            return Page();
        }

        var session = loginResult.Session;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.AccountId.ToString()),
            new(ClaimTypes.Name, session.DisplayName),
            new(ClaimTypes.Role, session.Role),
            new(BodyLifeClaimTypes.AccountType, session.AccountType),
            new(BodyLifeClaimTypes.SessionId, session.SessionId.ToString()),
        };

        if (!string.IsNullOrWhiteSpace(session.DeviceLabel))
        {
            claims.Add(new Claim(BodyLifeClaimTypes.DeviceLabel, session.DeviceLabel));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
            });

        return LocalRedirect(ResolveReturnUrl());
    }

    private string ResolveReturnUrl()
    {
        return !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? ReturnUrl
            : "/";
    }
}
