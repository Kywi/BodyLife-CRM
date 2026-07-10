using System.Security.Claims;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BodyLife.Crm.Web.Tests.Operations;

public sealed class BodyLifeAuthTechnicalLoggerTests
{
    [Fact]
    public void LoginFailureLogOmitsCredentialsLoginNameAndQueryToken()
    {
        var capture = new CapturingLogger<BodyLifeAuthTechnicalLogger>();
        var authLogger = new BodyLifeAuthTechnicalLogger(capture);
        var httpContext = HttpContext();
        httpContext.Request.Path = "/Login";
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.QueryString = new QueryString("?token=raw-token-value");
        httpContext.Items[RequestCorrelationMiddleware.ContextItemName] = new RequestCorrelationId("corr-auth");

        authLogger.LoginFailed(httpContext, "owner@example.test", AccountLoginStatus.InvalidCredentials);

        var entry = Assert.Single(capture.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Equal("auth.login_failed", entry.Property("event_name"));
        Assert.Equal("corr-auth", entry.Property("request_correlation_id"));
        Assert.Equal("authentication_failed", entry.Property("outcome"));
        Assert.Equal(true, entry.Property("login_name_present"));
        AssertSensitiveTextAbsent(
            entry,
            "owner@example.test",
            "raw-token-value",
            "password",
            "token=raw-token-value");
    }

    [Fact]
    public void LoginSuccessLogOmitsDisplayNameAndDeviceLabel()
    {
        var capture = new CapturingLogger<BodyLifeAuthTechnicalLogger>();
        var authLogger = new BodyLifeAuthTechnicalLogger(capture);
        var httpContext = HttpContext();
        httpContext.Request.Path = "/Login";
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Items[RequestCorrelationMiddleware.ContextItemName] = new RequestCorrelationId("corr-success");
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        authLogger.LoginSucceeded(
            httpContext,
            new AccountSessionSnapshot(
                accountId,
                sessionId,
                "Owner Private Name",
                BodyLifeAccountTypes.Owner,
                BodyLifeRoles.Owner,
                "Front desk tablet",
                new DateTimeOffset(2026, 7, 10, 21, 0, 0, TimeSpan.Zero)));

        var entry = Assert.Single(capture.Entries);
        Assert.Equal(LogLevel.Information, entry.LogLevel);
        Assert.Equal("auth.login_succeeded", entry.Property("event_name"));
        Assert.Equal(accountId, entry.Property("actor_account_id"));
        Assert.Equal(sessionId, entry.Property("session_id"));
        Assert.Equal(BodyLifeRoles.Owner, entry.Property("actor_role"));
        Assert.Equal(BodyLifeAccountTypes.Owner, entry.Property("account_type"));
        Assert.Equal(true, entry.Property("device_label_present"));
        AssertSensitiveTextAbsent(entry, "Owner Private Name", "Front desk tablet");
    }

    [Fact]
    public void SessionRejectedLogIncludesStableReasonAndOmitsPersonalClaims()
    {
        var capture = new CapturingLogger<BodyLifeAuthTechnicalLogger>();
        var authLogger = new BodyLifeAuthTechnicalLogger(capture);
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var httpContext = HttpContext(Principal(
            accountId,
            sessionId,
            "Named Admin Private",
            "Back office browser"));
        httpContext.Request.Path = "/";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Items[RequestCorrelationMiddleware.ContextItemName] = new RequestCorrelationId("corr-expired");

        authLogger.SessionRejected(httpContext, AccountSessionValidationStatus.Expired);

        var entry = Assert.Single(capture.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Equal("auth.session_rejected", entry.Property("event_name"));
        Assert.Equal("authentication_failed", entry.Property("outcome"));
        Assert.Equal("inactive_session", entry.Property("error_class"));
        Assert.Equal("expired", entry.Property("session_validation_result"));
        Assert.Equal(accountId, entry.Property("actor_account_id"));
        Assert.Equal(sessionId, entry.Property("session_id"));
        AssertSensitiveTextAbsent(entry, "Named Admin Private", "Back office browser");
    }

    [Fact]
    public void PermissionDeniedLogIncludesActorPolicyAndNoPersonalClaimValues()
    {
        var capture = new CapturingLogger<BodyLifeAuthTechnicalLogger>();
        var authLogger = new BodyLifeAuthTechnicalLogger(capture);
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var httpContext = HttpContext(Principal(
            accountId,
            sessionId,
            "Named Admin Private",
            "Back office browser"));
        httpContext.Request.Path = "/owner/settings";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Items[RequestCorrelationMiddleware.ContextItemName] = new RequestCorrelationId("corr-denied");
        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AuthorizeAttribute
            {
                Policy = BodyLifeAuthorizationPolicies.OwnerOnly,
            }),
            "Owner settings"));

