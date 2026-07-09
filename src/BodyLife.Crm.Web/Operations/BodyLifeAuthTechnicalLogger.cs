using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace BodyLife.Crm.Web.Operations;

public interface IBodyLifeAuthTechnicalLogger
{
    void LoginFailed(HttpContext httpContext, string? loginName, AccountLoginStatus status);

    void LoginSucceeded(HttpContext httpContext, AccountSessionSnapshot session);

    void Logout(HttpContext httpContext, Guid? sessionId, bool sessionEnded);

    void PermissionDenied(
        HttpContext httpContext,
        IReadOnlyCollection<string> requiredPolicies,
        string failureReason);
}

public sealed class BodyLifeAuthTechnicalLogger(
    ILogger<BodyLifeAuthTechnicalLogger> logger) : IBodyLifeAuthTechnicalLogger
{
    private const string Unknown = "unknown";

    public void LoginFailed(HttpContext httpContext, string? loginName, AccountLoginStatus status)
    {
        logger.LogWarning(
            "BodyLife auth technical event event_name={event_name} route_or_command={route_or_command} method={method} request_correlation_id={request_correlation_id} outcome={outcome} auth_result={auth_result} login_name_present={login_name_present}",
            "auth.login_failed",
            ResolveRouteOrCommand(httpContext),
            httpContext.Request.Method,
            ResolveRequestCorrelationId(httpContext),
            "authentication_failed",
            status.ToString(),
            BodyLifeSensitiveLogValues.HasPersonalValue(loginName));
    }

    public void LoginSucceeded(HttpContext httpContext, AccountSessionSnapshot session)
    {
        logger.LogInformation(
            "BodyLife auth technical event event_name={event_name} route_or_command={route_or_command} method={method} request_correlation_id={request_correlation_id} outcome={outcome} actor_account_id={actor_account_id} actor_role={actor_role} account_type={account_type} session_id={session_id} device_label_present={device_label_present}",
            "auth.login_succeeded",
            ResolveRouteOrCommand(httpContext),
            httpContext.Request.Method,
            ResolveRequestCorrelationId(httpContext),
            "success",
            session.AccountId,
            session.Role,
            session.AccountType,
            session.SessionId,
            BodyLifeSensitiveLogValues.HasPersonalValue(session.DeviceLabel));
    }

    public void Logout(HttpContext httpContext, Guid? sessionId, bool sessionEnded)
    {
        var actorContext = ResolveActorContext(httpContext);

        logger.LogInformation(
            "BodyLife auth technical event event_name={event_name} route_or_command={route_or_command} method={method} request_correlation_id={request_correlation_id} outcome={outcome} actor_account_id={actor_account_id} actor_role={actor_role} account_type={account_type} session_id={session_id} session_ended={session_ended}",
            "auth.logout",
            ResolveRouteOrCommand(httpContext),
            httpContext.Request.Method,
            ResolveRequestCorrelationId(httpContext),
            "success",
            actorContext.AccountId,
            actorContext.ActorRole,
            actorContext.AccountType,
            sessionId,
            sessionEnded);
    }

    public void PermissionDenied(
        HttpContext httpContext,
        IReadOnlyCollection<string> requiredPolicies,
        string failureReason)
    {
        var actorContext = ResolveActorContext(httpContext);

        logger.LogWarning(
            "BodyLife auth technical event event_name={event_name} route_or_command={route_or_command} method={method} request_correlation_id={request_correlation_id} outcome={outcome} error_class={error_class} actor_context_valid={actor_context_valid} actor_account_id={actor_account_id} actor_role={actor_role} account_type={account_type} session_id={session_id} required_policies={required_policies} failure_reason={failure_reason}",
            "authorization.permission_denied",
            ResolveRouteOrCommand(httpContext),
            httpContext.Request.Method,
            ResolveRequestCorrelationId(httpContext),
            "permission_denied",
            "authorization_forbidden",
            actorContext.IsValid,
            actorContext.AccountId,
            actorContext.ActorRole,
            actorContext.AccountType,
            actorContext.SessionId,
            FormatRequiredPolicies(requiredPolicies),
            failureReason);
    }

    private static ActorLogContext ResolveActorContext(HttpContext httpContext)
    {
        return BodyLifeClaimsPrincipalReader.TryReadActorContext(httpContext.User, out var actorContext)
            ? new ActorLogContext(
                true,
                actorContext.AccountId.Value,
                FormatActorRole(actorContext.Role),
                FormatAccountKind(actorContext.AccountKind),
                actorContext.SessionId.Value)
            : ActorLogContext.Empty;
    }

    private static string ResolveRequestCorrelationId(HttpContext httpContext)
    {
        var correlationId = RequestCorrelationMiddleware.GetCorrelationId(httpContext);

        return string.IsNullOrWhiteSpace(correlationId.Value)
            ? Unknown
            : correlationId.Value;
    }

    private static string ResolveRouteOrCommand(HttpContext httpContext)
    {
        var endpointName = httpContext.GetEndpoint()?.DisplayName;

        if (!string.IsNullOrWhiteSpace(endpointName))
        {
            return endpointName;
        }

        return httpContext.Request.Path.HasValue
            ? httpContext.Request.Path.Value!
            : Unknown;
    }

    private static string FormatRequiredPolicies(IReadOnlyCollection<string> requiredPolicies)
    {
        return requiredPolicies.Count == 0
            ? Unknown
            : string.Join(',', requiredPolicies.Select(NormalizeLogToken));
    }

    private static string NormalizeLogToken(string? value)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Unknown;
        }

        return trimmed.Length <= 120
            ? trimmed
            : trimmed[..120];
    }

    private static string FormatActorRole(ActorRole role)
    {
        return role switch
        {
            ActorRole.Owner => BodyLifeRoles.Owner,
            ActorRole.Admin => BodyLifeRoles.Admin,
            _ => Unknown,
        };
    }

    private static string FormatAccountKind(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.Owner => BodyLifeAccountTypes.Owner,
            AccountKind.NamedAdmin => BodyLifeAccountTypes.NamedAdmin,
            AccountKind.SharedReceptionAdmin => BodyLifeAccountTypes.SharedReceptionAdmin,
            _ => Unknown,
        };
    }

    private sealed record ActorLogContext(
        bool IsValid,
        Guid? AccountId,
        string ActorRole,
        string AccountType,
        Guid? SessionId)
    {
        public static ActorLogContext Empty { get; } = new(false, null, Unknown, Unknown, null);
    }
}

