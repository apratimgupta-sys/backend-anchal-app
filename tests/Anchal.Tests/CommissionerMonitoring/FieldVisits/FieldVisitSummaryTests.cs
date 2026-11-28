using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Anchal.Tests.CommissionerMonitoring.FieldVisits.MonitoringTestHost;

namespace Anchal.Tests.CommissionerMonitoring.FieldVisits;

/// The Commissioner's dashboard reads visits rolled up, never the list: how many, by how many officers, what
/// each field-work service counted, and in which wards. Each service counts its own thing, so nothing here
/// is added across services.
public sealed class FieldVisitSummaryTests
{
    private const string Observation = "Work done along the main lane; two bylanes still pending.";

    private static string Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).DateTime).ToString("yyyy-MM-dd");

    private static async Task<JsonElement> File(MonitoringTestHost host, int serviceId, int ward, object? details = null, int officeId = 3)
    {
        var input = new Dictionary<string, object?>
        {
            ["visitDate"] = Today(), ["visitTime"] = "00:01", ["officeId"] = officeId, ["ward"] = ward, ["serviceId"] = serviceId,
            ["gps"] = new { latitude = 23.3441, longitude = 85.3096, accuracyMetres = 18, capturedAt = DateTimeOffset.UtcNow.ToString("O") },
            ["issueAction"] = Observation
        };
        if (details is not null) input["details"] = details;
        using var response = await host.Client.PostAsync(Root + "/field-visits", Json(new { requestId = Guid.NewGuid().ToString("N"), input }));
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> Summary(MonitoringTestHost host, string extra = "")
    {
        await host.Login("commissioner");
        using var response = await host.Client.GetAsync(Root + $"/field-visits/summary?from={Today()}&to={Today()}{extra}");
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement Service(JsonElement summary, int serviceId) =>
        summary.GetProperty("services").EnumerateArray().Single(x => x.GetProperty("serviceId").GetInt32() == serviceId);

    private static JsonElement Number(JsonElement owner, string key) =>
        owner.GetProperty("numbers").EnumerateArray().Single(x => x.GetProperty("key").GetString() == key);

    private static JsonElement Option(JsonElement service, string choice, string value) =>
        service.GetProperty("choices").EnumerateArray().Single(x => x.GetProperty("key").GetString() == choice)
            .GetProperty("options").EnumerateArray().Single(x => x.GetProperty("value").GetString() == value);

    [Fact]
    public async Task WithNoVisitsEveryFieldWorkServiceIsThereAtZero()
    {
        await using var host = await Create();

        var summary = await Summary(host);

        Assert.Equal(0, summary.GetProperty("visits").GetInt32());
        Assert.Equal(0, summary.GetProperty("officers").GetInt32());
        Assert.Equal(Enumerable.Range(1, MonitoringRules.ServiceCount).Where(MonitoringRules.IsFieldWork),
            summary.GetProperty("services").EnumerateArray().Select(x => x.GetProperty("serviceId").GetInt32()));
        Assert.Equal(0, summary.GetProperty("wards").GetArrayLength());
        Assert.All(summary.GetProperty("services").EnumerateArray(), x => Assert.Equal(0, x.GetProperty("visits").GetInt32()));
    }

    [Fact]
    public async Task EachServiceCountsItsOwnThing()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        await File(host, 8, ward: 24, new { wasteType = "wet", quantityKg = 120.5 });
        await File(host, 8, ward: 25, new { wasteType = "dry", quantityKg = 80 });
        await File(host, 8, ward: 24);
        await File(host, 10, ward: 8, new { dogsPicked = 5, dogsNeutered = 3 });
        await File(host, 13, ward: 8, new { workType = "repair" });

        var summary = await Summary(host);
        var waste = Service(summary, 8);
        var dogs = Service(summary, 10);
        var lights = Service(summary, 13);

        Assert.Equal(5, summary.GetProperty("visits").GetInt32());
        Assert.Equal(1, summary.GetProperty("officers").GetInt32());

        Assert.Equal(3, waste.GetProperty("visits").GetInt32());
        Assert.Equal(2, waste.GetProperty("wardsCovered").GetInt32());
        Assert.Equal(200.5, Number(waste, "quantityKg").GetProperty("sum").GetDouble());
        // Two of the three visits gave a quantity; the page says so rather than letting the sum understate.
        Assert.Equal(2, Number(waste, "quantityKg").GetProperty("visits").GetInt32());
        Assert.Equal(120.5, Number(Option(waste, "wasteType", "wet"), "quantityKg").GetProperty("sum").GetDouble());
        Assert.Equal(80, Number(Option(waste, "wasteType", "dry"), "quantityKg").GetProperty("sum").GetDouble());
        Assert.Equal(0, Option(waste, "wasteType", "mixed").GetProperty("visits").GetInt32());

        Assert.Equal(5, Number(dogs, "dogsPicked").GetProperty("sum").GetDouble());
        Assert.Equal(3, Number(dogs, "dogsNeutered").GetProperty("sum").GetDouble());

        Assert.Equal(1, Option(lights, "workType", "repair").GetProperty("visits").GetInt32());
        Assert.Equal(0, Option(lights, "workType", "shift").GetProperty("visits").GetInt32());
        Assert.Equal(0, lights.GetProperty("numbers").GetArrayLength());
    }

    [Fact]
    public async Task TheWardsAreThoseWithAVisitInWardOrder()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        await File(host, 9, ward: 24);
        await File(host, 9, ward: 8);
        await File(host, 17, ward: 24);

        var wards = (await Summary(host)).GetProperty("wards").EnumerateArray()
            .Select(x => (x.GetProperty("ward").GetInt32(), x.GetProperty("visits").GetInt32())).ToArray();

        Assert.Equal(new[] { (8, 1), (24, 2) }, wards);
    }

    [Fact]
    public async Task AnAnchalFilterNarrowsEveryFigureAndOfficersAreCountedOnce()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        await File(host, 9, ward: 24);
        await File(host, 9, ward: 25);
        await host.SignInAsOfficer(username: "ae.hatia", name: "B. Singh", designation: "Assistant Engineer",
            homeOfficeId: 5, password: "Hatia#2026ae");
        await File(host, 13, ward: 51, officeId: 5);

        var all = await Summary(host);
        var five = await Summary(host, "&officeId=5");

        Assert.Equal(3, all.GetProperty("visits").GetInt32());
        Assert.Equal(2, all.GetProperty("officers").GetInt32());
        Assert.Equal(1, five.GetProperty("visits").GetInt32());
        Assert.Equal(1, five.GetProperty("officers").GetInt32());
        Assert.Equal(0, Service(five, 9).GetProperty("visits").GetInt32());
        Assert.Equal(1, Service(five, 13).GetProperty("visits").GetInt32());
    }

    [Fact]
    public async Task AVoidedVisitIsNotCounted()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var kept = await File(host, 9, ward: 24);
        var voided = await File(host, 9, ward: 25);
        await host.Login("commissioner");
        using var response = await host.Client.PostAsync(Root + $"/field-visits/{voided.GetProperty("id").GetGuid()}/void",
            Json(new { reason = "Filed twice by mistake; the first record stands." }));
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var summary = await Summary(host);

        Assert.NotEqual(kept.GetProperty("id").GetGuid(), voided.GetProperty("id").GetGuid());
        Assert.Equal(1, summary.GetProperty("visits").GetInt32());
        Assert.Equal(1, Service(summary, 9).GetProperty("wardsCovered").GetInt32());
    }

    /// Details are read defensively, and a visit for a service that is not field work can no longer be filed
    /// but may be stored. Neither may take the dashboard down or leak into a figure.
    [Fact]
    public async Task StoredRowsTheFormCouldNotHaveFiledAreToleratedAndLeftOut()
    {
        await using var host = await Create();
        var officer = await host.SignInAsOfficer();
        await Plant(host, officer.GetProperty("id").GetGuid(), serviceId: 8, detailsJson: "this is not json");
        await Plant(host, officer.GetProperty("id").GetGuid(), serviceId: 8, detailsJson: "{\"v\":1,\"quantityKg\":\"a lot\",\"wasteType\":\"liquid\"}");
        await Plant(host, officer.GetProperty("id").GetGuid(), serviceId: 1, detailsJson: null);

        var summary = await Summary(host);
        var waste = Service(summary, 8);

        Assert.Equal(2, summary.GetProperty("visits").GetInt32());
        Assert.Equal(2, waste.GetProperty("visits").GetInt32());
        Assert.Equal(0, Number(waste, "quantityKg").GetProperty("visits").GetInt32());
        Assert.Equal(0, Number(waste, "quantityKg").GetProperty("sum").GetDouble());
        Assert.All(waste.GetProperty("choices").EnumerateArray().Single().GetProperty("options").EnumerateArray(),
            x => Assert.Equal(0, x.GetProperty("visits").GetInt32()));
    }

    [Fact]
    public async Task ADateRangeTheWrongWayRoundIsRefused()
    {
        await using var host = await Create();
        await host.Login("commissioner");

        using var response = await host.Client.GetAsync(Root + "/field-visits/summary?from=2026-11-02&to=2026-11-01");

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
    }

    [Fact]
    public async Task AFieldOfficerAndAnOfficeAccountAreRefused()
    {
        await using var host = await Create();
        var route = Root + $"/field-visits/summary?from={Today()}&to={Today()}";

        await host.SignInAsOfficer();
        using var officer = await host.Client.GetAsync(route);
        await host.Login("anchal3");
        using var office = await host.Client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, officer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, office.StatusCode);
    }

    private static async Task Plant(MonitoringTestHost host, Guid officerId, int serviceId, string? detailsJson)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var office = await db.Offices.SingleAsync(x => x.Code == "anchal3");
        var now = DateTimeOffset.UtcNow;
        db.FieldVisits.Add(new MonitoringFieldVisit
        {
            Id = Guid.NewGuid(), RequestId = "plant-" + Guid.NewGuid().ToString("N"), Fingerprint = "plant",
            OfficeId = office.Id, OfficerUserId = officerId, Ward = 24, ServiceId = serviceId,
            VisitDate = MonitoringRules.Today(now), VisitTime = new(0, 1), Latitude = 23.3, Longitude = 85.3,
            AccuracyMetres = 18, GpsCapturedAt = now, ReceivedAt = now, IssueAction = Observation, DetailsJson = detailsJson
        });
        await db.SaveChangesAsync();
    }
}
