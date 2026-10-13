using System.Threading.RateLimiting;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Anchal.Api.CommissionerMonitoring;

public static class CommissionerMonitoringApiRegistration
{
    public const string CorsPolicy = "anchal-monitoring";

    public static IServiceCollection AddCommissionerMonitoringApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CommissionerMonitoringRouteOptions>(configuration.GetSection(CommissionerMonitoringRouteOptions.Section));
        services.AddScoped<MonitoringHttpAdapter>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
        services.AddRateLimiter(options => options.AddPolicy("monitoring-login", context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        services.PostConfigure<RateLimiterOptions>(options => options.OnRejected = async (context, ct) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.HttpContext.Response.WriteAsJsonAsync(new { code = "RATE_LIMITED", message = "Too many attempts. Try again later." }, ct);
        });
        services.PostConfigure<JwtBearerOptions>(MonitoringPolicies.AuthenticationScheme, options =>
        {
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (context.Request.Path.StartsWithSegments(CommissionerMonitoringRouteOptions.Prefix) &&
                        !context.Request.Headers.ContainsKey("Authorization"))
                        context.Token = context.Request.Cookies[CommissionerMonitoringRouteOptions.CookieName];
                    return Task.CompletedTask;
                },
                OnChallenge = context => { context.HandleResponse(); return WriteError(context.Response, 401, "UNAUTHORIZED", "Please sign in"); },
                OnForbidden = context => WriteError(context.Response, 403, "FORBIDDEN", "Access denied")
            };
        });
        return services;
    }

    public static IApplicationBuilder UseCommissionerMonitoringApi(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments(CommissionerMonitoringRouteOptions.Prefix)) { await next(); return; }
        context.Response.Headers.CacheControl = "no-store";
        if (!context.RequestServices.GetRequiredService<IOptionsMonitor<CommissionerMonitoringRouteOptions>>().CurrentValue.RoutesEnabled)
        { await WriteError(context.Response, 503, "UNAVAILABLE", "Commissioner Monitoring is unavailable"); return; }
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
        {
            var origin = context.Request.Headers.Origin.ToString();
            var allowed = context.RequestServices.GetRequiredService<IConfiguration>().GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            var sameOrigin = string.Equals(origin, $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase);
            var permitted = sameOrigin || allowed.Any(x => string.Equals(x, origin, StringComparison.OrdinalIgnoreCase));
            var login = context.Request.Path.StartsWithSegments(CommissionerMonitoringRouteOptions.Prefix + "/login");
            var cookie = context.Request.Cookies.ContainsKey(CommissionerMonitoringRouteOptions.CookieName) && !context.Request.Headers.ContainsKey("Authorization");
            var bearer = context.Request.Cookies.Count == 0 && context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && !login;
            if ((login || cookie || origin.Length > 0) && !permitted || (origin.Length == 0 && !bearer) || context.Request.Headers["Sec-Fetch-Site"] == "cross-site" && !permitted)
            { await WriteError(context.Response, 403, "FORBIDDEN", "Same-origin request required"); return; }
        }
        try
        {
            await next();
            if (!context.Response.HasStarted && context.Response.StatusCode is 404 or 405)
                await WriteError(context.Response, context.Response.StatusCode, context.Response.StatusCode == 404 ? "NOT_FOUND" : "METHOD_NOT_ALLOWED", context.Response.StatusCode == 404 ? "Endpoint not found" : "Method not allowed");
        }
        catch (MonitoringRuleException ex) when (!context.Response.HasStarted)
        { context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(ex.Error, context.RequestAborted); }
    });

    internal static Task WriteError(HttpResponse response, int status, string code, string message)
    { response.StatusCode = status; return response.WriteAsJsonAsync(new { code, message }); }
}
