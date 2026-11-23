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
        // An office account and a field officer are both bound to exactly one active office.
        var officeBound = user.Role is MonitoringPolicies.OfficeRole or MonitoringPolicies.FieldOfficerRole;
        var validOfficeBinding = officeBound &&
            officeIdClaims.Length == 1 &&
            Guid.TryParse(officeIdClaims[0].Value, out var officeId) &&
            user.OfficeAssignments.Count == 1 &&
            user.OfficeAssignments.Single().OfficeId == officeId &&
            user.OfficeAssignments.Single().Office.IsActive;
        var validOffice = user.Role == MonitoringPolicies.OfficeRole && validOfficeBinding;
        var validFieldOfficer = user.Role == MonitoringPolicies.FieldOfficerRole && validOfficeBinding;
        var validCommissioner = user.Role == MonitoringPolicies.CommissionerRole &&
            officeIdClaims.Length == 0 && user.OfficeAssignments.Count == 0;
        // The role manager administers users and, like the commissioner, belongs to no office.
        var validRoleManager = user.Role == MonitoringPolicies.RoleManagerRole &&
            officeIdClaims.Length == 0 && user.OfficeAssignments.Count == 0;

        foreach (var requirement in context.PendingRequirements.ToArray())
        {
            if (requirement is MonitoringRoleRequirement role &&
                ((role.Role == MonitoringPolicies.OfficeRole && validOffice) ||
                 (role.Role == MonitoringPolicies.CommissionerRole && validCommissioner) ||
                 (role.Role == MonitoringPolicies.FieldOfficerRole && validFieldOfficer) ||
                 (role.Role == MonitoringPolicies.RoleManagerRole && validRoleManager)))
            {
                context.Succeed(requirement);
            }
            else if (requirement is MonitoringAnyRoleRequirement any &&
                     ((validOffice && any.Roles.Contains(MonitoringPolicies.OfficeRole)) ||
                      (validFieldOfficer && any.Roles.Contains(MonitoringPolicies.FieldOfficerRole))))
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