        authLogger.PermissionDenied(
            httpContext,
            [BodyLifeAuthorizationPolicies.OwnerOnly],
            "forbidden");

        var entry = Assert.Single(capture.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Equal("authorization.permission_denied", entry.Property("event_name"));
        Assert.Equal("permission_denied", entry.Property("outcome"));
        Assert.Equal(true, entry.Property("actor_context_valid"));
        Assert.Equal(accountId, entry.Property("actor_account_id"));
        Assert.Equal(sessionId, entry.Property("session_id"));
        Assert.Equal(BodyLifeRoles.Admin, entry.Property("actor_role"));
        Assert.Equal(BodyLifeAccountTypes.NamedAdmin, entry.Property("account_type"));
        Assert.Equal(BodyLifeAuthorizationPolicies.OwnerOnly, entry.Property("required_policies"));
        AssertSensitiveTextAbsent(entry, "Named Admin Private", "Back office browser");
    }

    [Fact]
    public void SensitiveLogValueHelperRedactsSecrets()
    {
        Assert.Equal(BodyLifeSensitiveLogValues.Redacted, BodyLifeSensitiveLogValues.RedactSecret("secret-password"));
        Assert.Equal(BodyLifeSensitiveLogValues.NotPresent, BodyLifeSensitiveLogValues.RedactSecret("  "));
    }

    private static DefaultHttpContext HttpContext(ClaimsPrincipal? principal = null)
    {
        return new DefaultHttpContext
        {
            User = principal ?? new ClaimsPrincipal(new ClaimsIdentity()),
        };
    }

    private static ClaimsPrincipal Principal(
        Guid accountId,
        Guid sessionId,
        string displayName,
        string deviceLabel)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, accountId.ToString()),
            new Claim(ClaimTypes.Name, displayName),
            new Claim(ClaimTypes.Role, BodyLifeRoles.Admin),
            new Claim(BodyLifeClaimTypes.AccountType, BodyLifeAccountTypes.NamedAdmin),
            new Claim(BodyLifeClaimTypes.SessionId, sessionId.ToString()),
            new Claim(BodyLifeClaimTypes.DeviceLabel, deviceLabel),
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "BodyLife.Tests"));
    }

    private static void AssertSensitiveTextAbsent(LogEntry entry, params string[] sensitiveValues)
    {
        var logText = entry.AsText();

        foreach (var sensitiveValue in sensitiveValues)
        {
            Assert.DoesNotContain(sensitiveValue, logText, StringComparison.Ordinal);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> keyValuePairs
                ? keyValuePairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);

            Entries.Add(new LogEntry(logLevel, formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(
        LogLevel LogLevel,
        string Message,
        IReadOnlyDictionary<string, object?> Properties)
    {
        public object? Property(string name)
        {
            return Properties.TryGetValue(name, out var value)
                ? value
                : null;
        }

        public string AsText()
        {
            return string.Join(
                ' ',
                Properties
                    .Select(property => $"{property.Key}={property.Value}")
                    .Prepend(Message));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
