using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static Anchal.Tests.CommissionerMonitoring.FieldVisits.MonitoringTestHost;

namespace Anchal.Tests.CommissionerMonitoring.FieldVisits;

/// Every database bound that validation does not mirror reaches the officer as
/// a 500 saying "Please retry" — advice that can never work, on a form with no
/// edit path. These tests pin the bounds to 422 with a field error instead.
public sealed class FieldVisitBoundsTests
{
    private const string Root2 = Root + "/field-visits";

    private static string Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).DateTime).ToString("yyyy-MM-dd");

    private static object Input(string? issueAction = null, double accuracyMetres = 18,
        string visitTime = "10:30") => new
    {
        visitDate = Today(),
        visitTime,
        officeId = 3,
        ward = 24,
        serviceId = 9,
        gps = new
        {
            latitude = 23.3441,
            longitude = 85.3096,
            accuracyMetres,
            capturedAt = DateTimeOffset.UtcNow.ToString("O")
        },
        issueAction = issueAction ?? "Fogging done along the main lane; two bylanes still pending."
    };

    private static Task<HttpResponseMessage> Post(MonitoringTestHost host, object input) =>
        host.Client.PostAsync(Root2, Json(new { requestId = Guid.NewGuid().ToString("N"), input }));

    [Fact]
    public async Task AnObservationLongerThanTheColumnIsRefusedWithAFieldError()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(issueAction: new string('क', 4001)));

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(error.GetProperty("fieldErrors").TryGetProperty("input.issueAction", out _),
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnObservationAtTheColumnLimitIsStored()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(issueAction: new string('क', 4000)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ANegativeGpsAccuracyIsRefusedWithAFieldError()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(accuracyMetres: -1));

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(error.GetProperty("fieldErrors").TryGetProperty("input.gps", out _),
            await response.Content.ReadAsStringAsync());
    }

    /// A browser cannot send NaN — JSON.stringify turns it into null — so the
    /// shape that actually reaches the server is a null accuracy. It must be a
    /// field error, not a 500 from the column's check constraint.
    [Theory]
    [InlineData("null")]
    [InlineData("NaN")]
    [InlineData("\"18\"")]
    public async Task AnUnusableGpsAccuracyIsRefusedRatherThanStored(string literal)
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var body = $@"{{""requestId"":""{Guid.NewGuid():N}"",""input"":{{""visitDate"":""{Today()}"",
            ""visitTime"":""10:30"",""officeId"":3,""ward"":24,""serviceId"":9,
            ""gps"":{{""latitude"":23.3441,""longitude"":85.3096,""accuracyMetres"":{literal},
            ""capturedAt"":""{DateTimeOffset.UtcNow:O}""}},
            ""issueAction"":""Fogging done along the main lane; two bylanes still pending.""}}}}";

        using var response = await host.Client.PostAsync(Root2,
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        // 400 for a body that is not JSON at all, 422 for one that parses and
        // fails validation. Either is recoverable; a 500 is not.
        Assert.Contains(response.StatusCode,
            new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableContent });
    }

    [Fact]
    public async Task ACoarseButPossibleAccuracyIsStoredUnverifiedRatherThanRefused()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(accuracyMetres: 5000));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var visit = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(visit.GetProperty("verifiedOnSite").GetBoolean());
    }

    [Fact]
    public async Task AVisitTimeLaterThanNowIsRefused()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var tomorrow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).AddHours(2);

        using var response = await Post(host, Input(visitTime: tomorrow.ToString("HH:mm")));

        // Only meaningful when adding two hours stays inside the same IST day;
        // near midnight the date check owns this case instead.
        if (tomorrow.Hour >= 2)
        {
            Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(error.GetProperty("fieldErrors").TryGetProperty("input.visitTime", out _),
                await response.Content.ReadAsStringAsync());
        }
    }
}