public static class BodyLifeSensitiveLogValues
{
    public const string Redacted = "[redacted]";
    public const string NotPresent = "not_present";

    public static bool HasPersonalValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    public static string RedactSecret(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? NotPresent
            : Redacted;
    }
}

public sealed class BodyLifeAuthorizationMiddlewareResultHandler(
    IBodyLifeAuthTechnicalLogger authTechnicalLogger) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            authTechnicalLogger.PermissionDenied(
                context,
                ResolveRequiredPolicies(context),
                ResolveFailureReason(authorizeResult));
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    private static IReadOnlyCollection<string> ResolveRequiredPolicies(HttpContext context)
    {
        var policyNames = context.GetEndpoint()
            ?.Metadata
            .OfType<IAuthorizeData>()
            .Select(metadata => metadata.Policy?.Trim())
            .Where(policyName => !string.IsNullOrWhiteSpace(policyName))
            .Select(policyName => policyName!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return policyNames is { Length: > 0 }
            ? policyNames
            : Array.Empty<string>();
    }

    private static string ResolveFailureReason(PolicyAuthorizationResult authorizeResult)
    {
        var failure = authorizeResult.AuthorizationFailure;

        if (failure?.FailCalled == true)
        {
            return "explicit_failure";
        }

        return failure?.FailedRequirements.Any() == true
            ? "failed_requirements"
            : "forbidden";
    }
}
