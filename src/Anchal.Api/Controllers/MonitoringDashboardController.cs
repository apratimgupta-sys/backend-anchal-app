using Anchal.Api.CommissionerMonitoring;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anchal.Api.Controllers;

[ApiController, Route("v1/api/anchal/dashboard"), Authorize(Policy = MonitoringPolicies.Commissioner)]
public sealed class MonitoringDashboardController(IMonitoringService service, MonitoringHttpAdapter adapter) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Dashboard(CancellationToken ct) => await adapter.Read(Response, await service.GetDashboardAsync(User, await adapter.Filter(Request.Query, ct), ct), ct);
    [HttpGet("entries")] public async Task<IActionResult> Entries(CancellationToken ct) => await adapter.Read(Response, await service.GetDashboardEntriesAsync(User, await adapter.Filter(Request.Query, ct), ct), ct);
}
