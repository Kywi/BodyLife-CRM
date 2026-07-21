using System.Diagnostics;

namespace BodyLife.Crm.Web.Operations;

internal sealed class RequestOutcomeLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestOutcomeLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? exception = null;

        try
        {
            await next(context);
        }
        catch (Exception caughtException)
        {
            exception = caughtException;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            var statusCode = exception is null
                ? context.Response.StatusCode
                : StatusCodes.Status500InternalServerError;
            var outcome = exception is null
                ? ResolveOutcome(statusCode)
                : "system_error";
            var errorClass = exception?.GetType().Name ?? ResolveErrorClass(statusCode);
            var routeOrCommand = ResolveRouteOrCommand(context);
            var requestCorrelationId =
                RequestCorrelationMiddleware.GetCorrelationId(context).Value;
            var logLevel = statusCode >= StatusCodes.Status500InternalServerError
                ? LogLevel.Error
                : statusCode >= StatusCodes.Status400BadRequest
                    ? LogLevel.Warning
                    : LogLevel.Information;

            logger.Log(
                logLevel,
                exception,
                "HTTP request completed request_correlation_id={request_correlation_id} route_or_command={route_or_command} method={method} status_code={status_code} duration_ms={duration_ms} outcome={outcome} error_class={error_class}",
                requestCorrelationId,
                routeOrCommand,
                context.Request.Method,
                statusCode,
                (long)stopwatch.Elapsed.TotalMilliseconds,
                outcome,
                errorClass);
        }
    }

    private static string ResolveRouteOrCommand(HttpContext context)
    {
        var endpointName = context.GetEndpoint()?.DisplayName;

        if (!string.IsNullOrWhiteSpace(endpointName))
        {
            return endpointName;
        }

        return context.Request.Path.HasValue
            ? context.Request.Path.Value!
            : "unknown";
    }

    private static string ResolveOutcome(int statusCode)
    {
        return statusCode switch
        {
            >= StatusCodes.Status500InternalServerError => "system_error",
            StatusCodes.Status409Conflict => "conflict",
            StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => "permission_denied",
            StatusCodes.Status400BadRequest or StatusCodes.Status422UnprocessableEntity => "validation_error",
            >= StatusCodes.Status400BadRequest => "request_error",
            _ => "success",
        };
    }

    private static string ResolveErrorClass(int statusCode)
    {
        return statusCode >= StatusCodes.Status400BadRequest
            ? FormattableString.Invariant($"HttpStatus{statusCode}")
            : "none";
    }
}
