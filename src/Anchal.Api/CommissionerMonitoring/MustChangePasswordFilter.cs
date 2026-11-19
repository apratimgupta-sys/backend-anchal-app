using Anchal.Application.CommissionerMonitoring.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Anchal.Api.CommissionerMonitoring;

// An officer holding a temporary password may only read their session, change that
// password, or sign out. Everything else waits until they have set one of their own.
public sealed class MustChangePasswordFilter : IAsyncActionFilter
{
    private static readonly string[] Allowed =
    [
        CommissionerMonitoringRouteOptions.Prefix + "/session",
        CommissionerMonitoringRouteOptions.Prefix + "/password",
        CommissionerMonitoringRouteOptions.Prefix + "/logout",
        CommissionerMonitoringRouteOptions.Prefix + "/login"
    ];

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var mustChange = context.HttpContext.User
            .FindFirst(MonitoringPolicies.MustChangePasswordClaim)?.Value == "true";
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        if (mustChange && !Allowed.Any(x => path.Equals(x, StringComparison.OrdinalIgnoreCase)))
        {
            context.Result = new ObjectResult(new
            {
                code = "password_change_required",
                message = "Change your temporary password before using the portal"
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
