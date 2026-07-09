using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace BodyLife.Crm.Web.Operations;

public static class BodyLifeAuthorizationExtensions
{
    public static IServiceCollection AddBodyLifeAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, BodyLifeCorrectionAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                BodyLifeAuthorizationPolicies.OwnerOnly,
                policy => RequireBodyLifeSession(policy)
                    .RequireAssertion(context => BodyLifeAuthorizationClaims.IsOwner(context.User)));

            options.AddPolicy(
                BodyLifeAuthorizationPolicies.AdminOrOwner,
                policy => RequireBodyLifeSession(policy)
                    .RequireAssertion(context => BodyLifeAuthorizationClaims.IsAdminOrOwner(context.User)));

            options.AddPolicy(
                BodyLifeAuthorizationPolicies.CurrentOrOpenDayCorrection,
                policy => RequireBodyLifeSession(policy)
                    .AddRequirements(new BodyLifeCorrectionAuthorizationRequirement(CorrectionAuthorizationScope.CurrentOrOpenDay)));

            options.AddPolicy(
                BodyLifeAuthorizationPolicies.AfterDayCloseCorrection,
                policy => RequireBodyLifeSession(policy)
                    .AddRequirements(new BodyLifeCorrectionAuthorizationRequirement(CorrectionAuthorizationScope.AfterDayClose)));
        });

        return services;
    }

    private static AuthorizationPolicyBuilder RequireBodyLifeSession(AuthorizationPolicyBuilder policy)
    {
        return policy
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimTypes.NameIdentifier)
            .RequireClaim(BodyLifeClaimTypes.SessionId);
    }
}

public sealed record BodyLifeCorrectionAuthorizationContext(bool IsAfterDayClose);

internal sealed class BodyLifeCorrectionAuthorizationRequirement(
    CorrectionAuthorizationScope scope) : IAuthorizationRequirement
{
    public CorrectionAuthorizationScope Scope { get; } = scope;
}

internal enum CorrectionAuthorizationScope
{
    CurrentOrOpenDay,
    AfterDayClose,
}

internal sealed class BodyLifeCorrectionAuthorizationHandler
    : AuthorizationHandler<BodyLifeCorrectionAuthorizationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BodyLifeCorrectionAuthorizationRequirement requirement)
    {
        if (!BodyLifeAuthorizationClaims.HasBodyLifeSession(context.User))
        {
            return Task.CompletedTask;
        }

        var correctionContext = context.Resource as BodyLifeCorrectionAuthorizationContext;
        var isAfterDayClose = correctionContext?.IsAfterDayClose == true;

        if (requirement.Scope == CorrectionAuthorizationScope.CurrentOrOpenDay)
        {
            if (!isAfterDayClose && BodyLifeAuthorizationClaims.IsAdminOrOwner(context.User))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }

        if (BodyLifeAuthorizationClaims.IsOwner(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

internal static class BodyLifeAuthorizationClaims
{
    public static bool HasBodyLifeSession(ClaimsPrincipal user)
    {
        return user.Identity?.IsAuthenticated == true
            && HasNonEmptyClaim(user, ClaimTypes.NameIdentifier)
            && HasNonEmptyClaim(user, BodyLifeClaimTypes.SessionId);
    }

    public static bool IsAdminOrOwner(ClaimsPrincipal user)
    {
        return HasBodyLifeSession(user) && (IsOwner(user) || IsAdmin(user));
    }

    public static bool IsOwner(ClaimsPrincipal user)
    {
        return HasBodyLifeSession(user)
            && user.IsInRole(BodyLifeRoles.Owner)
            && HasClaimValue(user, BodyLifeClaimTypes.AccountType, BodyLifeAccountTypes.Owner);
    }

    private static bool IsAdmin(ClaimsPrincipal user)
    {
        return HasBodyLifeSession(user)
            && user.IsInRole(BodyLifeRoles.Admin)
            && (HasClaimValue(user, BodyLifeClaimTypes.AccountType, BodyLifeAccountTypes.NamedAdmin)
                || HasClaimValue(user, BodyLifeClaimTypes.AccountType, BodyLifeAccountTypes.SharedReceptionAdmin));
    }

    private static bool HasNonEmptyClaim(ClaimsPrincipal user, string claimType)
    {
        return user.Claims.Any(claim =>
            claim.Type == claimType
            && !string.IsNullOrWhiteSpace(claim.Value));
    }

    private static bool HasClaimValue(ClaimsPrincipal user, string claimType, string claimValue)
    {
        return user.Claims.Any(claim =>
            claim.Type == claimType
            && string.Equals(claim.Value, claimValue, StringComparison.Ordinal));
    }
}
