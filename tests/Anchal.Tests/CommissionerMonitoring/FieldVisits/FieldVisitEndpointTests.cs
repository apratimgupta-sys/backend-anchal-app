using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static Anchal.Tests.CommissionerMonitoring.FieldVisits.MonitoringTestHost;

namespace Anchal.Tests.CommissionerMonitoring.FieldVisits;

public sealed class FieldVisitEndpointTests
{
    private static object Input(
        string? visitDate = null, string visitTime = "10:30", int officeId = 3, int ward = 24,
        int serviceId = 9, double latitude = 23.3441, double longitude = 85.3096,
        double accuracyMetres = 18, DateTimeOffset? capturedAt = null,
        string issueAction = "Fogging done along the main lane; two bylanes still pending.") => new
        {
            visitDate = visitDate ?? Today(),
            visitTime,
            officeId,
            ward,
            serviceId,
            gps = new
            {
                latitude,
                longitude,
                accuracyMetres,
                capturedAt = (capturedAt ?? DateTimeOffset.UtcNow).ToString("O")
            },
            issueAction
        };

    private static string Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).DateTime).ToString("yyyy-MM-dd");

    private static string RequestId() => Guid.NewGuid().ToString("N");

    private static Task<HttpResponseMessage> Post(MonitoringTestHost host, object input, string? requestId = null) =>
        host.Client.PostAsync(Root + "/field-visits", Json(new { requestId = requestId ?? RequestId(), input }));

    [Fact]
    public async Task AnOfficerFilesAVisitAndTheServerStampsIt()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var visit = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, visit.GetProperty("officeId").GetInt32());
        Assert.Equal(24, visit.GetProperty("ward").GetInt32());
        Assert.Equal("A. Kumar", visit.GetProperty("officerName").GetString());
        Assert.Equal("Junior Engineer", visit.GetProperty("officerDesignation").GetString());
        Assert.False(visit.TryGetProperty("verifiedOnSite", out _));
        Assert.False(string.IsNullOrWhiteSpace(visit.GetProperty("receivedAt").GetString()));
        Assert.Equal(JsonValueKind.Null, visit.GetProperty("voidedAt").ValueKind);
    }

    [Fact]
    public async Task TheServerIgnoresAnythingTheClientClaimsAboutItself()
    {
        await using var host = await Create();
        var officer = await host.SignInAsOfficer();

        using var response = await host.Client.PostAsync(Root + "/field-visits", Json(new
        {
            requestId = RequestId(),
            input = Input(),
            receivedAt = "2001-01-01T00:00:00Z",
            verifiedOnSite = true,
            officerId = Guid.Empty
        }));

        var visit = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual("2001-01-01T00:00:00+00:00", visit.GetProperty("receivedAt").GetString());
        Assert.Equal(officer.GetProperty("id").GetString(), visit.GetProperty("officerId").GetString());
    }

    [Fact]
    public async Task ReplayingARequestIdReturnsTheFirstVisitInsteadOfCreatingASecond()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var requestId = RequestId();
        var input = Input();

        using var first = await Post(host, input, requestId);
        using var second = await Post(host, input, requestId);

        var one = await first.Content.ReadFromJsonAsync<JsonElement>();
        var two = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(one.GetProperty("id").GetString(), two.GetProperty("id").GetString());

        var list = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ReusingARequestIdForDifferentDataIsRefused()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var requestId = RequestId();

        using var first = await Post(host, Input(ward: 24), requestId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await Post(host, Input(ward: 25), requestId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task AVisitMustBeFiledOnTheDayItWasMade()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var yesterday = DateOnly.Parse(Today()).AddDays(-1).ToString("yyyy-MM-dd");

        using var response = await Post(host, Input(visitDate: yesterday));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("visitDate", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("too short")]
    [InlineData("                              ")]
    public async Task AnObservationMustSaySomething(string issueAction)
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(issueAction: issueAction));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("issueAction", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AKilometreWideFixIsStoredAsReported()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(accuracyMetres: 5000));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var visit = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5000, visit.GetProperty("gps").GetProperty("accuracyMetres").GetDouble());
    }

    [Fact]
    public async Task ALongMultilineDevanagariObservationRoundTripsUnchanged()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var observation = string.Join("\n", Enumerable.Repeat(
            "नाली की सफाई पूरी हुई; दो गलियों में कार्य शेष है।", 30));

        using var response = await Post(host, Input(issueAction: observation));

        var visit = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(observation, visit.GetProperty("issueAction").GetString());
    }

    [Fact]
    public async Task AnOfficerSeesOnlyTheirOwnVisits()
    {
        await using var host = await Create();
        var first = await host.SignInAsOfficer();
        await Post(host, Input());
        await host.SignInAsOfficer(username: "ae.hatia", name: "B. Singh", designation: "Assistant Engineer",
            homeOfficeId: 5, password: "Hatia#2026ae");
        await Post(host, Input(officeId: 5, ward: 51));

        var mine = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}");

        Assert.Equal(1, mine.GetProperty("total").GetInt32());
        Assert.Equal(51, mine.GetProperty("rows")[0].GetProperty("ward").GetInt32());
    }

    [Fact]
    public async Task AnOfficerCannotReadAnotherOfficersVisitsByFiltering()
    {
        await using var host = await Create();
        var first = await host.SignInAsOfficer();
        await Post(host, Input());
        var firstId = first.GetProperty("id").GetString();
        await host.SignInAsOfficer(username: "ae.hatia", name: "B. Singh", designation: "Assistant Engineer",
            homeOfficeId: 5, password: "Hatia#2026ae");

        var filtered = await host.Client.GetFromJsonAsync<JsonElement>(
            Root + $"/field-visits?from={Today()}&to={Today()}&officerId={firstId}");

        Assert.Equal(0, filtered.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task TheCommissionerSeesEveryOfficersVisitsAndCanNarrowThem()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        await Post(host, Input(ward: 24, serviceId: 9));
        await host.SignInAsOfficer(username: "ae.hatia", name: "B. Singh", designation: "Assistant Engineer",
            homeOfficeId: 5, password: "Hatia#2026ae");
        await Post(host, Input(officeId: 5, ward: 51, serviceId: 13));
        await host.Login("commissioner");

        var all = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}");
        var byOffice = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}&officeId=5");
        var byService = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}&serviceId=9");
        var byWard = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}&ward=51");

        Assert.Equal(2, all.GetProperty("total").GetInt32());
        Assert.Equal(1, byOffice.GetProperty("total").GetInt32());
        Assert.Equal(1, byService.GetProperty("total").GetInt32());
        Assert.Equal(1, byWard.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AnOfficeAccountIsRefusedEverywhereInThisModule()
    {
        await using var host = await Create();
        await host.Login("anchal1");

        using var create = await Post(host, Input());
        using var read = await host.Client.GetAsync(Root + $"/field-visits?from={Today()}&to={Today()}");
        using var toVoid = await host.Client.PostAsync(Root + "/field-visits/" + Guid.NewGuid() + "/void",
            Json(new { reason = "no" }));

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, toVoid.StatusCode);
    }

    [Fact]
    public async Task OnlyTheCommissionerMayVoidAVisit()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        using var created = await Post(host, Input());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        using var byOfficer = await host.Client.PostAsync(Root + $"/field-visits/{id}/void",
            Json(new { reason = "Filed against the wrong ward" }));
        Assert.Equal(HttpStatusCode.Forbidden, byOfficer.StatusCode);

        await host.Login("commissioner");
        using var byCommissioner = await host.Client.PostAsync(Root + $"/field-visits/{id}/void",
            Json(new { reason = "Filed against the wrong ward" }));

        Assert.True(byCommissioner.IsSuccessStatusCode,
            $"{byCommissioner.StatusCode}: {await byCommissioner.Content.ReadAsStringAsync()}");
        var voided = await byCommissioner.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Filed against the wrong ward", voided.GetProperty("voidReason").GetString());
        Assert.NotEqual(JsonValueKind.Null, voided.GetProperty("voidedAt").ValueKind);
    }

    [Fact]
    public async Task AVoidedVisitStaysInTheRegister()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        using var created = await Post(host, Input());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        await host.Login("commissioner");
        await host.Client.PostAsync(Root + $"/field-visits/{id}/void", Json(new { reason = "Wrong ward" }));

        var register = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}");

        Assert.Equal(1, register.GetProperty("total").GetInt32());
        Assert.Equal("Wrong ward", register.GetProperty("rows")[0].GetProperty("voidReason").GetString());
    }

    [Fact]
    public async Task VoidingWithoutAReasonIsRefused()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        using var created = await Post(host, Input());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        await host.Login("commissioner");

        using var response = await host.Client.PostAsync(Root + $"/field-visits/{id}/void", Json(new { reason = "   " }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AVisitCannotBeVoidedTwice()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        using var created = await Post(host, Input());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        await host.Login("commissioner");
        await host.Client.PostAsync(Root + $"/field-visits/{id}/void", Json(new { reason = "Wrong ward" }));

        using var again = await host.Client.PostAsync(Root + $"/field-visits/{id}/void", Json(new { reason = "Again" }));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task TheRegisterReportsItsTotalAndPagesThroughIt()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        for (var ward = 1; ward <= 55 - 3; ward++)
            Assert.Equal(HttpStatusCode.Created, (await Post(host, Input(ward: Math.Min(ward, 53)))).StatusCode);

        var first = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}");
        var second = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}&page=2");

        Assert.Equal(52, first.GetProperty("total").GetInt32());
        Assert.Equal(50, first.GetProperty("rows").GetArrayLength());
        Assert.Equal(2, second.GetProperty("rows").GetArrayLength());
    }
}
