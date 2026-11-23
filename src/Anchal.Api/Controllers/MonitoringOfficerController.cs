using Anchal.Api.CommissionerMonitoring;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Application.CommissionerMonitoring.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anchal.Api.Controllers;

[ApiController, Route("v1/api/anchal/officers"), Authorize(Policy = MonitoringPolicies.RoleManager)]
public sealed class MonitoringOfficerController(IMonitoringOfficerService officers, MonitoringHttpAdapter adapter) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => await adapter.Result(await officers.ListAsync(User, ct), ct);

    [HttpPost] public async Task<IActionResult> Create(CancellationToken ct)
    {
        var command = MonitoringHttpAdapter.Command<CreateOfficerCommand>(await adapter.Body(Request, ct));
        var result = await officers.CreateAsync(User, command, ct);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : new ObjectResult(result.Error) { StatusCode = result.Status };
    }

    [HttpPatch("{id}")] public async Task<IActionResult> SetActive(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var officerId)) return NotFound(new { code = "NOT_FOUND", message = "Officer not found" });
        var body = await adapter.Body(Request, ct);
        if (!body.TryGetProperty("active", out var active) ||
            (active.ValueKind != System.Text.Json.JsonValueKind.True && active.ValueKind != System.Text.Json.JsonValueKind.False))
            return UnprocessableEntity(new { code = "VALIDATION", message = "Please correct the highlighted fields", fieldErrors = new { active = "Enter true or false" } });
        return await adapter.Result(await officers.SetActiveAsync(User, officerId, active.GetBoolean(), ct), ct);
    }

    [HttpPost("{id}/password-reset")] public async Task<IActionResult> ResetPassword(string id, CancellationToken ct) =>
        !Guid.TryParse(id, out var officerId)
            ? NotFound(new { code = "NOT_FOUND", message = "Officer not found" })
            : await adapter.Result(await officers.ResetPasswordAsync(User, officerId, ct), ct);
}

[ApiController, Route("v1/api/anchal/password"), Authorize(Policy = MonitoringPolicies.OwnPassword)]
public sealed class MonitoringPasswordController(IMonitoringOfficerService officers, MonitoringHttpAdapter adapter) : ControllerBase
{
    [HttpPost] public async Task<IActionResult> Change(CancellationToken ct)
    {
        var command = MonitoringHttpAdapter.Command<ChangePasswordCommand>(await adapter.Body(Request, ct));
        var result = await officers.ChangeOwnPasswordAsync(User, command, ct);
        // The session's must-change claim is stale once the password is set; sign out so the next
        // request carries a fresh one.
        if (result.IsSuccess) Response.Cookies.Delete(CommissionerMonitoringRouteOptions.CookieName,
            new CookieOptions { Path = CommissionerMonitoringRouteOptions.Prefix });
        return result.IsSuccess ? NoContent() : new ObjectResult(result.Error) { StatusCode = result.Status };
    }
}
