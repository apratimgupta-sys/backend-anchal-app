using System.Security.Claims;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
            .FindFirst(MonitoringPolicies.MustChangePasswordClaim)?.Value == "true"
            || await ResetSinceSignIn(context.HttpContext);
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

    /// The claim is minted at sign-in, so a password reset the role manager made
    /// afterwards is invisible to it. Read the officer's row instead: a reset
    /// then ends the session on the next request, which is what "reset the
    /// password because the phone was lost" has to mean.
    private static async Task<bool> ResetSinceSignIn(HttpContext http)
    {
        if (http.User.FindFirstValue(MonitoringPolicies.RoleClaim) is not
                (MonitoringPolicies.FieldOfficerRole or MonitoringPolicies.OfficeRole) ||
            !Guid.TryParse(http.User.FindFirstValue(MonitoringPolicies.UserIdClaim), out var userId))
            return false;
        var db = http.RequestServices.GetService<CommissionerMonitoringDbContext>();
        if (db is null) return false;
        return await db.Users.AsNoTracking()
            .Where(x => x.Id == userId).Select(x => x.MustChangePassword).SingleOrDefaultAsync();
    }
}
