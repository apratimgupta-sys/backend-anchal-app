using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;

namespace Anchal.Infrastructure.CommissionerMonitoring.Auth;

public sealed class MonitoringAuthenticator(
    CommissionerMonitoringDbContext db, MonitoringPasswordHasher hasher, IConfiguration configuration)
    : IMonitoringAuthenticator
{
    public async Task<string?> SignInAsync(string loginName, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(password)) return null;
        var normalized = loginName.Trim().ToUpperInvariant();
        var user = await db.Users.AsNoTracking()
            .Include(x => x.OfficeAssignments).ThenInclude(x => x.Office)
            .SingleOrDefaultAsync(x => x.NormalizedLoginName == normalized, cancellationToken);
        if (user is null || !user.IsActive || !hasher.VerifyPassword(password, user.PasswordHash)) return null;

        Guid? officeId = null;
        if (user.Role == MonitoringPolicies.OfficeRole && user.OfficeAssignments.Count == 1 &&
            user.OfficeAssignments.Single().Office.IsActive)
        {
            officeId = user.OfficeAssignments.Single().OfficeId;
        }
        else if (user.Role != MonitoringPolicies.CommissionerRole || user.OfficeAssignments.Count != 0)
        {
            return null;
        }

        if (user.Role == MonitoringPolicies.OfficeRole && officeId is null) return null;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(MonitoringPolicies.UserIdClaim, user.Id.ToString()),
            new(MonitoringPolicies.ScopeClaim, MonitoringPolicies.ScopeValue),
            new(MonitoringPolicies.RoleClaim, user.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (officeId.HasValue)
            claims.Add(new Claim(MonitoringPolicies.OfficeIdClaim, officeId.Value.ToString()));

        var secret = Required("Secret");
        var issuer = Required("Issuer");
        var audience = Required("Audience");
        if (!int.TryParse(configuration["Anchal:Jwt:ExpiryMinutes"], out var minutes) || minutes is < 1 or > 1440)
            throw new InvalidOperationException("Anchal:Jwt:ExpiryMinutes must be between 1 and 1440.");
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, claims,
            expires: DateTime.UtcNow.AddMinutes(minutes), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string Required(string key) => configuration[$"Anchal:Jwt:{key}"] is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Anchal:Jwt:{key} is required.");
}
