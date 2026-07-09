using System.Security.Claims;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Http;

namespace BodyLife.Crm.Web.Tests.Operations;

public sealed class BodyLifeRequestContextResolverTests
{
    [Fact]
    public void CreateCommandEnvelopeUsesActorSessionAndCorrelationFromRequest()
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 7, 9, 12, 30, 0, TimeSpan.Zero);
        var httpContext = HttpContext(
            Principal(
                accountId,
                sessionId,
                BodyLifeAccountTypes.SharedReceptionAdmin,
                BodyLifeRoles.Admin,
                "  Front desk tablet  "));
        httpContext.Items[RequestCorrelationMiddleware.ContextItemName] = new RequestCorrelationId("corr-123");
        var resolver = Resolver(httpContext);

        var envelope = resolver.CreateCommandEnvelope(
            EntryOrigin.PaperFallback,
            occurredAt,
            " visit-key ",
            " paper batch ",
            " front desk note ");

        Assert.Equal(new AccountId(accountId), envelope.Actor.AccountId);
        Assert.Equal(ActorRole.Admin, envelope.Actor.Role);
        Assert.Equal(AccountKind.SharedReceptionAdmin, envelope.Actor.AccountKind);
        Assert.Equal(new SessionId(sessionId), envelope.Actor.SessionId);
        Assert.Equal("Front desk tablet", envelope.Actor.DeviceLabel);
        Assert.Equal("corr-123", envelope.RequestCorrelationId.Value);
        Assert.Equal(EntryOrigin.PaperFallback, envelope.EntryOrigin);
        Assert.Equal(occurredAt, envelope.OccurredAt);
        Assert.Equal("visit-key", envelope.IdempotencyKey);
        Assert.Equal("paper batch", envelope.Reason);
        Assert.Equal("front desk note", envelope.Comment);
    }

    [Fact]
    public void TryResolveReturnsActorAndFallbackCorrelationWhenMiddlewareHasNotRun()
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var resolver = Resolver(HttpContext(Principal(
            accountId,
            sessionId,
            BodyLifeAccountTypes.Owner,
            BodyLifeRoles.Owner)));

        var resolved = resolver.TryResolve(out var requestContext);

        Assert.True(resolved);
        Assert.NotNull(requestContext);
        Assert.Equal(new AccountId(accountId), requestContext.Actor.AccountId);
        Assert.Equal(ActorRole.Owner, requestContext.Actor.Role);
        Assert.Equal(AccountKind.Owner, requestContext.Actor.AccountKind);
        Assert.Equal(new SessionId(sessionId), requestContext.Actor.SessionId);
        Assert.Matches("^[a-f0-9]{32}$", requestContext.RequestCorrelationId.Value);
    }

    [Theory]
    [InlineData(BodyLifeAccountTypes.Owner, BodyLifeRoles.Admin)]
    [InlineData(BodyLifeAccountTypes.NamedAdmin, BodyLifeRoles.Owner)]
    [InlineData(BodyLifeAccountTypes.SharedReceptionAdmin, BodyLifeRoles.Owner)]
    public void TryResolveRejectsInconsistentAccountTypeAndRole(string accountType, string role)
    {
        var resolver = Resolver(HttpContext(Principal(
            Guid.NewGuid(),
            Guid.NewGuid(),
            accountType,
            role)));

        var resolved = resolver.TryResolve(out var requestContext);

        Assert.False(resolved);
        Assert.Null(requestContext);
    }

    [Fact]
    public void TryResolveRejectsInvalidSessionIdentifier()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, "Test Account"),
            new(ClaimTypes.Role, BodyLifeRoles.Admin),
            new(BodyLifeClaimTypes.AccountType, BodyLifeAccountTypes.NamedAdmin),
            new(BodyLifeClaimTypes.SessionId, "not-a-guid"),
        };
        var resolver = Resolver(HttpContext(new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "BodyLife.Tests"))));

        var resolved = resolver.TryResolve(out var requestContext);

        Assert.False(resolved);
        Assert.Null(requestContext);
    }

    [Fact]
    public void RequireThrowsWhenRequestHasNoAuthenticatedActor()
    {
        var resolver = Resolver(HttpContext(new ClaimsPrincipal(new ClaimsIdentity())));

        var exception = Assert.Throws<UnauthorizedAccessException>(() => resolver.Require());

        Assert.Contains("actor/session", exception.Message, StringComparison.Ordinal);
    }

    private static BodyLifeRequestContextResolver Resolver(HttpContext httpContext)
    {
        return new BodyLifeRequestContextResolver(new HttpContextAccessor
        {
            HttpContext = httpContext,
        });
    }

    private static DefaultHttpContext HttpContext(ClaimsPrincipal principal)
    {
        return new DefaultHttpContext
        {
            User = principal,
        };
    }

    private static ClaimsPrincipal Principal(
        Guid accountId,
        Guid sessionId,
        string accountType,
        string role,
        string? deviceLabel = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, accountId.ToString()),
            new(ClaimTypes.Name, "Test Account"),
            new(ClaimTypes.Role, role),
            new(BodyLifeClaimTypes.AccountType, accountType),
            new(BodyLifeClaimTypes.SessionId, sessionId.ToString()),
        };

        if (!string.IsNullOrWhiteSpace(deviceLabel))
        {
            claims.Add(new Claim(BodyLifeClaimTypes.DeviceLabel, deviceLabel));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "BodyLife.Tests"));
    }
}
