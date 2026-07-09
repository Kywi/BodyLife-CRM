using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace BodyLife.Crm.Web.Operations;

public interface IBodyLifeRequestContextResolver
{
    bool TryResolve([NotNullWhen(true)] out BodyLifeRequestContext? requestContext);

    BodyLifeRequestContext Require();

    CommandEnvelope CreateCommandEnvelope(
        EntryOrigin entryOrigin = EntryOrigin.Normal,
        DateTimeOffset? occurredAt = null,
        string? idempotencyKey = null,
        string? reason = null,
        string? comment = null);
}

public sealed record BodyLifeRequestContext(
    ActorContext Actor,
    RequestCorrelationId RequestCorrelationId);

public sealed class BodyLifeRequestContextResolver(
    IHttpContextAccessor httpContextAccessor) : IBodyLifeRequestContextResolver
{
    public bool TryResolve([NotNullWhen(true)] out BodyLifeRequestContext? requestContext)
    {
        var httpContext = httpContextAccessor.HttpContext;

        if (httpContext is null
            || !BodyLifeClaimsPrincipalReader.TryReadActorContext(httpContext.User, out var actorContext))
        {
            requestContext = null;
            return false;
        }

        requestContext = new BodyLifeRequestContext(
            actorContext,
            ResolveRequestCorrelationId(httpContext));

        return true;
    }

    public BodyLifeRequestContext Require()
    {
        return TryResolve(out var requestContext)
            ? requestContext
            : throw new UnauthorizedAccessException("Authenticated BodyLife actor/session claims are required.");
    }

    public CommandEnvelope CreateCommandEnvelope(
        EntryOrigin entryOrigin = EntryOrigin.Normal,
        DateTimeOffset? occurredAt = null,
        string? idempotencyKey = null,
        string? reason = null,
        string? comment = null)
    {
        var requestContext = Require();

        return new CommandEnvelope(
            requestContext.Actor,
            requestContext.RequestCorrelationId,
            entryOrigin,
            occurredAt,
            NormalizeOptionalValue(idempotencyKey),
            NormalizeOptionalValue(reason),
            NormalizeOptionalValue(comment));
    }

    private static RequestCorrelationId ResolveRequestCorrelationId(HttpContext httpContext)
    {
        var correlationId = RequestCorrelationMiddleware.GetCorrelationId(httpContext);

        if (!string.IsNullOrWhiteSpace(correlationId.Value))
        {
            return correlationId;
        }

        var fallback = new RequestCorrelationId(Guid.NewGuid().ToString("N"));
        httpContext.Items[RequestCorrelationMiddleware.ContextItemName] = fallback;

        return fallback;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed;
    }
}

public static class BodyLifeRequestContextServiceCollectionExtensions
{
    public static IServiceCollection AddBodyLifeRequestContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IBodyLifeRequestContextResolver, BodyLifeRequestContextResolver>();
        services.AddScoped<IQueryPermissionResolver, QueryPermissionResolver>();

        return services;
    }
}

internal static class BodyLifeClaimsPrincipalReader
{
    public static bool TryReadActorContext(
        ClaimsPrincipal user,
        [NotNullWhen(true)] out ActorContext? actorContext)
    {
        actorContext = null;

        if (user.Identity?.IsAuthenticated != true
            || !TryReadGuidClaim(user, ClaimTypes.NameIdentifier, out var accountId)
            || !TryReadGuidClaim(user, BodyLifeClaimTypes.SessionId, out var sessionId)
            || !TryReadRole(user, out var role)
            || !TryReadAccountKind(user, out var accountKind)
            || !IsConsistent(accountKind, role))
        {
            return false;
        }

        actorContext = new ActorContext(
            new AccountId(accountId),
            role,
            accountKind,
            new SessionId(sessionId),
            NormalizeDeviceLabel(ReadSingleClaimValue(user, BodyLifeClaimTypes.DeviceLabel)));

        return true;
    }

    private static bool TryReadGuidClaim(ClaimsPrincipal user, string claimType, out Guid value)
    {
        return Guid.TryParse(ReadSingleClaimValue(user, claimType), out value);
    }

    private static bool TryReadRole(ClaimsPrincipal user, out ActorRole role)
    {
        return TryReadSingleRecognizedValue(
            user,
            ClaimTypes.Role,
            static value => value switch
            {
                BodyLifeRoles.Owner => ActorRole.Owner,
                BodyLifeRoles.Admin => ActorRole.Admin,
                _ => null,
            },
            out role);
    }

    private static bool TryReadAccountKind(ClaimsPrincipal user, out AccountKind accountKind)
    {
        return TryReadSingleRecognizedValue(
            user,
            BodyLifeClaimTypes.AccountType,
            static value => value switch
            {
                BodyLifeAccountTypes.Owner => AccountKind.Owner,
                BodyLifeAccountTypes.NamedAdmin => AccountKind.NamedAdmin,
                BodyLifeAccountTypes.SharedReceptionAdmin => AccountKind.SharedReceptionAdmin,
                _ => null,
            },
            out accountKind);
    }

    private static bool TryReadSingleRecognizedValue<T>(
        ClaimsPrincipal user,
        string claimType,
        Func<string, T?> map,
        out T value)
        where T : struct
    {
        var values = user.FindAll(claimType)
            .Select(claim => claim.Value)
            .Where(claimValue => !string.IsNullOrWhiteSpace(claimValue))
            .Select(claimValue => map(claimValue.Trim()))
            .OfType<T>()
            .Distinct()
            .ToArray();

        if (values.Length == 1)
        {
            value = values[0];
            return true;
        }

        value = default;
        return false;
    }

    private static bool IsConsistent(AccountKind accountKind, ActorRole role)
    {
        return accountKind switch
        {
            AccountKind.Owner => role == ActorRole.Owner,
            AccountKind.NamedAdmin or AccountKind.SharedReceptionAdmin => role == ActorRole.Admin,
            _ => false,
        };
    }

    private static string? ReadSingleClaimValue(ClaimsPrincipal user, string claimType)
    {
        var values = user.FindAll(claimType)
            .Select(claim => claim.Value?.Trim())
            .Where(claimValue => !string.IsNullOrWhiteSpace(claimValue))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return values.Length == 1
            ? values[0]
            : null;
    }

    private static string? NormalizeDeviceLabel(string? deviceLabel)
    {
        var trimmedDeviceLabel = deviceLabel?.Trim();

        return string.IsNullOrWhiteSpace(trimmedDeviceLabel)
            ? null
            : trimmedDeviceLabel;
    }
}
