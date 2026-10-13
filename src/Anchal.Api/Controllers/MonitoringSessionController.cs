using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Anchal.Api.CommissionerMonitoring;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Anchal.Api.Controllers;

[ApiController]
[Route("v1/api/anchal")]
[Authorize(AuthenticationSchemes = MonitoringPolicies.AuthenticationScheme)]
public sealed class MonitoringSessionController(IMonitoringService service, IMonitoringAuthenticator authenticator, MonitoringHttpAdapter adapter) : ControllerBase
{
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("monitoring-login")]
    public async Task<IActionResult> Login(CancellationToken ct)
    {
        var body = await adapter.Body(Request, ct);
        var username = Property(body, "username");
        var password = Property(body, "password");
        if (username.ValueKind != JsonValueKind.String || password.ValueKind != JsonValueKind.String) return BadRequest(new { code = "VALIDATION", message = "Enter username and password" });
        var token = await authenticator.SignInAsync(username.GetString()!, password.GetString()!, ct);
        if (token is null) return Unauthorized(new { code = "LOGIN", message = "Invalid username or password" });
        Response.Cookies.Append(CommissionerMonitoringRouteOptions.CookieName, token, new CookieOptions { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict, Path = CommissionerMonitoringRouteOptions.Prefix, Expires = new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo, IsEssential = true });
        return Ok(new { ok = true });
    }
    [HttpGet("session")] public async Task<IActionResult> Session(CancellationToken ct) => await adapter.Read(Response, await service.GetSessionAsync(User, ct), ct);
    [HttpPost("logout")] public IActionResult Logout() { Response.Cookies.Delete(CommissionerMonitoringRouteOptions.CookieName, new CookieOptions { Path = CommissionerMonitoringRouteOptions.Prefix }); return Ok(new { ok = true }); }
    [HttpGet("services")] public async Task<IActionResult> Services(CancellationToken ct) => await adapter.Read(Response, await service.GetServicesAsync(User, ct), ct);
    [HttpGet("history")] public async Task<IActionResult> History(CancellationToken ct) => await adapter.Read(Response, await service.GetHistoryAsync(User, ct), ct);
    private static JsonElement Property(JsonElement body, string name) => body.TryGetProperty(name, out var value) ? value : body.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value) ? value : default;
}
