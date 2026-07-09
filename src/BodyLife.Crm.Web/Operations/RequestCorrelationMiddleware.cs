using System.Diagnostics.CodeAnalysis;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Web.Operations;

public sealed class RequestCorrelationMiddleware(
    RequestDelegate next,
    IHostEnvironment environment,
    ILogger<RequestCorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Request-Correlation-Id";
    public const string ContextItemName = "BodyLife.RequestCorrelationId";

    private const int MaxHeaderLength = 128;
    private const string AlternateHeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.Items[ContextItemName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId.Value;

        using var scope = logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["request_correlation_id"] = correlationId.Value,
                ["environment"] = environment.EnvironmentName,
            });

        await next(context);
    }

    public static RequestCorrelationId GetCorrelationId(HttpContext context)
    {
        return context.Items.TryGetValue(ContextItemName, out var value) && value is RequestCorrelationId correlationId
            ? correlationId
            : new RequestCorrelationId(string.Empty);
    }

    private static RequestCorrelationId ResolveCorrelationId(HttpContext context)
    {
        var candidate = ReadHeader(context, HeaderName) ?? ReadHeader(context, AlternateHeaderName);

        return IsValidHeaderValue(candidate)
            ? new RequestCorrelationId(candidate)
            : new RequestCorrelationId(Guid.NewGuid().ToString("N"));
    }

    private static string? ReadHeader(HttpContext context, string headerName)
    {
        return context.Request.Headers.TryGetValue(headerName, out var values)
            ? values.FirstOrDefault()?.Trim()
            : null;
    }

    private static bool IsValidHeaderValue([NotNullWhen(true)] string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= MaxHeaderLength
            && value.All(IsAllowedCorrelationCharacter);
    }

    private static bool IsAllowedCorrelationCharacter(char value)
    {
        return value is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '-'
            or '_'
            or '.';
    }
}
