using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Application.CommissionerMonitoring.Services;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anchal.Api.CommissionerMonitoring;

public sealed class MonitoringHttpAdapter(IMonitoringContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    // Request bodies are read strictly: the Web defaults allow numbers to be read from strings ("7" becomes 7).
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.Strict };
    private Dictionary<Guid, int>? officeNumbers;
    public DateOnly Today => MonitoringRules.Today(clock.GetUtcNow());
    public async Task<IActionResult> Result<T>(MonitoringResult<T> result, CancellationToken ct) => !result.IsSuccess ? new ObjectResult(result.Error) { StatusCode = result.Status } : new JsonResult(await Map(result.Value, ct)) { StatusCode = result.Status };
    public async Task<IActionResult> Read<T>(HttpResponse response, MonitoringResult<MonitoringRead<T>> result, CancellationToken ct)
    {
        if (!result.IsSuccess) return new ObjectResult(result.Error) { StatusCode = result.Status };
        var read = result.Value!; response.Headers["X-Monitoring-Retrieved-At"] = read.RetrievedAt.ToString("O");
        if (read.LastUpdated is { } updated) response.Headers["X-Monitoring-Last-Updated"] = updated.ToString("O");
        var node = await Map(read.Data, ct);
        if (read.Revision is not null)
            response.Headers["X-Monitoring-Revision"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node!.ToJsonString(Json))));
        if (read.Data is DashboardDto && node is JsonObject dashboard) { dashboard["updatedAt"] = JsonSerializer.SerializeToNode(read.LastUpdated ?? read.RetrievedAt, Json); dashboard["retrievedAt"] = JsonSerializer.SerializeToNode(read.RetrievedAt, Json); }
        return new JsonResult(node);
    }
    private async Task<JsonNode?> Map<T>(T value, CancellationToken ct)
    {
        var node = JsonSerializer.SerializeToNode(value, Json); if (node is null) return null;
        officeNumbers ??= (await db.Offices.AsNoTracking().Select(x => new { x.Id, x.Code }).ToListAsync(ct)).ToDictionary(x => x.Id, x => int.Parse(x.Code[6].ToString(), CultureInfo.InvariantCulture));
        Transform(node); return node;
    }
    private void Transform(JsonNode node) { if (node is JsonArray a) { foreach (var x in a) if (x is not null) Transform(x); return; } if (node is not JsonObject o) return; if (o["officeId"] is JsonValue id && Guid.TryParse(id.ToString(), out var guid)) o["officeId"] = officeNumbers![guid]; o.Remove("officeCode"); if (o["input"] is JsonObject input) { foreach (var p in input) if (p.Key != "isZeroActivity") o[p.Key] = p.Value?.DeepClone(); o.Remove("input"); } foreach (var p in o.ToArray()) if (p.Value is JsonArray or JsonObject) Transform(p.Value); }
    public DateOnly Date(string? value, string field, DateOnly? fallback = null) => value is null && fallback.HasValue ? fallback.Value : DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw Validation(field, "Enter a valid date");
    public async Task<PeriodFilter> Filter(IQueryCollection query, CancellationToken ct) { var from = Date(query["from"].ToString(), "from", Today); var to = Date(query["to"].ToString(), "to", from); Guid? office = null; if (query.ContainsKey("officeId")) { if (!int.TryParse(query["officeId"], out var n) || n is < 1 or > 6) throw Validation("officeId", "Select an office"); office = await db.Offices.Where(x => x.Code == "anchal" + n && x.IsActive).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct); if (office is null) throw Validation("officeId", "Select an active office"); } int? service = null; if (query.ContainsKey("serviceId")) { if (!int.TryParse(query["serviceId"], out var n) || n is < 1 or > 25) throw Validation("serviceId", "Select a service"); service = n; } return new(from, to, office, service); }
    public async Task<JsonElement> Body(HttpRequest request, CancellationToken ct)
    {
        var buffer = new byte[50_001]; var length = 0;
        while (length < buffer.Length) { var read = await request.Body.ReadAsync(buffer.AsMemory(length), ct); if (read == 0) break; length += read; }
        if (length > 50_000) throw new MonitoringRuleException(413, new("VALIDATION", "Request too large"));
        try { using var document = JsonDocument.Parse(buffer.AsMemory(0, length)); if (document.RootElement.ValueKind != JsonValueKind.Object) throw Validation("body", "Enter an object"); return document.RootElement.Clone(); }
        catch (JsonException) { throw new MonitoringRuleException(400, new("VALIDATION", "Invalid JSON")); }
    }
    public SetupCommand Setup(JsonElement body) => Command<SetupCommand>(body);
    public SaveEntryCommand Entry(JsonElement body) => Command<SaveEntryCommand>(body);
    // A type mismatch (fraction for an integer, null, bad date, wrong container) is a client error, not a server failure.
    private static T Command<T>(JsonElement body) where T : class
    {
        try { return body.Deserialize<T>(StrictJson) ?? throw Validation("body", "Enter an object"); }
        catch (JsonException ex) { throw Validation(string.IsNullOrEmpty(ex.Path) || ex.Path == "$" ? "body" : ex.Path.TrimStart('$', '.'), "Enter a valid value"); }
    }
    public static MonitoringRuleException Validation(string field, string message) => new(422, new("VALIDATION", "Please correct the highlighted fields", new Dictionary<string, string> { [field] = message }));
}
