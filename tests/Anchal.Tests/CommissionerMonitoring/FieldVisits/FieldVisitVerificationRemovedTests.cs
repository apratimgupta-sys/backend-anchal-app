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

/// RMC never asked for a "verified on site" verdict; the 100 m / 30 min thresholds
/// were invented in development. The product now stores where a visit happened
/// and says nothing about whether it believes it. The database column stays (no
/// migration in go-live week); only the computation and the DTO field are gone.
public sealed class FieldVisitVerificationRemovedTests
{
    private static string Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).DateTime).ToString("yyyy-MM-dd");

    private static object Input(double latitude, double longitude, double accuracyMetres, DateTimeOffset capturedAt) => new
    {
        visitDate = Today(),
        visitTime = "00:00",
        officeId = 3,
        ward = 24,
        serviceId = 9,
        gps = new { latitude, longitude, accuracyMetres, capturedAt = capturedAt.ToString("O") },
        issueAction = "Fogging done along the main lane; two bylanes still pending."
    };

    [Fact]
    public async Task ACoarseLateReadingIsAcceptedAndItsCoordinatesRoundTripUnchanged()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var captured = DateTimeOffset.UtcNow.AddMinutes(-90);

        using var created = await host.Client.PostAsync(Root + "/field-visits", Json(new
        {
            requestId = Guid.NewGuid().ToString("N"),
            input = Input(23.37412, 85.33891, 400, captured)
        }));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var visit = await created.Content.ReadFromJsonAsync<JsonElement>();
        AssertCarriesNoVerdict(visit);
        AssertGps(visit, 23.37412, 85.33891, 400, captured);

        var list = await host.Client.GetFromJsonAsync<JsonElement>(
            Root + $"/field-visits?from={Today()}&to={Today()}");
        var row = list.GetProperty("rows").EnumerateArray().Single();
        AssertCarriesNoVerdict(row);
        AssertGps(row, 23.37412, 85.33891, 400, captured);
    }

    /// A row written before this change, with the flag already true in the table,
    /// must still read back. EF still maps the column; the DTO simply no longer
    /// carries it. This is the case that would turn into a 500 on real data.
    [Fact]
    public async Task AVisitWrittenBeforeThisChangeStillReadsBackWithItsCoordinatesAndNoFlag()
    {
        await using var host = await Create();
        var officer = await host.SignInAsOfficer();
        var officerId = Guid.Parse(officer.GetProperty("id").GetString()!);
        var captured = DateTimeOffset.UtcNow.AddMinutes(-5);
        var legacyId = Guid.NewGuid();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
            var office = await db.Offices.SingleAsync(x => x.Code == "anchal3");
            db.FieldVisits.Add(new MonitoringFieldVisit
            {
                Id = legacyId, RequestId = "legacy-" + Guid.NewGuid().ToString("N"), Fingerprint = "legacy",
                OfficeId = office.Id, OfficerUserId = officerId, Ward = 24, ServiceId = 9,
                VisitDate = MonitoringRules.Today(captured), VisitTime = new(10, 30),
                Latitude = 23.37412, Longitude = 85.33891, AccuracyMetres = 18,
                GpsCapturedAt = captured, ReceivedAt = captured, VerifiedOnSite = true,
                IssueAction = "Fogging done along the main lane; two bylanes still pending."
            });
            await db.SaveChangesAsync();
        }
        var day = MonitoringRules.Today(captured).ToString("yyyy-MM-dd");

        using var response = await host.Client.GetAsync(Root + $"/field-visits?from={day}&to={day}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = list.GetProperty("rows").EnumerateArray().Single();
        Assert.Equal(legacyId.ToString(), row.GetProperty("id").GetString());
        AssertCarriesNoVerdict(row);
        AssertGps(row, 23.37412, 85.33891, 18, captured);
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }

    private static void AssertCarriesNoVerdict(JsonElement visit) =>
        Assert.False(visit.TryGetProperty("verifiedOnSite", out _),
            "a visit must not carry a verdict about where it was made: " + visit);

    private static void AssertGps(JsonElement visit, double latitude, double longitude, double accuracy,
        DateTimeOffset captured)
    {
        var gps = visit.GetProperty("gps");
        Assert.Equal(latitude, gps.GetProperty("latitude").GetDouble());
        Assert.Equal(longitude, gps.GetProperty("longitude").GetDouble());
        Assert.Equal(accuracy, gps.GetProperty("accuracyMetres").GetDouble());
        Assert.Equal(captured.UtcDateTime, gps.GetProperty("capturedAt").GetDateTimeOffset().UtcDateTime);
    }
}
