using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
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
builder.Services.AddBodyLifeRequestContext();
builder.Services.AddScoped<BodyLifeCookieAuthenticationEvents>();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AddPageRoute("/Reception/Index", string.Empty);
    options.Conventions.AuthorizeFolder("/Owner", BodyLifeAuthorizationPolicies.OwnerOnly);
    options.Conventions.AuthorizeFolder("/Reception", BodyLifeAuthorizationPolicies.AdminOrOwner);
    options.Conventions.AuthorizePage("/Logout", BodyLifeAuthorizationPolicies.AdminOrOwner);
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
        options.AccessDeniedPath = "/AccessDenied";
        options.ExpireTimeSpan = AccountSessionPolicy.IdleTimeout;
        options.SlidingExpiration = true;
        options.EventsType = typeof(BodyLifeCookieAuthenticationEvents);
    });
builder.Services.AddBodyLifeAuthorizationPolicies();

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
