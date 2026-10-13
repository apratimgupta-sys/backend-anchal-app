using Anchal.Api.CommissionerMonitoring;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anchal.Api.Controllers;

[ApiController, Route("v1/api/anchal/office"), Authorize(Policy = MonitoringPolicies.Office)]
public sealed class MonitoringOfficeController(IMonitoringService service, MonitoringHttpAdapter adapter) : ControllerBase
{
    [HttpGet("setup")] public async Task<IActionResult> Setup(CancellationToken ct) => await adapter.Read(Response, await service.GetSetupAsync(User, ct), ct);
    [HttpPut("setup")] public async Task<IActionResult> SaveSetup(CancellationToken ct) => await adapter.Result(await service.SaveSetupAsync(User, adapter.Setup(await adapter.Body(Request, ct)), ct), ct);
    [HttpGet("entries")] public async Task<IActionResult> Entries(CancellationToken ct) => await adapter.Read(Response, await service.GetEntriesAsync(User, Request.Query["mode"].ToString(), adapter.Date(Request.Query["date"].ToString(), "date", adapter.Today), ct), ct);
    [HttpPost("entries")] public async Task<IActionResult> SaveEntry(CancellationToken ct) => await adapter.Result(await service.SaveEntryAsync(User, adapter.Entry(await adapter.Body(Request, ct)), ct: ct), ct);
    [HttpPut("entries/{id}")] public async Task<IActionResult> Correct(string id, CancellationToken ct) => !Guid.TryParse(id, out var entry) ? NotFound(new { code = "NOT_FOUND", message = "Entry not found" }) : await adapter.Result(await service.SaveEntryAsync(User, adapter.Entry(await adapter.Body(Request, ct)), entry, ct), ct);
}
