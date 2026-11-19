using System.Globalization;
using Anchal.Api.CommissionerMonitoring;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Application.CommissionerMonitoring.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anchal.Api.Controllers;

[ApiController, Route("v1/api/anchal/field-visits")]
[Authorize(AuthenticationSchemes = MonitoringPolicies.AuthenticationScheme)]
public sealed class MonitoringFieldVisitController(
    IMonitoringFieldVisitService visits, MonitoringHttpAdapter adapter) : ControllerBase
{
    [HttpPost, Authorize(Policy = MonitoringPolicies.FieldOfficer)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var command = MonitoringHttpAdapter.Command<SaveFieldVisitCommand>(await adapter.Body(Request, ct));
        var result = await visits.CreateAsync(User, command, ct);
        if (!result.IsSuccess) return new ObjectResult(result.Error) { StatusCode = result.Status };
        // A replayed request identifier returns the visit that already exists.
        var created = await adapter.Result(result, ct);
        if (created is JsonResult json) json.StatusCode = StatusCodes.Status201Created;
        return created;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        FieldVisitFilter filter;
        try { filter = Filter(Request.Query); }
        catch (Anchal.Domain.CommissionerMonitoring.Rules.MonitoringRuleException exception)
        { return new ObjectResult(exception.Error) { StatusCode = exception.Status }; }
        return await adapter.Result(await visits.ListAsync(User, filter, ct), ct);
    }

    [HttpPost("{id}/void"), Authorize(Policy = MonitoringPolicies.Commissioner)]
    public async Task<IActionResult> Void(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var visitId))
            return NotFound(new { code = "NOT_FOUND", message = "Visit not found" });
        var body = await adapter.Body(Request, ct);
        var reason = body.TryGetProperty("reason", out var value) &&
            value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
        return await adapter.Result(await visits.VoidAsync(User, visitId, reason, ct), ct);
    }

    private FieldVisitFilter Filter(IQueryCollection query)
    {
        var from = adapter.Date(query["from"].ToString(), "from", adapter.Today);
        var to = adapter.Date(query["to"].ToString(), "to", from);
        return new FieldVisitFilter(from, to,
            Bounded(query, "officeId", 1, 6),
            Bounded(query, "ward", 1, 53),
            Bounded(query, "serviceId", 1, 25),
            query.ContainsKey("officerId") && Guid.TryParse(query["officerId"], out var officer) ? officer : null,
            Bounded(query, "page", 1, 10_000) ?? 1);
    }

    private static int? Bounded(IQueryCollection query, string key, int minimum, int maximum)
    {
        if (!query.ContainsKey(key)) return null;
        if (!int.TryParse(query[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value < minimum || value > maximum)
            throw MonitoringHttpAdapter.Validation(key, "Enter a valid value");
        return value;
    }
}
