using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;

namespace Anchal.Infrastructure.CommissionerMonitoring.Auth;

public sealed class MonitoringAuthorizationHandler(CommissionerMonitoringDbContext db) : IAuthorizationHandler
{
    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            context.User.FindAll(MonitoringPolicies.ScopeClaim).Count() != 1 ||
            context.User.FindFirstValue(MonitoringPolicies.ScopeClaim) != MonitoringPolicies.ScopeValue ||
            context.User.FindAll(MonitoringPolicies.UserIdClaim).Count() != 1 ||
            !Guid.TryParse(context.User.FindFirstValue(MonitoringPolicies.UserIdClaim), out var userId) ||
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) != userId.ToString() ||
            context.User.FindAll(MonitoringPolicies.RoleClaim).Count() != 1)
        {
            return;
        }

        var user = await db.Users.AsNoTracking()
            .Include(x => x.OfficeAssignments).ThenInclude(x => x.Office)
            .SingleOrDefaultAsync(x => x.Id == userId);
        if (user is null || !user.IsActive ||
            user.Role != context.User.FindFirstValue(MonitoringPolicies.RoleClaim))
        {
            return;
        }

        var officeIdClaims = context.User.FindAll(MonitoringPolicies.OfficeIdClaim).ToArray();
        var validOffice = user.Role == MonitoringPolicies.OfficeRole &&
            officeIdClaims.Length == 1 &&
            Guid.TryParse(officeIdClaims[0].Value, out var officeId) &&
            user.OfficeAssignments.Count == 1 &&
            user.OfficeAssignments.Single().OfficeId == officeId &&
            user.OfficeAssignments.Single().Office.IsActive;
        var validCommissioner = user.Role == MonitoringPolicies.CommissionerRole &&
            officeIdClaims.Length == 0 && user.OfficeAssignments.Count == 0;

        foreach (var requirement in context.PendingRequirements.ToArray())
        {
            if (requirement is MonitoringRoleRequirement role &&
                ((role.Role == MonitoringPolicies.OfficeRole && validOffice) ||
                 (role.Role == MonitoringPolicies.CommissionerRole && validCommissioner)))
            {
                context.Succeed(requirement);
            }
            else if (requirement is MonitoringOfficeScopeRequirement && validOffice &&
                     context.Resource is Guid requestedOfficeId &&
                     user.OfficeAssignments.Single().OfficeId == requestedOfficeId)
            {
                context.Succeed(requirement);
            }
        }
    }
}
