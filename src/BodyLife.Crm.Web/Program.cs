using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

builder.Services.AddBodyLifePersistence(builder.Configuration);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AddPageRoute("/Reception/Index", string.Empty);
});
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "BodyLife.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

builder.Services
    .AddHealthChecks()
    .AddCheck(
        "application",
        () => HealthCheckResult.Healthy("Application process is running."),
        tags: ["live", "ready"])
    .AddCheck<PostgreSqlHealthCheck>(
        "postgresql",
        tags: ["ready"]);

var app = builder.Build();

if (OwnerBootstrapCommand.IsRequested(args))
{
    Environment.ExitCode = await OwnerBootstrapCommand.ExecuteAsync(
        app.Services,
        app.Configuration,
        app.Logger,
        app.Lifetime.ApplicationStopping);

    return;
}

if (OwnerCredentialsCommand.IsRequested(args))
{
    Environment.ExitCode = await OwnerCredentialsCommand.ExecuteAsync(
        app.Services,
        app.Configuration,
        app.Logger,
        app.Lifetime.ApplicationStopping);

    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseMiddleware<RequestCorrelationMiddleware>();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<RequestOutcomeLoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("live"),
        ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    });
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
        ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    });
app.MapHealthChecks(
    "/health",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
        ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    });

app.Run();

public partial class Program
{
}
