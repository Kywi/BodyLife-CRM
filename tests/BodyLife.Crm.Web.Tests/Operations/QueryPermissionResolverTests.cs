using System.Security.Claims;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BodyLife.Crm.Web.Tests.Operations;

public sealed class QueryPermissionResolverTests
{
    [Fact]
    public async Task ResolveAsyncAllowsOwnerForOwnerAndAdminActions()
    {
        await using var provider = BuildServiceProvider(Principal(
            BodyLifeAccountTypes.Owner,
            BodyLifeRoles.Owner));
        var resolver = provider.GetRequiredService<IQueryPermissionResolver>();

        var permissions = await resolver.ResolveAsync(
        [
            new QueryPermissionRequest("edit_membership_type", BodyLifeAuthorizationPolicies.OwnerOnly),
            new QueryPermissionRequest("search_clients", BodyLifeAuthorizationPolicies.AdminOrOwner),
        ]);

        Assert.True(permissions.IsAllowed("edit_membership_type"));
        Assert.True(permissions.IsAllowed("search_clients"));
    }

    [Theory]
    [InlineData(BodyLifeAccountTypes.NamedAdmin)]
    [InlineData(BodyLifeAccountTypes.SharedReceptionAdmin)]
    public async Task ResolveAsyncDeniesOwnerOnlyForAdminAccounts(string accountType)
    {
        await using var provider = BuildServiceProvider(Principal(accountType, BodyLifeRoles.Admin));
        var resolver = provider.GetRequiredService<IQueryPermissionResolver>();

        var permissions = await resolver.ResolveAsync(
        [
            new QueryPermissionRequest("edit_membership_type", BodyLifeAuthorizationPolicies.OwnerOnly),
            new QueryPermissionRequest("search_clients", BodyLifeAuthorizationPolicies.AdminOrOwner),
        ]);

        Assert.False(permissions.IsAllowed("edit_membership_type"));
        Assert.True(permissions.IsAllowed("search_clients"));
        Assert.True(permissions.TryGet("edit_membership_type", out var deniedPermission));
        Assert.Equal(QueryPermissionDeniedReasonCodes.PermissionDenied, deniedPermission.DeniedReasonCode);
        Assert.Equal(BodyLifeAuthorizationPolicies.OwnerOnly, deniedPermission.RequiredPolicy);
    }

    [Fact]
    public async Task ResolveAsyncUsesCorrectionPolicyResources()
    {
        await using var provider = BuildServiceProvider(Principal(
            BodyLifeAccountTypes.SharedReceptionAdmin,
            BodyLifeRoles.Admin));
        var resolver = provider.GetRequiredService<IQueryPermissionResolver>();

        var permissions = await resolver.ResolveAsync(
        [
            new QueryPermissionRequest(
                "cancel_today_payment",
                BodyLifeAuthorizationPolicies.CurrentOrOpenDayCorrection,
                new BodyLifeCorrectionAuthorizationContext(IsAfterDayClose: false)),
            new QueryPermissionRequest(
                "cancel_closed_day_payment",
                BodyLifeAuthorizationPolicies.AfterDayCloseCorrection,
                new BodyLifeCorrectionAuthorizationContext(IsAfterDayClose: true)),
        ]);

        Assert.True(permissions.IsAllowed("cancel_today_payment"));
        Assert.False(permissions.IsAllowed("cancel_closed_day_payment"));
    }

    [Fact]
    public async Task ResolveAsyncReturnsNotAuthenticatedWhenNoActorExists()
    {
        await using var provider = BuildServiceProvider(new ClaimsPrincipal(new ClaimsIdentity()));
        var resolver = provider.GetRequiredService<IQueryPermissionResolver>();

        var permissions = await resolver.ResolveAsync(
        [
            new QueryPermissionRequest("search_clients", BodyLifeAuthorizationPolicies.AdminOrOwner),
        ]);

        Assert.False(permissions.IsAllowed("search_clients"));
        Assert.True(permissions.TryGet("search_clients", out var deniedPermission));
        Assert.Equal(QueryPermissionDeniedReasonCodes.NotAuthenticated, deniedPermission.DeniedReasonCode);
    }

    private static ServiceProvider BuildServiceProvider(ClaimsPrincipal principal)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBodyLifeAuthorizationPolicies();
        services.AddBodyLifeRequestContext();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = principal,
        };

        return provider;
    }

    private static ClaimsPrincipal Principal(string accountType, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, "Test Account"),
            new(ClaimTypes.Role, role),
            new(BodyLifeClaimTypes.AccountType, accountType),
            new(BodyLifeClaimTypes.SessionId, Guid.NewGuid().ToString()),
        };

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "BodyLife.Tests"));
    }
}
