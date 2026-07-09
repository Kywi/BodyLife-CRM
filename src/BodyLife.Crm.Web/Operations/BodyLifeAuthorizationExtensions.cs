using System.Security.Claims;
using BodyLife.Crm.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;

namespace BodyLife.Crm.Web.Operations;

public static class BodyLifeAuthorizationExtensions
{
    public static IServiceCollection AddBodyLifeAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddSingleton<IBodyLifeAuthTechnicalLogger, BodyLifeAuthTechnicalLogger>();
        services.AddSingleton<IAuthorizationHandler, BodyLifeCorrectionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, BodyLifeAuthorizationMiddlewareResultHandler>();

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
        return BodyLifeClaimsPrincipalReader.TryReadActorContext(user, out _);
    }

    public static bool IsAdminOrOwner(ClaimsPrincipal user)
    {
        return BodyLifeClaimsPrincipalReader.TryReadActorContext(user, out var actorContext)
            && actorContext.Role is ActorRole.Owner or ActorRole.Admin;
    }

    public static bool IsOwner(ClaimsPrincipal user)
    {
        return BodyLifeClaimsPrincipalReader.TryReadActorContext(user, out var actorContext)
            && actorContext is { Role: ActorRole.Owner, AccountKind: AccountKind.Owner };
    }
}
