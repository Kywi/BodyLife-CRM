using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BodyLife.Crm.Web.Operations;

internal static class HealthCheckResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new HealthCheckResponse(
            report.Status.ToString(),
            (long)report.TotalDuration.TotalMilliseconds,
            report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new HealthCheckEntryResponse(
                    entry.Key,
                    entry.Value.Status.ToString(),
                    (long)entry.Value.Duration.TotalMilliseconds,
                    entry.Value.Exception?.GetType().Name))
                .ToArray());

        return context.Response.WriteAsJsonAsync(response);
    }

    private sealed record HealthCheckResponse(
        string Status,
        long DurationMs,
        IReadOnlyCollection<HealthCheckEntryResponse> Checks);

    private sealed record HealthCheckEntryResponse(
        string Name,
        string Status,
        long DurationMs,
        string? ErrorClass);
}
