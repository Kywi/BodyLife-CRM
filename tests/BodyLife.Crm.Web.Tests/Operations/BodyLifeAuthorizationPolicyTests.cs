using System.Security.Claims;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace BodyLife.Crm.Web.Tests.Operations;

public sealed class BodyLifeAuthorizationPolicyTests
{
    [Theory]
    [InlineData(BodyLifeAccountTypes.Owner, BodyLifeRoles.Owner)]
    [InlineData(BodyLifeAccountTypes.NamedAdmin, BodyLifeRoles.Admin)]
    [InlineData(BodyLifeAccountTypes.SharedReceptionAdmin, BodyLifeRoles.Admin)]
    public async Task AdminOrOwnerAllowsOwnerNamedAdminAndSharedReceptionAdmin(
        string accountType,
        string role)
    {
        var result = await AuthorizeAsync(
            Principal(accountType, role),
            BodyLifeAuthorizationPolicies.AdminOrOwner);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(BodyLifeAccountTypes.Owner, BodyLifeRoles.Owner, true)]
    [InlineData(BodyLifeAccountTypes.NamedAdmin, BodyLifeRoles.Admin, false)]
    [InlineData(BodyLifeAccountTypes.SharedReceptionAdmin, BodyLifeRoles.Admin, false)]
    [InlineData(BodyLifeAccountTypes.Owner, BodyLifeRoles.Admin, false)]
    [InlineData(BodyLifeAccountTypes.NamedAdmin, BodyLifeRoles.Owner, false)]
    public async Task OwnerOnlyAllowsOnlyConsistentOwnerClaims(
        string accountType,
        string role,
        bool expected)
    {
        var result = await AuthorizeAsync(
            Principal(accountType, role),
            BodyLifeAuthorizationPolicies.OwnerOnly);

        Assert.Equal(expected, result.Succeeded);
    }

    [Theory]
    [InlineData(BodyLifeAccountTypes.Owner, BodyLifeRoles.Owner)]
    [InlineData(BodyLifeAccountTypes.NamedAdmin, BodyLifeRoles.Admin)]
    [InlineData(BodyLifeAccountTypes.SharedReceptionAdmin, BodyLifeRoles.Admin)]
    public async Task CurrentOrOpenDayCorrectionAllowsAdminOrOwnerAccounts(
        string accountType,
        string role)
    {
        var result = await AuthorizeAsync(
            Principal(accountType, role),
            BodyLifeAuthorizationPolicies.CurrentOrOpenDayCorrection,
            new BodyLifeCorrectionAuthorizationContext(IsAfterDayClose: false));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(BodyLifeAccountTypes.Owner, BodyLifeRoles.Owner, true)]
    [InlineData(BodyLifeAccountTypes.NamedAdmin, BodyLifeRoles.Admin, false)]
    [InlineData(BodyLifeAccountTypes.SharedReceptionAdmin, BodyLifeRoles.Admin, false)]
    public async Task AfterDayCloseCorrectionAllowsOnlyOwner(
        string accountType,
        string role,
        bool expected)
    {
        var result = await AuthorizeAsync(
            Principal(accountType, role),
            BodyLifeAuthorizationPolicies.AfterDayCloseCorrection,
            new BodyLifeCorrectionAuthorizationContext(IsAfterDayClose: true));

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task CurrentOrOpenDayCorrectionRejectsAfterDayCloseContext()
    {
        var result = await AuthorizeAsync(
            Principal(BodyLifeAccountTypes.Owner, BodyLifeRoles.Owner),
            BodyLifeAuthorizationPolicies.CurrentOrOpenDayCorrection,
            new BodyLifeCorrectionAuthorizationContext(IsAfterDayClose: true));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PoliciesRequireAuthenticatedSessionClaims()
    {
        var result = await AuthorizeAsync(
            PrincipalWithoutSession(BodyLifeAccountTypes.Owner, BodyLifeRoles.Owner),
            BodyLifeAuthorizationPolicies.OwnerOnly);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PoliciesRequireParseableActorAndSessionClaims()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "not-a-guid"),
            new(ClaimTypes.Name, "Test Account"),
            new(ClaimTypes.Role, BodyLifeRoles.Owner),
            new(BodyLifeClaimTypes.AccountType, BodyLifeAccountTypes.Owner),
            new(BodyLifeClaimTypes.SessionId, Guid.NewGuid().ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "BodyLife.Tests"));

        var result = await AuthorizeAsync(principal, BodyLifeAuthorizationPolicies.OwnerOnly);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PoliciesRejectUnauthenticatedUsers()
    {
        var result = await AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            BodyLifeAuthorizationPolicies.AdminOrOwner);

        Assert.False(result.Succeeded);
    }

    private static async Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal principal,
        string policyName,
        object? resource = null)
    {
        await using var provider = BuildServiceProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();

        return await authorizationService.AuthorizeAsync(principal, resource, policyName);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBodyLifeAuthorizationPolicies();

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Principal(string accountType, string role)
    {
        return Principal(accountType, role, includeSession: true);
    }

    private static ClaimsPrincipal PrincipalWithoutSession(string accountType, string role)
    {
        return Principal(accountType, role, includeSession: false);
    }

    private static ClaimsPrincipal Principal(string accountType, string role, bool includeSession)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, "Test Account"),
            new(ClaimTypes.Role, role),
            new(BodyLifeClaimTypes.AccountType, accountType),
        };

        if (includeSession)
        {
            claims.Add(new Claim(BodyLifeClaimTypes.SessionId, Guid.NewGuid().ToString()));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: "BodyLife.Tests");

        return new ClaimsPrincipal(identity);
    }
}
